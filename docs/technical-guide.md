# Technical guide

Deep dive into how the configuration was derived and implemented. Companion to
the main `README.md`.

---

## 1. Hardware and software context

| Item | Value |
|---|---|
| GPU | GeForce RTX 3080 Ti Founders Edition (GA102, device `0x2208`) |
| VBIOS | `94.02.71.00.ca` |
| Driver | NVIDIA 621.01 (Windows 11) |
| Power limits | default 350 W, min 100 W, max 400 W |
| Thermal limits | slowdown 95 °C, shutdown 98 °C |
| Stock clocks | base 1365 MHz, official boost 1665 MHz, domain max 2100 MHz |

The card has two axial fans and two independently reported channels.

---

## 2. Architecture

```
Windows boot
 ├─ Scheduled task NvidiaUndervolt  (SYSTEM, +30 s)
 │    └─ apply-undervolt.ps1
 │         ├─ snu.exe --in-tuning-file plateau-1845-831.json --no-persist
 │         ├─ nvidia-smi -pl 320
 │         └─ verify + log
 └─ Windows service NvidiaFanCurve  (LocalSystem, automatic)
      └─ nvfan.exe service
           ├─ reads fan-curve.txt every 2 s
           ├─ targets a common RPM on both channels
           └─ re-applies the V/F curve if NVAPI is reinitialised
```

Both run in session 0, with no window, and are covered by SCM restart policy
(5 s / 10 s / 30 s, daily reset).

---

## 3. NVAPI functions used

| Purpose | Function | ID |
|---|---|---|
| Init | `NvAPI_Initialize` | `0x0150E828` |
| Enumerate GPUs | `NvAPI_EnumPhysicalGPUs` | `0xE5AC921F` |
| Internal sensors | `NvAPI_GPU_GetAllTempsEx` | `0x65FE3AAD` |
| Fan info | `NvAPI_GPU_ClientFanCoolersGetInfo` | `0xFB85B01E` |
| Fan status | `NvAPI_GPU_ClientFanCoolersGetStatus` | `0x35AED5E8` |
| Fan control (read) | `NvAPI_GPU_ClientFanCoolersGetControl` | `0x814B209F` |
| Fan control (write) | `NvAPI_GPU_ClientFanCoolersSetControl` | `0xA58971A5` |
| Classic cooler API | `NvAPI_GPU_GetCoolerSettings` | `0xDA141340` — **not supported on RTX** |

### 3.1 Why the classic API fails

On RTX cards, `NvAPI_GPU_GetCoolerSettings` and `NvAPI_GPU_SetCoolerLevels`
return `NVAPI_INCOMPATIBLE_STRUCT_VERSION` (−9) for every structure size tried.
The RTX path is `ClientFanCoolers*`, whose structures are:

