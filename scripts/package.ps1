param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [ValidateSet("All", "Small", "Offline")]
    [string]$Package = "All"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/ScreenInk.App/ScreenInk.App.csproj"
[xml]$projectXml = Get-Content $project
$version = $projectXml.Project.PropertyGroup.Version

function New-ScreenInkPackage {
    param(
        [string]$Name,
        [bool]$SelfContained
    )

    $output = Join-Path $root "artifacts/$Name"
    $zip = Join-Path $root "artifacts/$Name.zip"
    Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Remove-Item "$zip.sha256" -Force -ErrorAction SilentlyContinue

    $selfContainedValue = $SelfContained.ToString().ToLowerInvariant()
    dotnet publish $project -c Release -r $Runtime `
        --self-contained $selfContainedValue `
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    Compress-Archive -Path "$output/*" -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)"
    Write-Host "Created $zip"
}

if ($Package -in @("All", "Small")) {
    New-ScreenInkPackage -Name "ScreenInk-$version-$Runtime" -SelfContained $false
}

if ($Package -in @("All", "Offline")) {
    New-ScreenInkPackage -Name "ScreenInk-$version-$Runtime-offline" -SelfContained $true
}
