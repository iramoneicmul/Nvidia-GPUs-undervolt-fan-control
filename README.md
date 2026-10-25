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
C:\Nvidia\nvidiaconf.exe --autotune     # or the button in the window
```

1. Reads the **real** voltage table from `courbe-stock.json` (no invented values).
2. For each tier, from 843 mV downward: writes a plateau curve, applies it via
   NVAPI, then runs a **240 s** stability test with bit-exact result checking.
3. Stops at the first unstable tier.
4. **Safety margin**: keeps the tier *above* the last stable one.
5. Writes `plateau-active.json` and a timestamped Markdown report.
6. Applies the power limit.

A progress bar and a step label show what is running at all times; every step
is also logged. The target frequency is read from `autotune.ini`
(`target_mhz`, 15 MHz hardware step).

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

**Latest release:** <https://github.com/iramoneicmul/rtx3080ti-undervolt-fan-control/releases/latest>

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
d55ec7065c793eeedd43aaf87d9da9a5f6158dd0abdbcddde8b3c81c2dd1c58b  nvidiaconf.exe
90a1da65c8e180356a0303df49b30635a1f4e9b093578a97629829de6ef4de8e  nvidiaconf.msi
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
