$ErrorActionPreference = 'Continue'
$root = 'C:\Nvidia'
$exe  = Join-Path $root 'nvidiaconf.exe'
$snu  = Join-Path $root 'snu.exe'
$smi  = 'C:\Windows\System32\nvidia-smi.exe'
$log  = 'C:\Nvidia\backup\setup.log'

function Log([string]$m) {
    try {
        New-Item -ItemType Directory -Force -Path 'C:\Nvidia\backup' | Out-Null
        Add-Content $log ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
    } catch { }
}

Log 'installation : creation des taches et application de la configuration'

Get-Process nvidiaconf -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName 'NvidiaAutoTune' -Confirm:$false -ErrorAction SilentlyContinue

# --- tache de l'interface ---
$user = "$env:USERDOMAIN\$env:USERNAME"
if ($env:USERNAME -eq 'SYSTEM') {
    $u = (Get-CimInstance Win32_ComputerSystem).UserName
    if ($u) { $user = $u }
}
if ([string]::IsNullOrEmpty($user) -or $user -notmatch '\\') { $user = 'iramone' }

$action = New-ScheduledTaskAction -Execute $exe -Argument '--tray'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 0)
Register-ScheduledTask -TaskName 'NvidiaConf' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Log "  tache NvidiaConf creee pour $user"

# --- service de ventilation ---
if (Test-Path (Join-Path $root 'install-fan-service.ps1')) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'install-fan-service.ps1') | Out-Null
    Log '  service de ventilation installe'
}

# --- tache d'undervolt ---
if (Test-Path (Join-Path $root 'install-task.ps1')) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'install-task.ps1') | Out-Null
    Log '  tache NvidiaUndervolt installee'
}

# --- application de la configuration retenue ---
if ((Test-Path $snu) -and (Test-Path (Join-Path $root 'plateau-active.json'))) {
    $out = & $snu --in-tuning-file (Join-Path $root 'plateau-active.json') --no-persist 2>&1 | Out-String
    Log ('  courbe : ' + ($out.Trim() -replace "`r?`n", ' | '))
}
if (Test-Path $smi) {
    $out = & $smi -pl 320 2>&1 | Out-String
    Log ('  power limit 320 : ' + ($out.Trim() -replace "`r?`n", ' | '))
}

Log '  configuration et taches en place'
