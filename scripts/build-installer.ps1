param([string]$Compiler, [switch]$TestBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$version = (Get-Content VERSION -Raw).Trim()
$appDirectory = Join-Path $root 'dist\release\Thermal'
& (Join-Path $root 'build.ps1') -OutputDirectory $appDirectory

# Keep the installer tool portable and isolated in the repo; no global installation.
if (!$Compiler) {
    $toolDirectory = Join-Path $root 'vendor\InnoSetup'
    $Compiler = Join-Path $toolDirectory 'ISCC.exe'
    if (!(Test-Path -LiteralPath $Compiler)) {
        $download = Join-Path $root 'vendor\innosetup-6.7.3.exe'
        if (!(Test-Path -LiteralPath $download)) {
            Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -UseBasicParsing -OutFile $download
        }
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'Installer compiler checksum mismatch.' }
        $arguments = @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $toolDirectory + '"'))
        $bootstrap = Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        if ($bootstrap.ExitCode -ne 0) { throw "Installer compiler setup failed ($($bootstrap.ExitCode))." }
    }
}
$defines = @("/DAppVersion=$version", "/DAppSource=$appDirectory", "/DRepoRoot=$root")
if ($TestBuild) { $defines += '/DTestBuild=1' }
& $Compiler $defines (Join-Path $root 'installer\Thermal.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
if (!$TestBuild) {
    $artifacts = @((Join-Path $root "dist\Thermal-Setup-$version.exe"),(Join-Path $root 'dist\Thermal-windows-x64.zip'))
    $artifacts | ForEach-Object { $hash=Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) } | Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ASCII
}
