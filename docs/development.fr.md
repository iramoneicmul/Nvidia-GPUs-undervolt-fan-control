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

---

## 10. Règles d'optimisation (v1.0.39)

### 10.1 Power limit

| Règle | Valeur |
|---|---|
| Marge de sécurité | **+10 W** au-dessus de la consommation réelle en jeu |
| Arrondi | pas de **10 W** supérieur (309 W → 320 W) |
| Plafond | **valeur d'origine du VBIOS**, lue à l'exécution |
| Plancher | `power.min_limit` de la carte |

Le plafond n'est **pas** codé en dur. Les limites sont relevées au démarrage :

```powershell
nvidia-smi --query-gpu=power.min_limit,power.max_limit,power.default_limit --format=csv,noheader,nounits
```

Sur la RTX 3080 Ti FE : `100..400 W, défaut 350 W`. Sur une autre carte, les
valeurs sont différentes et l'outil s'y adapte automatiquement.

Le calcul se fait en deux temps : une mesure courte donne un PL provisoire,
puis la **mesure finale** (plus longue) recale le PL (+10 W). Cela évite de
retenir un PL trop généreux à cause du bruit d'une mesure courte.

### 10.2 Tension

- La recherche part de 843 mV et descend par paliers de 6,25 mV.
- Durée par palier : **600 s** avec `stability.py` en 8192×8192 (PyTorch).
- Marge retenue : **3 %** au-dessus du dernier palier stable, soit ~4 pas.
- **Arrondi obligatoire au palier supérieur** : la courbe NVAPI n'accepte que
  des paliers de 6,25 mV ; demander 830 mV appliquait 826 mV (erreur corrigée
  en v1.0.38).

### 10.3 Fréquence et ventilation

- Fréquence cible : moyenne mesurée sur la charge « jeu » à l'origine, arrondie
  à 15 MHz.
- Ventilation : profil `equilibre` (consigne en tr/min), retour au VBIOS sous
  46 °C avec hystérésis de 4 °C.

### 10.4 Pourquoi 3 % de marge sur la tension

Le test manuel de référence (28 187 itérations sur 15 min) n'avait révélé que
**2 écarts** à 818 mV. Un test automatisé de 10 min (~2 400 itérations) ne peut
pas détecter une anomalie aussi rare : il déclarait 806 mV « stable ». La marge
de 3 % compense cette limite statistique et ramène la consigne autour de
831 mV, valeur validée manuellement.

---

## 11. Historique des corrections (v1.0.31 → v1.0.39)

| Version | Correction |
|---|---|
| 1.0.31 | Test « package » exécuté par l'instance en cours (`NvidiaConf.Package`) |
| 1.0.32 | Contrôle du moteur (PyTorch + `stability.py`) et journal par palier |
| 1.0.33 | `RunCapture()` : lecture asynchrone bornée, fin des `ReadToEnd()` bloquants |
| 1.0.34 | **Blocage résolu** : verrou NVAPI, timer UI suspendu, mesure en thread borné |
| 1.0.35 | Test de stabilité renforcé (8192×8192, 600 s par palier) |
| 1.0.36 | Marge du power limit : +10 W au lieu de +10 % |
| 1.0.37 | Limites de puissance lues sur la carte (fin du 350 W codé en dur) |
| 1.0.38 | Tension arrondie au palier NVAPI supérieur |
| 1.0.39 | Power limit recalculé sur la mesure finale |

### Cause racine du blocage (v1.0.34)

Trois défauts cumulés :

1. `p.StandardOutput.ReadToEnd()` était appelé **avant** `WaitForExit()`, sans
   délai : sous charge, `nvidia-smi` pouvait ne pas rendre la main et bloquer le
   thread indéfiniment.
2. **NVAPI était appelé simultanément** par le rafraîchissement de l'interface
   (timer de 2 s) et par la mesure. `nvapi64.dll` n'est pas réentrant : cela
   provoquait un interblocage.
3. Aucune borne globale : la mesure pouvait bloquer sans jamais rendre la main.

Corrections : verrou unique `NvSensors._sync`, timer de l'interface suspendu
pendant le test, échantillonnage dans un thread avec `Join` borné, et
journalisation de chaque échantillon (`echantillon N : ... MHz / ... W`).

---

## 12. Consignes utilisateur et comparaison (v1.0.47)

### 12.1 Trois consignes dans l'onglet Configuration

| Champ | Rôle | Valeur 0 |
|---|---|---|
| Fréquence cible (MHz) | point de fonctionnement visé | déduite de la mesure d'origine |
| Tension cible (mV) | impose la tension, sans recherche | recherche automatique |
| Power limit max (W) | plafonne le calcul du PL | consommation jeu + 10 W |

