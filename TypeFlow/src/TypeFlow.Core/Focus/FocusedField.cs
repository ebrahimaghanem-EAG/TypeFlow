using System.Runtime.InteropServices;
using TypeFlow.Core.Interop;

namespace TypeFlow.Core.Focus;

/// <summary>
/// Snapshot describing the currently focused window/control. Built with cheap Win32
/// calls only so it never blocks the hook. Replicates the extension's distinction
/// between single-line INPUT fields (gated by isInputEnabled), contenteditable/rich
/// editors (always eligible), and password fields (never expanded).
/// </summary>
public sealed class FocusedField
{
    public IntPtr ForegroundWindow { get; init; }
    public IntPtr FocusHwnd { get; init; }
    public uint FocusThreadId { get; init; }
    public uint ProcessId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public bool IsPlainInput { get; init; }   // single-line Win32 EDIT == <input>/<textarea> equivalent
    public bool IsPassword { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsKnownRich { get; init; }     // multiline EDIT / Richedit → contenteditable-equivalent

    public bool IsEditable
    {
        get
        {
            if (IsPassword) return false;
            if (IsReadOnly) return false;
            return true;
        }
    }

    public long Identity => FocusHwnd.ToInt64();

    public static FocusedField Poll()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return new FocusedField { ForegroundWindow = IntPtr.Zero, FocusHwnd = IntPtr.Zero, IsPlainInput = false };
        }

        uint pid = 0;
        uint tid = NativeMethods.GetWindowThreadProcessId(fg, out pid);

        var gti = new NativeMethods.GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
        bool got = NativeMethods.GetGUIThreadInfo(tid, out gti);

        IntPtr focus = got && gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : fg;

        string cls = GetClass(focus);
        int style = NativeMethods.GetWindowLong(focus, NativeMethods.GWL_STYLE);

        bool isClassicEdit = cls.Equals("Edit", StringComparison.OrdinalIgnoreCase);
        bool isRichEdit = cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
        bool multiLine = (style & NativeMethods.ES_MULTILINE) != 0;
        bool password = (style & NativeMethods.ES_PASSWORD) != 0;
        bool readOnly = (style & 0x0800) != 0; // ES_READONLY

        return new FocusedField
        {
            ForegroundWindow = fg,
            FocusHwnd = focus,
            FocusThreadId = tid,
            ProcessId = pid,
            ClassName = cls,
            IsPlainInput = isClassicEdit && !multiLine && !password && !readOnly,
            IsPassword = password,
            IsReadOnly = readOnly,
            IsKnownRich = (isClassicEdit && multiLine) || isRichEdit
        };
    }

    private static string GetClass(IntPtr hwnd)
    {
        try
        {
            var sb = new char[256];
            int n = NativeMethods.GetClassName(hwnd, sb, sb.Length);
            if (n > 0) return new string(sb, 0, n);
        }
        catch { }
        return string.Empty;
    }
}