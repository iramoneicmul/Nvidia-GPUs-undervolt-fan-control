# Installation guide / Guide d'installation

**English** | [Version française](INSTALL.fr.md)

---

## 1. Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 or 11, 64-bit |
| GPU | NVIDIA card exposing NVAPI fan control, V/F curve and power limit |
| Driver | NVIDIA display driver installed (`nvlddmkm` service present) |
| Runtime | .NET Framework 4.8 (included in Windows 10/11) |
| Optional | Python 3.10+ and PyTorch, for the strictest stability test |

## 2. Download

Latest release:
<https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control/releases/latest>

Download either:

- `nvidiaconf.msi` — recommended, installs everything;
- `nvidiaconf.exe` — portable executable only.

Verify the checksum (PowerShell):

```powershell
Get-FileHash .\nvidiaconf.msi -Algorithm SHA256
```

## 3. Install (MSI)

1. Double-click `nvidiaconf.msi`.
2. If no NVIDIA driver is detected, the installer stops with an explicit
   message. Nothing is installed.
3. Otherwise, files go to `C:\Nvidia` and `nvidiaconf.exe --tray` is added to
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
4. At the next logon, the green icon appears in the notification area.

## 4. Install (portable)

1. Create `C:\Nvidia`.
2. Copy `nvidiaconf.exe` there.
3. Run it. It looks for its data in `C:\Nvidia`, falling back to
   `C:\ProgramData\NvidiaUndervolt`.

The portable mode does not install the undervolt task or the fan service; use
**Réappliquer au démarrage** in the application to create them.

## 5. First run

1. The application checks compatibility (NVAPI, fans, V/F curve, power limit)
   and refuses to run if any is missing.
2. The **Identité** tab shows the card, VBIOS, driver and PCIe link.
3. **Appliquer la configuration** applies the stored profile.
4. **Trouver la configuration optimale** runs the full tuning wizard
   (~30 min, 7 voltage tiers).
5. **Mesurer les fréquences** records stock vs optimised clocks per load level.
6. **Tester les profils de ventilation** measures the three fan profiles.

## 6. Tray icon

- **Single left click**: opens the window.
- **Right click**: menu (show, refresh, apply, fan profile, report, quit).

## 7. Uninstall

- MSI: *Apps & Features* → *NVIDIA Configuration* → Uninstall, or
  `msiexec /x nvidiaconf.msi`.
- Before uninstalling, run **Restaurer l'origine** to remove the scheduled task
  and the fan service.
- Manual cleanup, if needed:

```powershell
powershell -File C:\Nvidia\rollback.ps1
sc.exe delete NvidiaFanCurve
```

## 8. Troubleshooting

| Symptom | Action |
|---|---|
| "Matériel incompatible" | card does not expose fan control or V/F curve; nothing can be done in software |
| Installer aborts (code 1603) | no NVIDIA driver detected |
| No tray icon | check the `Run` key and the `NvidiaConf` task |
| Fans stuck at a fixed speed | the service is stopped; start `NvidiaFanCurve` |
| Black screen during a test | TDR; reboot, the app restores stock automatically at next start |
