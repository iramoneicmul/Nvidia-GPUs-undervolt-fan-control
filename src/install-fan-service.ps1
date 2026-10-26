# =====================================================================
#  install-fan-service.ps1
#  Installe (ou remplace) le service Windows "NvidiaFanCurve".
#
#  - demarrage automatique au boot, avant toute ouverture de session
#  - tourne en LocalSystem, sans fenetre (session 0)
#  - redemarrage automatique par le SCM en cas d'arret anormal
#  - la courbe est lue dans fan-curve.txt (modifiable a chaud)
#
#  A lancer une fois, en administrateur.
# =====================================================================
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$exe  = Join-Path $root 'nvfan.exe'
$name = 'NvidiaFanCurve'

if (-not (Test-Path $exe)) { throw "Introuvable : $exe" }
if (-not (Test-Path (Join-Path $root 'fan-curve.txt'))) { throw "Introuvable : fan-curve.txt" }

# arret et suppression d'une eventuelle version precedente
if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    & sc.exe stop $name | Out-Null
    Start-Sleep -Seconds 2
    & sc.exe delete $name | Out-Null
    Start-Sleep -Seconds 2
}

$binPath = '"' + $exe + '" service'

& sc.exe create $name binPath= $binPath start= auto DisplayName= 'NVIDIA Fan Curve' | Out-Null
& sc.exe description $name "Regulation des deux ventilateurs de la RTX 3080 Ti FE (courbe en tr/min). Configuration : $root\fan-curve.txt. Reapplication automatique de l'undervolt apres rechargement du pilote." | Out-Null
& sc.exe failure $name reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
& sc.exe start $name | Out-Null

Start-Sleep -Seconds 6
Get-Service $name | Select-Object Name, Status, StartType | Format-Table -AutoSize
& sc.exe qc $name | Select-String -Pattern 'BINARY_PATH_NAME|START_TYPE|SERVICE_START_NAME'
"--- journal ---"
Get-Content (Join-Path $root 'fan-curve.log') -Tail 6 -ErrorAction SilentlyContinue
