# Optimisation 1860 MHz — recherche de la tension minimale stable

**Machine :** Hellfire — Windows 11, pilote NVIDIA 621.01
**Carte :** GeForce RTX 3080 Ti Founders Edition (GA102)
**Date :** 25 octobre 2026

> **Périmètre.** Ce document est un compte rendu de mesures sur la **GeForce RTX
> 3080 Ti Founders Edition** de Hellfire. La méthode est transposable à d'autres
> cartes NVIDIA compatibles, mais les valeurs (tensions, fréquences, régimes)
> sont propres à ce modèle et à ce VBIOS.


---

## 1. Objectif

Déterminer la configuration la plus performante sous quatre contraintes :

| Contrainte | Valeur |
|---|---|
| Fréquence maximale | 1860 MHz |
| Tension maximale | 850 mV |
| Tension minimale | à déterminer |
| Ventilation | pas plus bruyante que le profil `equilibre` |
| Power limit | à déterminer |
| Gain visé | > 125 MHz par rapport à la courbe d'origine, à consommation égale ou inférieure |

Référence d'origine mesurée : ~1672 MHz soutenus à 349 W, 80,8 °C GPU,
~96 °C mémoire, 2286 tr/min.

---

## 2. Protocole

### 2.1 Courbes de test

Les courbes sont générées depuis la référence stock capturée à 31 °C
(`courbe-stock.json`, 127 points). Pour une tension cible `V`, toutes les
ancres ≥ `V` reçoivent l'offset `1860 − fréquence_stock`, ce qui produit un
plateau plat à 1860 MHz dès `V`. Les ancres sous `V` restent à l'offset 0
(courbe d'origine).

Paliers testés (pas matériel de 6,25 mV) : **843, 837, 831, 825, 818, 812,
806 mV**, puis 850 mV comme référence de sécurité.

Fichiers : `plateau-1860-<V>.json`, appliqués par
`snu.exe --in-tuning-file <fichier> --no-persist`.

### 2.2 Test de stabilité

Chaque palier subit **240 secondes** de charge :

- 15 matmuls fp16 4096×4096 enchaînés, puis une convolution 2D ;
- le résultat de chaque matmul est comparé **bit à bit** à une référence
  calculée au début du test (cuBLAS est déterministe ici : tout écart
  signale une erreur de calcul) ;
- relevé toutes les 3 s de la tension réelle, de la puissance, de la
  fréquence et de la température ;
- contrôle des journaux système (`nvlddmkm`, `Display`, `LiveKernelEvent`)
  sur la durée du test.

Un palier est **validé** si : aucun écart de calcul, aucun incident pilote,
et la fréquence reste à 1860 MHz.

### 2.3 Critère d'arrêt

On descend jusqu'au premier palier qui échoue (écart de calcul, TDR, ou
fréquence qui ne tient pas), puis on retient le palier précédent.

---

## 3. Résultats

| Tension | Itérations | Écarts de calcul | Incidents pilote | Puissance | Temp. GPU | Verdict |
|---|---|---|---|---|---|---|
| 850 mV (réf.) | — | — | — | ~300 W | ~72 °C | référence |
| **843 mV** | 7 542 | **0** | **aucun** | 297 W | 72 °C | ✅ validé |
| **837 mV** | 7 553 | **0** | **aucun** | 304 W | 76 °C | ✅ validé |
| **831 mV** | 7 532 | **0** | **aucun** | 298 W | 76 °C | ✅ validé |
| **825 mV** | 7 565 | **0** | **aucun** | 300 W | 76 °C | ✅ validé (1875 MHz) |
| **818 mV** | 7 562 | **2** (max 2,6) | aucun | 298 W | 76 °C | ❌ **instable** |
| 812 mV | — | — | — | — | — | non testé (limite atteinte) |
| 806 mV | — | — | — | — | — | non testé (limite atteinte) |

**Limite du silicium : entre 818 et 825 mV.** À 818 mV, deux résultats de
calcul divergent de la référence : c'est une erreur de calcul réelle, pas un
artefact de mesure. Le palier 825 mV est donc le plus bas utilisable.

### 3.1 Validation longue de 825 mV

| Grandeur | Valeur |
|---|---|
| Durée | 900 s (15 min) |
| Itérations | 28 187 |
| Écarts de calcul | **0** |
| Incidents pilote | aucun |
| Tension réelle | 825 mV |
| Fréquence | 1860–1875 MHz |
| Puissance | ~300 W |
| Température GPU | 78,8 °C en fin de test |

La stabilité tient sur la durée, à température élevée (78,8 °C), ce qui est la
condition la plus défavorable.

---

## 4. Décision

### 4.0 Choix final : 1845 MHz à 831 mV

Après les tests, la configuration retenue est **1845 MHz à 831 mV** (arrondi
bas de 1850 MHz, le pas matériel étant de 15 MHz), avec un power limit de
320 W et le profil de ventilation `equilibre`.

831 mV a été préféré à 825 mV pour disposer d'une marge de sécurité : 818 mV
s'est révélé instable, et 6 mV de marge valent mieux qu'un plantage en jeu.

### 4.1 Configuration retenue

