param([switch]$FetchSensors, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'
$version = (Get-Content (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain a three-part version such as 1.0.0.' }
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (!(Test-Path "$framework\csc.exe")) { throw 'Thermal needs Windows x64 with .NET Framework 4.8. Install it from Microsoft, then try again.' }
$runtime = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
if (!$runtime -or $runtime.Release -lt 528040) { throw 'Install Microsoft .NET Framework 4.8 or newer, then try again.' }
$vendor = Join-Path $PSScriptRoot 'vendor\LibreHardwareMonitor'
if ($FetchSensors -or !(Test-Path "$vendor\LibreHardwareMonitorLib.dll")) {
    New-Item -ItemType Directory -Force vendor | Out-Null
    Invoke-WebRequest 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' -UseBasicParsing -OutFile vendor\sensors.zip
    if ((Get-FileHash vendor\sensors.zip -Algorithm SHA256).Hash -ne '086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001') { throw 'Sensor archive checksum mismatch.' }
    Expand-Archive vendor\sensors.zip -DestinationPath $vendor -Force
}
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'dist\Thermal' }
if (!(Test-Path vendor\PawnIO_setup.exe)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe' -UseBasicParsing -OutFile vendor\PawnIO_setup.exe
}
if ((Get-FileHash vendor\PawnIO_setup.exe).Hash -ne 'A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0') { throw 'Driver setup checksum mismatch.' }
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($binary in @('Thermal.exe','Start Thermal.exe')) {
    $target = Join-Path $out $binary
    if (Test-Path -LiteralPath $target) {
        try { $lock = [IO.File]::Open($target,'Open','ReadWrite','None'); $lock.Dispose() }
        catch { throw "Quit Thermal from its tray icon before rebuilding $out. No running process was stopped." }
    }
}
Copy-Item "$vendor\*.dll" $out
Get-ChildItem $out -Filter *.dll | Unblock-File
Copy-Item Thermal.exe.config,MainWindow.xaml,THIRD-PARTY-NOTICES.md $out
Copy-Item vendor\PawnIO_setup.exe $out
Copy-Item assets\Thermal.ico $out
Copy-Item licenses $out -Recurse -Force
$versionSource = Join-Path $out 'VersionInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("Thermal")]
[assembly: AssemblyProduct("Thermal")]
[assembly: AssemblyDescription("Local PC health and temperature monitor")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $versionSource -Encoding UTF8
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path "$framework\WPF" $_) }
$refs += '/reference:' + (Join-Path $vendor 'LibreHardwareMonitorLib.dll')
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest /win32icon:assets\Thermal.ico "/out:$out\Thermal.exe" $refs (Get-ChildItem src\*.cs).FullName $versionSource
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:launcher\app.manifest /win32icon:assets\Thermal.ico "/out:$out\Start Thermal.exe" "/reference:$framework\System.dll" "/reference:$framework\System.Windows.Forms.dll" launcher\Launcher.cs $versionSource
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Copy-Item Thermal.exe.config (Join-Path $out 'Start Thermal.exe.config')
Copy-Item README.md $out
$package = @("$out\Thermal.exe","$out\Start Thermal.exe","$out\Start Thermal.exe.config","$out\Thermal.exe.config","$out\Thermal.ico","$out\MainWindow.xaml","$out\README.md","$out\THIRD-PARTY-NOTICES.md","$out\PawnIO_setup.exe","$out\licenses") + (Get-ChildItem $out -Filter *.dll).FullName
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
Compress-Archive -Path $package -DestinationPath dist\Thermal-windows-x64.zip -Force
Write-Host "Built: $out\Thermal.exe"
