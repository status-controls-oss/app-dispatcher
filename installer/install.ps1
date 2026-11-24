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

# Lists the files in a release. Only these are copied on install and deleted on uninstall, so
# running this from a folder such as Downloads never copies or deletes anything else there.
$ManifestName = 'manifest.txt'

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
    (Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Directory -Filter '8.*').Count -gt 0
}

function Read-Manifest([string]$folder) {
    $path = Join-Path $folder $ManifestName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $names = @(Get-Content -LiteralPath $path | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    # Names only: a path in the manifest could reach outside the folder.
    foreach ($name in $names) {
        if ($name -ne [IO.Path]::GetFileName($name) -or $name -in '.', '..') { Stop-WithMessage "$path lists an unexpected file: $name" }
    }
    $names
}

function Remove-App {
    $picker = Join-Path $InstallDir 'AppDispatcher.exe'
    if (-not (Test-Path -LiteralPath $picker -PathType Leaf)) { Stop-WithMessage "App Dispatcher isn't installed in $InstallDir." }
    $files = Read-Manifest $InstallDir
    if (-not $files) { Stop-WithMessage "$InstallDir has no $ManifestName, so the files to remove aren't known. Install the latest version first, then uninstall." }
    Assert-NotRunning

    $settings = Join-Path $InstallDir 'AppDispatcher.Settings.exe'
    if (-not (Test-DesktopRuntime)) {
        Stop-WithMessage 'Removing App Dispatcher from Windows needs the .NET 8 Desktop Runtime, which is missing. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and try again.'
    }
    # Settings writes the file types and links that Windows was sending to it, with the app each used before.
    $restoreFile = [IO.Path]::GetTempFileName()
    try {
        $cleanup = Start-Process $settings -ArgumentList '--unregister' -Wait -PassThru -RedirectStandardOutput $restoreFile
        $restore = @(Get-Content -LiteralPath $restoreFile -Encoding UTF8 | Where-Object { $_ })
    }
    finally { Remove-Item -LiteralPath $restoreFile -Force -ErrorAction SilentlyContinue }
    if ($cleanup.ExitCode -ne 0) { Stop-WithMessage 'Could not remove App Dispatcher from Windows, so nothing was deleted.' }

    # Running from inside the folder would stop it being deleted.
    Set-Location $env:TEMP
    Remove-Files $InstallDir $files
    Write-Host 'App Dispatcher has been removed.'

    if ($RemoveSettings) {
        Remove-Files $SettingsDir 'config.json', 'config.json.tmp', 'config.json.bad'
        Write-Host 'Your settings have been deleted.'
    }
    else {
        Write-Host "Your settings were kept in $SettingsDir."
    }
    Show-RestoreList $restore
}

# Windows doesn't let apps choose defaults, so this lists what to choose again by hand.
function Show-RestoreList([string[]]$lines) {
    if (-not $lines) { return }
    Write-Host ''
    Write-Host 'Windows was sending these to App Dispatcher:'
    foreach ($line in $lines) { Write-Host "  $line" }
    Write-Host 'Windows will ask which app to use the next time you open one, or you can choose them again in Windows Settings > Default apps.'
    if ($Pause -and (Read-Host 'Open Default apps now? [Y/n]') -notmatch '^\s*n') { Start-Process 'ms-settings:defaultapps' }
}

# Deletes the named files, then the folder only if nothing else is left in it.
function Remove-Files([string]$folder, [string[]]$names) {
    foreach ($name in $names) {
        $path = Join-Path $folder $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    if ((Test-Path -LiteralPath $folder -PathType Container) -and -not (Get-ChildItem -LiteralPath $folder -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $folder -Force
    }
}

function Install-App {
    $source = $PSScriptRoot
    $files = Read-Manifest $source
    if (-not $files) {
        # GitHub offers the source code beside each release, and it contains this script too.
        if (Test-Path -LiteralPath (Join-Path $source '..\src') -PathType Container) {
            Stop-WithMessage "This is App Dispatcher's source code, not the release. Download AppDispatcher-<version>-win-x64.zip from https://github.com/status-controls-oss/app-dispatcher/releases rather than 'Source code', extract it, and run Install.cmd from there."
        }
        Stop-WithMessage "Run this from the extracted release folder. $ManifestName isn't in $source."
    }
    $missing = @($files | Where-Object { -not (Test-Path -LiteralPath (Join-Path $source $_) -PathType Leaf) })
    if ($missing) { Stop-WithMessage "The release folder is incomplete. Extract the whole zip again. Missing: $($missing -join ', ')" }
    Assert-NotRunning

    # Files from an earlier version that this one no longer ships.
    $previous = Read-Manifest $InstallDir
    if ($previous) { Remove-Files $InstallDir @($previous | Where-Object { $_ -notin $files }) }

    [void](New-Item -ItemType Directory -Force -Path $InstallDir)
    foreach ($name in $files) {
        $target = Join-Path $InstallDir $name
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target -Force
        # Clears the "downloaded from the internet" mark, which otherwise triggers a warning on each launch.
        Unblock-File -LiteralPath $target
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
