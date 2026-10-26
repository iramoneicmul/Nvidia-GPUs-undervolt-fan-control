# =====================================================================
#  verify.ps1  -  etat de l'undervoltage (lecture seule)
# =====================================================================
$ErrorActionPreference = 'Continue'
$Root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$Exe  = Join-Path $Root 'snu.exe'

'=== Courbe appliquee (NVAPI) ==='
& $Exe status

''
'=== Tache planifiee ==='
$t = Get-ScheduledTask -TaskName 'NvidiaUndervolt' -ErrorAction SilentlyContinue
if ($t) {
    $t | Select-Object TaskName, State | Format-Table -AutoSize
    (Get-ScheduledTaskInfo -TaskName 'NvidiaUndervolt') |
        Select-Object LastRunTime, LastTaskResult, NextRunTime | Format-List
} else {
    'Aucune tache NvidiaUndervolt.'
}

''
'=== 20 dernieres lignes du journal ==='
if (Test-Path (Join-Path $Root 'undervolt.log')) {
    Get-Content (Join-Path $Root 'undervolt.log') -Tail 20
} else {
    'Pas encore de journal.'
}

''
'=== GPU en direct ==='
nvidia-smi --query-gpu=clocks.gr,clocks.mem,power.draw,temperature.gpu,fan.speed --format=csv
