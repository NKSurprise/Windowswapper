using System.Runtime.InteropServices;
using static WindowSwapper.NativeMethods;

namespace WindowSwapper;

/// <summary>
/// A borderless, click-through, non-activating window that draws a colored frame around
/// whatever window is currently selected as "A". Uses the TransparencyKey trick: the form's
/// interior is filled with a key color that Windows renders as fully transparent, so only the
/// border pixels (painted in a different color) are actually visible.
/// </summary>
internal sealed class HighlightOverlay : Form
{
    private const int BorderThickness = 4;
    private static readonly Color KeyColor = Color.Magenta; // arbitrary, just needs to differ from BorderColor
    private static readonly Color BorderColor = Color.FromArgb(255, 0, 200, 255);

    private readonly System.Windows.Forms.Timer _trackingTimer;
    private nint _trackedHwnd;

    /// <summary>Fired when the tracked window is closed/destroyed while a selection is pending.</summary>
    public event Action? TargetLost;

    public HighlightOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
        DoubleBuffered = true;

        Paint += OnPaint;

        _trackingTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _trackingTimer.Tick += (_, _) => FollowTarget();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TRANSPARENT = 0x20;   // click-through
            const int WS_EX_LAYERED = 0x80000;    // required for TransparencyKey
            const int WS_EX_NOACTIVATE = 0x08000000; // never steals focus
            const int WS_EX_TOOLWINDOW = 0x80;    // hidden from alt-tab / taskbar

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>Start showing the frame around <paramref name="hwnd"/> and following it if it moves.</summary>
    public void TrackWindow(nint hwnd)
    {
        _trackedHwnd = hwnd;
        if (!UpdateBoundsFromTarget())
        {
            HideOverlay();
            return;
        }

        if (!Visible)
            ShowNoActivate();

        _trackingTimer.Start();
    }

    public void HideOverlay()
    {
        _trackingTimer.Stop();
        _trackedHwnd = 0;
        if (Visible)
            Hide();
    }

    private void FollowTarget()
    {
        if (_trackedHwnd == 0) return;

        if (!IsWindow(_trackedHwnd))
        {
            HideOverlay();
            TargetLost?.Invoke();
            return;
        }

        UpdateBoundsFromTarget();
    }

    private bool UpdateBoundsFromTarget()
    {
        if (_trackedHwnd == 0 || !GetWindowRect(_trackedHwnd, out RECT r))
            return false;

        Bounds = new Rectangle(r.Left, r.Top, r.Width, r.Height);
        return true;
    }

    /// <summary>Shows the window without letting it steal foreground focus from whatever the user was doing.</summary>
    private void ShowNoActivate()
    {
        const int SW_SHOWNOACTIVATE = 4;
        if (!IsHandleCreated)
            _ = Handle; // force handle creation
        ShowWindowNative(Handle, SW_SHOWNOACTIVATE);
        Visible = true;
    }

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool ShowWindowNative(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(KeyColor);
        using var pen = new Pen(BorderColor, BorderThickness);
        var rect = new Rectangle(
            BorderThickness / 2, BorderThickness / 2,
            ClientSize.Width - BorderThickness, ClientSize.Height - BorderThickness);
        e.Graphics.DrawRectangle(pen, rect);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _trackingTimer.Dispose();
        base.Dispose(disposing);
    }
}
