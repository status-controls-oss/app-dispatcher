# Installs or removes App Dispatcher for the current user. Needs no admin rights.
#
#   .\install.ps1                            Install or update, then open Settings
#   .\install.ps1 -Uninstall                 Remove the app, keeping your settings
#   .\install.ps1 -Uninstall -RemoveSettings Also delete your settings
param(
    [switch]$Uninstall,
    [switch]$RemoveSettings,
    [switch]$NoLaunch,
    [switch]$Pause,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\AppDispatcher'),
    [string]$SettingsDir = (Join-Path $env:APPDATA 'AppDispatcher')
)
$ErrorActionPreference = 'Stop'

function Finish([int]$code) {
    if ($Pause) { [void](Read-Host 'Press Enter to close') }
    exit $code
}

function Stop-WithMessage([string]$message) {
    Write-Host $message -ForegroundColor Yellow
    Finish 1
}

function Assert-NotRunning {
    $prefix = $InstallDir.TrimEnd('\') + '\'
    $running = @(Get-Process AppDispatcher, AppDispatcher.Settings -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0) {
        Stop-WithMessage 'App Dispatcher is running. Close Settings and any open picker, then try again.'
    }
}

function Test-DesktopRuntime {
    $root = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    (Test-Path $root) -and @(Get-ChildItem $root -Directory -Filter '8.*').Count -gt 0
}

function Remove-App {
    $picker = Join-Path $InstallDir 'AppDispatcher.exe'
    if (-not (Test-Path $picker)) { Stop-WithMessage "App Dispatcher isn't installed in $InstallDir." }
    Assert-NotRunning

    $settings = Join-Path $InstallDir 'AppDispatcher.Settings.exe'
    if (-not (Test-DesktopRuntime)) {
        Stop-WithMessage 'Removing App Dispatcher from Windows needs the .NET 8 Desktop Runtime, which is missing. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and try again.'
    }
    $cleanup = Start-Process $settings -ArgumentList '--unregister' -Wait -PassThru
    if ($cleanup.ExitCode -ne 0) { Stop-WithMessage 'Could not remove App Dispatcher from Windows, so nothing was deleted.' }

    # Running from inside the folder would stop it being deleted.
    Set-Location $env:TEMP
    Remove-Item $InstallDir -Recurse -Force
    Write-Host 'App Dispatcher has been removed.'

    if ($RemoveSettings) {
        if (Test-Path $SettingsDir) { Remove-Item $SettingsDir -Recurse -Force }
        Write-Host 'Your settings have been deleted.'
    }
    else {
        Write-Host "Your settings were kept in $SettingsDir."
    }
    Write-Host 'File types and links that used it now need a new default app, in Windows Settings > Default apps.'
}

function Install-App {
    $source = $PSScriptRoot
    if (-not (Test-Path (Join-Path $source 'AppDispatcher.exe'))) {
        Stop-WithMessage "Run this from the extracted release folder. AppDispatcher.exe isn't in $source."
    }
    Assert-NotRunning

    [void](New-Item -ItemType Directory -Force $InstallDir)
    foreach ($file in Get-ChildItem $source -File) {
        $target = Join-Path $InstallDir $file.Name
        Copy-Item $file.FullName $target -Force
        # Clears the "downloaded from the internet" mark, which otherwise triggers a warning on each launch.
        Unblock-File $target
    }
    Write-Host "Installed to $InstallDir."

    if (-not (Test-DesktopRuntime)) {
        Stop-WithMessage 'Settings needs the .NET 8 Desktop Runtime, which is missing. Install it from https://dotnet.microsoft.com/download/dotnet/8.0, then run AppDispatcher.exe from the folder above.'
    }
    if (-not $NoLaunch) { Start-Process (Join-Path $InstallDir 'AppDispatcher.Settings.exe') }
    Write-Host 'To finish, click Register in Settings, add your apps, then choose App Dispatcher in Windows Default Apps. The banners guide you.'
}

if ($Uninstall) { Remove-App } else { Install-App }
Finish 0
