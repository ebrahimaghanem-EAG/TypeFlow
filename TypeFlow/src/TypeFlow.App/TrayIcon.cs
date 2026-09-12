using System.Drawing;
using System.Windows.Forms;
using TypeFlow.Core.Engine;

namespace TypeFlow.App;

/// <summary>System-tray presence so expansion keeps working with the window hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _activeItem;
    private readonly ShortcutEngine _engine;

    public event Action? OpenMainWindow;
    public event Action? Exit;

    public TrayIcon(ShortcutEngine engine)
    {
        _engine = engine;

        _notifyIcon = new NotifyIcon();

        using var bmp = new Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 120, 215));
            g.DrawString("T", font, brush, 2, 0);
        }
        _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
        _notifyIcon.Text = "TypeFlow \u2014 text expander";

        var menu = new ContextMenuStrip();
        _activeItem = new ToolStripMenuItem("TypeFlow Active", null, (_, _) => ToggleActive());
        SyncActive();

        menu.Items.Add("Open TypeFlow", null, (_, _) => OpenMainWindow?.Invoke());
        menu.Items.Add(_activeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit?.Invoke());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenMainWindow?.Invoke();
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