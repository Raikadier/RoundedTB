#Requires -Version 5
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host 'Building Release...'
dotnet build RoundedTB.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$src = Join-Path $PSScriptRoot 'RoundedTB\bin\Release\net10.0-windows10.0.19041.0'
$dest = 'C:\Program Files\RoundedTB'
$startLnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\RoundedTB.lnk'

Get-Process RoundedTB -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$install = @'
$ErrorActionPreference = "Stop"
$src = "SRC_PLACEHOLDER"
$dest = "C:\Program Files\RoundedTB"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
robocopy $src $dest /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy $LASTEXITCODE" }
$exe = Join-Path $dest "RoundedTB.exe"
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut("START_PLACEHOLDER")
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Description = "RoundedTB"
$lnk.IconLocation = "$exe,0"
$lnk.Save()
Write-Host "Installed $exe"
'@
$install = $install.Replace('SRC_PLACEHOLDER', $src).Replace('START_PLACEHOLDER', $startLnk)
$tmp = Join-Path $env:TEMP 'rtb-install-run.ps1'
Set-Content -Path $tmp -Value $install -Encoding UTF8

Write-Host 'Installing to Program Files (UAC)...'
$p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$tmp)
if ($p.ExitCode -ne 0) { throw "install exit $($p.ExitCode)" }

Write-Host 'Launching...'
Start-Process (Join-Path $dest 'RoundedTB.exe')
Start-Sleep 2
Get-Process RoundedTB | Format-Table Id, ProcessName, Path -AutoSize
Write-Host 'OK — check tray icon; close config window should stay in tray.'
