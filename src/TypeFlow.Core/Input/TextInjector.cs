using TypeFlow.Core.Interop;

namespace TypeFlow.Core.Input;

/// <summary>
/// Injects synthetic keyboard input (Unicode text and backspaces) into the
/// foreground application, the Windows equivalent of the extension's DOM edits.
/// </summary>
public static class TextInjector
{
    public static void InjectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new NativeMethods.InputData[text.Length * 2];
        int index = 0;
        foreach (char c in text)
        {
            inputs[index++] = UnicodeKey(c, down: true);
            inputs[index++] = UnicodeKey(c, down: false);
        }
        NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.SizeOfInput);
    }

    public static void InjectBackspaces(int count)
    {
        if (count <= 0) return;
        var inputs = new NativeMethods.InputData[count * 2];
        int index = 0;
        for (int i = 0; i < count; i++)
        {
            inputs[index++] = VkKey(NativeMethods.VK_BACK, down: true);
            inputs[index++] = VkKey(NativeMethods.VK_BACK, down: false);
        }
        NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.SizeOfInput);
    }

    /// <summary>
    /// Deletes <paramref name="backspaceCount"/> characters then injects
    /// <paramref name="text"/> in a single atomic SendInput call so the
    /// target application processes the whole sequence without interleaving
    /// real user input.
    /// </summary>
    public static void InjectReplace(int backspaceCount, string text)
    {
        int bsLen = backspaceCount > 0 ? backspaceCount * 2 : 0;
        int txtLen = text?.Length ?? 0;
        int total = bsLen + txtLen * 2;
        if (total == 0) return;

        var inputs = new NativeMethods.InputData[total];
        int index = 0;

        for (int i = 0; i < backspaceCount; i++)
        {
            inputs[index++] = VkKey(NativeMethods.VK_BACK, down: true);
            inputs[index++] = VkKey(NativeMethods.VK_BACK, down: false);
        }

        if (text != null)
        {
            foreach (char c in text)
            {
                inputs[index++] = UnicodeKey(c, down: true);
                inputs[index++] = UnicodeKey(c, down: false);
            }
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.SizeOfInput);
    }

    private static NativeMethods.InputData UnicodeKey(char c, bool down)
    {
        var kbd = new NativeMethods.KEYBDINPUT();
        kbd.wVk = 0;
        kbd.wScan = (ushort)c;
        kbd.dwFlags = NativeMethods.KEYEVENTF_UNICODE | (down ? 0u : NativeMethods.KEYEVENTF_KEYUP);
        kbd.time = 0;
        kbd.dwExtraInfo = NativeMethods.GetMessageExtraInfo();

        return new NativeMethods.InputData
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion { ki = kbd }
        };
    }

    private static NativeMethods.InputData VkKey(int vk, bool down)
    {
        var kbd = new NativeMethods.KEYBDINPUT();
        kbd.wVk = (ushort)vk;
        kbd.wScan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        kbd.dwFlags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP;
        if ((kbd.dwFlags & NativeMethods.KEYEVENTF_KEYUP) == 0 && kbd.wScan > 0x00FF)
            kbd.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        kbd.time = 0;
        kbd.dwExtraInfo = NativeMethods.GetMessageExtraInfo();

        return new NativeMethods.InputData
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion { ki = kbd }
        };
    }
}
