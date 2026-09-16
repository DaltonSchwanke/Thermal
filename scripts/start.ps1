param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
try {
    if (![Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'Thermal needs an Intel or AMD 64-bit Windows PC.'
    }
    $output = Join-Path $root 'dist\source-app'
    $launcher = Join-Path $output 'Start Thermal.exe'
    if (!(Test-Path -LiteralPath $launcher) -or $BuildOnly) {
        Write-Host 'Preparing Thermal. The first run downloads sensor components and builds the app.'
        Write-Host 'No Visual Studio or developer tools need to be installed.'
        & (Join-Path $root 'build.ps1') -OutputDirectory $output
    }
    if (!$BuildOnly) {
        Write-Host 'Opening Thermal. Approve the Windows prompt for temperature access.'
        Start-Process -FilePath $launcher -WorkingDirectory $output
    }
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
