# Windows input and UI repair

## Confirmed defects

- A fully transparent layered canvas let native mouse input pass to the application below, leaving no way to begin a pen stroke on blank space. Drawing mode now paints a nonzero-alpha input plane; normal mode explicitly restores `WS_EX_TRANSPARENT`.
- Undo, redo and clear changed the document without requesting a redraw. Document mutations now notify the view.
- Physical monitor pixels were assigned directly to WPF logical window coordinates. Overlays now use native physical placement and refresh bounds when display geometry changes.
- Shape hit tests checked a diagonal between control points. Rendering and hit testing now share the actual shape geometry.
- Each selection mouse move created an Undo checkpoint. Selection now previews the gesture and commits it once.
- Shape completion appended a third point, losing Shift-constrained endpoints. Shapes retain their two constrained endpoints.
- Laser cleanup could leave its final rendered segment until another repaint. Removing the last sample now invalidates the canvas.
- Each icon stretched its own path bounds to a square. Icons now use a shared 24-unit coordinate box.
- A completed hide animation could hide a newly revealed toolbar. A reveal generation invalidates old completions.

## Implemented behavior

The repair adds visible selected-tool and toggle states, press animation, labeled More tools, overflow on narrow displays, custom tool cursors, inline text, selection resizing/recoloring, board scopes/containment and grouped board movement, cursor effects, and region/clipboard screenshot controls. Windows-native fonts substitute for system fonts exclusive to macOS.

## Automated verification

The core suite covers document notifications and transform ownership as well as the existing geometry/history behavior. The Windows suite renders WPF controls and uses native mouse events to draw and manipulate annotations. Windows CI uploads toolbar and tool screenshots as `ScreenInk-ui-evidence`.

The first repair test caught a toolbar logical-parent exception that a cross-build could not detect; it was fixed before publishing the candidate. The suite also checks that activating the drawing canvas leaves the toolbar clickable above it.

## Manual acceptance still required

- Test the packaged executable on the user's Windows machine, including Pen, Eraser, Undo, right-click exit and toolbar recovery.
- Use two monitors with different scale factors, reconnect a monitor and change resolution.
- Check text alignment and editing with multiple installed handwriting fonts.
- Verify PNG and clipboard region screenshots with boards, fullscreen apps and negative monitor origins.
- Check halo/laser animation smoothness on high-refresh displays and long annotation sessions.
- Confirm board element resize/recolor and multi-selection behavior on dense teaching material.

Reference: [Microsoft layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

## Extended-display update (0.3.0-preview.2)

The user reported that drawing worked only on the active monitor in Windows Extend mode. The existing single-monitor tests did not reproduce that physical setup.

Changes:
- Enumerate current native monitor rectangles and IDs directly instead of retaining WinForms screen snapshots.
- Reconcile each canvas against its actual HWND rectangle even when the display descriptor has not changed.
- Reapply placement after WPF load/render, DPI, size and activation changes; preserve negative desktop origins.
- React to display-settings notifications, with a periodic reconciliation fallback.
- Route Undo/Redo/Clear to the last drawing display; board/screenshot scope remains the toolbar display.
- New displays inherit the current global drawing/normal mode. Removed displays release their canvas without clearing other displays.
- Add a tray command to copy display/frame/DPI diagnostics if a hardware-specific failure persists.

Verification uses real Windows windows/mouse input on a runner with synthetic display partitions. It exercises simultaneous canvases, switching without reselecting a tool, geometry drift, negative origins, history isolation and reconnection. Physical extended monitors with different DPI settings remain a manual acceptance check; the exact reported hardware failure has not been reproduced on the runner.
