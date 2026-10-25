# RTX 3080 Ti FE — silent undervolt and dual-fan control

Persistent, headless tuning for the **NVIDIA GeForce RTX 3080 Ti Founders
Edition** on Windows 11 — no MSI Afterburner, no background overclocking suite.
Everything is done through NVAPI, one scheduled task and one lightweight
Windows service.

The result: **more performance than stock, 30 W less power, lower memory
temperatures and quieter fans.**

---

## Final configuration

| Setting | Value |
|---|---|
| V/F curve | plateau **1845 MHz from 831 mV** |
| Power limit | **320 W** (VBIOS default: 350 W, max: 400 W) |
| Fan control | two channels, RPM-targeted curve (`equilibre` profile) |
| Memory clock | stock (9501 MHz) |

---

## Measured results

Game-like load (convolutions + matmuls, 4 min), compared with the stock VBIOS:

| Metric | Stock VBIOS | This configuration | Delta |
|---|---|---|---|
| GPU clock | ~1672 MHz | **1827 MHz** | **+155 MHz** |
| Package power | 349 W | **310 W** | **−39 W (−11 %)** |
| GPU temperature | 80.8 °C | **73.5 °C** | −7.3 °C |
| Memory junction | ~96 °C | **83.8 °C** | −12 °C |
| Fan speed | 2286 rpm | **1942 rpm** | −344 rpm |

Heavy sustained load (fp16 matmul 8192²):

| Configuration | TFLOPS | Power | GPU |
|---|---|---|---|
| Stock (350 W) | 65.71 | 349 W | 79.4 °C |
| 850 mV / 1860 MHz (320 W) | 63.75 | 319 W | 78.4 °C |
| **831 mV / 1845 MHz (320 W)** | **67.52** | **319 W** | 67–78 °C |

---

## How it works

Two independent mechanisms, both silent and both running in session 0:

| Job | Mechanism | Nature |
|---|---|---|
| V/F curve + power limit | scheduled task `NvidiaUndervolt` | one-shot, re-applied on triggers |
| Fan curve | Windows service `NvidiaFanCurve` | resident, 2 s control loop |

Triggers covered: Windows startup, user logon, resume from sleep, NVIDIA driver
reload (driver update), reboot.

### V/F curve

The curve is written with [`simple-nvidia-undervolt`](https://github.com/vuplea/simple-nvidia-undervolt)
(NVAPI `ClkVfPointsSetControl`). A tuning file lists, for every voltage anchor
≥ 831 mV, the offset `1845 − stock_frequency`, producing a flat plateau.

The silicon limit was measured, not guessed: every voltage step was stress
tested with **bit-exact result verification** (28 187 verified computations).
818 mV produced two diverging results; 831 mV was kept for safety margin.

### Fan control

The Founders Edition uses the **`ClientFanCoolers`** NVAPI API (the classic
`NvAPI_GPU_SetCoolerLevels` returns `NVAPI_INCOMPATIBLE_STRUCT_VERSION` on
RTX cards). Two channels are exposed:

| Channel | Range | Max RPM |
|---|---|---|
| id 1 | 30–100 % | 3800 rpm |
| id 2 | 30–100 % | 3400 rpm |

Because the two fans have different response curves, the service targets a
**common RPM** and converts it to a per-channel percentage, with a bounded
adaptive correction. Below 46 °C it hands control back to the VBIOS (0 dB mode,
fans stopped) — manual control cannot go below 30 %.

### Monitoring app

`nvidiaconf.exe` (in `C:\Nvidia`) is a small GPU-Z-like tray application:

- static identity: name, revision, VBIOS, driver, PCI bus, VRAM;
- stock limits: max GPU/memory clocks, min/max/default power limit, thermal limits;
- applied configuration: undervolt, power limit, fan profile;
- live: GPU/memory clocks, voltage, power, GPU/hotspot/memory temperatures,
  both fan speeds, GPU utilization.

Double-click the tray icon to open it; right-click for the menu (apply
configuration, switch fan profile, open folder, quit).

---

## Repository layout

```
README.md                     this file
README.fr.md                  French version
docs/technical-guide.md       deep dive: NVAPI structures, test protocol
docs/technical-guide.fr.md    French version
```

Deployed files (on the machine) live in `C:\ProgramData\NvidiaUndervolt\`:

| File | Purpose |
|---|---|
| `snu.exe` | NVAPI undervolt engine |
| `plateau-1845-831.json` | active V/F curve |
| `courbe-stock.json` | stock reference captured at 31 °C |
| `apply-undervolt.ps1` | applies curve + power limit, verifies, logs |
| `install-task.ps1` | creates the `NvidiaUndervolt` task |
| `nvfan.exe` / `nvfan.cs` | fan control (console + service) |
| `fan-curve.txt` | active fan curve |
| `profil-silence.txt`, `profil-equilibre.txt`, `profil-froid.txt` | ready-made profiles |
| `set-fan-profile.ps1` | switch profile at runtime |
| `install-fan-service.ps1` | install the `NvidiaFanCurve` service |
| `verify.ps1`, `verify-fan.ps1` | read-only diagnostics |
| `rollback.ps1` | restore stock |

---

## Usage

```powershell
# full state
powershell -File C:\ProgramData\NvidiaUndervolt\verify.ps1
powershell -File C:\ProgramData\NvidiaUndervolt\verify-fan.ps1

# live fan state
C:\ProgramData\NvidiaUndervolt\nvfan.exe get

# change fan profile (no restart needed)
powershell -File C:\ProgramData\NvidiaUndervolt\set-fan-profile.ps1 -Profil equilibre

# re-apply undervolt + power limit
powershell -File C:\ProgramData\NvidiaUndervolt\apply-undervolt.ps1
```

---

## Safety

1. **The silicon limit is card-specific.** 831 mV works on this sample; do not
   copy it blindly. 818 mV failed here with computation errors.
2. **Keep a margin.** If you ever see a crash, a driver reset or visual
   artifacts, step up one 6.25 mV tier (837 mV). It costs ~0.3 % performance.
3. **Memory junction is the real constraint** on a 3080 Ti FE. It runs ~10 °C
   above the GPU. The fan service forces 100 % above 95 °C as a guard rail.
4. **Never stack fan-control tools.** Only one program may drive the two
   channels (this service, or Afterburner, or FanControl — not several).
5. **Power limit above 350 W is pointless here.** 400 W only helps synthetic
   loads and adds 50 W of heat; in games the card stays below 320 W.

---

## Hardware / software

- NVIDIA GeForce RTX 3080 Ti Founders Edition (GA102, VBIOS `94.02.71.00.ca`)
- Windows 11, NVIDIA driver 621.01
- .NET Framework 4.8 (for `nvfan.exe` and `nvidiaconf.exe`)
- Python only used for the stress/benchmark scripts, not required at runtime
