# RTX 3080 Ti FE — undervoltage silencieux et pilotage des deux ventilateurs

Réglage persistant et sans interface pour la **NVIDIA GeForce RTX 3080 Ti
Founders Edition** sous Windows 11 — sans MSI Afterburner, sans suite
d'overclocking résidente. Tout passe par NVAPI, une tâche planifiée et un
service Windows léger.

Résultat : **plus de performance qu'à l'origine, 30 W de moins, une mémoire
plus froide et des ventilateurs plus silencieux.**

---

## Configuration finale

| Réglage | Valeur |
|---|---|
| Courbe tension/fréquence | plateau **1845 MHz dès 831 mV** |
| Power limit | **320 W** (défaut VBIOS : 350 W, max : 400 W) |
| Ventilation | deux canaux, courbe en tr/min (profil `equilibre`) |
| Fréquence mémoire | stock (9501 MHz) |

---

## Résultats mesurés

Charge type jeu (convolutions + matmuls, 4 min), face au VBIOS d'origine :

| Grandeur | VBIOS d'origine | Cette configuration | Écart |
|---|---|---|---|
| Fréquence GPU | ~1672 MHz | **1827 MHz** | **+155 MHz** |
| Puissance | 349 W | **310 W** | **−39 W (−11 %)** |
| Température GPU | 80,8 °C | **73,5 °C** | −7,3 °C |
| Jonction mémoire | ~96 °C | **83,8 °C** | −12 °C |
| Ventilateurs | 2286 tr/min | **1942 tr/min** | −344 tr/min |

Charge lourde soutenue (matmul fp16 8192²) :

| Configuration | TFLOPS | Puissance | GPU |
|---|---|---|---|
| Origine (350 W) | 65,71 | 349 W | 79,4 °C |
| 850 mV / 1860 MHz (320 W) | 63,75 | 319 W | 78,4 °C |
| **831 mV / 1845 MHz (320 W)** | **64,44** (équilibre, 79,2 °C) | **319 W** | 79,2 °C |

---

## Fonctionnement

Deux mécanismes indépendants, silencieux, exécutés en session 0 :

| Tâche | Mécanisme | Nature |
|---|---|---|
| Courbe V/F + power limit | tâche planifiée `NvidiaUndervolt` | ponctuel, réappliqué aux déclencheurs |
| Courbe de ventilation | service Windows `NvidiaFanCurve` | résident, boucle de 2 s |

Déclencheurs couverts : démarrage de Windows, ouverture de session, reprise de
veille, rechargement du pilote NVIDIA (mise à jour), redémarrage.

### Courbe tension/fréquence

La courbe est écrite avec [`simple-nvidia-undervolt`](https://github.com/vuplea/simple-nvidia-undervolt)
(NVAPI `ClkVfPointsSetControl`). Un fichier de tuning liste, pour chaque ancre
de tension ≥ 831 mV, l'offset `1845 − fréquence_stock`, ce qui produit un
plateau plat.

La limite du silicium a été **mesurée**, pas supposée : chaque palier a subi un
test de charge avec **vérification bit à bit des résultats** (28 187 calculs
vérifiés). 818 mV a produit deux résultats divergents ; 831 mV a été retenu
pour garder une marge de sécurité.

### Pilotage des ventilateurs

La Founders Edition utilise l'API NVAPI **`ClientFanCoolers`** (l'ancienne
`NvAPI_GPU_SetCoolerLevels` renvoie `NVAPI_INCOMPATIBLE_STRUCT_VERSION` sur les
cartes RTX). Deux canaux sont exposés :

| Canal | Plage | Régime max |
|---|---|---|
| id 1 | 30–100 % | 3800 tr/min |
| id 2 | 30–100 % | 3400 tr/min |

Les deux ventilateurs n'ayant pas la même réponse, le service vise un **régime
commun** et le convertit en pourcentage par canal, avec une correction
adaptative bornée. Sous 46 °C, il rend la main au VBIOS (mode 0 dB, arrêt) —
le contrôle manuel ne descend pas sous 30 %.

### Application de surveillance

`nvidiaconf.exe` (dans `C:\Nvidia`) est une petite application de zone de
notification, dans l'esprit de GPU-Z :

- identité : nom, révision, VBIOS, pilote, bus PCI, mémoire vidéo ;
- limites d'origine : fréquences max GPU/mémoire, power limit min/max/défaut,
  limites thermiques ;
