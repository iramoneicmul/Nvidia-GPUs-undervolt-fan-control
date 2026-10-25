# NVIDIA Configuration 1.0.1 — RTX 3080 Ti FE tuning suite

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

### Nouveautés 1.0.1

- Garde-fou TDR : chaque exécution OpenCL est chronométrée ; au-delà de 1500 ms
  le test s'arrête avant que Windows ne réinitialise le pilote.
- Documentation du risque d'écran noir (PyTorch comme OpenCL).

### Prérequis

Windows 10/11 x64, pilote NVIDIA, .NET Framework 4.8. Python + PyTorch
optionnels (installables depuis l'application).

### Empreintes SHA-256

```
d55ec7065c793eeedd43aaf87d9da9a5f6158dd0abdbcddde8b3c81c2dd1c58b  nvidiaconf.exe
90a1da65c8e180356a0303df49b30635a1f4e9b093578a97629829de6ef4de8e  nvidiaconf.msi
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

### What's new in 1.0.1

- TDR guard: every OpenCL execution is timed; above 1500 ms the test aborts
  before Windows resets the driver.
- Documented black-screen risk (PyTorch and OpenCL).

### Requirements

Windows 10/11 x64, NVIDIA driver, .NET Framework 4.8. Python + PyTorch optional
(installable from the app).

### SHA-256

```
d55ec7065c793eeedd43aaf87d9da9a5f6158dd0abdbcddde8b3c81c2dd1c58b  nvidiaconf.exe
90a1da65c8e180356a0303df49b30635a1f4e9b093578a97629829de6ef4de8e  nvidiaconf.msi
```
