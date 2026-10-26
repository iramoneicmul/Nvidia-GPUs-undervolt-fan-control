# =====================================================================
#  set-fan-profile.ps1
#  Bascule la courbe de ventilation entre les profils prepares.
#  Le service relit fan-curve.txt automatiquement : aucun redemarrage.
#
#    powershell -File set-fan-profile.ps1 -Profil silence
#    powershell -File set-fan-profile.ps1 -Profil equilibre
#    powershell -File set-fan-profile.ps1 -Profil froid
# =====================================================================
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('silence', 'equilibre', 'froid')]
    [string]$Profil
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$src  = Join-Path $root ("profil-{0}.txt" -f $Profil)
$dst  = Join-Path $root 'fan-curve.txt'

if (-not (Test-Path $src)) { throw "Profil introuvable : $src" }
Copy-Item $src $dst -Force
"Profil applique : $Profil"
Start-Sleep -Seconds 3
& (Join-Path $root 'nvfan.exe') get
