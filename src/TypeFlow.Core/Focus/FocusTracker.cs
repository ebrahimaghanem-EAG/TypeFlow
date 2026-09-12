namespace TypeFlow.Core.Focus;

/// <summary>
/// Polls the focused control on every access. Cheap (pure Win32); removes the
/// foreground-change race between the watcher snapshot and a keystroke.
/// </summary>
public sealed class PollingFocusProvider : IFocusProvider
{
    public FocusedField Current => FocusedField.Poll();
}

/// <summary>
/// Watches for foreground/focus changes and publishes the current
/// <see cref="FocusedField"/>. Runs on its own thread; the hook reads the latest
/// snapshot, so a slow focus resolution never blocks typing.
/// </summary>
public sealed class FocusTracker : IFocusProvider, IDisposable
{
    private readonly object _gate = new object();
    private FocusedField _current = FocusedField.Poll();
    private long _generation;
    private Thread? _thread;
    private volatile bool _running;
    private readonly TimeSpan _interval;

    /// <summary>Raised on the watcher thread when the focused control changes.</summary>
    public event Action<FocusedField>? FieldChanged;

    public FocusTracker(TimeSpan? interval = null)
    {
        _interval = interval ?? TimeSpan.FromMilliseconds(300);
    }

    public FocusedField Current
    {
        get { lock (_gate) return _current; }
    }

    public long Generation
    {
        get { lock (_gate) return _generation; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "TypeFlow.Focus";
            _thread.Start();
        }
    }

    private void Loop()
    {
        long lastIdentity = _current.Identity == 0 ? long.MinValue : _current.Identity;
        while (_running)
        {
            try
            {
                var field = FocusedField.Poll();
                long currentIdentity;
                IntPtr currentForeground;
                lock (_gate)
                {
                    currentIdentity = _current.Identity;
                    currentForeground = _current.ForegroundWindow;
                }
                if (field.Identity != currentIdentity || field.ForegroundWindow != currentForeground)
                {
                    lock (_gate)
                    {
                        _generation++;
                        _current = field;
                    }
                    lastIdentity = field.Identity;
                    FieldChanged?.Invoke(field);
                }
            }
            catch
            {
                // keep polling regardless of transient failures
            }
            Thread.Sleep(_interval);
        }
    }

    public void Dispose()
    {
        _running = false;
    }
}