# Guide technique

Détail de la méthode et de l'implémentation. Complément au `README.fr.md`.

---

## 1. Contexte matériel et logiciel

| Élément | Valeur |
|---|---|
| GPU | GeForce RTX 3080 Ti Founders Edition (GA102, device `0x2208`) |
| VBIOS | `94.02.71.00.ca` |
| Pilote | NVIDIA 621.01 (Windows 11) |
| Limites de puissance | défaut 350 W, min 100 W, max 400 W |
| Limites thermiques | ralentissement 95 °C, coupure 98 °C |
| Fréquences d'origine | base 1365 MHz, boost officiel 1665 MHz, max du domaine 2100 MHz |

La carte possède deux ventilateurs axiaux et deux canaux indépendants.

---

## 2. Architecture

```
Démarrage de Windows
 ├─ Tâche NvidiaUndervolt  (SYSTEM, +30 s)
 │    └─ apply-undervolt.ps1
 │         ├─ snu.exe --in-tuning-file plateau-1845-831.json --no-persist
 │         ├─ nvidia-smi -pl 320
 │         └─ vérification + journal
 └─ Service NvidiaFanCurve  (LocalSystem, automatique)
      └─ nvfan.exe service
           ├─ relit fan-curve.txt toutes les 2 s
           ├─ vise un régime commun sur les deux canaux
           └─ réapplique la courbe V/F si NVAPI est réinitialisé
```

Les deux tournent en session 0, sans fenêtre, avec redémarrage automatique par
le SCM (5 s / 10 s / 30 s, remise à zéro quotidienne).

---

## 3. Fonctions NVAPI utilisées

| Usage | Fonction | ID |
|---|---|---|
| Initialisation | `NvAPI_Initialize` | `0x0150E828` |
| Énumération | `NvAPI_EnumPhysicalGPUs` | `0xE5AC921F` |
| Capteurs internes | `NvAPI_GPU_GetAllTempsEx` | `0x65FE3AAD` |
| Infos ventilateurs | `NvAPI_GPU_ClientFanCoolersGetInfo` | `0xFB85B01E` |
| État ventilateurs | `NvAPI_GPU_ClientFanCoolersGetStatus` | `0x35AED5E8` |
| Contrôle (lecture) | `NvAPI_GPU_ClientFanCoolersGetControl` | `0x814B209F` |
| Contrôle (écriture) | `NvAPI_GPU_ClientFanCoolersSetControl` | `0xA58971A5` |
| API classique | `NvAPI_GPU_GetCoolerSettings` | `0xDA141340` — **non supportée sur RTX** |

### 3.1 Pourquoi l'API classique échoue

Sur les cartes RTX, `NvAPI_GPU_GetCoolerSettings` et
`NvAPI_GPU_SetCoolerLevels` renvoient `NVAPI_INCOMPATIBLE_STRUCT_VERSION` (−9)
quelle que soit la taille de structure testée. Le chemin RTX est
`ClientFanCoolers*`, avec les structures suivantes :

```c
typedef struct {
    NvU32 coolerId;
    NvU32 level;         /* pourcentage */
    NvU32 controlMode;   /* 0 = auto, 1 = manuel */
    NvU32 reserved[8];
} FanCoolersControlEntry;

typedef struct {
    NvU32 version;       /* sizeof | (1 << 16) */
    NvU32 unknown;
    NvU32 count;
    NvU32 reserved[8];
    FanCoolersControlEntry entries[32];
} FanCoolersControl;
```

### 3.2 Jonction mémoire et hotspot

`NvAPI_GPU_GetThermalSettings` ne renvoie que le capteur GPU. La fonction non
documentée `NvAPI_GPU_GetAllTempsEx` (version de structure **2**) expose les
capteurs internes, comme le font GPU-Z et LibreHardwareMonitor :

| Indice | Capteur |
|---|---|
| `Temperatures[0]` | GPU |
| `Temperatures[1]` | hotspot |
| `Temperatures[9]` | jonction mémoire (RTX 30xx) |

Les valeurs sont en 1/256 °C, et un masque par capteur doit être fourni
(`0x01`, `0x02`, `0x200`).

