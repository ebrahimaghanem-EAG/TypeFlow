using System.Text;
using TypeFlow.Core.Interop;

namespace TypeFlow.Core.Input;

/// <summary>
/// Resolves the character produced by a physical key using the keyboard layout
/// of the thread that owns the focused window (mirror of the extension relying on
/// keydown + DOM text: we derive the same observable character).
/// </summary>
public static class KeyMapper
{
    /// <summary>Hashed key/value pairs to detect non-printable vk codes handled elsewhere.</summary>
    public static bool IsModifierOrControl(int vk)
    {
        switch (vk)
        {
            case NativeMethods.VK_SHIFT:
            case NativeMethods.VK_LSHIFT:
            case NativeMethods.VK_RSHIFT:
            case NativeMethods.VK_CONTROL:
            case NativeMethods.VK_LCONTROL:
            case NativeMethods.VK_RCONTROL:
            case NativeMethods.VK_MENU:
            case NativeMethods.VK_LMENU:
            case NativeMethods.VK_RMENU:
            case 0x5B: // LWIN
            case 0x5C: // RWIN
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the printable character a key maps to, or null if the key is a control key.
    /// </summary>
    public static char? ToChar(int vk, int scanCode, int flags, IntPtr layout, byte[] trackedKeyState)
    {
        if (IsModifierOrControl(vk)) return null;

        byte[] state = new byte[256];
        NativeMethods.GetKeyboardState(state);

        // Overlay our tracked modifier state (hook thread's own state is unreliable).
        if (trackedKeyState != null)
        {
            for (int i = 0; i < trackedKeyState.Length && i < state.Length; i++)
            {
                if (trackedKeyState[i] != 0) state[i] = trackedKeyState[i];
            }
        }

        // The key itself is considered down.
        state[vk & 0xFF] = 0x80;
        // Caps/num lock from async state (system-wide).
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_CAPITAL) & 1) != 0)
            state[NativeMethods.VK_CAPITAL] |= 0x01;
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_NUMLOCK) & 1) != 0)
            state[NativeMethods.VK_NUMLOCK] |= 0x01;

        StringBuilder sb = new StringBuilder(8);
        int count = NativeMethods.ToUnicodeEx((uint)vk, (uint)scanCode, state, sb, sb.Capacity, 0, layout);
        if (count > 0 && sb.Length > 0)
        {
            char c = sb[0];
            // Ignore control characters produced by keys like Enter/Ctrl combos.
            if (char.IsControl(c)) return null;
            return c;
        }
        return null;
    }
}