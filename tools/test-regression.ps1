# =====================================================================
#  test-regression.ps1 - tests de non-regression de nvidiaconf
#  A executer sur la machine cible (droits administrateur).
#  Code de sortie : 0 = tout est OK, 1 = au moins un echec.
# =====================================================================
param([string]$Root = 'C:\Nvidia')

$script:fail = 0
$script:pass = 0

function Check([string]$name, [bool]$cond, [string]$detail = '') {
    if ($cond) {
        $script:pass++
        Write-Output ("  OK    " + $name + $(if ($detail) { " ($detail)" } else { '' }))
    } else {
        $script:fail++
        Write-Output ("  ECHEC " + $name + $(if ($detail) { " ($detail)" } else { '' }))
    }
}

Write-Output '=== 1. Fichiers installes ==='
foreach ($f in 'nvidiaconf.exe', 'snu.exe', 'stability.py', 'courbe-stock.json',
               'profil-silence.txt', 'profil-equilibre.txt', 'profil-froid.txt') {
    Check $f (Test-Path (Join-Path $Root $f))
}
Check 'profil ventilation actif' (Test-Path (Join-Path $Root 'fan-curve.txt'))
Check 'courbe active' (Test-Path (Join-Path $Root 'plateau-active.json'))

Write-Output '=== 2. Services et taches ==='
$svc = Get-Service NvidiaFanCurve -ErrorAction SilentlyContinue
Check 'service NvidiaFanCurve Running' ($svc -and $svc.Status -eq 'Running')
$t1 = Get-ScheduledTask -TaskName NvidiaUndervolt -ErrorAction SilentlyContinue
Check 'tache NvidiaUndervolt presente' ($null -ne $t1)
$t2 = Get-ScheduledTask -TaskName NvidiaConf -ErrorAction SilentlyContinue
Check 'tache NvidiaConf presente' ($null -ne $t2)

Write-Output '=== 3. Coherence carte ==='
$smi = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
$pl = (& $smi --query-gpu=power.limit --format=csv,noheader,nounits 2>$null) -replace ',', '.'
Check 'power limit numerique' ([double]$pl -gt 0) ("$pl W")
$t = (& $smi --query-gpu=temperature.gpu --format=csv,noheader,nounits 2>$null) -replace ',', '.'
Check 'temperature plausible' ([int]$t -ge 0 -and [int]$t -lt 110) ("$t C")

Write-Output '=== 4. Limites lues sur la carte ==='
$lim = & $smi --query-gpu=power.min_limit,power.max_limit,power.default_limit --format=csv,noheader,nounits 2>$null
Check 'trois limites disponibles' ($lim -and ($lim.Split(',').Count -eq 3)) $lim

Write-Output '=== 5. Journal et rotation ==='
$log = Join-Path $Root 'nvidiaconf.log'
Check 'journal present' (Test-Path $log)
if (Test-Path $log) {
    $size = (Get-Item $log).Length
    Check 'taille du journal maitrisee' ($size -lt 3MB) ("$([math]::Round($size/1KB,1)) Ko")
}

Write-Output '=== 6. Generation du rapport ==='
Get-ChildItem (Join-Path $Root 'rapport-*.html') -ErrorAction SilentlyContinue | Remove-Item -Force
Start-Process -FilePath (Join-Path $Root 'nvidiaconf.exe') -ArgumentList '--report' -Wait -WindowStyle Hidden
$reports = @(Get-ChildItem (Join-Path $Root 'rapport-*.html') -ErrorAction SilentlyContinue)
Check 'rapport HTML genere' ($reports.Count -gt 0) ("$($reports.Count) fichier(s)")

Write-Output '=== 7. Validation des consignes (tension hors limites) ==='
$logBefore = if (Test-Path $log) { (Get-Content $log -Raw) } else { '' }
$taskName = 'NvidiaRegTest'
schtasks /delete /tn $taskName /f 2>$null | Out-Null
schtasks /create /tn $taskName /tr "`"$Root\nvidiaconf.exe`" --package --mv 500" /sc once /st 00:00 /ru SYSTEM /f | Out-Null
schtasks /run /tn $taskName | Out-Null
Start-Sleep 15
schtasks /end /tn $taskName 2>$null | Out-Null
Get-Process nvidiaconf -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq 0 } | Stop-Process -Force
schtasks /delete /tn $taskName /f 2>$null | Out-Null
$logAfter = if (Test-Path $log) { (Get-Content $log -Raw) } else { '' }
Check 'valeur hors limites refusee' ($logAfter -match 'valeur refusee' -and $logAfter -ne $logBefore)

Write-Output ''
Write-Output ("Resultat : {0} OK, {1} echec(s)" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 }
exit 0
