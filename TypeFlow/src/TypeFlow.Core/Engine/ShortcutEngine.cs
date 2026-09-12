using TypeFlow.Core.Focus;
using TypeFlow.Core.Hook;
using TypeFlow.Core.Input;
using TypeFlow.Core.Interop;

namespace TypeFlow.Core.Engine;

/// <summary>
/// Ports the extension's autocorrect logic to a global Windows context.
/// Replicates the keydown capture-phase listener in content.js: trigger on Space/'.',
/// word grammar <c>([a-zA-Z0-9_.\-]+)</c>, case-insensitive lookup, custom Ctrl+Z undo,
/// and the <c>lastExpansionState</c> clearing rules.
/// </summary>
public sealed class ShortcutEngine
{
    private readonly object _gate = new object();
    private readonly IFocusProvider _focus;
    private readonly IInputSink _sink;
    private readonly TypeBuffer _buffer = new TypeBuffer();
    private ExpansionState? _last;
    private Dictionary<string, string> _shortcuts = new Dictionary<string, string>(StringComparer.Ordinal);
    private bool _isEnabled = true;
    private bool _isInputEnabled = true;

    private readonly byte[] _modifierState = new byte[256];
    private IntPtr _lastLayout;
    private readonly int _selfProcessId = Environment.ProcessId;

    public ShortcutEngine(IFocusProvider focus, IInputSink? sink = null)
    {
        _focus = focus;
        _sink = sink ?? SendInputSink.Instance;
    }

    public bool IsEnabled
    {
        get { lock (_gate) return _isEnabled; }
        set { lock (_gate) _isEnabled = value; }
    }

    public bool IsInputEnabled
    {
        get { lock (_gate) return _isInputEnabled; }
        set { lock (_gate) _isInputEnabled = value; }
    }

    public int ShortcutCount
    {
        get { lock (_gate) return _shortcuts.Count; }
    }

    public void ReplaceShortcuts(IDictionary<string, string> shortcuts)
    {
        lock (_gate)
        {
            _shortcuts = new Dictionary<string, string>(shortcuts, StringComparer.Ordinal);
        }
    }

    /// <summary>Called from the hook thread; returns true if the key was consumed.</summary>
    public bool HandleKey(in GlobalKeyboardHook.KeyInfo key)
    {
        lock (_gate)
        {
            UpdateModifiers(key);

            // Never react to our own injected input.
            if (key.IsInjected) return false;

            // Engine-wide active toggle.
            if (!_isEnabled) return false;

            // Only process key-down events for buffering/triggering.
            if (key.IsUp) return false;

            var field = _focus.Current;

            // Never expand inside TypeFlow's own UI (the extension's content script
            // likewise does not run in the extension's popup).
            if (field.ProcessId == _selfProcessId) return false;

            // Modifier-only keys keep the tracker updated but do nothing else.
            if (KeyMapper.IsModifierOrControl(key.VirtualKey)) return false;

            // Ctrl+Z custom undo.
            bool ctrl = Mods().ControlDown;
            bool alt = Mods().AltDown;

            // AltGr (Ctrl+Alt) is used to *type* characters on many layouts — treat it
            // like plain printable input, not as a Ctrl command.
            if (ctrl && !alt && !KeyMapper.IsModifierOrControl(key.VirtualKey))
            {
                if (key.VirtualKey == 0x5A) // Z
                {
                    if (TryUndo(field)) return true;
                    return false;
                }
                // Ctrl+X/C/V/A etc. destroy the caret context we rely on.
                _last = null;
                _buffer.Reset();
                return false;
            }

            if (alt && !ctrl)
            {
                // Plain Alt shortcuts (e.g. Alt+S) — do not buffer or trigger.
                _last = null;
                _buffer.Reset();
                return false;
            }

            // Non-editable / password fields: nothing to do.
            if (!field.IsEditable) return false;

            if (field.IsPlainInput && !_isInputEnabled) return false;

            if (IsImeComposing(field))
            {
                _last = null;
                return false;
            }

            switch (key.VirtualKey)
            {
                case 0x08: // Backspace
                    _buffer.Backspace();
                    _last = null;
                    return false;
                case 0x0D: // Enter
                case 0x09: // Tab
                case 0x1B: // Escape
                    _buffer.Reset();
                    _last = null;
                    return false;
                case 0x23: // End
                case 0x24: // Home
                case 0x25:
                case 0x26:
                case 0x27:
                case 0x28: // Arrows
                case 0x21: // PgUp
                case 0x22: // PgDn
                    _buffer.Reset();
                    _last = null;
                    return false;
                case 0x2E: // Delete
                    _buffer.Reset();
                    _last = null;
                    return false;
            }

            char? ch = KeyMapper.ToChar(
                key.VirtualKey, key.ScanCode, key.Flags,
                ResolveLayout(field), _modifierState);

            if (ch == null) return false; // dead/control key

            if (ch == ' ' || ch == '.')
            {
                return HandleTrigger(field, ch.Value);
            }

            // Regular printable character typed — this is the extension's rule that
            // clears previous expansion state (except for trigger chars).
            _last = null;
            if (TypeBuffer.IsWordChar(ch.Value))
            {
                _buffer.Append(ch.Value);
            }
            else
            {
                _buffer.Reset();
            }
            return false;
        }
    }

