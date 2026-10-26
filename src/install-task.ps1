# =====================================================================
#  install-task.ps1
#  Cree (ou remplace) la tache planifiee "NvidiaUndervolt".
#
#  Principal : SYSTEM (compte de service), niveau le plus eleve.
#    -> NVAPI est accessible en session 0 (verifie sur cette machine),
#       donc la courbe est appliquee des le demarrage, sans attendre
#       qu'un utilisateur ouvre une session.
#
#  Declencheurs :
#    1. demarrage Windows (+30 s, le temps que le pilote NVIDIA monte)
#    2. ouverture de session (+20 s, filet de securite)
#    3. reprise apres veille (+15 s, le driver peut perdre la courbe)
#
#  A lancer une fois, en administrateur.
# =====================================================================

$ErrorActionPreference = 'Stop'

$Root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$Script = Join-Path $Root 'apply-undervolt.ps1'
$Task   = 'NvidiaUndervolt'

if (-not (Test-Path $Script)) { throw "Script introuvable : $Script" }

# Source du journal d'evenements Windows
if (-not [System.Diagnostics.EventLog]::SourceExists('NvidiaUndervolt')) {
    New-EventLog -LogName Application -Source 'NvidiaUndervolt'
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $Script)

# 1) demarrage
$tBoot = New-ScheduledTaskTrigger -AtStartup
$tBoot.Delay = 'PT30S'

# 2) ouverture de session
$tLogon = New-ScheduledTaskTrigger -AtLogOn
$tLogon.Delay = 'PT20S'

# 3) reprise apres veille
$class = Get-CimClass -ClassName MSFT_TaskEventTrigger -Namespace Root/Microsoft/Windows/TaskScheduler
$tWake = New-CimInstance -CimClass $class -ClientOnly
$tWake.Enabled = $true
$tWake.Subscription = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''Microsoft-Windows-Power-Troubleshooter''] and EventID=1]]</Select></Query></QueryList>'
$tWake.Delay = 'PT15S'

$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

Register-ScheduledTask -TaskName $Task -Action $action -Trigger @($tBoot, $tLogon, $tWake) `
    -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "Tache '$Task' enregistree (SYSTEM, demarrage + logon + reprise)."
Get-ScheduledTask -TaskName $Task | Select-Object TaskName, State | Format-Table -AutoSize
Get-ScheduledTask -TaskName $Task | ForEach-Object { $_.Triggers } |
    Select-Object CimClass, Delay | Format-Table -AutoSize