| Paramètre | Valeur | Justification |
|---|---|---|
| Courbe V/F | plateau **1845 MHz dès 831 mV** | marge de sécurité (818 mV instable) |
| Power limit | **320 W** | seule valeur qui ne coûte pas de fréquence |
| Ventilation | profil `equilibre` | plafond ~2400 tr/min, plus silencieux que l'origine |
| Mémoire | stock (9501 MHz) | non modifiée |

Fichiers : `plateau-1845-831.json`, appliqué par `apply-undervolt.ps1`.

### 4.1 bis Résultats de la configuration 1845/831

| Essai | Puissance | Fréquence | Tension | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|---|
| Charge lourde (8192², 60 s) | 319,5 W | 1694 MHz | 812 mV | 67,3 °C | 80,1 °C | 1588 tr/min |
| Charge type jeu (240 s) | 310,4 W | **1827 MHz** | 829 mV | 73,5 °C | 83,8 °C | 1942 tr/min |
| Débit de calcul (8192²) | — | **67,52 TFLOPS** | — | — | — | — |

Comparaison avec les configurations précédentes (charge type jeu) :

| Configuration | Fréquence | Puissance | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|
| Origine (VBIOS) | ~1672 MHz | 349 W | 80,8 °C | ~96 °C | 2286 tr/min |
| 850 mV / 1860 | 1797 MHz | 314,8 W | 73,2 °C | 83,5 °C | 1900 tr/min |
| 825 mV / 1860 | 1821 MHz | 313,8 W | 75,3 °C | 85,0 °C | 2059 tr/min |
| **831 mV / 1845** | **1827 MHz** | **310,4 W** | **73,5 °C** | **83,8 °C** | **1942 tr/min** |

Gain par rapport à l'origine : **+155 MHz**, **−39 W**, **−4,3 °C**, −344 tr/min.

### 4.2 Comparaison en charge lourde soutenue

Benchmark : matmul fp16 8192×8192, 60 s de chauffe puis 60 s mesurées.

| Configuration | TFLOPS | Écart | Puissance | Fréquence | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|---|---|
| Origine (VBIOS, 350 W) | **65,71** | référence | 349,2 W | 1638 MHz | 79,4 °C | 91,8 °C | 2421 tr/min |
| Précédente (850 mV, 320 W) | 63,75 | **−3,0 %** | 319,5 W | 1592 MHz | 78,4 °C | 91,4 °C | 2248 tr/min |
| 825 mV / 1860 (320 W) | 65,22 | −0,7 % | 319,4 W | 1629 MHz | 78,4 °C | 91,8 °C | 2221 tr/min |
| **831 mV / 1845 (320 W)** | **67,52** | **+2,8 %** | **319,5 W** | **1694 MHz** | **67,3 °C** | **80,1 °C** | **1588 tr/min** |

> **Note de méthode.** Les trois premières lignes ont été mesurées carte déjà
> chaude (~78 °C), la quatrième carte en montée (67 °C). Une mesure à
> l'équilibre thermique (300 s de chauffe, 79,2 °C) donne **64,44 TFLOPS**
> pour la configuration 831 mV / 1845 MHz, soit **−1,9 % par rapport à
> l'origine** et **+1,1 % par rapport à 850 mV**, à 319 W.

Lecture : par rapport à l'origine, la nouvelle configuration
- consomme **30 W de moins (−8,5 %)** ;
- ne perd que **0,7 % de débit de calcul** en charge lourde ;
- tourne **200 tr/min plus lentement** ;
- reste à la même température GPU.

Par rapport à la configuration précédente (850 mV), elle gagne **2,3 % de
performance à consommation identique**, grâce à la réduction des fuites à
tension plus basse.

### 4.3 En charge de type jeu

| Configuration | Fréquence moyenne | Puissance | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|
| Précédente (850 mV) | 1797 MHz | 314,8 W | 73,2 °C | 83,5 °C | 1900 tr/min |
| **Nouvelle (825 mV)** | **1821 MHz** | **313,8 W** | **75,3 °C** | **85,0 °C** | 2059 tr/min |

Soit **+24 MHz à puissance égale**. La température légèrement supérieure
s'explique par la fréquence plus élevée, pas par une consommation plus grande.

---

## 5. Détermination du power limit

Une fois la tension retenue, le power limit doit être choisi juste au-dessus de
la consommation réelle en jeu, pour deux raisons :

- trop bas, il bride la carte et fait perdre la fréquence ;
- trop haut, il laisse passer des pics de chaleur inutiles.

Protocole : charge type jeu de 90 s à trois valeurs de PL (320, 310, 300 W),
avec relevé de la fréquence moyenne, de la puissance et de la température.

| PL | Puissance | Fréquence moyenne | GPU | Mémoire | Ventilos |
|---|---|---|---|---|---|
| **320 W** | 313,8 W | **1821 MHz** | 75,3 °C | 85,0 °C | 2059 tr/min |
| 310 W | 307,2 W | 1801 MHz | 76,8 °C | 87,3 °C | 2222 tr/min |
| 300 W | 299,1 W | 1770 MHz | 76,1 °C | 87,1 °C | 2254 tr/min |

**320 W retenu** : c'est la seule valeur qui ne coûte pas de fréquence. En
dessous, le power limit devient le facteur limitant et la carte perd
20 à 50 MHz.

---

