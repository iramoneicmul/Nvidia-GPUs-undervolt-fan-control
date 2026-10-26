$ErrorActionPreference = 'Continue'
$root = 'C:\Nvidia'
$log  = 'C:\Nvidia\backup\setup.log'
$sc   = 'C:\Windows\System32\sc.exe'
$restore = Join-Path $root 'restore-stock.ps1'

function Log([string]$m) {
    try {
        New-Item -ItemType Directory -Force -Path 'C:\Nvidia\backup' | Out-Null
        Add-Content $log ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
    } catch { }
}

Log 'desinstallation : preparation du retour a la configuration d''origine'

# 1) supprimer les taches de l'application
foreach ($t in 'NvidiaConf','NvidiaUndervolt','NvidiaAutoTune') {
    Unregister-ScheduledTask -TaskName $t -Confirm:$false -ErrorAction SilentlyContinue
}
Log '  taches applicatives supprimees'

# 2) arreter et supprimer le service de ventilation
& $sc stop NvidiaFanCurve | Out-Null
Start-Sleep -Seconds 2
& $sc delete NvidiaFanCurve | Out-Null
Log '  service supprime'

# 3) restauration complete via une tache SYSTEM : c'est le seul contexte qui
#    garantit les droits necessaires pour ecrire le power limit.
if (Test-Path $restore) {
    $name = 'NvidiaRestore'
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $restore)
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

    Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $name

    # attente de la fin (max 40 s)
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Seconds 2
        $info = Get-ScheduledTaskInfo -TaskName $name -ErrorAction SilentlyContinue
        if ($info -and $info.LastTaskResult -ne 267009) { break }
    }
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    Log '  restauration SYSTEM executee'
} else {
    Log '  restore-stock.ps1 introuvable : restauration impossible'
}