Les bornes sont calculées à l'exécution, à partir des caractéristiques de la
carte, et affichées sous les champs :

| Paramètre | Borne basse | Borne haute | Source |
|---|---|---|---|
| Fréquence | 500 MHz | `clocks.max.graphics` | plancher d'exploitation / VBIOS |
| Tension | 700 mV | min(courbe, 1000 mV) | plancher de stabilité / courbe |
| Power limit | `power.min_limit` | `power.default_limit` | nvidia-smi |

Toute valeur hors plage est **refusée** avant le lancement du test :

```
package : valeur refusee - Tension 500 mV hors limites (700-1000 mV).
```

Les consignes peuvent aussi être passées en ligne de commande :

```powershell
C:\Nvidia\nvidiaconf.exe --package --mhz 1845 --mv 831 --pl 320
C:\Nvidia\nvidiaconf.exe --compare
```

Les valeurs transitent par `C:\Nvidia\package-request.txt` : l'événement
`NvidiaConf.Package` ne transporte pas de données, et une seconde instance ne
doit pas être créée.

### 12.2 Tension imposée

Si `--mv` (ou le champ) est renseigné, la recherche est remplacée par **un seul
test de 600 s** à cette tension. Si le test échoue, aucune modification n'est
appliquée : la valeur sûre supérieure est proposée.

### 12.3 Ventilation obligatoire

Après la ligne de base d'origine, les trois profils (`silence`, `equilibre`,
`froid`) sont mesurés sous charge PyTorch pendant 60 s. Le profil retenu est le
**plus silencieux dont la température GPU maximale reste sous
`TempSlowdown − 10 °C`**. Le choix est journalisé et reporté dans le rapport.

### 12.4 Comparaison origine / appliqué

Bouton **« Comparer origine / applique »** (onglet Rapport) : mesure les 4
niveaux de charge en configuration d'origine, puis avec les réglages appliqués,
et affiche un rapport HTML dans la fenêtre.

Dernier résultat (26/10/2026) :

| Charge | Origine | Appliqué | Gain MHz | Gain W |
|---|---|---|---|---|
| Légère | 1953 MHz / 264 W | 1860 MHz / 202 W | −93 | −62 |
| Jeu | 1783 MHz / 349 W | 1828 MHz / 319 W | **+45** | **−30** |
| Soutenue | 1823 MHz / 349 W | 1860 MHz / 295 W | **+37** | **−54** |
| Lourde | 1655 MHz / 349 W | 1634 MHz / 320 W | −21 | −30 |

### 12.5 Barre de progression

Chaque étape affiche une action synthétique et le **temps restant estimé** :

```
Origine - charge Jeu (2/4)  (~6 min restantes)
Ventilation : profil equilibre (2/3)  (~2 min restantes)
Tension 831 mV (3/7)  (~44 min restantes)
Package - charge Lourde (4/4)  (~4 min restantes)
```

### 12.6 Historique complémentaire

| Version | Ajout |
|---|---|
| 1.0.40 | Champs tension et power limit cible |
| 1.0.41 | Options `--mv` et `--pl` |
| 1.0.42 | Planchers de sécurité (500 MHz, 700 mV) |
| 1.0.43 | `MessageBox` désactivée hors session interactive |
| 1.0.44 | Transmission des consignes CLI par fichier |
| 1.0.45 | Bouton de comparaison + temps restant |
| 1.0.46 | Profil de ventilation reporté dans le rapport |
| 1.0.47 | Déclencheur `--compare` |

---

## 13. Les 15 améliorations (v1.1.0 → v1.3.0)

### 13.1 Fiabilité

| # | Amélioration | Implémentation |
|---|---|---|
| 1 | Erreurs CUDA | `stability.py` compte les exceptions CUDA (`erreurs=N`) ; intégrées au verdict |
| 2 | Erreurs Xid | `CountXid()` lit le journal System, fournisseur `nvlddmkm` (ID 13, 31, 43, 153) sur la durée du test |
| 3 | Restauration garantie | le service surveille `autotune-inprogress.json` ; au-delà de 4 min sans mise à jour, il réapplique `plateau-active.json` et le PL de `power-limit.txt` |
| 4 | Sauvegarde horodatée | `backup/AAAAMMJJ-HHMMSS-package/` contient courbe, ventilation, profil de fréquences et PL |
| 5 | Journal rotatif | `nvidiaconf.log` basculé en `.log.1` au-delà de 2 Mo |

Un palier est déclaré stable seulement si **écarts = 0, erreurs CUDA = 0 et Xid = 0**.

### 13.2 Pertinence

