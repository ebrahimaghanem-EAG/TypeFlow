using System.Runtime.InteropServices;
using System.Text;

namespace TypeFlow.Core.Interop;

public static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int LLKHF_INJECTED = 0x00000010;
    public const int LLKHF_EXTENDED = 0x00000001;
    public const int LLKHF_UP = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    public struct LowLevelKeyboardHookStruct
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public int rcCaret_left;
        public int rcCaret_top;
        public int rcCaret_right;
        public int rcCaret_bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const int ES_MULTILINE = 0x0004;
    public const int ES_PASSWORD = 0x0020;
    public const int WS_TABSTOP = 0x00010000;
    public const long WS_EX_CONTROLPARENT = 0x00010000L;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    // ToUnicodeEx returns the number of characters copied.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_BACK = 0x08;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_CAPITAL = 0x14;
    public const int VK_NUMLOCK = 0x90;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    // Keyboard input (SendInput)
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct InputData
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, InputData[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr GetMessageExtraInfo();

    public static readonly int SizeOfInput = Marshal.SizeOf(typeof(InputData));
    public static readonly int SizeOfKbdIn = Marshal.SizeOf(typeof(KEYBDINPUT));

    // Imm (IME)
    [DllImport("imm32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("imm32.dll", SetLastError = false)]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    public const uint GCS_COMPSTR = 0x0008;

    [DllImport("imm32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int ImmGetCompositionStringW(IntPtr hIMC, uint dwIndex, StringBuilder? lpBuf, int dwBufLen);

    [DllImport("imm32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
}