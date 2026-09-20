param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/ScreenInk-$Runtime"
$zip = Join-Path $root "artifacts/ScreenInk-0.2.0-$Runtime.zip"
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
dotnet publish "$root/src/ScreenInk.App/ScreenInk.App.csproj" -c Release -r $Runtime `
    --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $output
Compress-Archive -Path "$output/*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)"
Write-Host "Created $zip"
