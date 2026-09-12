using System.IO;
using System.Reflection;
using System.Windows;
using TypeFlow.Core.Engine;
using TypeFlow.Core.Focus;
using TypeFlow.Core.Hook;
using TypeFlow.Core.Storage;
using MessageBox = System.Windows.MessageBox;

namespace TypeFlow.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
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
            MessageBox.Show("TypeFlow is already running (check the system tray).", "TypeFlow",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(DataDirectory);

        // Single-line-input toggle persisted across restarts.
        bool inputEnabled = true;
        string configPath = Path.Combine(DataDirectory, "settings.json");
        if (File.Exists(configPath))
        {
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(configPath));
                if (cfg != null && cfg.TryGetValue("isInputEnabled", out bool v)) inputEnabled = v;
            }
            catch { }
        }

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
            Shutdown();
        }
    }
}