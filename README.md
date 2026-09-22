<p align="center">
  <img src="docs/assets/screenink-windows-icon.png" width="150" height="150" alt="ScreenInk for Windows icon">
</p>

# ScreenInk for Windows

ScreenInk for Windows is the native Windows port of the open-source macOS screen annotation tool. It uses .NET 8, WPF and focused Win32 interop. The app is offline, has no account or analytics service, and targets Windows 10 version 2004 or newer plus Windows 11.

<p align="center">
  <a href="https://github.com/developerkaushalkishor/ScreenInk-Windows/releases/download/v0.3.0-preview.1/ScreenInk-0.3.0-preview.1-win-x64.zip"><strong>Download for Windows (x64, small)</strong></a>
  ·
  <a href="https://github.com/developerkaushalkishor/ScreenInk-Windows/releases/download/v0.3.0-preview.1/ScreenInk-0.3.0-preview.1-win-x64-offline.zip">Download offline package</a>
  ·
  <a href="https://github.com/developerkaushalkishor/mac-tools/releases/download/v0.15.3/ScreenInk-0.15.3-macOS-arm64.zip">Download for macOS (Apple Silicon)</a>
</p>

> **Status: 0.3.0-preview.1 developer preview.** This preview repairs blank-canvas mouse input and restores the Mac-style toolbar. Windows CI exercises native mouse input, shape rendering, history, selection, boards, text and toolbar interactions. Mixed-DPI monitors and real Windows 10/11 hardware still need manual acceptance testing.

## Windows preview UI

![ScreenInk toolbar rendered on Windows](docs/assets/toolbar-windows-wide.png)

![Compact toolbar on a narrow display](docs/assets/toolbar-windows-narrow.png)

The toolbar follows the macOS tool order, monochrome vector icons, purple-first colors, selected-tool feedback and rounded floating panel. Controls that do not fit remain available in **More**. Windows system handwriting fonts are used in place of macOS-only fonts.

## Available tools

- Independent transparent topmost overlay on every connected monitor.
- Click-through normal mode and interactive drawing mode.
- Pen, translucent highlighter, whole-stroke eraser and temporary laser.
- Line, arrow, rectangle, ellipse and diamond with Shift constraints.
- Basic closed ellipse/rectangle recognition for Pen gestures.
- Inline text editing at the clicked location, handwriting/common font choices, size and alignment controls. Choose a font to activate Text; press Enter to save, Shift+Enter for a new line, or Escape to cancel.
- Single and marquee multi-selection, Shift-click selection, corner resize handles, selection recoloring and one Undo step per drag.
- Board frame selection moves/resizes contained annotations together.
- Undo, redo, recoverable clear and bounded history.
- Fading ink (off by default), continuous temporary laser trails, cursor halo and click ripples.
- Tool-specific cursors, including an I-beam for Text.
- Framed whiteboards and blackboards on the current display, all displays or a dragged region. Ink started inside a board stays within it.
- Display/region screenshots to PNG or clipboard with the toolbar excluded.
- Mac-style vector icon toolbar with press feedback, smooth fly-in/fade and stable one-shot top-edge reveal.
- Responsive quick colors plus Shapes, 24-color Palette and More popovers for narrow displays.
- Saved physical-pixel toolbar placement with per-monitor DPI scaling and display-disconnect recovery.
- Per-monitor overlay reconciliation for connect/disconnect.
- Tray icon recovery, master enable/disable and global `Ctrl+Alt+Shift+D` drawing toggle.
- Saved settings under `%LOCALAPPDATA%\ScreenInk\settings.json`.
- Self-contained x64/ARM64 packaging scripts and Windows CI.

## Build on Windows

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run PowerShell from the repository root:

```powershell
./scripts/test.ps1
./scripts/build.ps1
dotnet run --project ./src/ScreenInk.App/ScreenInk.App.csproj
```

Create both the small and offline ZIP packages:

```powershell
./scripts/package.ps1 -Runtime win-x64
```

The output is written under `artifacts/`. The current artifact is unsigned; public distribution needs Authenticode signing and SmartScreen/reputation testing.

## Try the portable build without development tools

1. Download the [small Windows x64 ZIP](https://github.com/developerkaushalkishor/ScreenInk-Windows/releases/download/v0.3.0-preview.1/ScreenInk-0.3.0-preview.1-win-x64.zip). It requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
2. If you need a package that works without installing .NET, download the larger [offline Windows x64 ZIP](https://github.com/developerkaushalkishor/ScreenInk-Windows/releases/download/v0.3.0-preview.1/ScreenInk-0.3.0-preview.1-win-x64-offline.zip).
3. Verify the ZIP's SHA-256 value against its accompanying `.sha256` file.
4. Extract the ZIP completely and launch `ScreenInk.exe`.
5. Use the tray icon to enable/disable ScreenInk or recover the toolbar.
6. Use `Ctrl+Alt+Shift+D` to toggle drawing mode.

Exit any older ScreenInk instance from its tray menu before launching this preview.

The small package is the recommended download on slower or unstable connections. The offline package bundles the .NET desktop runtime and is therefore much larger.

The preview is unsigned, so Windows SmartScreen can show an unknown-publisher warning. A public release should be Authenticode-signed before asking non-developers to install it.

For macOS, use the [ScreenInk macOS repository](https://github.com/developerkaushalkishor/mac-tools).

## Project layout

| Path | Responsibility |
| --- | --- |
| `src/ScreenInk.Core` | Platform-neutral strokes, history, geometry and recognition |
| `src/ScreenInk.App` | WPF overlays, toolbar, tray, input and Windows services |
| `tests/ScreenInk.CoreTests` | Dependency-free executable core regression suite |
| `scripts` | Test, build and self-contained package commands |
| `docs/PLAN.md` | Port decisions, parity matrix and acceptance gates |

## Verification and remaining acceptance checks

- Ten platform-neutral regression tests cover history, fade cleanup, geometry, toolbar reveal and transforms.
- `tests/ScreenInk.WindowsTests` runs on a Windows runner and uses real native mouse input. It covers blank-canvas drawing, click-through recovery, shapes, erasing, history, board containment and movement, inline text focus/commit, toolbar overflow and animation cancellation.
- Run the Windows suite with `dotnet run --project tests/ScreenInk.WindowsTests/ScreenInk.WindowsTests.csproj -c Release` from an interactive Windows desktop. It temporarily moves the pointer and creates test windows.
- Physical mixed-DPI/negative-origin monitors, stylus pressure, fullscreen applications, sleep/wake, long sessions and screenshot output on real user hardware still need acceptance checks. Passing CI is not a claim of complete macOS parity.
- The preview is unsigned. Distribution signing and installer work remain separate from the drawing repair.

See [Windows repair verification](docs/WINDOWS-REPAIR.md) for the causes, regression coverage and remaining checks.

## License

MIT. ScreenInk is independent and is not affiliated with Epic Pen or Presentify.
