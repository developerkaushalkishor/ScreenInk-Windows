# ScreenInk for Windows — end-to-end port plan

Prepared 2026-09-19 from a code-level review of the macOS ScreenInk 0.15.2 project.

## Product goal

Deliver an offline Windows 10/11 screen-annotation utility with feature behavior matching ScreenInk for macOS: per-monitor drawing, reliable click-through, fast exit, presentation effects, shapes, text, boards, screenshots, selection/transforms, master disable and a tray recovery path.

## Technology

- **.NET 8 + WPF:** mature transparent windows, custom rendering, input and desktop packaging.
- **Win32 interop:** `WS_EX_TRANSPARENT` click-through, `RegisterHotKey`, monitor and optional low-level mouse hooks.
- **System.Drawing capture initially:** user-triggered full-monitor capture. Replace with Windows Graphics Capture if protected-window behavior or HDR correctness requires it.
- **JSON settings:** local offline preferences in `%LOCALAPPDATA%`.
- **No runtime dependencies for users:** publish self-contained x64 and ARM64 binaries.

WinUI 3 was not selected because transparent always-on-top overlay windows and click-through behavior still require substantial Win32 interop. Avalonia would improve cross-platform reuse but adds a dependency without removing the Windows-specific overlay work.

## macOS-to-Windows architecture mapping

| macOS component | Windows component |
| --- | --- |
| `InkCore` Swift structs | `ScreenInk.Core` C# records/services |
| `NSPanel` per display | Borderless transparent WPF `OverlayWindow` per `Screen` |
| `ignoresMouseEvents` | `WS_EX_TRANSPARENT` extended window style |
| AppKit toolbar panel | Topmost WPF `ToolbarWindow` |
| Menu-bar status item | WinForms `NotifyIcon` system tray menu |
| Carbon global hot key | Win32 `RegisterHotKey` |
| Core Graphics rendering | WPF `DrawingContext`/`StreamGeometry` |
| ScreenCaptureKit | GDI capture first; Windows Graphics Capture follow-up |
| `UserDefaults` | `%LOCALAPPDATA%` JSON settings |

## Milestones and gates

| Milestone | Scope | Acceptance gate | Status |
| --- | --- | --- | --- |
| W0 — Repository/tooling | Solution, Core/App/Test projects, CI, scripts, MIT docs | Windows CI restores, tests and builds | Cross-build and core tests pass; Windows CI run pending |
| W1 — Overlay MVP | Per-monitor overlays, click-through, pen, colors, widths, undo/redo/clear | Draw on every monitor; normal mode never traps input | Implemented in source; hardware check pending |
| W2 — Essential drawing | Highlighter, whole-stroke eraser, settings, 24-color palette | Undo restores one eraser gesture; restart restores preferences | Implemented in source; hardware check pending |
| W3 — Presentation | Fade, laser, halo, click animation, global toggle, ink visibility | Smooth effects at 60 FPS; no input theft | Fade/laser/hotkey/visibility implemented; halo/click pending |
| W4 — Shapes | Line, arrow, rounded rectangle, ellipse, diamond, recognition | Correct every drag direction and mixed DPI | Implemented in source; polish pending |
| W5 — Text and transforms | Inline editor, fonts/alignment, single/multi selection, move/resize/recolor | Exact hit-testing; each gesture is one Undo operation | Text/single move implemented; inline/multi/resize pending |
| W6 — Boards | White/black board, current/all/region scope, contained drawing | Board frame and contents transform together | Full-monitor board implemented; scopes/containment pending |
| W7 — Screenshots | Full/region, clipboard/PNG, toolbar exclusion | Pixel-correct at mixed DPI and negative origins | Full-monitor PNG implemented; remainder pending |
| W8 — Reliability | Display hot-plug, virtual desktops, fullscreen, pen input, long session | Complete manual matrix on Windows 10 and 11 | Pending |
| W9 — Distribution | Icon, Authenticode, self-contained ZIP/MSIX option, release CI | Clean-PC install, SmartScreen and upgrade checks | Packaging scaffold implemented; signing pending |

## Feature parity backlog

1. Replace modal text dialog with calibrated inline editor.
2. Add marquee multi-selection, corner/endpoint resize handles and recoloring.
3. Add board scopes, custom regions and contained annotation transforms.
4. Add cursor halo and global left-click ripple with an opt-in low-level hook.
5. Add 24-color/font/alignment popovers and tool-specific cursor artwork.
6. Add region selection, clipboard output and Windows Graphics Capture evaluation.
7. Validate toolbar fly-in/fade, saved physical placement, More/palette interactions and exact per-monitor edge trigger on Windows hardware.
8. Add pressure/stylus sampling and performance profiling for long sessions.
9. Add an original Windows `.ico`, Authenticode signing and release workflow.

## Windows hardware verification matrix

- Windows 10 22H2 and current Windows 11.
- x64 and ARM64 where available.
- One monitor, two monitors, negative virtual origins and monitors above/below primary.
- 100%, 125%, 150% and mixed DPI; resolution change and hot-plug.
- Fullscreen PowerPoint, browser video, Teams/Zoom screen share and virtual desktops.
- Mouse, trackpad, touch and pen where available.
- Sleep/wake, lock/unlock, Explorer restart and display-driver reset.
- 30-minute drawing session with memory/CPU tracking.

## Definition of done

The Windows port reaches parity only when every milestone gate is observed on Windows hardware, no overlay can trap normal input, the tray can always recover or quit the app, mixed-DPI coordinates are correct, all core tests and Windows CI pass, and a signed clean-PC artifact installs and updates successfully.
