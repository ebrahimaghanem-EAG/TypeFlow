using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using TypeFlow.App.Localization;
using TypeFlow.Core.Engine;
using TypeFlow.Core.Focus;
using TypeFlow.Core.Hook;
using TypeFlow.Core.Storage;

namespace TypeFlow.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showRequestHandle;
    private Thread? _showWaiterThread;
    private ShortcutStore? _store;
    private FocusTracker? _focusTracker;
    private ShortcutEngine? _engine;
    private GlobalKeyboardHook? _hook;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeFlow");

    public static string StorePath => Path.Combine(DataDirectory, "shortcuts.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A silent background app must capture its own crashes.
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            try { File.WriteAllText(Path.Combine(DataDirectory, "crash.log"), ex.ExceptionObject?.ToString()); } catch { }
        };
        DispatcherUnhandledException += (s, ex) =>
        {
            try { File.WriteAllText(Path.Combine(DataDirectory, "crash.log"), ex.Exception.ToString()); } catch { }
            // Keep the hook alive rather than letting a UI fault kill the background utility.
            ex.Handled = true;
        };

        bool isPrimary;
        _singleInstance = new Mutex(true, "Local\\TypeFlow.SingleInstance", out isPrimary);
        if (!isPrimary)
        {
            // Another TypeFlow is already running (likely hidden in the tray).
            // Ask it to bring its window forward, then exit silently.
            try { EventWaitHandle.OpenExisting("Local\\TypeFlow.ShowRequest")?.Set(); } catch { }
            Shutdown();
            return;
        }

        // A re-launch tells us to surface the window again.
        _showRequestHandle = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TypeFlow.ShowRequest");
        _showWaiterThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_showRequestHandle == null) break;
                    _showRequestHandle.WaitOne();
                    Dispatcher.Invoke(ShowMainWindow);
                }
                catch { break; }
            }
        }) { IsBackground = true };
        _showWaiterThread.Start();

        Directory.CreateDirectory(DataDirectory);

        // Persisted settings (input toggle + language); old {"isInputEnabled":…}
        // payloads still deserialize. Empty language = auto-detect from the OS.
        string configPath = Path.Combine(DataDirectory, "settings.json");
        var settings = SettingsIo.Load(configPath);
        bool inputEnabled = settings.IsInputEnabled;
        string language = settings.Language.Length > 0
            ? settings.Language
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
        try
        {
            L10n.Apply(language);
        }
        catch { }

        string? defaults = null;
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TypeFlow.defaults.csv");
            if (s != null)
            using (var reader = new StreamReader(s))
            {
                defaults = reader.ReadToEnd();
            }
        }
        catch { }

        _store = ShortcutStore.Create(StorePath, defaults);

        _focusTracker = new FocusTracker();
        _engine = new ShortcutEngine(new PollingFocusProvider())
        {
            IsInputEnabled = inputEnabled
        };
        _engine.ReplaceShortcuts(_store.Shortcuts);
        _focusTracker.FieldChanged += _ => _engine.Reset();
        _focusTracker.Start();

        _hook = new GlobalKeyboardHook();
        _hook.KeyPressed += OnKeyPress;
        _hook.Start();

        _tray = new TrayIcon(_engine);
        _tray.OpenMainWindow += () => Dispatcher.Invoke(ShowMainWindow);
        _tray.Exit += () => Dispatcher.Invoke(ApplicationExit);

        _mainWindow = new MainWindow(_store, _engine, configPath);
        _mainWindow.Closing += (s, args) =>
        {
            // Keep running in the tray; the window hides instead of closing.
            if (!_exiting)
            {
                args.Cancel = true;
                _mainWindow.Hide();
            }
        };

        ShowMainWindow();
        _tray.Show();
    }

    private bool _exiting;

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private GlobalKeyboardHook.KeyHandleResult OnKeyPress(GlobalKeyboardHook.KeyInfo key)
    {
        if (_engine == null) return GlobalKeyboardHook.KeyHandleResult.Pass;
        try
        {
            return _engine.HandleKey(key)
                ? GlobalKeyboardHook.KeyHandleResult.Consume
                : GlobalKeyboardHook.KeyHandleResult.Pass;
        }
        catch
        {
            return GlobalKeyboardHook.KeyHandleResult.Pass;
        }
    }

    private void ApplicationExit()
    {
        if (_exiting) return;
        _exiting = true;

        try
        {
            _hook?.Stop();
            _focusTracker?.Dispose();
            _tray?.Dispose();
        }
        finally
        {
            try { _showRequestHandle?.Dispose(); } catch { }
            Shutdown();
        }
    }
}