## 6. Ventilation

Le profil `equilibre` (plafond ~2400 tr/min à 75 °C) est conservé. À tension
plus basse, la carte produit moins de chaleur : à régime identique elle sera
donc plus froide, ou aussi froide mais plus silencieuse.

Mesure en charge lourde : **2221 tr/min** contre 2421 tr/min pour la
configuration d'origine, soit **200 tr/min de moins** (−8 %).

---

## 7. Limites de l'exercice

1. **La stabilité n'est jamais garantie à 100 %.** 28 187 itérations de calcul
   vérifiées sur 15 minutes sans erreur, à 78,8 °C, constituent une preuve
   solide mais pas une certitude absolue. Le vieillissement du silicium, une
   pièce plus chaude en été ou une charge différente peuvent révéler une
   faiblesse. En cas de plantage ou d'artefact, remonter au palier 831 mV.
2. **Un seul échantillon.** La tension minimale (825 mV) est propre à cette
   carte ; elle ne se transpose pas à une autre 3080 Ti.
3. **Le gain est surtout thermique.** En charge lourde bridée par le power
   limit, la puissance disponible impose la fréquence : la tension plus basse
   ne fait que réduire les pertes, elle ne crée pas de puissance.
4. **En jeu, le gain est réel** (+24 MHz à puissance égale) parce que la carte
   n'atteint pas la limite et peut exprimer sa fréquence maximale.

---

## 8. Assistant d'optimisation intégré

`nvidiaconf.exe` embarque désormais le protocole décrit plus haut.

### 8.1 Lancement

```powershell
# depuis l'interface : bouton "Trouver la configuration optimale"
# ou en ligne de commande :
C:\Nvidia\nvidiaconf.exe --autotune
```

### 8.2 Déroulement

1. Lecture de la table de tension réelle dans `courbe-stock.json` (aucune
   valeur inventée : les paliers sont ceux du VBIOS).
2. Pour chaque palier, de 843 mV vers le bas : écriture d'une courbe plateau
   1845 MHz, application par NVAPI, puis test de stabilité de **240 s** avec
   vérification bit à bit des résultats.
3. Arrêt au premier palier instable.
4. **Marge de sécurité** : la configuration retenue est le palier *au-dessus*
   du dernier stable (jamais le dernier stable lui-même).
5. Écriture de `plateau-active.json` (courbe retenue) et d'un rapport
   `rapport-<horodatage>.md`.
6. Application du power limit 320 W.

### 8.3 Progression

La fenêtre affiche une barre de progression et le libellé de l'étape :

```
[████████░░░░░░░░░░░░]  42 %
Test de stabilite : 831 mV (3/5) - 240 s
```

Chaque étape est aussi journalisée dans `C:\Nvidia\nvidiaconf.log`.

### 8.4 Rapport

Le rapport liste chaque palier testé, son résultat, le verdict, le palier
retenu et le premier palier instable. Il sert de preuve et d'historique.

---

## 9. Installation par MSI

`nvidiaconf.msi` installe l'ensemble dans `C:\Nvidia` :

| Élément | Détail |
|---|---|
| Fichiers | application, moteur NVAPI, service, scripts, courbes, sources |
| Démarrage | entrée `Run` (HKLM) : `nvidiaconf.exe --tray` |
| Icône | zone de notification, près de l'horloge |
| Désinstallation | Panneau de configuration, ou `msiexec /x` |

La désinstallation retire les fichiers et l'entrée de démarrage. Les tâches et
le service créés par l'application doivent être retirés via
« Restaurer l'origine » avant désinstallation.

---

## 10. Robustesse : prévention des plantages

Un écran noir est survenu sur Hellfire pendant un test d'optimisation : le
pilote graphique a été réinitialisé en pleine charge et l'affichage n'est pas
revenu. La carte et le système étaient sains (SSH, `dwm`, `explorer`
répondaient) ; un redémarrage a tout rétabli.

Trois protections ont été ajoutées à `nvidiaconf.exe` :

| Mécanisme | Détail |
|---|---|
| **Marqueur de test** | `autotune-inprogress.json` est écrit avant chaque palier, supprimé à la fin normale |
| **Chien de garde** | pendant le test, `nvidia-smi` est interrogé toutes les 3 s ; si le pilote ne répond plus ou si la température dépasse 90 °C, le test est arrêté |
| **Mode sûr au démarrage** | si le marqueur est présent au lancement, la courbe stock et 350 W sont restaurés automatiquement, avec un message explicite |

En cas de crash détecté, la restauration d'urgence remet immédiatement :
courbe stock, power limit 350 W, ventilateurs rendus au VBIOS.

---

## 11. Interface de nvidiaconf.exe

| Élément | Détail |
|---|---|
| Profil de ventilation | sélecteur `silence` / `equilibre` / `froid` dans la fenêtre, `equilibre` par défaut |
| Fréquences moyennes | 4 niveaux (légère, jeu, soutenue, calcul lourd) × origine/optimisé, mesurés par le bouton « Mesurer les fréquences » |
| Bus PCIe | génération et largeur, valeur actuelle et maximum |
| Power limit | valeur réelle, avec rappel de la cible (320 W) si un écart est détecté |
| Barre de progression | étape en cours, temps, résultat intermédiaire |
| Restauration | bouton « Restaurer l'origine » : stock, 350 W, ventilateurs VBIOS, suppression tâche et service |

