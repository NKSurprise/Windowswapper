namespace WindowSwapper;

/// <summary>
/// A UI-less "app" that lives entirely in the system tray. No main window is ever shown —
/// ApplicationContext keeps the message loop alive without one.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly HookManager _hookManager;
    private readonly HighlightOverlay _highlight;

    private static readonly Icon ActiveIcon = SystemIcons.Application;
    private static readonly Icon SuspendedIcon = SystemIcons.Shield; // TODO: swap for custom .ico assets

    public TrayApplicationContext()
    {
        _hookManager = new HookManager();
        _highlight = new HighlightOverlay();

        _hookManager.ActiveStateChanged += OnActiveStateChanged;
        _hookManager.WindowsSwapped += OnWindowsSwapped;
        _hookManager.SelectionStarted += hwnd => _highlight.TrackWindow(hwnd);
        _hookManager.SelectionEnded += () => _highlight.HideOverlay();
        _highlight.TargetLost += () => _hookManager.CancelPendingSelection();

        var menu = new ContextMenuStrip();
        var statusItem = new ToolStripMenuItem("Status: Active") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = ActiveIcon,
            Text = "WindowSwapper — active",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Keep a reference so OnActiveStateChanged can update its text.
        _statusMenuItem = statusItem;

        _hookManager.Start();
    }

    private readonly ToolStripMenuItem _statusMenuItem;

    private void OnActiveStateChanged(bool isActive)
    {
        // Hook callbacks can fire off the UI thread's normal call path in edge cases;
        // NotifyIcon/ToolStripMenuItem property sets are cheap enough here that we don't
        // strictly need BeginInvoke, but if you add anything heavier, marshal it.
        _trayIcon.Icon = isActive ? ActiveIcon : SuspendedIcon;
        _trayIcon.Text = isActive
            ? "WindowSwapper — active"
            : "WindowSwapper — suspended (fullscreen app focused)";
        _statusMenuItem.Text = isActive ? "Status: Active" : "Status: Suspended (fullscreen)";
    }

    private void OnWindowsSwapped(nint a, nint b)
    {
        // TODO: optional brief balloon/toast confirmation. Kept silent for now since a
        // background utility that pops notifications on every swap gets annoying fast.
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hookManager.Dispose();
            _highlight.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