- configuration appliquée : undervolt, power limit, profil de ventilation ;
- temps réel : fréquences GPU/mémoire, tension, puissance, températures
  GPU/hotspot/mémoire, les deux ventilateurs, charge GPU.

Double-clic sur l'icône pour ouvrir la fenêtre ; clic droit pour le menu
(appliquer la configuration, changer de profil, ouvrir le dossier, quitter).

---

## Contenu du dépôt

```
README.md                     version anglaise
README.fr.md                  ce fichier
docs/technical-guide.md       détails : structures NVAPI, protocole de test
docs/technical-guide.fr.md    version française
```

Les fichiers déployés (sur la machine) se trouvent dans
`C:\ProgramData\NvidiaUndervolt\` :

| Fichier | Rôle |
|---|---|
| `snu.exe` | moteur NVAPI d'undervolt |
| `plateau-1845-831.json` | courbe V/F active |
| `courbe-stock.json` | référence stock capturée à 31 °C |
| `apply-undervolt.ps1` | applique courbe + power limit, vérifie, journalise |
| `install-task.ps1` | crée la tâche `NvidiaUndervolt` |
| `nvfan.exe` / `nvfan.cs` | contrôle des ventilateurs (console + service) |
| `fan-curve.txt` | courbe de ventilation active |
| `profil-silence.txt`, `profil-equilibre.txt`, `profil-froid.txt` | profils prêts |
| `set-fan-profile.ps1` | bascule de profil à chaud |
| `install-fan-service.ps1` | installe le service `NvidiaFanCurve` |
| `verify.ps1`, `verify-fan.ps1` | diagnostics en lecture seule |
| `rollback.ps1` | retour au stock |

---

## Utilisation

```powershell
# état complet
powershell -File C:\ProgramData\NvidiaUndervolt\verify.ps1
powershell -File C:\ProgramData\NvidiaUndervolt\verify-fan.ps1

# état des ventilateurs
C:\ProgramData\NvidiaUndervolt\nvfan.exe get

# changer de profil (sans redémarrage)
powershell -File C:\ProgramData\NvidiaUndervolt\set-fan-profile.ps1 -Profil equilibre