| # | Amélioration | Implémentation |
|---|---|---|
| 6 | Dichotomie | 4 tests au lieu de 7 (référence haute + `log2(N)`), soit ~40 min au lieu de 70 |
| 7 | Fréquence maximale | paliers de +15 MHz à tension fixée, 240 s par palier, arrêt au premier échec |
| 8 | PL sur 10 min | `MeasureDetailed(py, 2048, 600)` puis marge +10 W |
| 9 | Plancher de sécurité | tension retenue ≥ 88 % de la tension d'origine en charge |

### 13.3 Ergonomie

| # | Amélioration | Implémentation |
|---|---|---|
| 10 | Graphique temps réel | composant `MiniGraph` (température, watts, MHz), 150 points |
| 11 | Export CSV | bouton dans l'onglet Capteurs → `export-AAAAMMJJ-HHMMSS.csv` |
| 12 | Notifications | `NotifyIcon` : température ≥ `TempSlowdown − 5 °C`, mise à jour disponible |
| 13 | Historique | `history.txt` (une ligne par configuration) + fenêtre avec restauration |

### 13.4 Industrialisation

| # | Amélioration | Implémentation |
|---|---|---|
| 14 | Vérification de version | `CheckForUpdates()` interroge l'API GitHub au démarrage ; notification + entrée de menu |
| 15 | Non-régression et signature | `tools/test-regression.ps1` (19 contrôles) et `tools/sign-release.sh` (SHA256 + GPG) |

### 13.5 Nouveaux fichiers d'exploitation

| Fichier | Rôle |
|---|---|
| `power-limit.txt` | dernier PL appliqué, utilisé par le watchdog |
| `history.txt` | historique des configurations (séparateur `\|`) |
| `profiles-apps.json` | ventilation par application |
| `backup/` | sauvegardes horodatées avant chaque test |
| `export-*.csv` | export des statistiques de capteurs |
| `comparatif-*.html` | rapports de comparaison origine / appliqué |

### 13.6 Ventilation par application

`C:\Nvidia\profiles-apps.json` :

```json
{
  "cyberpunk2077.exe": "froid",
  "bf2042.exe": "froid",
  "notepad.exe": "silence"
}
```

Le service vérifie toutes les 5 s si un de ces processus tourne et applique le
profil correspondant (testé : « profil par application : froid »).

### 13.7 Tests de non-régression

```powershell
powershell -ExecutionPolicy Bypass -File D:\opencode\test-regression.ps1
```

Couverture : fichiers installés, service et tâches, cohérence carte (PL,
température), limites lues, taille du journal, génération du rapport, refus
d'une consigne hors limites. Dernier passage : **19 OK, 0 échec**.

### 13.8 Signature des releases

```bash
./sign-release.sh /chemin/vers/release
```

Produit `SHA256SUMS` et, si une clé GPG existe, `SHA256SUMS.asc`.

---

## 14. Onglet GPU-Unlock (v1.4.0 / 1.4.1)

### 14.1 Ce qu'est gpu-unlock

