# Développement de nvidiaconf

Guide de reprise du développement : sources, compilation, tests et publication.

---

## 1. Vue d'ensemble

`nvidiaconf` est une suite Windows (C# / .NET Framework 4.8) qui pilote une carte
NVIDIA via NVAPI :

| Brique | Rôle | Fichier source |
|---|---|---|
| Interface + moniteur | fenêtre à onglets, tray, capteurs, rapport | `nvidiaconf.cs` |
| Assistant d'optimisation | recherche de la tension minimale stable | `autotune.cs` |
| Mesure des fréquences | 4 niveaux de charge, origine/optimisé | `freq.cs` |
| Onglet Capteurs + test ventilation | valeurs min/max, 3 profils | `sensors.cs` |
| Rapport HTML | comparatif avant/après | `report.cs` |
| Noyau de test GPU | charge et vérification bit à bit | `OpenClTest.cs` |
| Détection de compatibilité | 4 capacités NVAPI | `compat.cs` |
| Service de ventilation | régulation en tr/min | `nvfan.cs` |
| Scripts d'exploitation | application, tâches, service | `*.ps1` |

Trois mécanismes persistent au démarrage :

| Élément | Mécanisme |
|---|---|
| Courbe V/F + power limit | tâche planifiée `NvidiaUndervolt` |
| Courbe de ventilation | service Windows `NvidiaFanCurve` |
| Interface | tâche planifiée `NvidiaConf` (interactive, `--tray`) |

L'application est **auto-suffisante** : à chaque lancement elle vérifie le power
limit, la courbe, le service et les tâches, et recrée ce qui manque
(`EnsureConfigurationApplied` et `EnsureTasks` dans `nvidiaconf.cs`).

---

## 2. Arborescence

```
/home/iramone/Documents/OpenCode_Docs/Nvidia/
├── README.md                     vue d'ensemble du projet
├── DEVELOPPEMENT.md              ce guide
├── undervolt-rtx3080ti-sans-afterburner.md
├── ventilation-rtx3080ti-fe.md
├── optimisation-1860mhz.md       historique des mesures et décisions
├── nvidiaconf.exe                dernière version compilée
├── nvidiaconf.msi                dernier installeur
├── src/                          sources complètes (34 fichiers)
└── tools/
    ├── build.ps1                 chaîne de compilation
    └── gen-wxs.py                générateur du fichier WiX
```

Sur la machine cible :

```
C:\Nvidia          installation et fonctionnement uniquement (32 fichiers)
D:\opencode\Nvidia atelier temporaire, effaçable sans risque
```

---

## 3. Prérequis de compilation

| Outil | Emplacement | Remarque |
|---|---|---|
| `csc.exe` (.NET Framework 4.8) | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\` | inclus dans Windows |
| WiX Toolset 3.14 | `D:\opencode\Nvidia\Nvidia_old\tools\wix\` | à retélécharger si absent |
| Python 3.10+ | `%LOCALAPPDATA%\Programs\Python\Python310\` | pour `gen-wxs.py` |

WiX 3.14 (binaires, aucune installation) :
<https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip>

---

## 4. Compilation

```powershell
# depuis Hellfire, en administrateur
powershell -NoProfile -ExecutionPolicy Bypass `
  -File D:\opencode\Nvidia\build.ps1 -Version 1.0.20
```

Le script :

1. prépare `D:\opencode\Nvidia\build\src` ;
2. copie la restauration complète (`Nvidia_old`), puis écrase avec `C:\Nvidia`
   si présent (versions les plus récentes) ;
3. recompile `nvfan.exe` puis `nvidiaconf.exe` ;
4. génère `nvidiaconf.wxs` via `gen-wxs.py` ;
5. compile le MSI avec `candle.exe` + `light.exe` ;
6. affiche le SHA-256.

Sortie : `D:\opencode\Nvidia\build\nvidiaconf.msi`

**Ne jamais modifier `C:\Nvidia` pendant la compilation** : le script le lit
seulement.

---

## 5. Règles à respecter

### 5.1 Version du MSI
`Version` doit **toujours être incrémentée** (`1.0.20`, `1.0.21`…). Deux MSI de
même version avec des `ProductCode` différents s'installent **en parallèle** au
lieu de se remplacer (constaté avec 1.0.16).

### 5.2 Fichiers de configuration
Ne pas remettre `NeverOverwrite="yes"` sur les fichiers de configuration : le MSI
ne les réinstallerait plus s'ils ont été supprimés manuellement. La sauvegarde
(`C:\Nvidia\backup`) est restaurée après `InstallFiles` par `setup-choice.ps1`.

### 5.3 Fermeture avant installation
L'action `KillApp` exécute `taskkill nvidiaconf.exe`, arrête puis supprime le
service avant `InstallValidate`. Sans elle, Windows demande de fermer
l'application et laisse des fichiers verrouillés.

### 5.4 Démarrage au logon
Une **seule** méthode doit lancer l'application : la tâche `NvidiaConf`. L'ancienne
entrée `Run` est supprimée par le MSI. Deux lancements déclenchent le mécanisme de
réouverture et affichent la fenêtre.

### 5.5 Fenêtre au démarrage
En mode `--tray`, le formulaire est créé avec `Opacity = 0` puis masqué sur
`Shown`. `Application.Run(form)` seul réafficherait la fenêtre.

### 5.6 Chien de garde de l'auto-tuning
Désactivé : `nvidia-smi` peut renvoyer une sortie vide sous forte charge selon le
contexte du processus, ce qui produisait des faux positifs. La sécurité repose sur
`autotune-inprogress.json` (restauration au démarrage suivant) et sur la
vérification des écarts de calcul.

---

## 6. Tests

```powershell
C:\Nvidia\nvidiaconf.exe --check      # compatibilité (4 capacités)
C:\Nvidia\nvidiaconf.exe --report     # rapport HTML
C:\Nvidia\nvidiaconf.exe --freqtest   # fréquences (8 × 1 min)
C:\Nvidia\nvidiaconf.exe --fantest    # ventilation (3 × 1 min)
C:\Nvidia\nvidiaconf.exe --autotune   # optimisation (7 × 4 min)
```

Toujours lancer avec `Start-Process ... -Wait` depuis SSH : sans attente, le
processus est tué à la fermeture de la session.

Cycle d'installation à valider :

```powershell
msiexec /i nvidiaconf.msi /qn /norestart
msiexec /x <ProductCode> /qn /norestart
```

Contrôles après installation : une seule entrée, `Publisher = iramone`, 32
fichiers dans `C:\Nvidia`, tâches `NvidiaConf` + `NvidiaUndervolt`, service
`NvidiaFanCurve` démarré, courbe et power limit appliqués.

---

## 7. Publication

```bash
gh release create v1.0.20 --title "NVIDIA Configuration 1.0.20 (FR/EN)" \
  --notes-file NOTES.md nvidiaconf.exe nvidiaconf.msi
```

Dépôt : <https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control>

Mettre à jour les empreintes SHA-256 dans `README.md` et `README.fr.md`.

---

## 8. Points connus

| Point | Détail |
|---|---|
| OpenCL plafonne à ~240 W | moins sévère que PyTorch (~300 W) ; PyTorch reste prioritaire |
| Auto-tuning ~30 min | 7 paliers de 4 min |
| Bruit non mesurable | le programme raisonne en tr/min |
| `nvidiaconf.log` et `fan-curve.log` | journaux recréés à chaque exécution |
| Sauvegarde des réglages | `C:\Nvidia\backup`, créée uniquement lors d'une désinstallation avec conservation |

---

## 9. Version affichée dans l'interface

La version affichée dans le premier onglet (Identité) provient de
`Application.ProductVersion`, donc de l'attribut d'assembly généré par
`build.ps1` :

```powershell
$verFile = Join-Path $stage 'VersionInfo.cs'
[assembly: AssemblyVersion("<Version>.0")]
[assembly: AssemblyFileVersion("<Version>.0")]
```

Conséquence : **la version de l'exécutable est toujours identique à celle du
MSI**, sans risque d'oubli. Il suffit de passer `-Version` au script de build.

Vérification rapide :

```powershell
(Get-Item C:\Nvidia\nvidiaconf.exe).VersionInfo.FileVersion
```
