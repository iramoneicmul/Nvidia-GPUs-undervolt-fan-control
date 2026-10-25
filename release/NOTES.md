# NVIDIA Configuration 1.0.5 — RTX 3080 Ti FE tuning suite

## Français

Première version publique de la suite de réglage pour **NVIDIA GeForce RTX 3080 Ti
Founders Edition** sous Windows 11, sans MSI Afterburner.

### Contenu

- `nvidiaconf.msi` — installation complète dans `C:\Nvidia`, démarrage
  automatique, icône dans la zone de notification.
- `nvidiaconf.exe` — exécutable portable (le MSI reste recommandé).

### Fonctionnalités

- **Undervoltage** : courbe tension/fréquence persistante (plateau 1845 MHz à
  831 mV sur cette carte), power limit 320 W.
- **Ventilation** : contrôle des deux canaux en tr/min, profils
  `silence` / `equilibre` / `froid`, mode 0 dB sous 46 °C.
- **Assistant d'optimisation** : recherche automatique de la tension minimale
  stable, avec vérification bit à bit des calculs et marge de sécurité.
- **Protection anti-plantage** : marqueur de test, chien de garde, restauration
  d'urgence, mode sûr au démarrage.
- **Interface en onglets** : Identité, Fréquences, Configuration, Temps réel,
  Capteurs (façon GPU-Z), Rapport.
- **Rapport comparatif HTML** avant/après, consultable dans l'application.
- **Compatibilité** : refus explicite sur matériel non supporté ; le MSI exige
  un pilote NVIDIA.

### Nouveautés 1.0.5

- **Éditeur corrigé** : « iramone » (et non « irame »).
- **Fermeture automatique** de l'application et du service de ventilation avant
  toute installation ou désinstallation : Windows ne demande plus de fermer
  `nvidiaconf.exe`, et le programme ne reste plus actif après coup.
- Version MSI 1.0.5.0.

### Nouveautés 1.0.4

- **Version MSI réelle** (1.0.4.0) : les anciennes installations sont remplacées
  au lieu de s'empiler. Plus de doublons dans « Applications et fonctionnalités ».
- **Lancement automatique** de l'application à la fin de l'installation.
- Correctif : la désinstallation ne demande plus de fermer l'application
  plusieurs fois.

### Nouveautés 1.0.3

- Icône de la zone de notification épinglée par défaut ; menu « Épingler l'icône
  dans la barre » pour forcer sa promotion (Windows 11 masque les nouvelles icônes).
- Instance unique : relancer `nvidiaconf.exe` ouvre la fenêtre existante au lieu
  de créer une seconde instance.
- Fenêtre légèrement agrandie (620×680) et révision PCI affichée correctement
  (`0x2208 / 0x153510DE`) ou « n/a ».
- Tests effectués sur Hellfire par capture d'écran de la session utilisateur.

### Nouveautés 1.0.2

- Onglet Rapport : bouton « Actualiser » placé dans un bandeau fixe, il ne
  recouvre plus le texte ni la barre de défilement.
- Un simple clic gauche sur l'icône de la zone de notification ouvre la fenêtre
  (le double-clic fonctionne toujours).

### Nouveautés 1.0.1

- Garde-fou TDR : chaque exécution OpenCL est chronométrée ; au-delà de 1500 ms
  le test s'arrête avant que Windows ne réinitialise le pilote.
- Documentation du risque d'écran noir (PyTorch comme OpenCL).

### Prérequis

Windows 10/11 x64, pilote NVIDIA, .NET Framework 4.8. Python + PyTorch
optionnels (installables depuis l'application).

### Empreintes SHA-256

```
0ae6cb33a89490d368edc46fdb514819161f7eb8ddc7d11a43f2b5327874b079  nvidiaconf.exe
578ac56ac4ef15d2664ab1d65edab90384b28876301a249e2e6d86a2bfc55dc6  nvidiaconf.msi
```

---

## English

First public release of the tuning suite for the **NVIDIA GeForce RTX 3080 Ti
Founders Edition** on Windows 11, without MSI Afterburner.

### Contents

- `nvidiaconf.msi` — full install into `C:\Nvidia`, automatic startup,
  notification-area icon.
- `nvidiaconf.exe` — portable executable (MSI recommended).

### Features

- **Undervolt**: persistent V/F curve (1845 MHz plateau at 831 mV on this card),
  320 W power limit.
- **Fan control**: both channels driven by target RPM, `silence` / `equilibre` /
  `froid` profiles, 0 dB mode below 46 °C.
- **Optimisation wizard**: automatic search for the lowest stable voltage, with
  bit-exact computation checks and a safety margin.
- **Crash protection**: test marker, watchdog, emergency restore, safe mode on
  startup.
- **Tabbed UI**: Identity, Clocks, Configuration, Live, Sensors (GPU-Z style),
  Report.
- **HTML comparative report**, available inside the application.
- **Compatibility**: explicit refusal on unsupported hardware; the MSI requires
  an NVIDIA driver.

### What's new in 1.0.5

- **Fixed publisher**: "iramone" instead of "irame".
- **Automatic shutdown** of the application and the fan service before any
  install or uninstall: Windows no longer asks to close `nvidiaconf.exe`, and
  the program no longer stays alive afterwards.
- MSI version 1.0.5.0.

### What's new in 1.0.4

- **Real MSI version** (1.0.4.0): previous installs are replaced instead of
  stacking. No more duplicate entries in Apps & Features.
- **The application starts automatically** at the end of the installation.
- Fix: uninstall no longer asks to close the application several times.

### What's new in 1.0.3

- Notification icon promoted by default; menu entry "Pin icon in the bar" to
  force promotion (Windows 11 hides new icons).
- Single instance: relaunching `nvidiaconf.exe` brings up the existing window
  instead of creating a second instance.
- Slightly larger window (620×680) and correct PCI revision
  (`0x2208 / 0x153510DE`) or "n/a".
- Tested on Hellfire through user-session screenshots.

### What's new in 1.0.2

- Report tab: the refresh button now sits in a fixed bar, no longer overlapping
  the text or the scrollbar.
- A single left click on the notification icon opens the window (double click
  still works).

### What's new in 1.0.1

- TDR guard: every OpenCL execution is timed; above 1500 ms the test aborts
  before Windows resets the driver.
- Documented black-screen risk (PyTorch and OpenCL).

### Requirements

Windows 10/11 x64, NVIDIA driver, .NET Framework 4.8. Python + PyTorch optional
(installable from the app).

### SHA-256

```
0ae6cb33a89490d368edc46fdb514819161f7eb8ddc7d11a43f2b5327874b079  nvidiaconf.exe
578ac56ac4ef15d2664ab1d65edab90384b28876301a249e2e6d86a2bfc55dc6  nvidiaconf.msi
```