---

## 4. Courbe tension/fréquence

### 4.1 Courbe de référence

La courbe stock est capturée **au repos et à froid** (31 °C ici), car la courbe
du pilote dérive avec la température. `snu.exe set-reference-curve` la stocke
une fois ; le tuning est ensuite calculé à partir de cette référence stable.

### 4.2 Génération d'un plateau

Pour une fréquence cible `F` à la tension `V`, chaque ancre de tension ≥ `V`
reçoit :

```
offset(ancre) = F − fréquence_stock(ancre)
```

ce qui aplatit la courbe à partir de `V`. Les ancres sous `V` gardent un offset
nul : la courbe sous le cap reste d'origine.

Le pas matériel est de **15 MHz** : 1850 MHz n'existe pas. Le pilote arrondit
1850 à 1860 ; 1845 a donc été choisi (le plus proche en dessous) pour garder
une marge.

### 4.3 Application

```
snu.exe --in-tuning-file plateau-1845-831.json --no-persist
nvidia-smi -pl 320
```

`--no-persist` est volontaire : la persistance est assurée par la tâche
planifiée, pas par la tâche de logon de l'outil (qui ferait doublon).

---

## 5. Protocole de test de stabilité

L'objectif était de trouver la tension la plus basse réellement exempte
d'erreurs, et pas seulement « qui ne plante pas ».

Chaque palier a subi **240 s** de charge (900 s pour le candidat final) :

- 15 matmuls fp16 4096×4096 enchaînés, puis une convolution 2D ;
- chaque résultat comparé **bit à bit** à une référence calculée au début
  (cuBLAS est déterministe à entrées et algorithme fixes : tout écart signale
  une erreur de calcul matérielle) ;
- tension, puissance, fréquence et température relevées toutes les 3 s ;
- journaux système contrôlés (`nvlddmkm`, `Display`, `LiveKernelEvent`).

Un palier est validé si : **aucun résultat divergent, aucun incident pilote, et
la fréquence cible tenue**.

### 5.1 Résultats

| Tension | Itérations | Résultats divergents | Incidents | Verdict |
|---|---|---|---|---|
| 843 mV | 7 542 | 0 | aucun | validé |
| 837 mV | 7 553 | 0 | aucun | validé |
| 831 mV | 7 532 | 0 | aucun | **retenu** |
| 825 mV | 28 187 (15 min) | 0 | aucun | validé (sans marge) |
| 818 mV | 7 562 | **2** (max 2,6) | aucun | **échec** |

**Limite du silicium : entre 818 et 825 mV.** 831 mV a été retenu pour la marge
de sécurité : 6 mV coûtent environ 0,3 % de performance et suppriment le risque
d'un plantage lors d'une longue session de jeu.

---

## 6. Power limit

Mesures sur charge type jeu à 825 mV :

| Power limit | Puissance moyenne | Fréquence moyenne | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|
| 320 W | 313,8 W | 1821 MHz | 75,3 °C | 85,0 °C | 2059 tr/min |
| 310 W | 307,2 W | 1801 MHz | 76,8 °C | 87,3 °C | 2222 tr/min |
| 300 W | 299,1 W | 1770 MHz | 76,1 °C | 87,1 °C | 2254 tr/min |

320 W est la seule valeur qui ne coûte pas de fréquence : en dessous, le power
limit devient le facteur limitant. 400 W a été testé et écarté : +50 W de
chaleur, jonction mémoire à 94–96 °C, et aucun gain en jeu (270–320 W).

---

## 7. Pilotage des ventilateurs

### 7.1 Calibration

Les deux canaux répondent différemment :

| Consigne | Canal 1 (3800 tr/min max) | Canal 2 (3400 tr/min max) |
|---|---|---|
| 30 % | 990 tr/min | 1000 tr/min |
| 40 % | 1402 tr/min | 1342 tr/min |
| 55 % | 2000 tr/min | 1857 tr/min |
| 70 % | 2601 tr/min | 2371 tr/min |

