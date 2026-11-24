# Builds the release zip: artifacts\AppDispatcher-<version>-win-x64.zip
# The version comes from Directory.Build.props.
$ErrorActionPreference = 'Stop'

& "$PSScriptRoot\publish.ps1"
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$version = ([xml](Get-Content "$PSScriptRoot\Directory.Build.props")).Project.PropertyGroup.Version
$name = "AppDispatcher-$version-win-x64"
$stage = Join-Path $PSScriptRoot "artifacts\release\$name"
$zip = Join-Path $PSScriptRoot "artifacts\$name.zip"

# Emptied rather than deleted, as a console left open in it after a test install locks the folder.
[void](New-Item -ItemType Directory -Force $stage)
Get-ChildItem -LiteralPath $stage -Force | Remove-Item -Recurse -Force

# Debug symbols are large and only useful for development.
Get-ChildItem "$PSScriptRoot\artifacts\AppDispatcher" -File |
    Where-Object Extension -ne '.pdb' |
    Copy-Item -Destination $stage
Copy-Item "$PSScriptRoot\installer\*" $stage
Copy-Item "$PSScriptRoot\README.md", "$PSScriptRoot\LICENSE" $stage

# The installer copies, and later deletes, only the files listed here.
$manifest = Join-Path $stage 'manifest.txt'
@((Get-ChildItem $stage -File).Name) + 'manifest.txt' | Sort-Object | Set-Content $manifest -Encoding utf8

foreach ($required in 'AppDispatcher.exe', 'AppDispatcher.Settings.exe', 'install.ps1', 'Install.cmd', 'Uninstall.cmd', 'LICENSE', 'manifest.txt') {
    if (-not (Test-Path (Join-Path $stage $required))) { throw "Package is missing $required" }
}

if (Test-Path $zip) { Remove-Item $zip -Force }
# Zipped inside a folder, so "Extract here" in Downloads doesn't scatter the files among others.
Compress-Archive -Path $stage -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "SHA-256: $hash"
