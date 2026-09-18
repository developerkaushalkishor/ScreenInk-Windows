# Contributing to ScreenInk for Windows

Thank you for helping improve ScreenInk. Keep changes focused, offline-first and accessible on Windows 10 and Windows 11.

## Development setup

1. Install Visual Studio 2022 with **.NET desktop development**, or install the .NET 8 SDK.
2. Clone the repository and run `./scripts/test.ps1`.
3. Run `./scripts/build.ps1`.
4. Launch `src/ScreenInk.App/ScreenInk.App.csproj` from Visual Studio or with `dotnet run`.

## Pull requests

- Explain the user-visible behavior and the Windows versions/display layouts tested.
- Add focused core tests for geometry, history or recognition changes.
- For overlay changes, test normal-mode click-through and tray recovery.
- Do not add analytics, network calls or bundled fonts/assets without a clear license.
- Keep UI and documentation accessible to keyboard and screen-reader users where practical.

See `docs/PLAN.md` for the parity backlog and hardware verification matrix.
