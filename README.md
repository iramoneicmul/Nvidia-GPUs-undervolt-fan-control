[![Latest release](https://img.shields.io/github/v/release/iramoneicmul/Nvidia-GPUs-undervolt-fan-control)](https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)](https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control)

**English** | [Version française](README.fr.md) &middot; [Installation guide](INSTALL.md)

---

# NVIDIA GPUs — silent undervolt, power limit and fan control

Persistent, headless tuning for **NVIDIA graphics cards exposing NVAPI control**
of the V/F curve, power limit and fans. Validated on the **GeForce RTX 3080 Ti
Founders Edition**; designed to refuse cleanly on unsupported hardware.

Windows 10/11 — no MSI Afterburner, no background overclocking suite.
Everything is done through NVAPI, one scheduled task and one lightweight
Windows service.

The result: **more performance than stock, 30 W less power, lower memory
temperatures and quieter fans.**

## Targets and comparison

The Configuration tab accepts three optional targets:

| Field | Bounds (computed from the card) |
|---|---|
| Target frequency (MHz) | 500 .. `clocks.max.graphics` |
| Target voltage (mV) | 700 .. min(curve, 1000) |
| Max power limit (W) | `power.min_limit` .. `power.default_limit` |

Out-of-range values are refused before any test starts:

```
package : valeur refusee - Tension 500 mV hors limites (700-1000 mV).
```

A target voltage replaces the search with a single 600 s stability test.

**Fan profile is always tested** after the stock baseline: `silence`,
`equilibre` and `froid` run under load and the quietest profile that stays under
`TempSlowdown - 10 C` is kept.

**Compare stock / applied** (Report tab, or `--compare`) measures the four load
levels in both configurations and displays an HTML report inside the window.
Every step shows the action and the **remaining time in minutes**.

```powershell
C:\Nvidia\nvidiaconf.exe --package --mhz 1845 --mv 831 --pl 320
C:\Nvidia\nvidiaconf.exe --compare
```

---

## Monitoring, history and updates

- **Real-time graph** in the Live tab: temperature, power and frequency (150 points).
- **CSV export** of sensor statistics (Sensors tab).
- **Notifications**: high temperature (`TempSlowdown - 5 C`) and available updates.
- **Configuration history** with one-click restore (Configuration tab).
- **Watchdog**: if the app is killed during a test, the service restores the
  active curve and power limit within a few minutes.
- **Per-application fan profiles** via `C:\Nvidia\profiles-apps.json`.
- **Version check** at startup against the GitHub releases API.

Stability verdicts now require **zero numerical differences, zero CUDA errors
and zero Xid driver errors**. Voltage search uses a **dichotomy** (4 tests
instead of 7) and the maximum stable frequency is searched at the retained
voltage, in 15 MHz steps. The power limit is measured over **10 minutes** of
gaming load with a +10 W margin.

Regression tests: `tools/test-regression.ps1` (19 checks).
Release signing: `tools/sign-release.sh` (SHA256 + optional GPG).

---

## GPU-Unlock tab

[Zagitta/gpu-unlock](https://github.com/Zagitta/gpu-unlock) unlocks the FP/BF16
instruction issue rate on GA102/GA103/GA104/GA106 cards, doubling it. It is a
**Linux kernel module** loaded from the initramfs before the NVIDIA driver
binds the card: it cannot be enabled from a Windows application.

The tab therefore reports the real situation and **measures the potential**:

| Precision | Measured (RTX 3080 Ti, 320 W) | Unlocked potential |
|---|---|---|
| FP16 | 71.7 TFLOPS | ~143 TFLOPS |
| BF16 | 73.2 TFLOPS | ~146 TFLOPS |
| TF32 | 36.6 TFLOPS | ~73 TFLOPS |
| FP32 | 22.6 TFLOPS | not affected |

The Windows port (`impacifist/Windows-GPU-UNLOCK`) is an experimental UEFI
loader, validated on RTX 3060 / 3070 Ti / 3090 FE / 3090 Ti — **not on
RTX 3080 Ti**. The enable/disable buttons explain this and link to both
projects instead of pretending to activate something.

Two caveats: the **power limit remains the limiting factor** (at 320 W the card
runs out of power before issue rate), and **games gain nothing** (FP32 is not
affected). The benefit concerns AI and FP16/BF16 compute.

---

## Compatibility

The application does **not** rely on a model list — it checks capabilities at
runtime and refuses to start if one is missing.

| Capability | Check |
|---|---|
| NVIDIA driver / NVAPI | `NvAPI_Initialize` + GPU enumeration |
| Controllable fans | `ClientFanCoolersGetInfo` returns at least one channel |
| V/F curve | `snu.exe status` answers for the GPU |
| Modifiable power limit | `nvidia-smi` reports min < max |

### Likely compatible

| Generation | Models | Confidence |
|---|---|---|
| Ampere | RTX 30xx | very high (validated on 3080 Ti FE) |
| Ada | RTX 40xx | high |
| Blackwell | RTX 50xx | medium to high |
| Turing | RTX 20xx, GTX 16xx | high |
| Professional | RTX A-series, Quadro RTX | high |

### Likely refused

| Case | Reason |
|---|---|
| Laptops | power limit and fans often locked by the vendor |
| Tesla / datacenter | passive cooling, no controllable fan |
| Kepler and older | no longer supported by recent drivers |
| Locked OEM cards | VBIOS without power-limit adjustment |
| Fanless / custom watercooling | no `ClientFanCoolers` channel |

To check a specific card safely: `nvidiaconf.exe --check`.

### Legacy API fallback

On cards without `ClientFanCoolers` (Maxwell, Pascal and earlier), the fan layer
automatically falls back to the classic `NvAPI_GPU_SetCoolerLevels` /
`GetCoolerSettings` pair, with `NvAPI_GPU_GetTachReading` for the tachometer.
RTX behaviour is unchanged: the modern API is tried first.

This path is **not yet tested on real Pascal hardware**; it is implemented so
the application degrades gracefully instead of refusing outright.

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
| **831 mV / 1845 MHz (320 W)** | **64.44** (equilibrium, 79.2 °C) | **319 W** | 79.2 °C |

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

## Built-in optimisation wizard

`nvidiaconf.exe` embeds the whole tuning protocol.

```powershell
C:\Nvidia\nvidiaconf.exe --package    # full analysis (baseline + voltage + power limit)
C:\Nvidia\nvidiaconf.exe --autotune   # voltage search only
```

**Full analysis (`--package`)**

1. Measures a 4-level baseline (light, gaming, sustained, heavy) on the stock
   configuration, with PyTorch (bit-exact result checking, mandatory).
2. Derives the target frequency from the gaming measurement (15 MHz step).
3. Searches voltage from 843 mV downward, **600 s per tier** on 8192x8192
   matrices; stops at the first unstable tier.
4. **Voltage margin: 3 %** above the last stable tier, rounded **up** to the
   next NVAPI step (6.25 mV) - the curve only accepts those steps.
5. **Power limit: measured gaming draw + 10 W**, rounded to the next 10 W
   (309 W -> 320 W), capped at the card's `power.default_limit`. The limit is
   read from the GPU at runtime, never hardcoded, and recomputed from the
   final measurement.
6. Applies the `equilibre` fan profile, re-measures the 4 levels and writes a
   timestamped Markdown report.

The test runs inside the already-running instance: the tray icon stays
available and the progress bar is shown in its window. Every measurement is
logged (`echantillon N : ... MHz / ... W / ... mV`).

---

## Compatibility

The application refuses to start on unsupported hardware, with an explicit
message. A card is considered compatible only if **all** of these are true:

| Capability | Check |
|---|---|
| NVIDIA driver / NVAPI | `NvAPI_Initialize` + GPU enumeration |
| Controllable fans | `ClientFanCoolersGetInfo` returns at least one channel |
| V/F curve | `snu.exe status` answers for the GPU |
| Modifiable power limit | `nvidia-smi` reports min < max |

The MSI enforces the same idea at install time: it requires the `nvlddmkm`
service key and `nvapi64.dll`, and aborts with an explicit message otherwise.

---

## MSI installation

`nvidiaconf.msi` installs everything into `C:\Nvidia` and registers
`nvidiaconf.exe --tray` in the machine `Run` key, so the green icon appears in
the notification area at logon.

### Crash protection

A test marker (`autotune-inprogress.json`) is written before each voltage
tier. While a test runs, the driver is polled every 3 s; if it stops
responding or the GPU exceeds 90 °C, the test is aborted. On next start, a
leftover marker triggers an automatic restore of the stock curve and 350 W with
an explicit message. Emergency restore also returns the fans to the VBIOS.

### Main window

| Element | Detail |
|---|---|
| Fan profile | `silence` / `equilibre` / `froid` selector, `equilibre` by default |
| Average frequencies | 4 load levels (light, game, sustained, heavy) × stock/optimised |
| PCIe bus | generation and link width, current and maximum |
| Power limit | actual value, with the 320 W target flagged when they differ |
| Progress bar | current step, duration, intermediate result |
| Restore | stock curve, 350 W, fans back to VBIOS, task and service removed |

### OpenCL test engine

The stability test no longer needs Python. `nvidiaconf.exe` embeds an OpenCL
kernel (int32 matrix multiplication, 2048×2048): the result is deterministic,
the first run is the reference, and every subsequent run is compared bit for
bit. Any difference is a hardware computation error. Measured load: ~240 W.
Python/PyTorch remains as an automatic fallback.

### Tabbed interface

| Tab | Content |
|---|---|
| **Identity** | name, revision, VBIOS, driver, PCI bus, VRAM, PCIe generation and width |
| **Clocks** | stock limits, **curve min/max voltages**, plateau voltage, average clocks per load level |
| **Configuration** | applied undervolt, power limit (with target), fan profile, 831 mV curve |
| **Live** | clocks, voltage, power, GPU/hotspot/memory temperatures, fans, utilisation |

The bottom area (progress bar, fan profile selector, action buttons) stays
visible under every tab.

### Sensors tab and fan profile test

A GPU-Z-style live table shows each value with its minimum and maximum since
start: GPU/memory clocks, voltage, power, power limit, GPU/hotspot/memory
temperatures, both fans (rpm and %), GPU load, VRAM used, PCIe generation and
width.

The "Test fan profiles" button applies `silence`, `equilibre` and `froid` in
turn, runs a 1-minute load for each, records average/max rpm and peak
temperatures, writes a Markdown report and restores `equilibre`.

### Known limitation

The naive OpenCL int32 kernel peaks at **~240 W**, versus **~300 W** with
PyTorch, so it is less demanding: a full run declared every tier down to
806 mV stable, while PyTorch had found 818 mV unstable. PyTorch therefore
remains the preferred engine when available, and OpenCL is the fallback. The
validated reference stays **831 mV**.

### Comparative report

The comparison report is available directly in the window, in a dedicated
**Report** tab (HTML rendering, refresh button). It can also be opened from the
Configuration tab, from the tray menu, or generated headless with
`nvidiaconf.exe --report`.

Content: applied configuration, average clocks per load level (stock /
optimised / colour-coded gain) and the voltage tiers tested. Files:
`rapport-comparatif-<timestamp>.html`.

---

## Download and installation

**Latest release:** <https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control/releases/latest>

### Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / 11 (x64) |
| GPU | NVIDIA card exposing NVAPI fan control, V/F curve and power limit |
| Driver | NVIDIA display driver installed |
| Runtime | .NET Framework 4.8 (built into Windows 10/11) |
| Python + PyTorch | optional, only for the most demanding stability test (installable from the app) |

### MSI (recommended)

1. Download `nvidiaconf.msi`.
2. Double-click it. The installer requires an NVIDIA driver (`nvlddmkm`); it
   aborts with an explicit message otherwise.
3. Files are installed in `C:\Nvidia`, and `nvidiaconf.exe --tray` is added to
   the machine `Run` key, so the green icon appears in the notification area at
   logon.
4. Uninstall from *Apps & Features*, or with `msiexec /x nvidiaconf.msi`.

### Portable executable

`nvidiaconf.exe` can be run directly (it looks for its data in `C:\Nvidia`,
falling back to `C:\ProgramData\NvidiaUndervolt`). The MSI is preferred because
it also installs the NVAPI engine and the fan service.

### Checksums (v1.0.0)

```
0e4e4730875792ebe2d8d4bca28e81b4f0db25dc0732e3ba939d86c41903a847  nvidiaconf.exe
66063ceb736754c8dc2c7995f1287677b3b349690215b90279e1e160d33ed08c  nvidiaconf.msi
```

### First run

1. The app checks compatibility and refuses to start on unsupported hardware.
2. It shows the current configuration and live sensors.
3. Use **Trouver la configuration optimale** to run the tuning wizard, or
   **Appliquer la configuration** to apply the known-good profile.

### Black-screen risk and TDR guard

Windows resets the display driver if a GPU kernel runs longer than **2 seconds**
(TDR). The screen goes black for a few seconds and may not come back.

The risk exists with **both PyTorch and OpenCL**. Our OpenCL kernel runs for
**~100 ms**, far below the threshold; the observed incident was more likely
caused by the low voltage than by the test engine.

Every OpenCL execution is now timed: above **1500 ms** the test aborts
immediately (`arret=kernel_trop_long`) before Windows resets the driver.

---

## Hardware / software

- NVIDIA GeForce RTX 3080 Ti Founders Edition (GA102, VBIOS `94.02.71.00.ca`)
- Windows 11, NVIDIA driver 621.01
- .NET Framework 4.8 (for `nvfan.exe` and `nvidiaconf.exe`)
- Python only used for the stress/benchmark scripts, not required at runtime