[Zagitta/gpu-unlock](https://github.com/Zagitta/gpu-unlock) est un **module
noyau Linux** chargé dans l'initramfs, **avant** que le pilote `nvidia` ne
prenne la carte. Il écrit les sélecteurs de vitesse (SS0 `0x88888888`, SS1
`0x8`) et effectue un FLR. Effet : **doublement du débit d'émission FP/BF16**
sur GA102, GA103, GA104 et GA106.

Gains publiés (3090 Ti, 450 W) :

| Entrée | Avant | Après |
|---|---|---|
| FP16 | 87,46 TFLOPS | 118,39 TFLOPS |
| BF16 | 87,50 TFLOPS | 157,41 TFLOPS |
| TF32 | 42,58 TFLOPS | 82,61 TFLOPS |

### 14.2 Pourquoi l'activation Windows est impossible

| Point | Constat |
|---|---|
| Type de composant | module noyau Linux, pas un service ni un pilote Windows |
| Moment d'action | initramfs, avant le binding du pilote NVIDIA |
| API disponible | aucune : rien à appeler depuis une application Windows |
| Port Windows | `impacifist/Windows-GPU-UNLOCK`, **chargeur UEFI expérimental** |
| Prérequis du port | Secure Boot désactivé, BitLocker désactivé, ESP dédiée ≥ 1 Gio, build local (clang/lld 20.1.8, 5 firmwares) |
| Profils validés | RTX 3060 12 Go, 3070 Ti, 3090 FE, 3090 Ti |
| RTX 3080 Ti | **aucun profil validé** |

Le bouton « Activer gpu-unlock » **n'active donc rien** : il explique la
situation, liste les profils existants et renvoie vers les projets. Il a été
conçu ainsi volontairement : un bouton qui prétendrait activer un module
absent serait trompeur.

### 14.3 Ce que fait réellement l'onglet

1. **État de compatibilité** : carte, architecture, nature du moteur, profils
   Windows validés, statut de la carte installée.
2. **Boutons** : Activer (explication), Désactiver (explication), Mesurer les
   performances, Ouvrir le projet, Port Windows.
3. **Benchmark réel** (`unlock-bench.py`) : quatre multiplications matricielles
   8192³ de 5 s (FP16, BF16, TF32, FP32) et calcul des TFLOPS.
4. **Chiffrage du gain** : débit actuel, potentiel déverrouillé (+100 %), gain
   public observé (×1,88 à ×1,95) et rappel du power limit.

### 14.4 Résultats mesurés sur Hellfire (RTX 3080 Ti, 320 W)

| Précision | Débit mesuré | Potentiel déverrouillé |
|---|---|---|
| FP16 | 71,7 TFLOPS | ~143 TFLOPS |
| BF16 | 73,2 TFLOPS | ~146 TFLOPS |
| TF32 | 36,6 TFLOPS | ~73 TFLOPS |
| FP32 | 22,6 TFLOPS | non concerné |

Deux réserves importantes :

1. **Le power limit reste le facteur limitant** : à 320 W, la carte sature
   énergétiquement avant de saturer l'issue rate. Le gain réel serait donc
   inférieur à +100 % sans augmenter le PL.
2. **Aucun gain en jeu** : les jeux utilisent FP32, que le déblocage ne touche
   pas. Le bénéfice concerne l'IA et le calcul FP16/BF16.

### 14.5 Fichiers

| Fichier | Rôle |
|---|---|
| `unlock-bench.py` | benchmark FP16/BF16/TF32/FP32 (embarqué dans le MSI) |
| `unlock-bench.txt` | dernier résultat (`RESULTAT taille=... FP16=...`) |

Commande directe équivalente :

```powershell
python C:\Nvidia\unlock-bench.py 8192 C:\Nvidia\unlock-bench.txt
```

### 14.6 Activation réelle (v1.5.0 → 1.5.2)

L'onglet ne se contente plus d'expliquer : il **pilote le chargeur UEFI** du
port Windows lorsque celui-ci est présent.

| Action | Commande exécutée |
|---|---|
| Activer | `powershell -File <port>\internal-boot\Headless.ps1 -EnableAutomatic -ConfirmedSuccess` |
| Désactiver | `powershell -File <port>\internal-boot\Headless.ps1 -RestoreWindowsFirst` |
| Vérifier | `Confirm-SecureBootUEFI`, `Get-BitLockerVolume C:`, `bcdedit /enum firmware` |
| Choisir le port | `FolderBrowserDialog`, chemin mémorisé dans `gpu-unlock.txt` |

**Garde-fous** : confirmation explicite avant toute modification de
l'amorçage, refus si Secure Boot est actif, refus si `Headless.ps1` est absent,
message indiquant les profils validés et le statut de la carte installée.

**Cycle d'état** :

```
desactive --(Activer)--> ACTIVE (attente redemarrage) --(reboot + Mesurer)--> verdict
```

Le verdict compare le débit mesuré à la **référence verrouillée** :

| Gain FP16 | Verdict affiché |
|---|---|
| > 50 % | « déblocage détecté » |
| 10 – 50 % | « gain partiel, à confirmer » |
| < 10 % | « pas de déblocage détecté » |

### 14.7 Fichier d'état

`C:\Nvidia\gpu-unlock.txt` :

```
enabled=0
pending=0
port=C:\Nvidia\gpu-unlock
last=
ref_fp16=71.51
ref_bf16=73.11
ref_tf32=36.65
last_fp16=0.00
last_bf16=0.00
last_tf32=0.00
```

### 14.8 Ligne de commande

```powershell
C:\Nvidia\nvidiaconf.exe --unlock-status
C:\Nvidia\nvidiaconf.exe --unlock-bench --reference
C:\Nvidia\nvidiaconf.exe --unlock-bench
```

`--unlock-status` journalise les cinq contrôles de prérequis ; `--unlock-bench`
mesure et enregistre soit la référence, soit la mesure courante.

### 14.9 État constaté sur Hellfire (26/10/2026)

```
[OK]   Secure Boot         : desactive
[OK]   BitLocker C:        : Off
[----] Port Windows        : absent (C:\Nvidia\gpu-unlock)
[OK]   Entree de demarrage : absente
[OK]   Architecture        : GA102/GA104
prerequis : manquants
```

L'activation reste donc bloquée à l'étape « chargeur absent », ce qui est le
comportement attendu tant que le port UEFI n'est pas compilé et déposé.
