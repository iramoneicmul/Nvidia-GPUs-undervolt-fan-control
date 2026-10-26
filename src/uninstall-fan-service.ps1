# =====================================================================
#  uninstall-fan-service.ps1
#  Arrete et supprime le service "NvidiaFanCurve", puis rend la main
#  au VBIOS (mode automatique).
# =====================================================================
$ErrorActionPreference = 'Continue'
$root = 'C:\ProgramData\NvidiaUndervolt'
$name = 'NvidiaFanCurve'

if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    & sc.exe stop $name | Out-Null
    Start-Sleep -Seconds 3
    & sc.exe delete $name | Out-Null
    Start-Sleep -Seconds 2
    'Service supprime.'
} else {
    'Service absent.'
}

if (Test-Path (Join-Path $root 'nvfan.exe')) {
    & (Join-Path $root 'nvfan.exe') auto | Out-Null
    'Ventilateurs rendus au VBIOS (auto).'
}

Get-Service $name -ErrorAction SilentlyContinue | Select-Object Name, Status
