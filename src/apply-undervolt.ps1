# =====================================================================
#  apply-undervolt.ps1
#  Applique l'undervoltage de la RTX 3080 Ti FE :
#    - courbe d'origine conservee jusqu'a 843 mV
#    - plateau a 1860 MHz a partir de 850 mV
#    - aucun point au-dessus de 850 mV utile / 1860 MHz
#  Appele par la tache planifiee "NvidiaUndervolt" (logon + reprise).
#  Ecrit un journal dans C:\ProgramData\NvidiaUndervolt\undervolt.log
# =====================================================================

$ErrorActionPreference = 'Stop'

$Root = if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'snu.exe'))) { $PSScriptRoot } else { 'C:\ProgramData\NvidiaUndervolt' }
$Exe       = Join-Path $Root 'snu.exe'
# Courbe active : ecrite par l'assistant d'optimisation de nvidiaconf.exe
# (plateau 1845 MHz, tension minimale stable + marge de securite).
$Tune      = Join-Path $Root 'plateau-active.json'
$Log       = Join-Path $Root 'undervolt.log'
$MaxLogKB  = 256

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    try { Add-Content -Path $Log -Value $line -Encoding UTF8 } catch { }
}

function Trim-Log {
    try {
        if ((Test-Path $Log) -and ((Get-Item $Log).Length -gt ($MaxLogKB * 1024))) {
            $keep = Get-Content -Path $Log -Tail 400
            Set-Content -Path $Log -Value $keep -Encoding UTF8
        }
    } catch { }
}

function Fail-Fatal {
    param([string]$Message)
    Write-Log $Message 'ERROR'
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists('NvidiaUndervolt')) {
            New-EventLog -LogName Application -Source 'NvidiaUndervolt'
        }
        Write-EventLog -LogName Application -Source 'NvidiaUndervolt' -EventId 1001 `
            -EntryType Error -Message $Message
    } catch { }
    exit 1
}

Write-Log '--- Demarrage de apply-undervolt.ps1 ---'
Trim-Log

if (-not (Test-Path $Exe))  { Fail-Fatal "Binaire introuvable : $Exe" }
if (-not (Test-Path $Tune)) { Fail-Fatal "Fichier de courbe introuvable : $Tune" }

# 1) Attendre que le pilote NVIDIA soit pret (max 60 s)
$ready = $false
for ($i = 1; $i -le 30; $i++) {
    $probe = & $Exe voltage 2>&1 | Out-String
    if (($LASTEXITCODE -eq 0) -and ($probe -match 'NVIDIA')) { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) { Fail-Fatal 'GPU NVIDIA non detecte apres 60 s - undervolt NON applique.' }

# 2) Appliquer la courbe (3 tentatives)
$applied = $false
for ($try = 1; $try -le 3; $try++) {
    $out = & $Exe --in-tuning-file $Tune --no-persist 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($code -eq 0) { $applied = $true; break }
    Write-Log ("Tentative {0} en echec (code {1}) : {2}" -f $try, $code, ($out.Trim() -replace "`r?`n", ' | ')) 'WARN'
    Start-Sleep -Seconds 5
}
if (-not $applied) { Fail-Fatal 'Application de la courbe impossible apres 3 tentatives.' }

# 3) Limite de puissance (0 = ne pas toucher)
#    Le VBIOS autorise 350 W par defaut et 400 W au maximum.
#    400 W a ete teste : aucun gain en jeu, davantage de chaleur.
#    320 W est le reglage retenu : en jeu la consommation reste sous cette
#    limite (la carte garde sa frequence), tandis que les charges lourdes
#    sont plafonnees et chauffent nettement moins.
# Power limit memorise par nvidiaconf (boutons d'application ou assistant).
# Repli sur 320 W si le fichier est absent.
$PowerLimitW = 320
$plFile = Join-Path $Root 'power-limit.txt'
if (Test-Path $plFile) {
    $mpl = [regex]::Match((Get-Content $plFile -Raw), '(\d+)')
    if ($mpl.Success) { $PowerLimitW = [int]$mpl.Groups[1].Value }
}
if ($PowerLimitW -gt 0) {
    try {
        $pl = & nvidia-smi -pl $PowerLimitW 2>&1 | Out-String
        $plTxt = ($pl.Trim() -replace "`r?`n", ' | ')
        if ($pl -match 'was set to') {
            Write-Log ("Power limit -> {0} W ({1})" -f $PowerLimitW, $plTxt)
        } else {
            Write-Log ("Power limit non applique : {0}" -f $plTxt) 'WARN'
        }
    } catch {
        Write-Log ("Power limit : exception {0}" -f $_.Exception.Message) 'WARN'
    }
}

# 4) Verifier que la courbe est bien appliquee
#    (le "point de fonctionnement" affiche par l'outil est une estimation qui
#     varie avec la temperature : 826 mV a chaud, 913 mV a froid. Il ne peut
#     donc pas servir de seuil. On controle la courbe reellement ecrite.)
$status = & $Exe status 2>&1 | Out-String
if ($status -match 'Core curve offset:\s*stock') {
    Fail-Fatal 'La courbe est restee au stock : application NON effective.'
}

$curve = & $Exe curve 2>&1 | Out-String
if ($curve -match '831 mV\s*->\s*(\d+)\s*MHz') {
    $mhz831 = [int]$Matches[1]
    if ($mhz831 -gt 1860) {
        Fail-Fatal ("Courbe inattendue : {0} MHz a 831 mV." -f $mhz831)
    }
    Write-Log ("OK - courbe appliquee : {0} MHz a 831 mV." -f $mhz831)
} else {
    Write-Log 'Courbe appliquee (point 831 mV non lisible dans le dump).' 'WARN'
}

Write-Log '--- Fin ---'
exit 0