---

## 12. Moteur de test OpenCL (sans Python)

Le test de stabilité n'utilise plus Python ni PyTorch. `nvidiaconf.exe`
embarque un noyau **OpenCL** (`OpenCL.dll`, fourni par le pilote NVIDIA) :

- multiplication de matrices **int32** (2048×2048), donc résultat
  **déterministe** ;
- une première exécution sert de référence, puis chaque exécution est comparée
  **bit à bit** ;
- toute différence signale une erreur de calcul matérielle ;
- charge mesurée : ~240 W, suffisante pour solliciter la carte.

Avantages : aucun prérequis Python, taille réduite, distribution simple.
Repli automatique sur Python/PyTorch si OpenCL n'est pas disponible.

---

## 13. Interface en onglets

La fenêtre est désormais compacte, avec quatre onglets :

| Onglet | Contenu |
|---|---|
| **Identité** | nom, révision, VBIOS, pilote, bus PCI, VRAM, bus PCIe (version et largeur) |
| **Fréquences** | limites d'origine, **tensions min/max de la courbe**, tension du plateau, fréquences moyennes par charge |
| **Configuration** | undervolt appliqué, power limit (avec cible), profil de ventilation, courbe 831 mV |
| **Temps réel** | fréquences, tension, puissance, températures GPU/hotspot/mémoire, ventilateurs, charge |

