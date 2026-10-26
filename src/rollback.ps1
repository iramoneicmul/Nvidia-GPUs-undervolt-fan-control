# =====================================================================
#  rollback.ps1  -  revient a la courbe d'origine et retire la tache
# =====================================================================
$ErrorActionPreference = 'Continue'
$Root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$Exe  = Join-Path $Root 'snu.exe'

Get-ScheduledTask -TaskName 'NvidiaUndervolt' -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false
'Tache NvidiaUndervolt supprimee.'

if (Test-Path $Exe) {
    & $Exe clear
    & $Exe status
} else {
    'Binaire absent : rien a reinitialiser.'
}
