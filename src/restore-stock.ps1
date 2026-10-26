$ErrorActionPreference = 'Continue'
$root = 'C:\Nvidia'
$log  = 'C:\Nvidia\backup\setup.log'
$snu  = Join-Path $root 'snu.exe'
$nvfan = Join-Path $root 'nvfan.exe'
$smi  = 'C:\Windows\System32\nvidia-smi.exe'

function Log([string]$m) {
    try {
        New-Item -ItemType Directory -Force -Path 'C:\Nvidia\backup' | Out-Null
        Add-Content $log ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
    } catch { }
}

Log 'restauration complete de la configuration d''origine (SYSTEM)'

# 1) courbe tension/frequence -> stock
if (Test-Path $snu) {
    $o = & $snu clear 2>&1 | Out-String
    Log ('  courbe V/F : ' + (($o.Trim() -replace "`r?`n", ' | ')))
}

# 2) power limit -> valeur d'origine (350 W)
if (Test-Path $smi) {
    $o = & $smi -pl 350 2>&1 | Out-String
    Log ('  power limit 350 : ' + (($o.Trim() -replace "`r?`n", ' | ')))
    Start-Sleep -Milliseconds 500
    $cur = (& $smi --query-gpu=power.limit --format=csv,noheader).Trim()
    Log ('  power limit verifie : ' + $cur)
}

# 3) ventilation -> VBIOS (auto)
if (Test-Path $nvfan) {
    & $nvfan auto | Out-Null
    Log '  ventilation rendue au VBIOS'
}

# 4) verification finale
if (Test-Path $snu) {
    $s = & $snu status 2>&1 | Out-String
    $m = [regex]::Match($s, 'Core curve offset:\s*(.+)')
    if ($m.Success) { Log ('  courbe verifiee : ' + $m.Groups[1].Value.Trim()) }
}
Log 'restauration terminee'
