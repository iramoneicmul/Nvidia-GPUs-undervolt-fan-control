param(
    [ValidateSet('install','uninstall')] [string]$Mode = 'install',
    [string]$UiLevel = '5'
)

# =====================================================================
#  setup-choice.ps1
#  Gere la conservation / suppression des fichiers de configuration
#  lors de l'installation et de la desinstallation.
#
#  Fichiers concernes : courbe active, profils de ventilation, mesures,
#  journaux et rapports. Ils sont sauvegardes dans
#  C:\ProgramData\NvidiaConf, hors du perimetre du MSI.
# =====================================================================

$root   = 'C:\Nvidia'
$backup = 'C:\Nvidia\backup'
$logs   = 'C:\Nvidia\backup\setup.log'

function Log([string]$m) {
    try {
        New-Item -ItemType Directory -Force -Path $backup | Out-Null
        Add-Content -Path $logs -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
    } catch { }
}

function Ask([string]$text, [string]$title) {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $r = [System.Windows.Forms.MessageBox]::Show($text, $title,
             [System.Windows.Forms.MessageBoxButtons]::YesNo,
             [System.Windows.Forms.MessageBoxIcon]::Question)
        return ($r -eq [System.Windows.Forms.DialogResult]::Yes)
    } catch { return $true }
}

$files = @(
    'fan-curve.txt',
    'profil-silence.txt', 'profil-equilibre.txt', 'profil-froid.txt',
    'plateau-active.json', 'plateau-1845-831.json',
    'freq-profile.json', 'autotune.ini',
    'undervolt.log', 'fan-curve.log'
)

function Copy-Settings([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    foreach ($f in $files) {
        $p = Join-Path $from $f
        if (Test-Path $p) { Copy-Item $p $to -Force }
    }
    Get-ChildItem $from -Filter 'rapport-*' -ErrorAction SilentlyContinue |
        Copy-Item -Destination $to -Force -ErrorAction SilentlyContinue
}

function Remove-Settings([string]$dir) {
    foreach ($f in $files) {
        $p = Join-Path $dir $f
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
    Get-ChildItem $dir -Filter 'rapport-*' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

$interactive = ($UiLevel -eq '5')

if ($Mode -eq 'install') {
    Log "installation : verification d'une sauvegarde"
    if (Test-Path $backup) {
        $restore = $true
        if ($interactive) {
            $restore = Ask ("Une configuration precedente a ete sauvegardee (undervolt, profils de ventilation, rapports).`n`n" +
                            "La restaurer ?`n`nOui = reutiliser cette configuration`nNon = la supprimer") "NVIDIA Configuration"
        }
        if ($restore) {
            Copy-Settings $backup $root
            Log "configuration precedente restauree"
        } else {
            Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
            Log "sauvegarde supprimee sur demande"
        }
    } else {
        Log "aucune sauvegarde"
    }
}
elseif ($Mode -eq 'uninstall') {
    Log "desinstallation : choix de conservation"
    $keep = $true
    if ($interactive) {
        $keep = Ask ("Conserver la configuration actuelle (undervolt, profils de ventilation, mesures, rapports) ?`n`n" +
                     "Oui = la conserver pour une reinstillation`nNon = la supprimer definitivement") "NVIDIA Configuration"
    }
    if ($keep) {
        Copy-Settings $root $backup
        Log "configuration sauvegardee pour une reinstillation"
    } else {
        Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
        Log "configuration supprimee sur demande"
    }
}