    /// <summary>Reset volatile state (called when the focused control changes).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _last = null;
            _buffer.Reset();
        }
    }

    // ─── Test seams (no layout dependency for unit verification) ───

    internal bool IsFieldEligibleForExpansion(FocusedField field)
    {
        if (!field.IsEditable) return false;
        if (field.IsPlainInput && !_isInputEnabled) return false;
        return true;
    }

    internal void SetBufferForTest(string word)
    {
        lock (_gate) _buffer.ReplaceTrailingWord(word);
    }

    internal string PeekBufferForTest()
    {
        lock (_gate) return _buffer.CurrentWord;
    }

    internal bool HandleTriggerForTest(FocusedField field, char trigger)
    {
        lock (_gate) return HandleTrigger(field, trigger);
    }

    private bool HandleTrigger(FocusedField field, char triggerChar)
    {
        string word = _buffer.CurrentWord;
        if (word.Length == 0) return false;

        string key = word.ToLowerInvariant();
        if (!_shortcuts.TryGetValue(key, out string? expansion))
        {
            // Trigger with no match is a word boundary (regex maximal-munch parity).
            _buffer.Reset();
            _last = null;
            return false;
        }

        string expansionWithTrigger = (expansion ?? string.Empty) + triggerChar;

        _sink.InjectReplace(word.Length, expansionWithTrigger);

        _buffer.ReplaceTrailingWord(expansionWithTrigger);
        _last = new ExpansionState
        {
            FieldIdentity = field.Identity,
            ShortcutWord = word,
            TriggerChar = triggerChar.ToString(),
            ExpansionWithTrigger = expansionWithTrigger
        };
        return true;
    }

    private bool TryUndo(FocusedField field)
    {
        if (_last == null) return false;
        if (_last.FieldIdentity != field.Identity) return false;
        if (!field.IsEditable) return false;

        var state = _last;
        _last = null;

        string restore = state.ShortcutWord + state.TriggerChar;
        _sink.InjectReplace(state.CharCount, restore);
        _buffer.ReplaceTrailingWord(restore);
        return true;
    }

    private ref struct ModifierFlags
    {
        public bool ShiftDown;
        public bool ControlDown;
        public bool AltDown;
    }

    private ModifierFlags Mods()
    {
        var m = new ModifierFlags();
        m.ShiftDown = IsDown(NativeMethods.VK_LSHIFT) || IsDown(NativeMethods.VK_RSHIFT);
        m.ControlDown = IsDown(NativeMethods.VK_LCONTROL) || IsDown(NativeMethods.VK_RCONTROL);
        m.AltDown = IsDown(NativeMethods.VK_LMENU) || IsDown(NativeMethods.VK_RMENU);
        return m;
    }

    private bool IsDown(int vk) => (_modifierState[vk & 0xFF] & 0x80) != 0;

    private void UpdateModifiers(in GlobalKeyboardHook.KeyInfo key)
    {
        int vk = key.VirtualKey;
        if (vk is < 0 or > 0xFF) return;

        byte value = key.IsUp ? (byte)0 : (byte)0x80;

        // Low-level hooks may report either the generic OR per-side VK; normalize both.
        switch (vk)
        {
            case 0x10: // Shift
                _modifierState[0xA0] = value;
                _modifierState[0xA1] = value;
                break;
            case 0x11: // Control
                _modifierState[0xA2] = value;
                _modifierState[0xA3] = value;
                break;
            case 0x12: // Menu (Alt)
                _modifierState[0xA4] = value;
                _modifierState[0xA5] = value;
                break;
        }
        _modifierState[vk] = value;
    }

    private bool IsImeComposing(FocusedField field)
    {
        try
        {
            IntPtr imc = NativeMethods.ImmGetContext(field.FocusHwnd);
            if (imc == IntPtr.Zero) return false;
            try
            {
                // Only suppress buffering while the IME is actually composing text;
                // an open IME in direct mode must still expand normally.
                int len = NativeMethods.ImmGetCompositionStringW(imc, NativeMethods.GCS_COMPSTR, null, 0);
                return len > 0;
            }
            finally
            {
                NativeMethods.ImmReleaseContext(field.FocusHwnd, imc);
            }
        }
        catch
        {
            return false;
        }
    }

    private IntPtr ResolveLayout(FocusedField field)
    {
        IntPtr layout = NativeMethods.GetKeyboardLayout(field.FocusThreadId);
        if (layout == IntPtr.Zero) layout = _lastLayout;
        else _lastLayout = layout;
        return layout;
    }
}