# =====================================================================
#  verify-fan.ps1  -  diagnostic lecture seule
#  Etat du service, de la courbe, de NVAPI, de l'undervolt et du journal.
# =====================================================================
$root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }

"=== Service NvidiaFanCurve ==="
if (Get-Service -Name NvidiaFanCurve -ErrorAction SilentlyContinue) {
    Get-Service NvidiaFanCurve | Select-Object Name, Status, StartType | Format-Table -AutoSize
    & sc.exe qc NvidiaFanCurve | Select-String -Pattern 'BINARY_PATH_NAME|START_TYPE|SERVICE_START_NAME'
} else {
    'service absent'
}

"`n=== Courbe configuree ==="
Get-Content (Join-Path $root 'fan-curve.txt') -ErrorAction SilentlyContinue |
    Where-Object { $_ -match '=' -and $_ -notmatch '^\s*#' }

"`n=== Etat des ventilateurs ==="
& (Join-Path $root 'nvfan.exe') get

"`n=== Journal du service (10 dernieres lignes) ==="
Get-Content (Join-Path $root 'fan-curve.log') -Tail 10 -ErrorAction SilentlyContinue

"`n=== Tache undervolt ==="
Get-ScheduledTask -TaskName NvidiaUndervolt -ErrorAction SilentlyContinue |
    Select-Object TaskName, State | Format-Table -AutoSize
Get-ScheduledTaskInfo -TaskName NvidiaUndervolt -ErrorAction SilentlyContinue |
    Select-Object LastRunTime, LastTaskResult | Format-List

"=== Courbe V/F ==="
& (Join-Path $root 'snu.exe') status

"=== Carte ==="
nvidia-smi --query-gpu=temperature.gpu,fan.speed,power.draw,clocks.gr,clocks.max.graphics --format=csv
