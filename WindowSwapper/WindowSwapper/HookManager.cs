using static WindowSwapper.NativeMethods;

namespace WindowSwapper;

/// <summary>
/// Owns the global mouse hook and the foreground-window watcher, and drives the
/// two-click "select A, select B, swap" state machine.
///
/// Threading note: both SetWindowsHookEx(WH_MOUSE_LL, ...) and SetWinEventHook rely on a
/// live Win32 message loop pumping on the thread that installed them. This class assumes
/// it's constructed and driven from the UI thread (see Program.cs / TrayApplicationContext).
/// </summary>
internal sealed class HookManager : IDisposable
{
    // Keep the delegate fields alive for the process lifetime — if the GC collects them
    // while the hook is installed, Windows will call into freed memory and crash the app.
    private readonly LowLevelMouseProc _mouseProc;
    private readonly WinEventDelegate _winEventProc;

    private nint _mouseHookHandle;
    private nint _winEventHookHandle;

    private nint _pendingWindowA;

    /// <summary>True while the hotkey combo is allowed to act (i.e. no fullscreen app is focused).</summary>
    public bool IsActive { get; private set; } = true;

    public event Action<bool>? ActiveStateChanged;
    public event Action<nint, nint>? WindowsSwapped;

    /// <summary>Fired the instant window A is selected — payload is its HWND, for the highlight overlay.</summary>
    public event Action<nint>? SelectionStarted;

    /// <summary>Fired whenever a pending selection ends, whatever the reason (swap, cancel, suppression).</summary>
    public event Action? SelectionEnded;

    public HookManager()
    {
        // Store the delegates in fields so they aren't garbage-collected while native code holds
        // a pointer to them.
        _mouseProc = MouseHookCallback;
        _winEventProc = WinEventCallback;
    }

    public void Start()
    {
        nint hModule = GetModuleHandle(Environment.ProcessPath is null ? string.Empty : "");
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hModule, 0);
        if (_mouseHookHandle == 0)
            throw new InvalidOperationException("Failed to install low-level mouse hook.");

        _winEventHookHandle = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            0, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Establish initial state based on whatever's focused right now.
        RefreshActiveState();
    }

    private void WinEventCallback(nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND)
            RefreshActiveState();
    }

    private void RefreshActiveState()
    {
        bool wasActive = IsActive;
        IsActive = !IsForegroundWindowExclusiveFullscreen();

        // If we got suppressed mid-selection, drop whatever was pending — don't let a stale
        // "window A" from before a game launch silently complete a swap ten minutes later.
        if (!IsActive && _pendingWindowA != 0)
        {
            _pendingWindowA = 0;
            SelectionEnded?.Invoke();
        }

        if (IsActive != wasActive)
            ActiveStateChanged?.Invoke(IsActive);
    }

    /// <summary>
    /// Heuristic used by most overlay tools (Discord, RTSS, etc): a window is "exclusive
    /// fullscreen" if it exactly covers its monitor and has no caption/border. Borderless
    /// windowed games can evade this — that's a known limitation of the technique itself,
    /// not something fixable from user-mode without a lot more complexity.
    /// </summary>
    private static bool IsForegroundWindowExclusiveFullscreen()
    {
        nint fg = GetForegroundWindow();
        if (fg == 0) return false;

        if (!GetWindowRect(fg, out RECT windowRect))
            return false;

        nint monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi))
            return false;

        bool coversMonitor =
            windowRect.Left <= mi.rcMonitor.Left &&
            windowRect.Top <= mi.rcMonitor.Top &&
            windowRect.Right >= mi.rcMonitor.Right &&
            windowRect.Bottom >= mi.rcMonitor.Bottom;

        int style = GetWindowLong(fg, GWL_STYLE);
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;

        return coversMonitor && !hasCaption;
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && IsActive && wParam == WM_LBUTTONDOWN)
        {
            bool ctrlHeld = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if (ctrlHeld)
            {
                HandleCtrlClick();
                return 1; // swallow the click — nothing under the cursor sees it
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void HandleCtrlClick()
    {
        var cursor = GetCursorScreenPoint();
        nint clicked = GetTopLevelWindowAt(cursor);
        if (clicked == 0)
            return;

        if (_pendingWindowA == 0)
        {
            _pendingWindowA = clicked;
            SelectionStarted?.Invoke(clicked);
            return;
        }

        if (clicked == _pendingWindowA)
        {
            // Clicked the same window twice — treat as "cancel selection" rather than a no-op swap.
            _pendingWindowA = 0;
            SelectionEnded?.Invoke();
            return;
        }

        SwapWindows(_pendingWindowA, clicked);
        WindowsSwapped?.Invoke(_pendingWindowA, clicked);
        _pendingWindowA = 0;
        SelectionEnded?.Invoke();
    }

    /// <summary>Lets the UI layer (e.g. the highlight overlay) cancel a stale selection —
    /// used when the tracked window closes mid-selection.</summary>
    public void CancelPendingSelection() => _pendingWindowA = 0;

    private static void SwapWindows(nint a, nint b)
    {
        if (!GetWindowRect(a, out RECT rectA)) return;
        if (!GetWindowRect(b, out RECT rectB)) return;

        // Move+resize in one call each (SWP_NOZORDER/SWP_NOACTIVATE keep this purely
        // positional — we don't want the swap itself to steal focus or reorder Z-order).
        SetWindowPos(a, 0, rectB.Left, rectB.Top, rectB.Width, rectB.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowPos(b, 0, rectA.Left, rectA.Top, rectA.Width, rectA.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static POINT GetCursorScreenPoint()
    {
        // Cursor.Position pulls from System.Windows.Forms, which already wraps GetCursorPos —
        // reusing it here avoids one more P/Invoke declaration.
        var p = System.Windows.Forms.Cursor.Position;
        return new POINT { X = p.X, Y = p.Y };
    }

    private static nint GetTopLevelWindowAt(POINT screenPoint)
    {
        nint hwnd = WindowFromPoint(screenPoint);
        return hwnd == 0 ? 0 : GetAncestor(hwnd, GA_ROOT);
    }

    public void Dispose()
    {
        if (_mouseHookHandle != 0)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = 0;
        }

        if (_winEventHookHandle != 0)
        {
            UnhookWinEvent(_winEventHookHandle);
            _winEventHookHandle = 0;
        }
    }
}
