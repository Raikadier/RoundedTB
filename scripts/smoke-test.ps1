# RoundedTB smoke tests (ISO 25010 — Reliability / Functional)
# Requires: .NET 8 SDK, Windows desktop session.
param(
    [int]$RunSeconds = 25
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Ok([string]$msg) { Write-Host "[PASS] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

Info "1/6 Build Release"
dotnet build "$root\RoundedTB.sln" -c Release --nologo
if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed" }
Ok "Build succeeded"

$exeDir = Join-Path $root "RoundedTB\bin\Release\net8.0-windows10.0.19041.0"
$exe = Join-Path $exeDir "RoundedTB.exe"
if (-not (Test-Path $exe)) { Fail "Missing exe" }
Ok "Exe present"

Info "2/6 Stop previous instances"
Get-Process RoundedTB -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Info "3/6 Launch"
Start-Process -FilePath $exe -WorkingDirectory $exeDir | Out-Null
Start-Sleep -Seconds 5
$procs = @(Get-Process RoundedTB -ErrorAction SilentlyContinue)
if ($procs.Count -lt 1) { Fail "RoundedTB did not start" }
Ok ("Running processes: {0}" -f $procs.Count)

Info ("4/6 Wait {0}s soak" -f $RunSeconds)
$deadline = (Get-Date).AddSeconds($RunSeconds)
while ((Get-Date) -lt $deadline) {
    $alive = @(Get-Process RoundedTB -ErrorAction SilentlyContinue)
    if ($alive.Count -eq 0) { Fail ("Process died during soak ({0}s)" -f $RunSeconds) }
    Start-Sleep -Seconds 2
}
Ok ("Survived {0}s soak without exit" -f $RunSeconds)

$log = Join-Path $env:LOCALAPPDATA "rtb.log"
if (Test-Path $log) {
    $tail = Get-Content $log -Tail 40 -ErrorAction SilentlyContinue
    $crash = $tail | Select-String -Pattern "UnhandledException|DispatcherUnhandledException|bw exception"
    if ($crash) {
        Info "Recent log mentions exceptions (review):"
        $crash | ForEach-Object { Write-Host ("  {0}" -f $_) }
    } else {
        Ok "No crash markers in last log lines"
    }
    if (($tail | Out-String) -match "in bw|bw heartbeat") {
        Ok "Worker log activity seen"
    } else {
        Info "No heartbeat yet (may need ~60s)"
    }
} else {
    Info "rtb.log not found yet"
}

Info "5/6 TaskbarAnimations check"
$anim = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name TaskbarAnimations -ErrorAction SilentlyContinue).TaskbarAnimations
if ($anim -eq 0) {
    Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name TaskbarAnimations -Value 1 -Type DWord
    Info "Was 0 - restored to 1"
} else {
    Ok ("TaskbarAnimations={0}" -f $anim)
}

Info "6/6 Stop processes"
Get-Process RoundedTB -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
$left = @(Get-Process RoundedTB -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) {
    $left | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
Ok "Smoke finished"

Write-Host ""
Write-Host "Manual checklist:" -ForegroundColor Yellow
Write-Host "  [ ] Tray icon; X hides config (process stays)"
Write-Host "  [ ] Dynamic segments; FillOnMaximise off keeps rounding"
Write-Host "  [ ] Windows AH + Always show + margins 0 -> hover shows TB"
Write-Host "  [ ] Tray Close -> taskbar restored"
Write-Host "  [ ] End task main -> watchdog restores RGN"
Write-Host "  [ ] No infinite flicker on calendar / Action Center"
