param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$Test,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $installation = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($installation) { $msbuild = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe" }
}
if (!$msbuild -or !(Test-Path $msbuild)) {
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (!(Test-Path $msbuild)) { throw "MSBuild not found. Install Visual Studio 2022 Build Tools with the .NET Framework 4.7.2 targeting pack." }

& $msbuild (Join-Path $root "MeetingBar.Windows.csproj") /t:Rebuild /p:Configuration=$Configuration /m
if ($LASTEXITCODE -ne 0) { throw "MeetingBar build failed." }

$dotnet = $env:MEETINGBAR_DOTNET
if (!$dotnet) {
    $sdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($sdkCommand -and (& $sdkCommand.Source --list-sdks)) { $dotnet = $sdkCommand.Source }
}
if (!$dotnet -and (Test-Path "E:\codex-tools\dotnet8\dotnet.exe")) { $dotnet = "E:\codex-tools\dotnet8\dotnet.exe" }
if (!$dotnet -or !(Test-Path $dotnet)) { throw ".NET 8 SDK not found. Set MEETINGBAR_DOTNET to dotnet.exe." }
$env:NUGET_PACKAGES = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:LOCALAPPDATA "NuGet\Packages" }
$settingsPublish = Join-Path $root "SettingsApp\bin\$Configuration\publish-folder"
& $dotnet publish (Join-Path $root "SettingsApp\MeetingBar.Settings.csproj") -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:PublishTrimmed=false -o $settingsPublish
if ($LASTEXITCODE -ne 0) { throw "MeetingBar WinUI settings build failed." }
$settingsOutput = Join-Path $root "bin\$Configuration\Settings"
New-Item -ItemType Directory -Force $settingsOutput | Out-Null
Copy-Item (Join-Path $settingsPublish "*") $settingsOutput -Recurse -Force

$widgetPublish = Join-Path $root "WidgetProvider\bin\$Configuration\publish-folder"
& $dotnet publish (Join-Path $root "WidgetProvider\MeetingBar.WidgetProvider.csproj") -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:PublishTrimmed=false -o $widgetPublish
if ($LASTEXITCODE -ne 0) { throw "MeetingBar Win+W widget provider build failed." }
Copy-Item (Join-Path $root "WidgetProvider\Package.appxmanifest") (Join-Path $widgetPublish "AppxManifest.xml") -Force
$widgetOutput = Join-Path $root "bin\$Configuration\WinWWidget"
New-Item -ItemType Directory -Force $widgetOutput | Out-Null
Copy-Item (Join-Path $widgetPublish "*") $widgetOutput -Recurse -Force

if ($Test) {
    & $msbuild (Join-Path $root "Tests\MeetingBar.Tests.csproj") /t:Rebuild /p:Configuration=$Configuration /m
    if ($LASTEXITCODE -ne 0) { throw "MeetingBar test build failed." }
    & (Join-Path $root "Tests\bin\$Configuration\MeetingBar.Tests.exe")
    if ($LASTEXITCODE -ne 0) { throw "MeetingBar tests failed." }
    & $dotnet run --project (Join-Path $root "Tests\SettingsTests\SettingsTests.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "MeetingBar Settings tests failed." }
}

if ($Package) {
    $dist = Join-Path $root "dist"
    New-Item -ItemType Directory -Force $dist | Out-Null
    $packageInputs = @(
        (Join-Path $root "bin\$Configuration\MeetingBar.exe")
        (Join-Path $root "bin\$Configuration\Settings")
        (Join-Path $root "bin\$Configuration\WinWWidget")
        (Join-Path $root "raycast")
        (Join-Path $root "docs")
        (Join-Path $root "README.md")
        (Join-Path $root "THIRD-PARTY-NOTICES.md")
        (Join-Path $root "LICENSE")
    )
    Compress-Archive -Path $packageInputs -DestinationPath (Join-Path $dist "MeetingBar-Windows-portable.zip") -Force
    Write-Host "Package: $dist\MeetingBar-Windows-portable.zip"
}
