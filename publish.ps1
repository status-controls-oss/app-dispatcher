# Builds the picker (Native AOT) and Settings (WPF) into one folder: artifacts\AppDispatcher
# Requires the .NET 8 SDK and the Visual Studio C++ build tools (the AOT linker).
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'artifacts\AppDispatcher'

# The AOT toolchain locates the linker with vswhere, which the VS installer doesn't put on PATH.
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path $installer) { $env:PATH = "$env:PATH;$installer" }

dotnet publish "$PSScriptRoot\src\AppDispatcher\AppDispatcher.csproj" -c Release -o $out --nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }

dotnet publish "$PSScriptRoot\src\AppDispatcher.Settings\AppDispatcher.Settings.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $out --nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Write-Host "Published to $out"