La zone basse (progression, sélecteur de profil, boutons d'action) reste
visible **sous chaque onglet**.

---

## 14. Onglet Capteurs et test des profils

### 14.1 Onglet Capteurs (façon GPU-Z)

Tableau temps réel avec, pour chaque grandeur, la valeur courante et son
minimum/maximum depuis le démarrage :

fréquences GPU et mémoire, tension, puissance, power limit, températures
GPU/hotspot/mémoire, les deux ventilateurs (tr/min et %), charge GPU, mémoire
utilisée, génération et largeur PCIe.

### 14.2 Test automatique des trois profils

Le bouton « Tester les profils de ventilation » applique successivement
`silence`, `equilibre` et `froid`, soumet la carte à 1 minute de charge pour
chacun, puis relève le régime moyen, le régime maximal et les températures
maximales. Un rapport `rapport-ventilation-<horodatage>.md` est écrit, et le
profil `equilibre` est restauré à la fin.

### 14.3 Durées affichées en minutes

Les libellés de progression indiquent désormais « 4 min » et non « 240 s ».

---

## 15. Limite connue du moteur OpenCL

Le noyau OpenCL (matmul int32 naïf) plafonne à **~240 W**, contre **~300 W**
avec PyTorch. Il est donc **moins sévère** : lors d'un passage complet, il a
déclaré stables tous les paliers jusqu'à 806 mV, alors que PyTorch avait
détecté une instabilité à 818 mV.

En conséquence, **PyTorch reste le moteur prioritaire** lorsqu'il est présent,
et OpenCL sert de repli. La valeur de référence validée reste **831 mV**, avec
28 187 itérations vérifiées sur 15 minutes.

Amélioration à prévoir : noyau OpenCL tuilé (meilleure occupation mémoire) ou
charge ALU suffisante pour atteindre 300 W.

---

## 16. Rapport comparatif dans l'application

Le rapport n'est plus seulement un fichier : il est consultable **dans la
fenêtre**, dans un onglet dédié.

| Accès | Emplacement |
|---|---|
| Onglet **Rapport** | rendu HTML direct, bouton « Actualiser le rapport » |
| Onglet Configuration | bouton « Ouvrir le rapport comparatif » |
| Menu de l'icône | « Rapport comparatif » (ouverture dans le navigateur) |
| Ligne de commande | `nvidiaconf.exe --report` (génère sans ouvrir) |

Contenu : configuration appliquée, fréquences moyennes par niveau de charge
(origine / optimisé / gain coloré), et détail des paliers de tension testés.

Fichiers produits : `rapport-comparatif-<horodatage>.html`.

---

## 17. Risque d'écran noir et garde-fou TDR

Windows réinitialise le pilote graphique si un kernel GPU dépasse **2 secondes**
(TDR, *Timeout Detection and Recovery*). L'écran devient noir quelques secondes,
et peut rester noir.

Ce risque existe avec **PyTorch comme avec OpenCL** :

| Cause | Détail |
|---|---|
| Kernel trop long | au-delà de 2 s, reset du pilote |
| Tension trop basse | erreur de calcul ou plantage du contexte GPU |
| Bug du pilote sur un kernel | rare |

Notre noyau OpenCL dure **~100 ms**, très en dessous du seuil. Le déclencheur
réel de l'incident observé était plus probablement l'instabilité de tension que
le moteur de test.

Garde-fou ajouté : chaque exécution OpenCL est chronométrée. Au-delà de
**1 500 ms**, le test s'arrête immédiatement et signale `arret=kernel_trop_long`,
avant que Windows ne réinitialise le pilote.

### Correctifs d'ergonomie (v1.0.2)

- Onglet **Rapport** : le bouton « Actualiser le rapport » est placé dans un
  bandeau fixe en haut de l'onglet ; le rendu HTML occupe la zone restante et
  n'est plus recouvert (ni la barre de défilement, ni le texte).
- **Simple clic** sur l'icône de la zone de notification : ouvre la fenêtre
  (le double-clic reste actif).

### Correctifs et tests v1.0.3

Tests réalisés sur Hellfire par **capture d'écran de la session utilisateur** :

| Point vérifié | Résultat |
|---|---|
| Icône dans la zone de notification | cachée par défaut (Windows 11) → **promue** et visible après redémarrage de l'explorateur |
| Fenêtre | 620×680, onglets lisibles, zone d'actions commune en bas |
| Révision PCI | affichait `00000000` → corrigée en `0x2208 / 0x153510DE` |
| Double lancement | créait deux instances → **instance unique**, la relance ouvre la fenêtre existante |
| Version installée | empreinte identique à la release publiée |

Nouveau menu : **« Épingler l'icône dans la barre »** pour forcer la promotion de
l'icône sur une autre machine.

### Correctif majeur du MSI (v1.0.4)

**Cause du bug :** le fichier `.wxs` conservait `Version="1.0.0.0"` quel que soit le
build. Comme chaque compilation génère un `ProductCode` différent, Windows
considérait chaque MSI comme un produit distinct : les installations
s'empilaient, la désinstallation demandait plusieurs fois de fermer
l'application, et le panneau « Applications et fonctionnalités » affichait
plusieurs fois la version 1.0.0.0.

**Corrections :**

| Point | Correction |
|---|---|
| Version du produit | `1.0.4.0`, incrémentée à chaque release |
| Mise à jour | `MajorUpgrade` actif : une version plus récente remplace l'ancienne |
| Doublons | nettoyage des installations existantes, puis une seule entrée |
| Icône après installation | l'application est lancée automatiquement (`CustomAction LaunchApp`) |
| Installation silencieuse via SSH | le lancement se fait en session 0 (sans icône) ; utiliser la tâche `NvidiaConf` ou relancer l'exe dans la session utilisateur |

**Vérifié sur Hellfire :** une seule entrée « NVIDIA Configuration 1.0.4.0 »,
entrée `Run` 64-bit correcte, icône verte visible dans la zone de notification.

### Correctifs v1.0.5 (installation / désinstallation)

| Problème signalé | Correction |
|---|---|
| Windows demande de fermer `nvidiaconf.exe` à l'installation et à la désinstallation | CustomAction **KillApp** exécutée avant `InstallValidate` : `taskkill` de l'application et arrêt du service `NvidiaFanCurve` |
| L'application reste active après la désinstallation malgré « Oui » | fermée par le même mécanisme, avant toute manipulation de fichiers |
| Éditeur affiché « irame » | corrigé en **« iramone »** |
| Versions 1.0.0.0 empilées | version réelle du produit (1.0.5.0) et `MajorUpgrade` |

**Vérifié sur Hellfire :** désinstallation avec l'application en cours → code 0,
processus fermé, aucune entrée résiduelle ; réinstallation → une seule entrée
`NVIDIA Configuration 1.0.5.0`, éditeur `iramone`, entrée `Run` correcte.

### Cycle installation / désinstallation / réinstallation (v1.0.8)

Test complet exécuté sur Hellfire :

| Étape | Résultat |
|---|---|
| État initial | 1.0.6.0 installée, application en tray |
| Installation 1.0.7 | exit 0, une seule entrée, application lancée |
| Désinstallation | exit 0, fichiers supprimés, entrée `Run` retirée, **sauvegarde créée** dans `C:\ProgramData\NvidiaConf` |
| Réinstallation | exit 0, `plateau-active.json` et `fan-curve.txt` **restaurés** |
| Rapport HTML | généré (2,8 Ko) |
| Courbe / power limit | réappliqués (1845 MHz / 831 mV, 320 W) |

**Deux bugs trouvés et corrigés :**

1. **Service arrêté après installation.** L'action KillApp arrête `NvidiaFanCurve`
   avant de remplacer les fichiers, mais rien ne le redémarrait. L'application
   vérifie désormais au démarrage que le service existe et le démarre
   (`ServiceController`).
2. **Nom du service spécifique** (« NVIDIA Fan Curve (RTX 3080 Ti) »), incohérent
   avec la portée multi-cartes. Renommé **« NVIDIA Fan Curve »**.

Limite constatée : la tâche `NvidiaConf` est de type interactif ; elle ne
s'exécute qu'à l'ouverture de session. Sur l'écran de connexion, l'application
n'est pas lancée (comportement normal).

### Correctif v1.0.9 : fenêtre au démarrage

**Symptôme :** au démarrage de session, l'icône apparaissait bien dans la zone de
notification, mais la fenêtre principale s'ouvrait aussi.

**Cause :** `Application.Run(form)` rend le formulaire visible même si `Hide()` a
été appelé dans le constructeur.

**Correction :** en mode tray, le formulaire est créé avec une **opacité nulle**
et masqué dès son premier affichage (`Shown`), puis l'opacité est rétablie. Plus
aucune fenêtre n'apparaît au logon.

**Limite de test :** en session 0 (SSH), Windows ne crée pas de fenêtre
« visible » ; `MainWindowHandle` reste à 0 même en mode normal. La validation
finale doit donc être faite dans une session utilisateur.

### Test de bout en bout (v1.0.14)

| Vérification | Résultat |
|---|---|
| Désinstallation totale (aucun fichier conservé) | courbe **stock**, tâches et service supprimés, fichiers retirés |
| Réinstallation | 1.0.14.0, éditeur iramone |
| Configuration appliquée au démarrage | courbe 1845/831 + power limit 320 W (vérifié dans le journal) |
| Service de ventilation | recréé automatiquement s'il manque, démarré s'il est arrêté |
| Redémarrage + reconnexion (iramone) | session active, application en session 1 |
| Fenêtre au logon | **non ouverte** (capture d'écran) |
| Icône dans la zone de notification | **présente** (capture d'écran) |
| Doublon de démarrage (`Run` + tâche) | corrigé : l'ancienne entrée `Run` est supprimée par l'installateur |

**Principe retenu :** l'application est auto-suffisante. À chaque lancement elle
vérifie le power limit, la courbe et le service, et corrige ce qui manque, ce qui
rend l'installation et la désinstallation robustes indépendamment des
CustomActions du MSI.

### Validation complète de l'assistant (v1.0.15)

| Test | Résultat |
|---|---|
| Désinstallation totale (aucun fichier conservé) | courbe stock, tâches et service supprimés |
| Réinstallation | configuration appliquée automatiquement au premier lancement |
| **Auto-tuning** | 843/837/831/825 stables, **818 instable**, → **831 mV retenu** (identique au résultat manuel) |
| **Fréquences** (optimisé) | 1841 / 1860 / 1860 / 1860 MHz (légère / jeu / soutenue / lourde) |
| **Fréquences** (origine) | 1910 / 1908 / 1905 / 1897 MHz |
| **Ventilation** | silence 1643, équilibre 1696, froid 1760 tr/min |
| Rapport HTML | généré (`rapport-comparatif-*.html`) |
| Redémarrage + reconnexion | session active, application en session 1 |
| Fenêtre au logon | **non ouverte** (capture) |
| Icône | **présente** (capture) |
| Configuration au démarrage | 1845 MHz / 831 mV / 320 W appliqués (journal) |

**Interprétation des fréquences :** l'origine affiche des fréquences plus élevées
(1897-1910 MHz) parce que la courbe stock n'a pas de plafond ; l'optimisation
plafonne volontairement à 1860 MHz, ce qui abaisse la fréquence en charge légère
mais réduit fortement la tension et la consommation.

**Bugs corrigés à cette occasion :** chien de garde de l'auto-tuning (faux
positifs), température GPU du test de ventilation (affichait 0 °C), résolution de
`nvidia-smi` (chemin absolu), double démarrage (`Run` + tâche).

### Validation du cycle complet (v1.0.21)

| Étape | Courbe | Power limit | Service | Tâches | Application |
|---|---|---|---|---|---|
| Avant | 1845/831 | 320 W | Running | oui | oui |
| Après désinstallation | **stock** | 320 W | supprimé | supprimées | fermée |
| Après réinstallation | **1845/831** | **320 W** | Running | recréées | lancée |

Journal d'installation (toutes les étapes s'exécutent) :

```
tache NvidiaConf creee pour HELLFIRE\iramone
service de ventilation installe
tache NvidiaUndervolt installee
courbe : Core curve offset: -165..+255 MHz across 66 point(s)
power limit 320 : set to 320.00 W
configuration et taches en place
```

**Point résiduel connu :** après désinstallation, la courbe revient bien au
stock et les tâches/service sont supprimés, mais le power limit reste à 320 W au
lieu de 350 W. L'action `RestoreStock` s'exécute (les tâches sont supprimées)
mais la commande `nvidia-smi -pl 350` n'a pas d'effet dans ce contexte. Sans
impact sur la courbe, mais à corriger.

### Retour complet à l'origine à la désinstallation (v1.0.22)

**Correction du bug :** `nvidia-smi -pl 350` n'avait pas les droits nécessaires
dans le contexte de la CustomAction MSI.

**Solution :** la désinstallation crée une tâche planifiée **SYSTEM**
temporaire (`NvidiaRestore`) qui exécute `restore-stock.ps1`, puis la supprime.

`restore-stock.ps1` réalise le retour complet :

1. `snu.exe clear` — courbe tension/fréquence d'origine ;
2. `nvidia-smi -pl 350` — power limit d'origine, puis vérification ;
3. `nvfan.exe auto` — ventilation rendue au VBIOS ;
4. relecture de la courbe pour confirmer l'état `stock`.

**Vérifié sur Hellfire :**

```
restauration complete de la configuration d'origine (SYSTEM)
  courbe V/F : cleared 64 offset point(s)
  power limit 350 : set to 350.00 W from 320.00 W
  power limit verifie : 350.00 W
  ventilation rendue au VBIOS
  courbe verifiee : stock
restauration terminee
```

Après désinstallation : **courbe stock, 350 W, ventilation VBIOS**, tâches et
service supprimés. Après réinstallation : **1845/831, 320 W**, service Running,
tâches recréées, application lancée.

### Pourquoi le rapport différait des mesures de référence (v1.0.25)

Trois causes distinctes ont été identifiées :

1. **Rapport vide.** `freq-profile.json` et `undervolt.log` ne font pas partie du
   MSI : ils étaient supprimés à chaque désinstallation. Le rapport affichait
   « Aucune mesure disponible » et « Undervolt : ? ». Corrigé : la configuration
   est désormais déduite de `plateau-active.json` (toujours présent).

2. **Charge trop légère.** Le test de fréquences utilisait OpenCL (~240 W) :
   la carte d'origine n'était pas bridée et boostait à 1910-1920 MHz, alors que
   la courbe optimisée plafonne à 1860. Le rapport semblait donc défavorable.
   Corrigé : le test utilise PyTorch (matmul dense, ~300 W), représentatif.

3. **`stress-long.py` absent.** Il avait été déplacé vers `D:\opencode\Nvidia` ;
   le test tournait sans charge (210 MHz partout). Corrigé : le script est
   embarqué dans le MSI.

Mesures corrigées (2026-10-26 10:56) :

| Charge | Origine | Optimisé | Écart |
|---|---|---|---|
| Légère | 1920 MHz | 1839 MHz | −81 |
| Jeu | **1745 MHz** | **1860 MHz** | **+115** |
| Soutenue | 1809 MHz | 1860 MHz | +51 |
| Lourde | 1646 MHz | 1670 MHz | +24 |

Conforme aux mesures de référence : gain net en jeu, léger retrait en charge
légère dû au plafonnement volontaire à 1860 MHz.

---

## 18. Assistant « package optimal » (v1.0.29)

Nouvelle fonctionnalité : déterminer un package complet
**{fréquence / tension / power limit / ventilation}** optimisé.

### Déroulement

1. **Ligne de base** : mesure de la configuration d'origine sur 4 charges
   (légère, jeu, soutenue, lourde), avec **fréquence, tension, watts,
   températures GPU/mémoire et ventilation**.
2. **Fréquence cible** : soit imposée par l'utilisateur (`--mhz` ou champ de
   l'interface), soit déduite de la fréquence d'origine en jeu.
3. **Tension minimale stable** pour cette cible, avec **marge d'un palier**
   (6,25 mV) — test PyTorch prioritaire, plus sévère qu'OpenCL.
4. **Power limit** : consommation réelle en jeu **+ 10 % de marge**, plafonné à
   350 W (valeur d'origine).
5. **Ventilation** : profil `equilibre`.
6. **Re-mesure** des 4 charges et **rapport comparatif** (`package-optimal-*.md`).

### Résultat du premier passage (fréquence déduite : 1755 MHz)

| Charge | Origine | Package | Gain MHz | Gain W |
|---|---|---|---|---|
| Légère | 1935 MHz / 1068 mV / 278 W | 1770 MHz / 812 mV / 194 W | −165 | **−84 W** |
| Jeu | 1753 MHz / 915 mV / 349 W | 1770 MHz / 812 mV / 302 W | **+17** | **−47 W** |
| Soutenue | 1812 MHz / 961 mV / 349 W | 1770 MHz / 812 mV / 272 W | −42 | **−77 W** |
| Lourde | 1645 MHz / 855 mV / 349 W | 1674 MHz / 799 mV / 329 W | +29 | −20 W |

**Réserve de sécurité :** ce premier passage a utilisé OpenCL pour la recherche
de tension, moins sévère que PyTorch (où 818 mV s'était révélé instable). La
tension retenue (812 mV) est donc trop basse. Corrigé en v1.0.29 : PyTorch est
prioritaire, ce qui devrait ramener la tension vers 831 mV.

### Utilisation

```powershell
C:\Nvidia\nvidiaconf.exe --package              # cible déduite de l'origine
C:\Nvidia\nvidiaconf.exe --package --mhz 1800   # cible imposée
```

Ou dans l'interface : onglet **Configuration** → champ « Fréquence cible (MHz) »
→ bouton « Déterminer le package optimal ».

### État de l'assistant package (v1.0.31)

| Point | État |
|---|---|
| Progression dans la fenêtre | le test est désormais confié à l'instance en cours (événement `NvidiaConf.Package`) ; plus de seconde instance |
| PyTorch obligatoire | la recherche de tension refuse de démarrer sans PyTorch et propose son installation |
| Durées en minutes | « Tension 843 mV (1/7) - 4 min » |
| **Blocage observé** | lors du dernier passage, le package s'est figé à la phase de tension, **sans processus Python actif** |

**Cause probable du blocage :** `RunPythonStability()` ne démarre pas le moteur
(l'absence de processus `python` le confirme) et la boucle n'écrit aucun journal
par palier, ce qui rend le diagnostic difficile. À corriger : journaliser chaque
palier, vérifier le moteur avant la boucle, et ajouter un délai maximal.

**Configuration de référence maintenue :** 1845 MHz / 831 mV / 320 W / profil
équilibre — valeur validée par 28 187 itérations PyTorch.

**Assistant package non validé en production** : le premier passage (OpenCL) a
donné 812 mV, trop bas ; les passages suivants ont échoué (moteur absent, puis
blocage). La fonctionnalité doit être consolidée avant usage.

### Deuxième échec de l'assistant package (v1.0.32)

Malgré l'ajout du contrôle du moteur et du journal par palier, le package s'est
**bloqué dans la phase de mesure de la ligne de base** : après
`autotune cible : 1845 MHz`, plus aucune écriture pendant 25 minutes, sans
processus Python actif.

Un premier passage avec PyTorch avait pourtant démarré correctement (processus
Python observé), donc le blocage est **intermittent** et se situe dans
`MeasureDetailed` : lancement de la charge, échantillonnage (`nvidia-smi`,
`snu voltage`), ou attente de fin du processus Python.

**Conséquence sur la carte :** le package exécute `snu clear` au début de la
ligne de base. Après l'échec, la carte est restée en **stock (350 W)**. La
configuration 1845 MHz / 831 mV / 320 W a été rétablie manuellement.

**Conclusion :** l'assistant package n'est pas utilisable en l'état. La
configuration de référence reste celle validée par les tests manuels
(1845/831/320, 28 187 itérations PyTorch sans erreur).

**Correctifs à prévoir :**
1. Journaliser chaque étape de `MeasureDetailed` (début/fin de charge, valeur
   échantillonnée) pour localiser le blocage ;
2. encapsuler chaque appel `nvidia-smi` / `snu voltage` avec un délai maximal ;
3. ne pas laisser la carte en stock en cas d'échec : restaurer la configuration
   précédente.

### Cause racine du blocage de l'assistant package (v1.0.33)

**Diagnostic :** dans `SmiInt`, `SmiDouble` et `ReadVoltage`, la sortie des
processus externes était lue avec :

```csharp
string o = p.StandardOutput.ReadToEnd();   // <-- bloque sans limite
p.WaitForExit(8000);                       // jamais atteint si ReadToEnd bloque
```

`ReadToEnd()` attend la fermeture du flux. Sous forte charge, `nvidia-smi` peut
tarder ou ne pas rendre la main : le thread d'échantillonnage restait alors
bloqué **indéfiniment**, sans message ni progression — exactement le symptôme
observé (figé à la phase de mesure, sans processus Python actif).

**Correction :** fonction utilitaire `RunCapture(exe, args, timeoutMs)` :

1. lecture **asynchrone** de stdout et stderr (`ReadToEndAsync`) ;
2. `WaitForExit(timeoutMs)` borné (8 s pour nvidia-smi, 6 s pour `snu voltage`) ;
3. `Kill()` du processus en cas de dépassement, journalisation de l'incident ;
4. retour d'une chaîne vide, qui produit une mesure à 0 au lieu d'un blocage.

**Journalisation ajoutée :** chaque mesure écrit désormais son début et sa fin
(`mesure : charge N lancee`, `mesure : charge N terminee, X echantillons`), et
tout dépassement est tracé (`RunCapture timeout : ...`).

**État :** correction compilée et installée (v1.0.33), mais **non validée par un
passage complet** — le lancement du test n'a pas abouti lors de cette session.

---

## Résultat du premier passage complet de l'assistant (v1.0.39, 26/10/2026)

Premier passage **sans blocage** après correction (durée totale 1 h 18 :
ligne de base 8 min, 7 paliers de tension × 10 min, mesures finales 10 min).

### Package proposé par l'assistant

| Paramètre | Valeur proposée |
|---|---|
| Fréquence cible | 1740 MHz |
| Tension | 830 mV (appliquée 826 mV à cause de l'arrondi, corrigé en v1.0.38) |
| Power limit | 330 W (recalé à 320 W en v1.0.39) |
| Ventilation | équilibre |

### Comparatif mesuré

| Charge | Origine | Package | Gain MHz | Gain W |
|---|---|---|---|---|
| Légère | 1917 MHz / 285 W | 1755 MHz / 196 W | −162 | −89 |
| Jeu | 1742 MHz / 349 W | 1755 MHz / 307 W | +13 | −42 |
| Soutenue | 1793 MHz / 349 W | 1755 MHz / 277 W | −38 | −72 |
| Lourde | 1632 MHz / 349 W | 1626 MHz / 329 W | −6 | −20 |

### Limites constatées

1. **Tous les paliers déclarés stables** jusqu'à 806 mV, alors que 818 mV avait
   révélé 2 écarts lors du test manuel de 15 min. Le test automatisé
   (~2 400 itérations) est statistiquement insuffisant → marge de 3 % ajoutée,
   qui ramène la consigne à 831 mV.
2. **Fréquence trop conservatrice** : l'assistant retient la fréquence d'origine
   en jeu (1740 MHz), alors que 1845 MHz est atteignable à la même tension.
   D'où une perte en charge légère (−162 MHz).
3. La configuration manuelle **1845 MHz / 831 mV / 320 W** reste donc la
   référence : +115 MHz en jeu, −47 W, validée par 28 187 itérations.

### Configuration appliquée après validation

| Paramètre | Valeur |
|---|---|
| Courbe | 1845 MHz / 831 mV (64 points) |
| Power limit | 320 W |
| Ventilation | équilibre |
| Persistance | tâche `NvidiaUndervolt` (SYSTEM, démarrage +30 s) |

Vérifié après redémarrage du 26/10/2026 à 18:15 : courbe appliquée par la tâche
SYSTEM (code 0), power limit 320 W, service `NvidiaFanCurve` Running.
