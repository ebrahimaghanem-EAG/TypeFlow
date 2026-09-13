using System.Runtime.InteropServices;
using TypeFlow.Core.Interop;

namespace TypeFlow.Core.Hook;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) on a dedicated thread with a
/// message pump. Replicates the extension's capture-phase (true) document keydown
/// listener, but system-wide.
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    public struct KeyInfo
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public bool IsInjected;
        public bool IsUp;
        public bool IsExtended;
        public int Time;
    }

    private readonly object _gate = new object();
    private volatile IntPtr _hookId = IntPtr.Zero;
    private Thread? _thread;
    private volatile uint _threadId;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private volatile bool _running;

    /// <summary>Return true to swallow the key.</summary>
    public event Func<KeyInfo, KeyHandleResult>? KeyPressed;

    public sealed class KeyHandleResult
    {
        public bool Handled;
        public static readonly KeyHandleResult Pass = new KeyHandleResult { Handled = false };
        public static readonly KeyHandleResult Consume = new KeyHandleResult { Handled = true };
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread != null) throw new InvalidOperationException("Hook already started.");

            _proc = HookCallback;
            _running = true;
            _thread = new Thread(HookThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "TypeFlow.Hook";
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            // Wake the message pump with its real (native) thread id; WM_QUIT makes
            // GetMessage return false so the loop unwinds cleanly.
            if (_threadId != 0)
            {
                NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _thread = null;
        }
    }

    private void HookThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc!,
            IntPtr.Zero,
            0);

        if (_hookId == IntPtr.Zero)
        {
            _running = false;
            return;
        }

        while (_running)
        {
            if (!NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
                break;
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        IntPtr hook = _hookId;
        if (hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hook);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        var info = (NativeMethods.LowLevelKeyboardHookStruct)Marshal.PtrToStructure(
            lParam, typeof(NativeMethods.LowLevelKeyboardHookStruct))!;

        var keyInfo = new KeyInfo
        {
            VirtualKey = info.vkCode,
            ScanCode = info.scanCode,
            Flags = info.flags,
            IsInjected = (info.flags & NativeMethods.LLKHF_INJECTED) != 0,
            IsUp = (info.flags & NativeMethods.LLKHF_UP) != 0,
            IsExtended = (info.flags & NativeMethods.LLKHF_EXTENDED) != 0,
            Time = info.time
        };

        bool handled = false;
        var handler = KeyPressed;
        if (handler != null)
        {
            try
            {
                handled = handler(keyInfo).Handled;
            }
            catch
            {
                handled = false; // never let an engine fault lock the keyboard
            }
        }

        if (handled)
            return new IntPtr(1);

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}