using System;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Clicker.Services;

/// <summary>Notification-area icon with a context menu (Open / Start-Stop / Exit).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _showItem;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private readonly Drawing.Icon _idleIcon;
    private readonly Drawing.Icon _activeIcon;

    public TrayIcon()
    {
        _idleIcon = LoadIcon("clicker.ico");
        _activeIcon = LoadIcon("clicker_active.ico");

        _showItem = new WinForms.ToolStripMenuItem("Open", null, (_, _) => ShowRequested?.Invoke());
        _showItem.Font = new Drawing.Font(_showItem.Font, Drawing.FontStyle.Bold);
        _toggleItem = new WinForms.ToolStripMenuItem("Start", null, (_, _) => ToggleRequested?.Invoke());
        _exitItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _showItem,
            _toggleItem,
            new WinForms.ToolStripSeparator(),
            _exitItem,
        });

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _idleIcon,
            ContextMenuStrip = _menu,
            Text = "Clicker",
            Visible = true,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                ShowRequested?.Invoke();
        };
    }

    public event Action? ShowRequested;

    public event Action? ToggleRequested;

    public event Action? ExitRequested;

    public void Update(bool running, string tooltip, string showText, string toggleText, string exitText)
    {
        _notifyIcon.Icon = running ? _activeIcon : _idleIcon;
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip; // Windows limit
        _showItem.Text = showText;
        _toggleItem.Text = toggleText;
        _exitItem.Text = exitText;
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _idleIcon.Dispose();
        _activeIcon.Dispose();
    }

    private static Drawing.Icon LoadIcon(string fileName)
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/Assets/{fileName}", UriKind.Absolute))
            ?? throw new InvalidOperationException($"Missing resource {fileName}");

        using var stream = resource.Stream;
        return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }
}
