// =====================================================================
//  nvidiaconf.cs  -  moniteur de configuration NVIDIA
//  RTX 3080 Ti Founders Edition - Hellfire
//
//  Compilation (Windows, .NET Framework 4.x) :
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//      /target:winexe /platform:x64 /out:nvidiaconf.exe
//      /win32icon:icon.ico
//      /r:System.Windows.Forms.dll /r:System.Drawing.dll nvidiaconf.cs
//
//  Affiche la configuration appliquee (undervolt, power limit, ventilation)
//  et les informations statiques + temps reel de la carte.
//  Icone dans la zone de notification, menu contextuel.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

#region NVAPI : capteurs internes et ventilateurs

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanInfoEntry
{
    public uint coolerId, unknown3, unknown4, maxRpm;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] reserved;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanCoolersInfo
{
    public uint version, unknown1, count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public FanInfoEntry[] entries;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanStatusEntry
{
    public uint coolerId, currentRpm, minLevel, maxLevel, currentLevel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] reserved;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FanCoolersStatus
{
    public uint version, count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public FanStatusEntry[] entries;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ThermalSensorsEx
{
    public uint version, mask;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public int[] temperatures;
}

internal static class NvSensors
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QI(uint id);

    private delegate uint D_Init();
    private delegate uint D_Enum([Out] IntPtr[] h, out uint n);
    private delegate uint D_Temps(IntPtr gpu, ref ThermalSensorsEx t);
    private delegate uint D_FanInfo(IntPtr gpu, ref FanCoolersInfo i);
    private delegate uint D_FanStatus(IntPtr gpu, ref FanCoolersStatus s);
    private delegate uint D_FanControl(IntPtr gpu, ref object c);

    private static D_Temps _temps;
    private static D_FanInfo _fanInfo;
    private static D_FanStatus _fanStatus;
    private static IntPtr _gpu;
    private static bool _ok;

    public static bool Init()
    {
        if (_ok) return true;
        try
        {
            var init = Bind<D_Init>(0x0150E828);
            var en = Bind<D_Enum>(0xE5AC921F);
            _temps = Bind<D_Temps>(0x65FE3AAD);
            _fanInfo = Bind<D_FanInfo>(0xFB85B01E);
            _fanStatus = Bind<D_FanStatus>(0x35AED5E8);
            if (init() != 0) return false;
            var h = new IntPtr[64];
            uint n;
            if (en(h, out n) != 0 || n == 0) return false;
            _gpu = h[0];
            _ok = true;
            return true;
        }
        catch { return false; }
    }

    private static T Bind<T>(uint id) where T : class
    {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(QI(id), typeof(T));
    }

    private static int Sensor(uint mask, int index)
    {
        try
        {
            var t = new ThermalSensorsEx();
            t.version = (uint)Marshal.SizeOf(typeof(ThermalSensorsEx)) | (2u << 16);
            t.reserved = new int[8];
            t.temperatures = new int[32];
            t.mask = mask;
            if (_temps(_gpu, ref t) != 0) return -1;
            int raw = t.temperatures[index];
            if (raw <= 0) return -1;
            double c = raw / 256.0;
            if (c < 1 || c > 200) return -1;
            return (int)Math.Round(c);
        }
        catch { return -1; }
    }

    public static void ReadTemps(out int gpu, out int hotspot, out int mem)
    {
        gpu = hotspot = mem = -1;
        if (!Init()) return;
        gpu = Sensor(0x01, 0);
        hotspot = Sensor(0x02, 1);
        mem = Sensor(0x200, 9);
    }

    public static void ReadFans(out int rpm1, out int pct1, out int rpm2, out int pct2)
    {
        rpm1 = pct1 = rpm2 = pct2 = -1;
        if (!Init()) return;
        try
        {
            var info = new FanCoolersInfo();
            info.version = (uint)Marshal.SizeOf(typeof(FanCoolersInfo)) | (1u << 16);
            info.reserved = new uint[8];
            info.entries = new FanInfoEntry[32];
            for (int i = 0; i < 32; i++) info.entries[i].reserved = new uint[8];
            if (_fanInfo(_gpu, ref info) != 0) return;

            var st = new FanCoolersStatus();
            st.version = (uint)Marshal.SizeOf(typeof(FanCoolersStatus)) | (1u << 16);
            st.reserved = new uint[8];
            st.entries = new FanStatusEntry[32];
            for (int i = 0; i < 32; i++) st.entries[i].reserved = new uint[8];
            if (_fanStatus(_gpu, ref st) != 0) return;

            for (int k = 0; k < info.count && k < 32; k++)
            {
                uint id = info.entries[k].coolerId;
                for (int j = 0; j < st.count && j < 32; j++)
                {
                    if (st.entries[j].coolerId != id) continue;
                    if (k == 0) { rpm1 = (int)st.entries[j].currentRpm; pct1 = (int)st.entries[j].currentLevel; }
                    else if (k == 1) { rpm2 = (int)st.entries[j].currentRpm; pct2 = (int)st.entries[j].currentLevel; }
                }
            }
        }
        catch { }
    }
}

#endregion

internal sealed class Snapshot
{
    public string Name = "", Driver = "", Vbios = "", BusId = "", DeviceId = "", SubId = "";
    public long VramTotal, VramUsed;
    public int ClockNow, ClockMax, MemClockNow, MemClockMax;
    public int PowerMin, PowerMax, PowerDefault;
    public double PowerNow, PowerLimit;
    public int TempShutdown, TempSlowdown;
    public int Temp, Hotspot, MemTemp, Fan1Rpm, Fan1Pct, Fan2Rpm, Fan2Pct;
    public double VoltageMv;
    public int GpuUsage;
    public string Undervolt = "?", Profile = "?", Curve831 = "?";
}

internal sealed class MainForm : Form
{
    private const string Root = @"C:\ProgramData\NvidiaUndervolt";
    private const string Smi = "nvidia-smi";

    private readonly NotifyIcon _tray;
    private readonly Timer _timer;
    private readonly Snapshot _s = new Snapshot();

    private Label _lTitle, _lStatus;
    private readonly Dictionary<string, Label> _v = new Dictionary<string, Label>();

    private readonly bool _startHidden;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        Text = "NVIDIA Configuration - RTX 3080 Ti FE";
        ClientSize = new Size(560, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildUi();

        _tray = new NotifyIcon();
        try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        _tray.Visible = true;
        _tray.Text = "NVIDIA Configuration";

        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (o, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Actualiser", null, (o, e) => Refresh(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Appliquer la configuration", null, (o, e) => RunScript("apply-undervolt.ps1"));
        var prof = new ToolStripMenuItem("Profil de ventilation");
        prof.DropDownItems.Add("Silence", null, (o, e) => SetProfile("silence"));
        prof.DropDownItems.Add("Equilibre", null, (o, e) => SetProfile("equilibre"));
        prof.DropDownItems.Add("Froid", null, (o, e) => SetProfile("froid"));
        menu.Items.Add(prof);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ouvrir le dossier", null, (o, e) => Process.Start("explorer.exe", Root));
        menu.Items.Add("Quitter", null, (o, e) => { _tray.Visible = false; Application.Exit(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (o, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        _timer = new Timer { Interval = 2000 };
        _timer.Tick += (o, e) => Refresh(false);
        _timer.Start();

        Refresh(true);
        if (_startHidden)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        int y = 14;

        _lTitle = new Label
        {
            Text = "NVIDIA Configuration",
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = Color.FromArgb(118, 185, 0),
            AutoSize = true,
            Location = new Point(16, y)
        };
        Controls.Add(_lTitle);

        _lStatus = new Label
        {
            Text = "",
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(520, 18),
            Location = new Point(18, y + 30)
        };
        Controls.Add(_lStatus);

        y += 62;
        y = Section("Identite", y, new[]
        {
            "Nom", "Revision", "VBIOS", "Pilote", "Bus PCI", "Memoire video"
        });
        y = Section("Limites d'origine", y, new[]
        {
            "Frequence GPU max", "Frequence memoire max", "Puissance min", "Puissance max",
            "Puissance par defaut", "Coupure thermique", "Ralentissement thermique"
        });
        y = Section("Configuration appliquee", y, new[]
        {
            "Undervolt", "Power limit", "Profil ventilation", "Courbe 831 mV"
        });
        y = Section("Temps reel", y, new[]
        {
            "Frequence GPU", "Frequence memoire", "Tension", "Puissance",
            "Temperature GPU", "Hotspot", "Temperature memoire",
            "Ventilateur 1", "Ventilateur 2", "Charge GPU"
        });
    }

    private int Section(string title, int y, string[] keys)
    {
        var box = new GroupBox
        {
            Text = title,
            ForeColor = Color.FromArgb(150, 200, 60),
            Location = new Point(14, y),
            Size = new Size(532, 26 + keys.Length * 18),
            Font = new Font("Segoe UI Semibold", 9f)
        };

        int ky = 20;
        foreach (string k in keys)
        {
            var lk = new Label { Text = k, ForeColor = Color.Gray, AutoSize = false, Size = new Size(210, 17), Location = new Point(12, ky) };
            var lv = new Label { Text = "-", ForeColor = Color.WhiteSmoke, AutoSize = false, Size = new Size(290, 17), Location = new Point(226, ky), TextAlign = ContentAlignment.MiddleRight };
            box.Controls.Add(lk);
            box.Controls.Add(lv);
            _v[k] = lv;
            ky += 18;
        }
        Controls.Add(box);
        return y + box.Height + 8;
    }

    private void Set(string key, string value)
    {
        Label l;
        if (_v.TryGetValue(key, out l)) l.Text = value;
    }

    private static string Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                return o;
            }
        }
        catch { return ""; }
    }

    private void Refresh(bool full)
    {
        try
        {
            string q = Run(Smi, "--query-gpu=name,driver_version,vbios_version,pci.bus_id,pci.device_id,pci.sub_device_id,memory.total,memory.used,clocks.current.graphics,clocks.current.memory,clocks.max.graphics,clocks.max.memory,power.draw,power.limit,temperature.gpu,utilization.gpu,fan.speed --format=csv,noheader,nounits");
            if (!string.IsNullOrWhiteSpace(q))
            {
                string[] p = q.Trim().Split(',');
                for (int i = 0; i < p.Length; i++) p[i] = p[i].Trim();
                if (p.Length >= 17)
                {
                    _s.Name = p[0]; _s.Driver = p[1]; _s.Vbios = p[2]; _s.BusId = p[3];
                    _s.DeviceId = p[4]; _s.SubId = p[5];
                    _s.VramTotal = ParseLong(p[6]); _s.VramUsed = ParseLong(p[7]);
                    _s.ClockNow = ParseInt(p[8]); _s.MemClockNow = ParseInt(p[9]);
                    _s.ClockMax = ParseInt(p[10]); _s.MemClockMax = ParseInt(p[11]);
                    _s.PowerNow = ParseDouble(p[12]);
                    _s.PowerLimit = ParseDouble(p[13]);
                    _s.Temp = ParseInt(p[14]); _s.GpuUsage = ParseInt(p[15]);
                }
            }

            int g, h, m;
            NvSensors.ReadTemps(out g, out h, out m);
            if (g > 0) _s.Temp = g;
            _s.Hotspot = h; _s.MemTemp = m;

            int r1, c1, r2, c2;
            NvSensors.ReadFans(out r1, out c1, out r2, out c2);
            _s.Fan1Rpm = r1; _s.Fan1Pct = c1; _s.Fan2Rpm = r2; _s.Fan2Pct = c2;

            string v = Run(Path.Combine(Root, "snu.exe"), "voltage");
            var mt = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)\s*mV");
            if (mt.Success) _s.VoltageMv = int.Parse(mt.Groups[1].Value);

            if (full)
            {
                string power = Run(Smi, "-q -d POWER");
                var mm = System.Text.RegularExpressions.Regex.Matches(power, @"(Min|Max|Default) Power Limit\s*:\s*([\d.]+) W");
                foreach (Match x in mm)
                {
                    double val = double.Parse(x.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (x.Groups[1].Value == "Min") _s.PowerMin = (int)val;
                    else if (x.Groups[1].Value == "Max") _s.PowerMax = (int)val;
                    else _s.PowerDefault = (int)val;
                }

                string temps = Run(Smi, "-q -d TEMPERATURE");
                var ts = System.Text.RegularExpressions.Regex.Matches(temps, @"(Shutdown|Slowdown) Temp\s*:\s*(\d+) C");
                foreach (Match x in ts)
                {
                    if (x.Groups[1].Value == "Shutdown") _s.TempShutdown = int.Parse(x.Groups[2].Value);
                    else _s.TempSlowdown = int.Parse(x.Groups[2].Value);
                }

                ReadAppliedConfig();
            }

            Render();
        }
        catch (Exception e)
        {
            _lStatus.Text = "Erreur : " + e.Message;
        }
    }

    private void ReadAppliedConfig()
    {
        try
        {
            string log = Path.Combine(Root, "undervolt.log");
            if (File.Exists(log))
            {
                string[] lines = File.ReadAllLines(log);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].Contains("courbe appliquee"))
                    {
                        int idx = lines[i].IndexOf("courbe appliquee");
                        _s.Undervolt = lines[i].Substring(idx + 18).Trim(' ', ':');
                        break;
                    }
                }
            }

            string curve = Path.Combine(Root, "fan-curve.txt");
            if (File.Exists(curve))
            {
                string[] lines = File.ReadAllLines(curve);
                string joined = string.Join(";", lines);
                if (joined == string.Join(";", File.ReadAllLines(Path.Combine(Root, "profil-silence.txt")))) _s.Profile = "silence";
                else if (joined == string.Join(";", File.ReadAllLines(Path.Combine(Root, "profil-froid.txt")))) _s.Profile = "froid";
                else _s.Profile = "equilibre";
            }

            string dump = Run(Path.Combine(Root, "snu.exe"), "curve");
            var mh = System.Text.RegularExpressions.Regex.Match(dump, @"831 mV\s*->\s*(\d+)\s*MHz");
            if (mh.Success) _s.Curve831 = mh.Groups[1].Value + " MHz";
        }
        catch { }
    }

    private static int ParseInt(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? (int)d : 0; }
    private static long ParseLong(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? (long)d : 0; }
    private static double ParseDouble(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }

    private void Render()
    {
        _lStatus.Text = _s.Name + "  |  " + _s.Driver;

        Set("Nom", _s.Name);
        Set("Revision", _s.DeviceId + " / " + _s.SubId);
        Set("VBIOS", _s.Vbios);
        Set("Pilote", _s.Driver);
        Set("Bus PCI", _s.BusId);
        Set("Memoire video", (_s.VramTotal / 1024) + " Mo  (" + _s.VramUsed + " Mo utilises)");

        Set("Frequence GPU max", _s.ClockMax + " MHz");
        Set("Frequence memoire max", _s.MemClockMax + " MHz");
        Set("Puissance min", _s.PowerMin + " W");
        Set("Puissance max", _s.PowerMax + " W");
        Set("Puissance par defaut", _s.PowerDefault + " W");
        Set("Coupure thermique", _s.TempShutdown + " C");
        Set("Ralentissement thermique", _s.TempSlowdown + " C");

        Set("Undervolt", _s.Undervolt);
        Set("Power limit", _s.PowerLimit.ToString("0") + " W");
        Set("Profil ventilation", _s.Profile);
        Set("Courbe 831 mV", _s.Curve831);

        Set("Frequence GPU", _s.ClockNow + " MHz");
        Set("Frequence memoire", _s.MemClockNow + " MHz");
        Set("Tension", _s.VoltageMv > 0 ? _s.VoltageMv + " mV" : "n/a");
        Set("Puissance", _s.PowerNow.ToString("0.0") + " W");
        Set("Temperature GPU", (_s.Temp > 0 ? _s.Temp + " C" : "n/a"));
        Set("Hotspot", (_s.Hotspot > 0 ? _s.Hotspot + " C" : "n/a"));
        Set("Temperature memoire", (_s.MemTemp > 0 ? _s.MemTemp + " C" : "n/a"));
        Set("Ventilateur 1", _s.Fan1Rpm >= 0 ? (_s.Fan1Rpm + " tr/min  (" + _s.Fan1Pct + " %)") : "n/a");
        Set("Ventilateur 2", _s.Fan2Rpm >= 0 ? (_s.Fan2Rpm + " tr/min  (" + _s.Fan2Pct + " %)") : "n/a");
        Set("Charge GPU", _s.GpuUsage + " %");

        try
        {
            _tray.Text = ("GPU " + _s.Temp + "C | " + _s.Fan1Pct + "% | " + _s.PowerNow.ToString("0") + " W | " + _s.ClockNow + " MHz");
            if (_tray.Text.Length > 63) _tray.Text = _tray.Text.Substring(0, 63);
        }
        catch { }
    }

    private void RunScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(Root, script) + "\"")
            { UseShellExecute = true, Verb = "runas" };
            Process.Start(psi);
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Erreur"); }
    }

    private void SetProfile(string name)
    {
        try
        {
            File.Copy(Path.Combine(Root, "profil-" + name + ".txt"), Path.Combine(Root, "fan-curve.txt"), true);
            _s.Profile = name;
            Render();
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Erreur"); }
    }
}

internal static class Program
{
    private static void Log(string msg)
    {
        try { File.AppendAllText(@"C:\Nvidia\nvidiaconf.log", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine); } catch { }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (o, e) => Log("UNHANDLED " + e.ExceptionObject);
        Application.ThreadException += (o, e) => Log("THREAD " + e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Log("demarrage args=" + string.Join(" ", args));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool hidden = false;
        foreach (string a in args)
            if (a == "--tray" || a == "/tray") hidden = true;
        try { Application.Run(new MainForm(hidden)); Log("arret normal"); }
        catch (Exception e) { Log("FATAL " + e); throw; }
    }
}
