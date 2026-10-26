// =====================================================================
//  nvfan.cs  -  regulation des ventilateurs NVIDIA via NVAPI
//               API ClientFanCoolers (cartes RTX)
//
//  Deux modes d'execution :
//    - console : nvfan get | set | auto | curve
//    - service : nvfan service   (lance par le SCM, nom "NvidiaFanCurve")
//
//  Compilation (aucun SDK requis, .NET Framework 4.x) :
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//        /target:exe /platform:x64 /out:nvfan.exe
//        /r:System.ServiceProcess.dll nvfan.cs
//
//  Principe :
//    - courbe en TR/MIN cible (pas en %), pour que les deux
//      ventilateurs tournent a la meme vitesse malgre des plages
//      differentes (3800 et 3400 tr/min) ;
//    - conversion tr/min -> % par calibration mesuree sur la carte,
//      avec correction adaptative (offset borne) ;
//    - sous "auto_below", le service rend la main au VBIOS
//      (mode 0 dB, ventilateurs a l'arret) ;
//    - reinitialisation NVAPI automatique apres mise a jour du pilote
//      ou reprise de veille, puis reapplication de l'undervolt ;
//    - fail-safe : 100 % au-dela de failsafe_c.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

#region structures NVAPI (ClientFanCoolers, Pack=8)

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanInfoEntry
{
    public uint coolerId;
    public uint unknown3;
    public uint unknown4;
    public uint maxRpm;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanCoolersInfo
{
    public uint version;
    public uint unknown1;
    public uint count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public FanInfoEntry[] entries;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanStatusEntry
{
    public uint coolerId;
    public uint currentRpm;
    public uint minLevel;
    public uint maxLevel;
    public uint currentLevel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanCoolersStatus
{
    public uint version;
    public uint count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public FanStatusEntry[] entries;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanControlEntry
{
    public uint coolerId;
    public uint level;
    public uint controlMode;   // 0 = auto, 1 = manuel
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanCoolersControl
{
    public uint version;
    public uint unknown;
    public uint count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
    public uint[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public FanControlEntry[] entries;
}

// --- API classique (Maxwell / Pascal / avant RTX) ---
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct LegacyCoolerLevel
{
    public uint coolerId;
    public short currentLevel;
    public short defaultLevel;
    public uint currentPolicy;
    public uint defaultPolicy;
    public uint target;
    public uint controlType;
    public uint active;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct LegacyCoolerSettings
{
    public uint version;
    public uint count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public LegacyCoolerLevel[] level;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ThermalSensor
{
    public uint controller;
    public uint defaultMinTemp;
    public uint defaultMaxTemp;
    public uint currentTemp;
    public uint target;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ThermalSettings
{
    public uint version;
    public uint count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public ThermalSensor[] sensor;
}

// NvAPI_GPU_GetAllTempsEx (0x65FE3AAD) : capteurs internes, version 2.
// Temperatures sont en 1/256 de degre Celsius.
//   [0] GPU, [1] hotspot, [9] jonction memoire (RTX 30xx)
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ThermalSensorsEx
{
    public uint version;
    public uint mask;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public int[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public int[] temperatures;
}

#endregion

/// <summary>Acces NVAPI, reinitialisable (mise a jour du pilote, veille).</summary>
internal sealed class NvApi
{
    private const uint MAX_GPUS = 64;
    private const int MAX_ENTRIES = 32;
    private const uint OK = 0;

    private const uint ID_Initialize = 0x0150E828;
    private const uint ID_EnumPhysicalGPUs = 0xE5AC921F;
    private const uint ID_FanGetInfo = 0xFB85B01E;
    private const uint ID_FanGetStatus = 0x35AED5E8;
    private const uint ID_FanGetControl = 0x814B209F;
    private const uint ID_FanSetControl = 0xA58971A5;
    private const uint ID_GetThermal = 0xE3640A56;
    private const uint ID_GetAllTemps = 0x65FE3AAD;
    private const uint ID_GetCoolerSettings = 0xDA141340;
    private const uint ID_SetCoolerLevels = 0x891FA0AE;
    private const uint ID_GetTach = 0x5F608315;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private delegate uint D_Init();
    private delegate uint D_Enum([Out] IntPtr[] handles, out uint count);
    private delegate uint D_FanInfo(IntPtr gpu, ref FanCoolersInfo info);
    private delegate uint D_FanStatus(IntPtr gpu, ref FanCoolersStatus status);
    private delegate uint D_FanControl(IntPtr gpu, ref FanCoolersControl control);
    private delegate uint D_Thermal(IntPtr gpu, uint sensorIndex, ref ThermalSettings settings);
    private delegate uint D_TempsEx(IntPtr gpu, ref ThermalSensorsEx sensors);
    private delegate uint D_GetCooler(IntPtr gpu, uint index, ref LegacyCoolerSettings settings);
    private delegate uint D_SetCooler(IntPtr gpu, uint index, ref LegacyCoolerSettings levels);
    private delegate uint D_Tach(IntPtr gpu, out uint rpm);

    private D_Init _init;
    private D_Enum _enum;
    private D_FanInfo _info;
    private D_FanStatus _status;
    private D_FanControl _getControl;
    private D_FanControl _setControl;
    private D_Thermal _thermal;
    private D_TempsEx _tempsEx;
    private D_GetCooler _getCooler;
    private D_SetCooler _setCooler;
    private D_Tach _tach;
    private bool _legacy;
    private IntPtr _gpu;
    private bool _ready;

    public bool Ready { get { return _ready; } }

    private static T Bind<T>(uint id) where T : class
    {
        IntPtr p = QueryInterface(id);
        if (p == IntPtr.Zero)
            throw new InvalidOperationException("NVAPI : fonction indisponible 0x" + id.ToString("X8"));
        return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    public bool Open()
    {
        _ready = false;
        try
        {
            _init = Bind<D_Init>(ID_Initialize);
            _enum = Bind<D_Enum>(ID_EnumPhysicalGPUs);
            _info = Bind<D_FanInfo>(ID_FanGetInfo);
            _status = Bind<D_FanStatus>(ID_FanGetStatus);
            _getControl = Bind<D_FanControl>(ID_FanGetControl);
            _setControl = Bind<D_FanControl>(ID_FanSetControl);
            _thermal = Bind<D_Thermal>(ID_GetThermal);
            _tempsEx = Bind<D_TempsEx>(ID_GetAllTemps);

            try
            {
                _getCooler = Bind<D_GetCooler>(ID_GetCoolerSettings);
                _setCooler = Bind<D_SetCooler>(ID_SetCoolerLevels);
                _tach = Bind<D_Tach>(ID_GetTach);
            }
            catch { _getCooler = null; _setCooler = null; _tach = null; }

            if (_init() != OK) return false;

            var handles = new IntPtr[MAX_GPUS];
            uint n;
            if (_enum(handles, out n) != OK || n == 0) return false;
            _gpu = handles[0];

            // RTX : ClientFanCoolers. Maxwell/Pascal : API classique.
            _legacy = false;
            try
            {
                var probe = new FanCoolersInfo();
                probe.version = (uint)Marshal.SizeOf(typeof(FanCoolersInfo)) | (1u << 16);
                probe.reserved = new uint[8];
                probe.entries = new FanInfoEntry[32];
                for (int k = 0; k < 32; k++) probe.entries[k].reserved = new uint[8];
                if (_info(_gpu, ref probe) != OK) _legacy = true;
            }
            catch { _legacy = true; }

            _ready = true;
            return true;
        }
        catch
        {
            _ready = false;
            return false;
        }
    }

    public void Close()
    {
        _ready = false;
        _gpu = IntPtr.Zero;
    }

    public bool Legacy { get { return _legacy; } }

    private LegacyCoolerSettings ReadLegacy()
    {
        if (_getCooler == null) throw new InvalidOperationException("API classique indisponible");
        var s = new LegacyCoolerSettings();
        s.version = (uint)Marshal.SizeOf(typeof(LegacyCoolerSettings)) | (1u << 16);
        s.level = new LegacyCoolerLevel[20];
        uint st = _getCooler(_gpu, 0, ref s);
        if (st != OK) throw new InvalidOperationException("GetCoolerSettings : " + st);
        return s;
    }

    private void WriteLegacy(LegacyCoolerSettings s)
    {
        if (_setCooler == null) throw new InvalidOperationException("API classique indisponible");
        uint st = _setCooler(_gpu, 0, ref s);
        if (st != OK) throw new InvalidOperationException("SetCoolerLevels : " + st);
    }

    public FanCoolersInfo GetInfo()
    {
        if (_legacy)
        {
            var fake = new FanCoolersInfo();
            fake.version = (uint)Marshal.SizeOf(typeof(FanCoolersInfo)) | (1u << 16);
            fake.reserved = new uint[8];
            fake.entries = new FanInfoEntry[32];
            for (int k = 0; k < 32; k++) fake.entries[k].reserved = new uint[8];
            fake.count = 1;
            fake.entries[0].coolerId = 0;
            fake.entries[0].maxRpm = 0;
            return fake;
        }

        var i = new FanCoolersInfo();
        i.version = (uint)Marshal.SizeOf(typeof(FanCoolersInfo)) | (1u << 16);
        i.reserved = new uint[8];
        i.entries = new FanInfoEntry[MAX_ENTRIES];
        for (int k = 0; k < MAX_ENTRIES; k++) i.entries[k].reserved = new uint[8];
        uint st = _info(_gpu, ref i);
        if (st != OK) throw new InvalidOperationException("FanCoolersGetInfo : " + st);
        return i;
    }

    public FanCoolersStatus GetStatus()
    {
        if (_legacy)
        {
            var l = ReadLegacy();
            uint rpm = 0;
            try { if (_tach != null) _tach(_gpu, out rpm); } catch { }
            var fake = new FanCoolersStatus();
            fake.version = (uint)Marshal.SizeOf(typeof(FanCoolersStatus)) | (1u << 16);
            fake.reserved = new uint[8];
            fake.entries = new FanStatusEntry[32];
            for (int k = 0; k < 32; k++) fake.entries[k].reserved = new uint[8];
            fake.count = 1;
            fake.entries[0].coolerId = 0;
            fake.entries[0].currentRpm = rpm;
            fake.entries[0].minLevel = 0;
            fake.entries[0].maxLevel = 100;
            fake.entries[0].currentLevel = (uint)(l.count > 0 ? l.level[0].currentLevel : 0);
            return fake;
        }

        var s = new FanCoolersStatus();
        s.version = (uint)Marshal.SizeOf(typeof(FanCoolersStatus)) | (1u << 16);
        s.reserved = new uint[8];
        s.entries = new FanStatusEntry[MAX_ENTRIES];
        for (int k = 0; k < MAX_ENTRIES; k++) s.entries[k].reserved = new uint[8];
        uint st = _status(_gpu, ref s);
        if (st != OK) throw new InvalidOperationException("FanCoolersGetStatus : " + st);
        return s;
    }

    public FanCoolersControl GetControl()
    {
        var c = new FanCoolersControl();
        c.version = (uint)Marshal.SizeOf(typeof(FanCoolersControl)) | (1u << 16);
        c.reserved = new uint[8];
        c.entries = new FanControlEntry[MAX_ENTRIES];
        for (int k = 0; k < MAX_ENTRIES; k++) c.entries[k].reserved = new uint[8];
        uint st = _getControl(_gpu, ref c);
        if (st != OK) throw new InvalidOperationException("FanCoolersGetControl : " + st);
        return c;
    }

    public void SetControl(FanCoolersControl c)
    {
        uint st = _setControl(_gpu, ref c);
        if (st != OK) throw new InvalidOperationException("FanCoolersSetControl : " + st);
    }

    public int GpuTemp()
    {
        var t = new ThermalSettings();
        t.version = (uint)Marshal.SizeOf(typeof(ThermalSettings)) | (1u << 16);
        t.sensor = new ThermalSensor[3];
        uint st = _thermal(_gpu, 0, ref t);
        if (st != OK) throw new InvalidOperationException("GetThermalSettings : " + st);
        return (int)t.sensor[0].currentTemp;
    }

    /// <summary>Capteurs internes : GPU, hotspot, jonction memoire. -1 si absent.</summary>
    public void InternalTemps(out int gpu, out int hotspot, out int memory)
    {
        gpu = hotspot = memory = -1;
        gpu = ReadSensor(0x01, 0);
        hotspot = ReadSensor(0x02, 1);
        memory = ReadSensor(0x200, 9);
    }

    private int ReadSensor(uint mask, int index)
    {
        try
        {
            var t = new ThermalSensorsEx();
            t.version = (uint)Marshal.SizeOf(typeof(ThermalSensorsEx)) | (2u << 16);
            t.reserved = new int[8];
            t.temperatures = new int[32];
            t.mask = mask;
            if (_tempsEx(_gpu, ref t) != OK) return -1;
            int raw = t.temperatures[index];
            if (raw <= 0) return -1;
            double c = raw / 256.0;
            if (c < 1 || c > 200) return -1;
            return (int)Math.Round(c);
        }
        catch { return -1; }
    }

    /// <summary>Ecrit le niveau de chaque canal. levels[k] = pourcentage.</summary>
    public void ApplyLevels(FanCoolersInfo info, int[] levels, uint mode)
    {
        if (_legacy)
        {
            var l = ReadLegacy();
            if (l.count > 0)
            {
                l.level[0].currentLevel = (short)Math.Max(0, Math.Min(100, levels[0]));
                l.level[0].currentPolicy = 1; // manuel
                l.level[0].active = 1;
                WriteLegacy(l);
            }
            return;
        }

        var c = new FanCoolersControl();
        c.version = (uint)Marshal.SizeOf(typeof(FanCoolersControl)) | (1u << 16);
        c.reserved = new uint[8];
        c.entries = new FanControlEntry[MAX_ENTRIES];
        int n = Math.Min((int)info.count, MAX_ENTRIES);
        c.count = (uint)n;
        for (int k = 0; k < n; k++)
        {
            c.entries[k].coolerId = info.entries[k].coolerId;
            c.entries[k].level = (uint)Math.Max(0, Math.Min(100, levels[k]));
            c.entries[k].controlMode = mode;
            c.entries[k].reserved = new uint[8];
        }
        SetControl(c);
    }

    public void ApplyAuto(FanCoolersInfo info)
    {
        if (_legacy)
        {
            try
            {
                var l = ReadLegacy();
                if (l.count > 0)
                {
                    l.level[0].currentPolicy = 2; // auto
                    l.level[0].active = 1;
                    WriteLegacy(l);
                }
            }
            catch { }
            return;
        }

        var c = new FanCoolersControl();
        c.version = (uint)Marshal.SizeOf(typeof(FanCoolersControl)) | (1u << 16);
        c.reserved = new uint[8];
        c.entries = new FanControlEntry[MAX_ENTRIES];
        int n = Math.Min((int)info.count, MAX_ENTRIES);
        c.count = (uint)n;
        for (int k = 0; k < n; k++)
        {
            c.entries[k].coolerId = info.entries[k].coolerId;
            c.entries[k].level = 0;
            c.entries[k].controlMode = 0;
            c.entries[k].reserved = new uint[8];
        }
        SetControl(c);
    }
}

/// <summary>Configuration de la courbe, rechargeable a chaud.</summary>
internal sealed class FanConfig
{
    public int AutoBelow = 46;
    public int Hysteresis = 4;
    public int IntervalMs = 2000;
    public int FailsafeC = 85;
    public int FailsafeMemC = 95;
    public int ReapplyUndervoltMinutes = 5;
    public string UndervoltScript = @"C:\ProgramData\NvidiaUndervolt\apply-undervolt.ps1";
    public readonly List<KeyValuePair<int, int>> Points = new List<KeyValuePair<int, int>>();

    public static FanConfig Load(string path)
    {
        var cfg = new FanConfig();
        if (!File.Exists(path)) return cfg;

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            string val = line.Substring(eq + 1).Trim();

            if (key == "undervolt_script") { cfg.UndervoltScript = val; continue; }

            int iv;
            if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) continue;

            switch (key)
            {
                case "auto_below": cfg.AutoBelow = iv; break;
                case "hysteresis": cfg.Hysteresis = iv; break;
                case "interval_ms": cfg.IntervalMs = Math.Max(500, iv); break;
                case "failsafe_c": cfg.FailsafeC = iv; break;
                case "failsafe_mem_c": cfg.FailsafeMemC = iv; break;
                case "reapply_undervolt_minutes": cfg.ReapplyUndervoltMinutes = iv; break;
                default:
                    int t;
                    if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out t))
                        cfg.Points.Add(new KeyValuePair<int, int>(t, iv));
                    break;
            }
        }
        cfg.Points.Sort((a, b) => a.Key.CompareTo(b.Key));
        return cfg;
    }

    public int RpmFor(int temp)
    {
        if (Points.Count == 0) return 0;
        int rpm = Points[0].Value;
        for (int i = 0; i < Points.Count; i++)
        {
            if (temp >= Points[i].Key) rpm = Points[i].Value;
            else break;
        }
        return rpm;
    }
}

/// <summary>Moteur de regulation, partage entre le mode console et le service.</summary>
internal sealed class CurveEngine
{
    private readonly string _root;
    private readonly string _configPath;
    private readonly Action<string> _log;
    private readonly NvApi _api = new NvApi();

    private FanConfig _cfg;
    private DateTime _configStamp;
    private bool _manual;
    private bool _autoApplied;
    private int[] _offsets;              // correction adaptative par canal
    private DateTime _lastUndervolt = DateTime.MinValue;
    private int _lastMemTemp = -1;
    private int _lastHotspot = -1;

    public CurveEngine(string root, Action<string> log)
    {
        _root = root;
        _configPath = Path.Combine(root, "fan-curve.txt");
        _log = log;
    }

    private bool _everOpened;

    private void EnsureOpen()
    {
        if (_api.Ready) return;
        while (!_api.Open())
        {
            _log("NVAPI indisponible, nouvelle tentative dans 5 s.");
            Thread.Sleep(5000);
        }
        _log("NVAPI initialise.");
        // Au tout premier demarrage, la tache planifiee NvidiaUndervolt
        // s'occupe deja de la courbe V/F : on ne double pas l'appel.
        // Apres un rechargement du pilote en cours de session, en revanche,
        // la courbe est perdue : on la reapplique.
        if (_everOpened) ReapplyUndervolt();
        _everOpened = true;
    }

    private void ReapplyUndervolt()
    {
        if (_cfg == null || _cfg.ReapplyUndervoltMinutes <= 0) return;
        if ((DateTime.Now - _lastUndervolt).TotalMinutes < _cfg.ReapplyUndervoltMinutes) return;
        if (!File.Exists(_cfg.UndervoltScript)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + _cfg.UndervoltScript + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
            _lastUndervolt = DateTime.Now;
            _log("Reapplication de l'undervolt demandee.");
        }
        catch (Exception e) { _log("Undervolt : " + e.Message); }
    }

    private void LoadConfigIfChanged()
    {
        DateTime stamp = File.Exists(_configPath) ? File.GetLastWriteTimeUtc(_configPath) : DateTime.MinValue;
        if (_cfg != null && stamp == _configStamp) return;
        _cfg = FanConfig.Load(_configPath);
        _configStamp = stamp;
        _log("Configuration chargee : " + _cfg.Points.Count + " points, auto sous " + _cfg.AutoBelow + " C.");
    }

    /// <summary>Un cycle de regulation. Retourne false si NVAPI a du etre reinitialise.</summary>
    public bool Tick()
    {
        try
        {
            EnsureOpen();
            LoadConfigIfChanged();

            var info = _api.GetInfo();
            int n = (int)info.count;
            if (n <= 0) return true;

            if (_offsets == null || _offsets.Length != n) _offsets = new int[n];

            int temp;
            try { temp = _api.GpuTemp(); }
            catch { _api.Close(); return false; }

            int gpuEx, hotspot, memTemp;
            _api.InternalTemps(out gpuEx, out hotspot, out memTemp);
            _lastMemTemp = memTemp;
            _lastHotspot = hotspot;

            // zone 0 dB : on rend la main au VBIOS
            bool wantAuto = temp <= (_cfg.AutoBelow - _cfg.Hysteresis) ||
                            (!_manual && temp < _cfg.AutoBelow);

            if (wantAuto)
            {
                // On force l'auto aussi au premier cycle : les canaux ont pu
                // etre laisses en manuel par un essai ou un autre outil.
                if (_manual || !_autoApplied)
                {
                    _api.ApplyAuto(info);
                    _manual = false;
                    _autoApplied = true;
                    _log("Temp " + temp + " C -> mode auto (0 dB).");
                }
                return true;
            }
            _autoApplied = false;

            int rpm = _cfg.RpmFor(temp);
            if (temp >= _cfg.FailsafeC) rpm = 3800;
            if (memTemp > 0 && memTemp >= _cfg.FailsafeMemC) rpm = 3800;
            if (hotspot > 0 && hotspot >= _cfg.FailsafeC + 10) rpm = 3800;

            var st = _api.GetStatus();
            var levels = new int[n];

            for (int k = 0; k < n; k++)
            {
                uint maxRpm = info.entries[k].maxRpm;
                double baseRpm = maxRpm >= 3600 ? 990.0 : 1000.0;
                double slopeRpm = maxRpm >= 3600 ? 40.0 : 34.0;
                double pct = 30.0 + (rpm - baseRpm) / slopeRpm + _offsets[k];

                // correction adaptative : on vise le meme regime sur les deux canaux
                int real = -1;
                for (int j = 0; j < st.count; j++)
                    if (st.entries[j].coolerId == info.entries[k].coolerId)
                        real = (int)st.entries[j].currentRpm;

                if (real > 0 && _manual)
                {
                    int err = rpm - real;
                    if (err > 80 && _offsets[k] < 15) _offsets[k]++;
                    else if (err < -80 && _offsets[k] > -15) _offsets[k]--;
                }

                levels[k] = (int)Math.Round(pct);
            }

            _api.ApplyLevels(info, levels, 1);
            if (!_manual)
            {
                _manual = true;
                _log("Temp " + temp + " C (hotspot " + hotspot + ", memoire " + memTemp +
                     ") -> mode manuel, cible " + rpm + " tr/min.");
            }
            return true;
        }
        catch (Exception e)
        {
            _log("Erreur : " + e.Message + " (reinitialisation NVAPI)");
            _api.Close();
            Thread.Sleep(2000);
            return false;
        }
    }

    public void RestoreAuto()
    {
        try
        {
            if (_api.Ready)
            {
                var info = _api.GetInfo();
                _api.ApplyAuto(info);
                _log("Mode auto restaure a l'arret.");
            }
        }
        catch (Exception e) { _log("Restauration auto : " + e.Message); }
        _manual = false;
    }
}

internal sealed class FanCurveService : ServiceBase
{
    private Thread _thread;
    private volatile bool _stop;
    private CurveEngine _engine;

    public FanCurveService()
    {
        ServiceName = "NvidiaFanCurve";
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    private static void Log(string root, string msg)
    {
        try
        {
            string p = Path.Combine(root, "fan-curve.log");
            var fi = new FileInfo(p);
            if (fi.Exists && fi.Length > 256 * 1024)
                File.Move(p, p + ".old");
            File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [SRV] " + msg + Environment.NewLine);
        }
        catch { }
    }

    protected override void OnStart(string[] args)
    {
        string root = Paths.Root;
        Directory.CreateDirectory(root);
        _engine = new CurveEngine(root, m => Log(root, m));
        _thread = new Thread(Loop);
        _thread.IsBackground = true;
        _thread.Start();
        Log(root, "Service demarre.");
    }

    private void Loop()
    {
        while (!_stop)
        {
            _engine.Tick();
            int ms = 2000;
            for (int i = 0; i < ms / 100 && !_stop; i++) Thread.Sleep(100);
        }
    }

    protected override void OnStop()
    {
        _stop = true;
        if (_thread != null) _thread.Join(5000);
        if (_engine != null) _engine.RestoreAuto();
        Log(Paths.Root, "Service arrete.");
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }
}

internal static class Paths
{
    public static string Root
    {
        get
        {
            string a = @"C:\Nvidia";
            if (File.Exists(Path.Combine(a, "snu.exe"))) return a;
            return @"C:\ProgramData\NvidiaUndervolt";
        }
    }
}

internal static class Program
{
    private static string Root { get { return Paths.Root; } }

    private static void ConsoleLog(string msg)
    {
        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + msg);
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].ToLowerInvariant() == "service")
        {
            ServiceBase.Run(new FanCurveService());
            return 0;
        }

        if (args.Length == 0) { Usage(); return 2; }

        // mode console de regulation : pas besoin d'ouvrir NVAPI ici
        if (args[0].ToLowerInvariant() == "curve")
            return Curve(null);

        var api = new NvApi();
        if (!api.Open()) { Console.Error.WriteLine("NVAPI indisponible."); return 1; }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "get": return Get(api);

                case "auto":
                {
                    var info = api.GetInfo();
                    api.ApplyAuto(info);
                    Console.WriteLine("Mode auto restaure.");
                    return Get(api);
                }

                case "set":
                {
                    if (args.Length < 3) { Usage(); return 2; }
                    int id = int.Parse(args[1], CultureInfo.InvariantCulture);
                    int pct = int.Parse(args[2], CultureInfo.InvariantCulture);
                    if (pct < 30 || pct > 100) { Console.Error.WriteLine("pct hors 30..100"); return 2; }
                    var info = api.GetInfo();
                    var cur = api.GetControl();
                    var c = new FanCoolersControl();
                    c.version = (uint)Marshal.SizeOf(typeof(FanCoolersControl)) | (1u << 16);
                    c.reserved = new uint[8];
                    c.entries = new FanControlEntry[32];
                    int n = (int)info.count;
                    c.count = (uint)n;
                    for (int k = 0; k < n; k++)
                    {
                        uint cid = info.entries[k].coolerId;
                        uint lv = 0, cm = 0;
                        for (int j = 0; j < cur.count && j < 32; j++)
                            if (cur.entries[j].coolerId == cid) { lv = cur.entries[j].level; cm = cur.entries[j].controlMode; }
                        if (cid == (uint)id) { lv = (uint)pct; cm = 1; }
                        c.entries[k].coolerId = cid;
                        c.entries[k].level = lv;
                        c.entries[k].controlMode = cm;
                        c.entries[k].reserved = new uint[8];
                    }
                    api.SetControl(c);
                    Console.WriteLine("Canal " + id + " -> " + pct + " %.");
                    Thread.Sleep(1500);
                    return Get(api);
                }

                case "csv":
                {
                    var info = api.GetInfo();
                    var st = api.GetStatus();
                    var parts = new List<string>();
                    int g, h, m;
                    api.InternalTemps(out g, out h, out m);
                    if (g < 0) { try { g = api.GpuTemp(); } catch { g = -1; } }
                    parts.Add(g.ToString(CultureInfo.InvariantCulture));
                    parts.Add(h.ToString(CultureInfo.InvariantCulture));
                    parts.Add(m.ToString(CultureInfo.InvariantCulture));
                    for (int k = 0; k < info.count && k < 32; k++)
                    {
                        uint id = info.entries[k].coolerId;
                        int rpm = 0, lvl = 0;
                        for (int j = 0; j < st.count && j < 32; j++)
                            if (st.entries[j].coolerId == id) { rpm = (int)st.entries[j].currentRpm; lvl = (int)st.entries[j].currentLevel; }
                        parts.Add(rpm.ToString(CultureInfo.InvariantCulture));
                        parts.Add(lvl.ToString(CultureInfo.InvariantCulture));
                    }
                    Console.WriteLine(string.Join(",", parts));
                    return 0;
                }

                default: Usage(); return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Erreur : " + e.Message);
            return 1;
        }
    }

    private static int Get(NvApi api)
    {
        var info = api.GetInfo();
        var st = api.GetStatus();
        int g, h, m;
        api.InternalTemps(out g, out h, out m);
        if (g < 0) { try { g = api.GpuTemp(); } catch { g = -1; } }
        Console.WriteLine("GPU : " + g + " C  |  hotspot : " + h + " C  |  memoire : " + m + " C");
        Console.WriteLine("Canaux : " + info.count);
        for (int k = 0; k < info.count && k < 32; k++)
        {
            uint id = info.entries[k].coolerId;
            Console.Write("  canal id=" + id + "  max " + info.entries[k].maxRpm + " tr/min");
            for (int j = 0; j < st.count && j < 32; j++)
                if (st.entries[j].coolerId == id)
                    Console.Write("  |  " + st.entries[j].currentRpm + " tr/min, " +
                                  st.entries[j].currentLevel + " %");
            Console.WriteLine();
        }
        return 0;
    }

    private static int Curve(NvApi api)
    {
        Console.WriteLine("Regulation active (Ctrl+C pour arreter). Configuration : " + Path.Combine(Root, "fan-curve.txt"));
        bool stop = false;
        Console.CancelKeyPress += (o, e) => { e.Cancel = true; stop = true; };
        var engine = new CurveEngine(Root, ConsoleLog);
        while (!stop)
        {
            engine.Tick();
            for (int i = 0; i < 20 && !stop; i++) Thread.Sleep(100);
        }
        engine.RestoreAuto();
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("nvfan get");
        Console.WriteLine("nvfan set <canal> <30..100>");
        Console.WriteLine("nvfan auto");
        Console.WriteLine("nvfan curve      (lit C:\\ProgramData\\NvidiaUndervolt\\fan-curve.txt)");
        Console.WriteLine("nvfan service    (mode service Windows)");
    }
}