Appliquer le même pourcentage désynchroniserait les ventilateurs (jusqu'à
230 tr/min d'écart), produisant un battement audible. Le service vise donc un
**régime commun** et le convertit par canal :

```
canal 1 : pct = 30 + (tr/min − 990) / 40
canal 2 : pct = 30 + (tr/min − 1000) / 34
```

avec une correction adaptative bornée (±15 points) qui compense l'usure.

### 7.2 Courbe

`fan-curve.txt` est une simple liste `température=tr/min`, relue
automatiquement quand le fichier change :

```
auto_below=46      # sous ce seuil, rend la main au VBIOS (mode 0 dB)
hysteresis=4       # retour en auto sous 42 °C
failsafe_c=85      # 100 % au-delà
failsafe_mem_c=95  # 100 % si la jonction mémoire dépasse ce seuil
50=1050
56=1300
62=1550
66=1800
70=2100
73=2400
76=2800
...
```

Le contrôle manuel ne descend pas sous 30 % (~990 tr/min) : l'état 0 dB
(ventilateurs arrêtés) n'est accessible qu'en rendant la main au VBIOS.

### 7.3 Profils

| Profil | Équilibre à 320 W | Ventilos | Mémoire |
|---|---|---|---|
| `silence` | ~81 °C | ~2300 tr/min | ~96 °C |
| `equilibre` | ~79 °C | ~2500 tr/min | ~94 °C |
| `froid` | ~78 °C | ~2770 tr/min | ~91 °C |

Bascule à chaud avec `set-fan-profile.ps1`.

---

## 8. Détails de persistance

| Événement | Comportement |
|---|---|
| Démarrage Windows | service automatique ; tâche à +30 s |
| Ouverture de session | déclencheur de la tâche (filet) |
| Reprise de veille | déclencheur sur `Power-Troubleshooter` id 1 |
| Mise à jour du pilote | handles NVAPI invalidés → le service se réinitialise et réapplique la courbe |
| Plantage du service | redémarrage par le SCM (5 s / 10 s / 30 s) |
| Redémarrage | les deux mécanismes repartent |

Le service de ventilation détecte les rechargements de pilote (erreurs NVAPI)
et relance `apply-undervolt.ps1`, ce qui couvre une mise à jour sans redémarrage.

---

## 9. Application de surveillance

`nvidiaconf.exe` (C:\Nvidia) est une application WinForms de zone de
notification :

- lecture unique des données statiques (identité, limites) via `nvidia-smi` et NVAPI ;
- rafraîchissement toutes les 2 s (fréquences, tension, puissance, températures, ventilateurs) ;
- affichage de la configuration appliquée en lisant `undervolt.log` et `fan-curve.txt` ;
- menu : appliquer la configuration, changer de profil, ouvrir le dossier, quitter.

Elle est lancée à l'ouverture de session par la tâche `NvidiaConf` (session
interactive), avec `--tray` pour démarrer masquée.

---

## 10. Limites

1. **La stabilité n'est jamais prouvée, seulement non démentie.** 28 187 calculs
   vérifiés sur 15 minutes à 78,8 °C constituent une preuve solide, pas une
   garantie. Le vieillissement du silicium et une température ambiante plus
   élevée peuvent révéler des faiblesses plus tard.
2. **Un seul exemplaire.** 831 mV est la valeur de cette carte, pas une valeur
   universelle.
3. **Le gain est surtout thermique.** Sous un power limit serré, baisser la
   tension ne fait que retirer des pertes ; cela ne crée pas de puissance.
4. **La jonction mémoire n'est pas refroidie différemment.** La maintenir sous
   ~90 °C est la raison pour laquelle la courbe reste ferme au-delà de 76 °C.

---

## 11. Reproduction

1. Installer `simple-nvidia-undervolt`, capturer la courbe stock au repos.
2. Générer les fichiers JSON de plateau pour les tensions candidates.
3. Dérouler le protocole de stabilité ci-dessus, palier par palier, en descendant.
4. Retenir le dernier palier validé, plus un pas de marge.
5. Choisir le power limit juste au-dessus de la charge réelle en jeu.
6. Installer le service de ventilation et régler la courbe selon le budget sonore.
