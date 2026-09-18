$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root/tests/ScreenInk.CoreTests/ScreenInk.CoreTests.csproj" -c Release
