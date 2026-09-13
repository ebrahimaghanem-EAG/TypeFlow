using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TypeFlow.App.Localization;
using TypeFlow.Core.Engine;

namespace TypeFlow.App;

/// <summary>System-tray presence so expansion keeps working with the window hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _activeItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ShortcutEngine _engine;

    public event Action? OpenMainWindow;
    public event Action? Exit;

    public TrayIcon(ShortcutEngine engine)
    {
        _engine = engine;

        _notifyIcon = new NotifyIcon { Icon = LoadTrayIcon() };

        var menu = new ContextMenuStrip();
        _activeItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ToggleActive());
        _openItem = new ToolStripMenuItem(string.Empty, null, (_, _) => OpenMainWindow?.Invoke());
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) => Exit?.Invoke());
        SyncActive();

        menu.Items.Add(_openItem);
        menu.Items.Add(_activeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenMainWindow?.Invoke();

        // Follow live language switches.
        L10n.LanguageChanged += ApplyStrings;
        ApplyStrings();
    }

    private void ApplyStrings()
    {
        _notifyIcon.Text = L10n.T("tray.title");
        _openItem.Text = L10n.T("tray.open");
        _activeItem.Text = L10n.T("tray.active");
        _exitItem.Text = L10n.T("tray.exit");
    }

    // Prefer the shipped icon (16px frame) so the tray matches the window/exe artwork.
    private static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("TypeFlow.TrayIcon.ico");
            if (stream != null) return new Icon(stream, 16, 16);
        }
        catch { }
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TypeFlow.ico");
            if (File.Exists(path)) return new Icon(path, 16, 16);
        }
        catch { }

        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var font = new Font("Segoe UI", 8, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.DrawString("T", font, brush, 2, 0);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void SyncActive() => _activeItem.Checked = _engine.IsEnabled;

    private void ToggleActive()
    {
        _engine.IsEnabled = !_engine.IsEnabled;
        SyncActive();
    }

    public void Show() => _notifyIcon.Visible = true;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}