```c
typedef struct {
    NvU32 coolerId;
    NvU32 level;         /* percent */
    NvU32 controlMode;   /* 0 = auto, 1 = manual */
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

### 3.2 Memory junction and hotspot

The standard `NvAPI_GPU_GetThermalSettings` only returns the GPU sensor. The
undocumented `NvAPI_GPU_GetAllTempsEx` (structure version **2**) exposes the
internal sensors, exactly as GPU-Z and LibreHardwareMonitor do:

| Index | Sensor |
|---|---|
| `Temperatures[0]` | GPU |
| `Temperatures[1]` | hotspot |
| `Temperatures[9]` | memory junction (RTX 30xx) |

Values are in 1/256 °C, and a per-sensor mask must be passed in (`0x01`, `0x02`,
`0x200`).

---

## 4. V/F curve

### 4.1 Reference curve

The stock curve is captured **at idle and cool** (31 °C here) because the
driver's curve drifts with temperature. `snu.exe set-reference-curve` stores it
once; tuning is then computed from that stable reference.

### 4.2 Generating a plateau

For a target frequency `F` at voltage `V`, every anchor with voltage ≥ `V`
receives:

```
offset(anchor) = F − stock_frequency(anchor)
```

which flattens the curve from `V` upward. Anchors below `V` keep offset 0, so
the sub-cap curve remains stock.

The hardware step is **15 MHz**: 1850 MHz does not exist. The driver rounds
1850 to 1860, so 1845 was chosen (closest below) for extra safety margin.

### 4.3 Applying

```
snu.exe --in-tuning-file plateau-1845-831.json --no-persist
nvidia-smi -pl 320
```

`--no-persist` is deliberate: persistence is handled by the scheduled task, not
by the tool's own logon task (which would duplicate ours).

---

## 5. Stability test protocol

The goal was to find the lowest voltage that is genuinely error-free, not just
crash-free.

Each voltage tier was stressed for **240 s** (900 s for the final candidate):

- 15 chained fp16 4096×4096 matmuls, then a 2D convolution;
- each matmul result compared **bit-exact** with a reference computed at the
  start (cuBLAS is deterministic for fixed inputs and algorithm, so any
  difference means a hardware computation error);
- voltage, power, clock and temperature sampled every 3 s;
- system logs checked for `nvlddmkm`, `Display` and `LiveKernelEvent` entries.

A tier passes only if it produces **zero diverging results, zero driver
incidents, and holds the target clock**.

### 5.1 Results

| Voltage | Iterations | Diverging results | Driver events | Verdict |
|---|---|---|---|---|
| 843 mV | 7 542 | 0 | none | pass |
| 837 mV | 7 553 | 0 | none | pass |
| 831 mV | 7 532 | 0 | none | **retained** |
| 825 mV | 28 187 (15 min) | 0 | none | pass (no margin) |
| 818 mV | 7 562 | **2** (max 2.6) | none | **fail** |

**Silicon limit: between 818 and 825 mV.** 831 mV was retained for a safety
margin — 6 mV cost roughly 0.3 % performance and remove the risk of a crash in
a long gaming session.

---

## 6. Power limit

Measured on a game-like load at 825 mV:

| Power limit | Average power | Average clock | GPU | Memory | Fans |
|---|---|---|---|---|---|
| 320 W | 313.8 W | 1821 MHz | 75.3 °C | 85.0 °C | 2059 rpm |
| 310 W | 307.2 W | 1801 MHz | 76.8 °C | 87.3 °C | 2222 rpm |
| 300 W | 299.1 W | 1770 MHz | 76.1 °C | 87.1 °C | 2254 rpm |

320 W is the only value that does not cost clock: below it, the power limit
becomes the limiting factor. 400 W was tested separately and rejected: it adds
50 W of heat, pushes memory junction to 94–96 °C, and brings nothing in games
(270–320 W).

---

## 7. Fan control

### 7.1 Calibration

The two channels respond differently:

| Command | Channel 1 (3800 rpm max) | Channel 2 (3400 rpm max) |
|---|---|---|
| 30 % | 990 rpm | 1000 rpm |
| 40 % | 1402 rpm | 1342 rpm |
| 55 % | 2000 rpm | 1857 rpm |
| 70 % | 2601 rpm | 2371 rpm |

Applying the same percentage would desynchronise the fans (up to 230 rpm apart),
producing an audible beating. The service therefore targets a **common RPM** and
converts it per channel:

```
channel 1:  pct = 30 + (rpm − 990) / 40
channel 2:  pct = 30 + (rpm − 1000) / 34
```

with a bounded adaptive correction (±15 points) to compensate for wear.

### 7.2 Curve

`fan-curve.txt` is a simple `temperature=rpm` list, reloaded automatically when
the file changes:

```
auto_below=46      # below this, hand control back to VBIOS (0 dB mode)
hysteresis=4       # return to auto below 42 °C
failsafe_c=85      # 100 % above
failsafe_mem_c=95  # 100 % if memory junction exceeds this
50=1050
56=1300
62=1550
66=1800
70=2100
73=2400
76=2800
...
```

Manual control cannot go below 30 % (~990 rpm); the 0 dB (fans stopped) state is
only reachable by returning control to the VBIOS.

### 7.3 Profiles

| Profile | Equilibrium at 320 W | Fans | Memory |
|---|---|---|---|
| `silence` | ~81 °C | ~2300 rpm | ~96 °C |
| `equilibre` | ~79 °C | ~2500 rpm | ~94 °C |
| `froid` | ~78 °C | ~2770 rpm | ~91 °C |

Switch at runtime with `set-fan-profile.ps1`.

---

## 8. Persistence details

| Event | Behaviour |
|---|---|
| Windows boot | service starts automatically; task runs at +30 s |
| User logon | task trigger (backup) |
| Resume from sleep | event trigger on `Power-Troubleshooter` id 1 |
| NVIDIA driver update | NVAPI handles invalidated → service reinitialises and re-applies the V/F curve |
| Service crash | SCM restart after 5 s / 10 s / 30 s |
| Reboot | both mechanisms restart |

The fan service detects driver reloads (NVAPI errors) and re-runs
`apply-undervolt.ps1`, which covers a driver update without reboot.

---

## 9. Monitoring application

`nvidiaconf.exe` (C:\Nvidia) is a WinForms tray application:

- reads static data once (identity, limits) via `nvidia-smi` and NVAPI;
- refreshes live data every 2 s (clocks, voltage, power, temperatures, fans);
- displays the applied configuration by reading `undervolt.log` and
  `fan-curve.txt`;
- menu: apply configuration, switch fan profile, open folder, quit.

It is started at logon by the scheduled task `NvidiaConf` (interactive session),
with `--tray` so it starts hidden.

---

## 10. Limitations

1. **Stability is never proven, only not disproven.** 28 187 verified
   computations over 15 minutes at 78.8 °C is strong evidence, not a
   guarantee. Silicon ageing and higher ambient temperatures can reveal
   weaknesses later.
2. **One sample only.** 831 mV is this card's value, not a universal one.
3. **The gain is mostly thermal.** Under a hard power limit, voltage reduction
   only removes losses; it does not create power.
4. **Memory junction is not actively cooled differently.** Keeping it under
   ~90 °C is the reason the fan curve stays firm above 76 °C.

---

## 11. Reproducing

1. Install `simple-nvidia-undervolt`, capture the stock curve at idle.
2. Generate plateau JSON files for candidate voltages.
3. Run the stability protocol above, one tier at a time, descending.
4. Keep the last passing tier plus one step of margin.
5. Choose the power limit just above the real game load.
6. Install the fan service and tune the curve to your noise budget.
