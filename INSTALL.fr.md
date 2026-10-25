# Guide d'installation / Installation guide

[English version](INSTALL.md) | **Français**

---

## 1. Prérequis

| Élément | Exigence |
|---|---|
| Système | Windows 10 ou 11, 64 bits |
| GPU | carte NVIDIA exposant le contrôle des ventilateurs, la courbe V/F et le power limit via NVAPI |
| Pilote | pilote d'affichage NVIDIA installé (service `nvlddmkm` présent) |
| Runtime | .NET Framework 4.8 (inclus dans Windows 10/11) |
| Optionnel | Python 3.10+ et PyTorch, pour le test de stabilité le plus sévère |

## 2. Téléchargement

Dernière version :
<https://github.com/iramoneicmul/rtx3080ti-undervolt-fan-control/releases/latest>

Téléchargez soit :

- `nvidiaconf.msi` — recommandé, installe tout ;
- `nvidiaconf.exe` — exécutable portable seul.

Vérifiez l'empreinte (PowerShell) :

```powershell
Get-FileHash .\nvidiaconf.msi -Algorithm SHA256
```

## 3. Installation (MSI)

1. Double-cliquez sur `nvidiaconf.msi`.
2. Si aucun pilote NVIDIA n'est détecté, l'installeur s'arrête avec un message
   explicite. Rien n'est installé.
3. Sinon, les fichiers vont dans `C:\Nvidia` et `nvidiaconf.exe --tray` est
   ajouté à `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
4. À l'ouverture de session suivante, l'icône verte apparaît dans la zone de
   notification.

## 4. Installation (portable)

1. Créez `C:\Nvidia`.
2. Copiez-y `nvidiaconf.exe`.
3. Lancez-le. Il cherche ses données dans `C:\Nvidia`, avec repli sur
   `C:\ProgramData\NvidiaUndervolt`.

Le mode portable n'installe ni la tâche d'undervolt ni le service de
ventilation ; utilisez **Réappliquer au démarrage** dans l'application pour les
créer.

## 5. Premier lancement

1. L'application vérifie la compatibilité (NVAPI, ventilateurs, courbe V/F,
   power limit) et refuse de démarrer si l'un manque.
2. L'onglet **Identité** affiche la carte, le VBIOS, le pilote et le lien PCIe.
3. **Appliquer la configuration** applique le profil enregistré.
4. **Trouver la configuration optimale** lance l'assistant complet
   (~30 min, 7 paliers de tension).
5. **Mesurer les fréquences** enregistre les fréquences origine et optimisées
   par niveau de charge.
6. **Tester les profils de ventilation** mesure les trois profils.

## 6. Icône de la zone de notification

- **Simple clic gauche** : ouvre la fenêtre.
- **Clic droit** : menu (afficher, actualiser, appliquer, profil, rapport,
  quitter).

## 7. Désinstallation

- MSI : *Applications et fonctionnalités* → *NVIDIA Configuration* →
  Désinstaller, ou `msiexec /x nvidiaconf.msi`.
- Avant de désinstaller, lancez **Restaurer l'origine** pour supprimer la tâche
  planifiée et le service de ventilation.
- Nettoyage manuel si nécessaire :

```powershell
powershell -File C:\Nvidia\rollback.ps1
sc.exe delete NvidiaFanCurve
```

## 8. Dépannage

| Symptôme | Action |
|---|---|
| « Matériel incompatible » | la carte n'expose pas le contrôle des ventilateurs ou la courbe V/F ; aucun réglage logiciel n'est possible |
| L'installeur s'arrête (code 1603) | aucun pilote NVIDIA détecté |
| Pas d'icône dans la zone de notification | vérifier la clé `Run` et la tâche `NvidiaConf` |
| Ventilateurs bloqués à un régime fixe | le service est arrêté ; démarrer `NvidiaFanCurve` |
| Écran noir pendant un test | TDR ; redémarrer, l'application restaure le stock automatiquement au lancement suivant |