# réappliquer undervolt + power limit
powershell -File C:\ProgramData\NvidiaUndervolt\apply-undervolt.ps1
```

---

## Précautions

1. **La limite du silicium est propre à chaque carte.** 831 mV fonctionne sur
   cet exemplaire ; ne recopiez pas la valeur à l'aveugle. 818 mV a échoué ici
   avec des erreurs de calcul.
2. **Gardez une marge.** En cas de plantage, de reset du pilote ou d'artefacts,
   remontez d'un palier (837 mV) : cela coûte ~0,3 % de performance.
3. **La mémoire est la vraie contrainte** sur une 3080 Ti FE : elle tourne
   ~10 °C au-dessus du GPU. Le service force 100 % au-delà de 95 °C.
4. **Ne cumulez pas les outils de ventilation.** Un seul programme doit piloter
   les deux canaux (ce service, ou Afterburner, ou FanControl — pas plusieurs).
5. **Dépasser 350 W n'a aucun intérêt ici.** 400 W n'aide que les charges
   synthétiques et ajoute 50 W de chaleur ; en jeu la carte reste sous 320 W.

---

## Assistant d'optimisation intégré

`nvidiaconf.exe` embarque tout le protocole de réglage.

```powershell
C:\Nvidia\nvidiaconf.exe --autotune     # ou le bouton dans la fenêtre
```

1. Lit la **vraie** table de tension dans `courbe-stock.json` (aucune valeur inventée).
2. Pour chaque palier, de 843 mV vers le bas : écrit une courbe plateau,
   l'applique par NVAPI, puis lance un test de stabilité de **240 s** avec
   vérification bit à bit des résultats.
3. S'arrête au premier palier instable.
4. **Marge de sécurité** : conserve le palier *au-dessus* du dernier stable.
5. Écrit `plateau-active.json` et un rapport Markdown horodaté.
6. Applique le power limit.

Une barre de progression et un libellé d'étape indiquent en permanence ce qui
est en cours ; chaque étape est aussi journalisée. La fréquence cible se règle
dans `autotune.ini` (`target_mhz`, pas matériel de 15 MHz).

---

## Compatibilité

L'application refuse de démarrer sur du matériel non supporté, avec un message
explicite. Une carte est compatible seulement si **tout** ce qui suit est vrai :

| Capacité | Vérification |
|---|---|
| Pilote NVIDIA / NVAPI | `NvAPI_Initialize` + énumération du GPU |
| Ventilateurs pilotables | `ClientFanCoolersGetInfo` renvoie au moins un canal |
| Courbe V/F | `snu.exe status` répond pour le GPU |
| Power limit modifiable | `nvidia-smi` indique min < max |

Le MSI applique la même logique à l'installation : il exige la clé du service
`nvlddmkm` et `nvapi64.dll`, et s'arrête avec un message explicite sinon.

---

## Installation par MSI

`nvidiaconf.msi` installe tout dans `C:\Nvidia` et enregistre
`nvidiaconf.exe --tray` dans la clé `Run` machine, pour que l'icône verte
apparaisse dans la zone de notification à l'ouverture de session.

### Protection contre les plantages

Un marqueur de test (`autotune-inprogress.json`) est écrit avant chaque
palier. Pendant le test, le pilote est interrogé toutes les 3 s ; s'il ne
répond plus ou si le GPU dépasse 90 °C, le test est interrompu. Au démarrage
suivant, un marqueur résiduel déclenche la restauration automatique de la
courbe stock et de 350 W, avec un message explicite. La restauration d'urgence
rend aussi les ventilateurs au VBIOS.

### Fenêtre principale

| Élément | Détail |
|---|---|
| Profil de ventilation | sélecteur `silence` / `equilibre` / `froid`, `equilibre` par défaut |
| Fréquences moyennes | 4 niveaux (légère, jeu, soutenue, lourde) × origine/optimisé |
| Bus PCIe | génération et largeur, valeurs actuelle et maximale |
| Power limit | valeur réelle, cible 320 W signalée en cas d'écart |
| Barre de progression | étape en cours, durée, résultat intermédiaire |
| Restauration | courbe stock, 350 W, ventilateurs au VBIOS, tâche et service supprimés |

### Moteur de test OpenCL

Le test de stabilité ne dépend plus de Python. `nvidiaconf.exe` embarque un
noyau OpenCL (multiplication de matrices int32, 2048×2048) : le résultat est
déterministe, la première exécution sert de référence, et chaque exécution
suivante est comparée bit à bit. Toute différence est une erreur de calcul
matérielle. Charge mesurée : ~240 W. Python/PyTorch reste en repli automatique.

### Interface en onglets

| Onglet | Contenu |
|---|---|
| **Identité** | nom, révision, VBIOS, pilote, bus PCI, VRAM, génération et largeur PCIe |
| **Fréquences** | limites d'origine, **tensions min/max de la courbe**, tension du plateau, fréquences moyennes par charge |
| **Configuration** | undervolt appliqué, power limit (avec cible), profil de ventilation, courbe 831 mV |
| **Temps réel** | fréquences, tension, puissance, températures GPU/hotspot/mémoire, ventilateurs, charge |

La zone basse (barre de progression, sélecteur de profil, boutons d'action)
reste visible sous chaque onglet.

### Onglet Capteurs et test des profils

Un tableau temps réel façon GPU-Z affiche chaque valeur avec son minimum et son
maximum depuis le démarrage : fréquences GPU/mémoire, tension, puissance, power
limit, températures GPU/hotspot/mémoire, les deux ventilateurs (tr/min et %),
charge GPU, mémoire utilisée, génération et largeur PCIe.

Le bouton « Tester les profils de ventilation » applique successivement
`silence`, `equilibre` et `froid`, lance 1 minute de charge pour chacun, relève
le régime moyen/maximal et les températures maximales, écrit un rapport
Markdown et restaure `equilibre`.

### Limite connue

Le noyau OpenCL naïf plafonne à **~240 W**, contre **~300 W** avec PyTorch : il
est donc moins sévère. Un passage complet a déclaré tous les paliers stables
jusqu'à 806 mV, alors que PyTorch avait détecté une instabilité à 818 mV.
PyTorch reste donc le moteur prioritaire lorsqu'il est disponible, OpenCL
servant de repli. La référence validée reste **831 mV**.

---

## Matériel / logiciel

- NVIDIA GeForce RTX 3080 Ti Founders Edition (GA102, VBIOS `94.02.71.00.ca`)
- Windows 11, pilote NVIDIA 621.01
- .NET Framework 4.8 (pour `nvfan.exe` et `nvidiaconf.exe`)
- Python uniquement pour les scripts de charge/benchmark, pas au runtime
