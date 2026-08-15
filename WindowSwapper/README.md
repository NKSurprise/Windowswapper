# WindowSwapper

A tiny Windows tray utility that lets you swap the position of two open windows instead of
dragging them — especially useful across multiple monitors, where dragging means fighting DPI
scaling and screen edges.

**Hold Ctrl and left-click a window, then Ctrl-click a second window** — they instantly swap
position and size. The click is swallowed at the OS level, so nothing inside either window
gets activated by the selection clicks. The hotkey automatically suspends while a fullscreen
app (e.g. a game) is focused, and re-activates the moment you tab out.

## Status

Early skeleton — core hook, swap logic, and fullscreen suppression are implemented and should
run, but it hasn't been exercised across real multi-monitor DPI setups yet. Treat this as a
working prototype, not a finished tool.

## How it works

- **Selection & swap**: a low-level global mouse hook (`WH_MOUSE_LL`) intercepts left-clicks,
  checks whether Ctrl is held, and if so swallows the click and records the window under the
  cursor. The second Ctrl-click swaps both windows' position and size via `SetWindowPos`.
- **Fullscreen suppression**: on every foreground-window change (`SetWinEventHook` /
  `EVENT_SYSTEM_FOREGROUND`), the app checks whether the newly focused window exactly covers
  its monitor with no caption/border — the same heuristic overlay tools like Discord use to
  detect exclusive fullscreen. If so, the hotkey is suspended until focus moves elsewhere.
- **Idle cost**: both hooks are event-driven, not polled, so there's no background loop
  burning CPU — the fullscreen check only runs once per foreground-window change.
- **Selection highlight**: the first Ctrl-click shows an instant colored frame around the
  selected window (`HighlightOverlay.cs`) — a layered, click-through, non-activating window
  using the `TransparencyKey` trick so only the border is visible, not a solid rectangle. It
  polls the target's position every 150ms to follow it if moved, and auto-cancels the pending
  selection if the tracked window is closed before the second click.

## Not done yet

- Custom tray icons for active/suspended state (using stock system icons for now)
- Startup registration (Run key or Task Scheduler) — planned, not wired up
- Real-world testing on mixed-DPI multi-monitor setups
- Handling of UWP/elevated windows (an elevated game/app will silently ignore hooks from a
  non-elevated WindowSwapper process — running WindowSwapper elevated fixes this but has its
  own UAC trade-offs)

## Building

Requires Visual Studio 2022+ (or `dotnet` CLI) with the .NET 8 SDK and the
".NET desktop development" workload. Open `WindowSwapper.sln` and run — it's a WinForms app
that starts in the system tray with no visible window.

## License

TBD
