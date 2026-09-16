$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $root 'dist\Thermal-PackagingTest.exe'
if (!(Test-Path -LiteralPath $installer)) { throw 'Build scripts\build-installer.ps1 -TestBuild first.' }
$destination = Join-Path $root ('artifacts\installer-smoke\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $destination | Out-Null
$log = Join-Path $destination 'install.log'
$process = Start-Process -FilePath $installer -Verb RunAs -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS="!desktopicon,!sensordriver"',('/DIR="' + $destination + '"'),('/LOG="' + $log + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Install failed: $($process.ExitCode). See $log" }
try {
    foreach ($file in @('Thermal.exe','Start Thermal.exe','Thermal.ico','MainWindow.xaml','LibreHardwareMonitorLib.dll','PawnIO_setup.exe','unins000.exe','licenses\LibreHardwareMonitor.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $destination $file))) { throw "Missing installed file: $file" }
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Thermal Packaging Test\Thermal.lnk'
    if (!(Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut was not created.' }
    if ($shell.CreateShortcut($shortcut).TargetPath -ne (Join-Path $destination 'Start Thermal.exe')) { throw 'Shortcut does not target the elevated launcher.' }
    $app = Start-Process -FilePath (Join-Path $destination 'Thermal.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    $report = Get-Content (Join-Path $destination 'self-test.txt') -Raw
    if ($app.ExitCode -ne 0 -or !$report.StartsWith('PASS:')) { throw "Installed app failed tests: $report" }
    Write-Host 'PASS: installation, required files, Start menu shortcut, installed application tests.'
} finally {
    # Invoke only the uninstaller for this uniquely named smoke-test installation.
    $uninstaller = Join-Path $destination 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $cleanup = Start-Process -FilePath $uninstaller -Verb RunAs -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($cleanup.ExitCode -ne 0) { throw "Test uninstall failed ($($cleanup.ExitCode))." }
    }
}
if (Test-Path -LiteralPath (Join-Path $destination 'Thermal.exe')) { throw 'Uninstall left the installed executable behind.' }
if (Test-Path -LiteralPath $shortcut) { throw 'Uninstall left the Start menu shortcut behind.' }
Write-Host 'PASS: uninstallation removes app files and shortcut. Test logs are retained.'
