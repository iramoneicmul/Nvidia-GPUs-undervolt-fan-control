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
using System.Threading;
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
    private delegate uint D_GetCooler(IntPtr gpu, uint index, ref LegacyCoolerSettings settings);
    private delegate uint D_Tach(IntPtr gpu, out uint rpm);

    private static D_Temps _temps;
    private static D_FanInfo _fanInfo;
    private static D_FanStatus _fanStatus;
    private static D_GetCooler _getCooler;
    private static D_Tach _tach;
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
            try
            {
                _getCooler = Bind<D_GetCooler>(0xDA141340);
                _tach = Bind<D_Tach>(0x5F608315);
            }
            catch { _getCooler = null; _tach = null; }
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

    private delegate uint D_FullName(IntPtr gpu, StringBuilder name);

    public static string GpuName()
    {
        if (!Init()) return "inconnu";
        try
        {
            var fn = Bind<D_FullName>(0xCEEE8E9F);
            var sb = new StringBuilder(64);
            if (fn(_gpu, sb) != 0) return "inconnu";
            return sb.ToString();
        }
        catch { return "inconnu"; }
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

        // Repli API classique (Maxwell / Pascal / avant RTX)
        if (rpm1 < 0 && _getCooler != null)
        {
            try
            {
                var l = new LegacyCoolerSettings();
                l.version = (uint)Marshal.SizeOf(typeof(LegacyCoolerSettings)) | (1u << 16);
                l.level = new LegacyCoolerLevel[20];
                if (_getCooler(_gpu, 0, ref l) == 0 && l.count > 0)
                {
                    uint rpm = 0;
                    try { if (_tach != null) _tach(_gpu, out rpm); } catch { }
                    rpm1 = (int)rpm;
                    pct1 = l.level[0].currentLevel;
                }
            }
            catch { }
        }
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
    public string PcieGen = "-", PcieWidth = "-";
    public string VoltMin = "-", VoltMax = "-", VoltPlateau = "-";
    public string Torch = "non detecte";
    public string AppVersion = "-";
}

internal sealed partial class MainForm : Form
{
    private static readonly string Root = DetectRoot();

    private static string DetectRoot()
    {
        string a = @"C:\Nvidia";
        if (File.Exists(Path.Combine(a, "snu.exe"))) return a;
        return @"C:\ProgramData\NvidiaUndervolt";
    }
    private const string Smi = "nvidia-smi";

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Snapshot _s = new Snapshot();

    private Label _lTitle, _lStatus;
    private readonly Dictionary<string, Label> _v = new Dictionary<string, Label>();
    private ProgressBar _bar;
    private Label _step;
    private Button _btnApply, _btnRestore, _btnPersist;
    private ComboBox _profileBox;
    private WebBrowser _reportView;
    private TextBox _targetMhzBox;
    private Label _lProfile;
    private volatile bool _busy;
    public static volatile bool ShowRequested;

    private readonly bool _startHidden;

    public MainForm(bool startHidden, bool autoTune = false)
    {
        _startHidden = startHidden;
        _autoTuneRequested = autoTune;
        Text = "NVIDIA Configuration - RTX 3080 Ti FE";
        ClientSize = new Size(620, 680);
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
        menu.Items.Add("Epingler l'icone dans la barre", null, (o, e) => PromoteTrayIcon());
        menu.Items.Add("Rapport comparatif", null, (o, e) => OpenComparativeReport());
        menu.Items.Add("Ouvrir le dossier", null, (o, e) => Process.Start("explorer.exe", Root));
        menu.Items.Add("Quitter", null, (o, e) => { _tray.Visible = false; Application.Exit(); });
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (o, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Show();
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
                Activate();
                BringToFront();
            }
        };
        _tray.DoubleClick += (o, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (o, e) => Refresh(false);
        _timer.Start();

        try { _s.AppVersion = Application.ProductVersion; } catch { }
        Refresh(true);
        if (_autoTuneRequested) AutoTune();
        if (_startHidden)
        {
            ShowInTaskbar = false;
            Visible = false;
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
        // ---------------- zone basse commune ----------------
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 156,
            BackColor = Color.FromArgb(28, 28, 28)
        };
        Controls.Add(bottom);

        _lProfile = new Label
        {
            Text = "Profil de ventilation",
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(160, 18),
            Location = new Point(16, 10)
        };
        bottom.Controls.Add(_lProfile);

        _profileBox = new ComboBox
        {
            Location = new Point(182, 6),
            Size = new Size(160, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            FlatStyle = FlatStyle.Flat
        };
        _profileBox.Items.AddRange(new object[] { "silence", "equilibre", "froid" });
        _profileBox.SelectedIndex = 1;
        _profileBox.SelectedIndexChanged += (o, e) => OnProfileChanged();
        bottom.Controls.Add(_profileBox);

        _lStatus = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(118, 185, 0),
            AutoSize = false,
            Size = new Size(360, 18),
            Location = new Point(180, 10),
            TextAlign = ContentAlignment.MiddleRight
        };
        bottom.Controls.Add(_lStatus);

        _step = new Label
        {
            Text = "Pret.",
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(524, 18),
            Location = new Point(16, 36)
        };
        bottom.Controls.Add(_step);

        _bar = new ProgressBar
        {
            Location = new Point(16, 56),
            Size = new Size(524, 16),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        bottom.Controls.Add(_bar);

        _btnApply = MakeButton("Appliquer la configuration", 16, 80, 168);
        _btnApply.Click += (o, e) => ApplyConfig();
        bottom.Controls.Add(_btnApply);

        _btnRestore = MakeButton("Restaurer l'origine", 196, 80, 156);
        _btnRestore.Click += (o, e) => RestoreOriginal();
        bottom.Controls.Add(_btnRestore);

        _btnPersist = MakeButton("Reappliquer au demarrage", 364, 80, 176);
        _btnPersist.Click += (o, e) => InstallPersistence();
        bottom.Controls.Add(_btnPersist);

        var btnTune = MakeButton("Trouver la configuration optimale", 16, 114, 340);
        btnTune.Click += (o, e) => AutoTune();
        bottom.Controls.Add(btnTune);

        var btnFreq = MakeButton("Mesurer les frequences", 364, 114, 176);
        btnFreq.Click += (o, e) => MeasureFrequencies();
        bottom.Controls.Add(btnFreq);

        var btnFanTest = MakeButton("Tester les profils de ventilation", 16, 80, 240);
        btnFanTest.Click += (o, e) => TestFanProfiles();
        bottom.Controls.Add(btnFanTest);

        btnTune.Location = new Point(264, 80);
        btnFreq.Location = new Point(16, 112);
        _btnApply.Location = new Point(224, 112);
        _btnRestore.Location = new Point(388, 112);
        _btnPersist.Location = new Point(16, 144);
        _btnPersist.Width = 280;

        var btnTorch = MakeButton("Installer PyTorch", 304, 144, 236);
        btnTorch.Click += (o, e) => InstallTorch();
        bottom.Controls.Add(btnTorch);

        bottom.Height = 182;

        // ---------------- onglets ----------------
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f)
        };

        var tIdent = new TabPage("Identite") { BackColor = Color.FromArgb(24, 24, 24) };
        var tFreq = new TabPage("Frequences") { BackColor = Color.FromArgb(24, 24, 24) };
        var tConf = new TabPage("Configuration") { BackColor = Color.FromArgb(24, 24, 24) };
        var tLive = new TabPage("Temps reel") { BackColor = Color.FromArgb(24, 24, 24) };
        var tSensors = new TabPage("Capteurs") { BackColor = Color.FromArgb(24, 24, 24) };
        var tReport = new TabPage("Rapport") { BackColor = Color.FromArgb(24, 24, 24) };

        tabs.TabPages.AddRange(new TabPage[] { tIdent, tFreq, tConf, tLive, tSensors, tReport });
        Controls.Add(tabs);
        tabs.BringToFront();

        int y1 = 8;
        y1 = Section(tIdent, "Identite", y1, new[]
        {
            "Version de nvidiaconf", "Nom", "Revision", "VBIOS", "Pilote", "Bus PCI", "Memoire video", "PyTorch"
        });
        Section(tIdent, "Bus PCI Express", y1, new[]
        {
            "Bus PCIe (version)", "Bus PCIe (largeur)"
        });

        int y2 = 8;
        y2 = Section(tFreq, "Limites d'origine", y2, new[]
        {
            "Frequence GPU max", "Frequence memoire max", "Puissance min",
            "Puissance max", "Puissance par defaut", "Coupure thermique",
            "Ralentissement thermique"
        });
        y2 = Section(tFreq, "Tensions de la courbe", y2, new[]
        {
            "Tension min (courbe)", "Tension max (courbe)", "Tension du plateau"
        });
        Section(tFreq, "Frequences moyennes (MHz)", y2, new[]
        {
            "Legere - origine", "Legere - optimise",
            "Jeu - origine", "Jeu - optimise",
            "Soutenue - origine", "Soutenue - optimise",
            "Lourde - origine", "Lourde - optimise"
        });

        int y3 = 8;
        y3 = Section(tConf, "Configuration appliquee", y3, new[]
        {
            "Undervolt", "Power limit", "Profil ventilation", "Courbe 831 mV"
        });

        var lblTarget = new Label
        {
            Text = "Frequence cible (MHz) :",
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(150, 18),
            Location = new Point(18, y3 + 10)
        };
        tConf.Controls.Add(lblTarget);

        _targetMhzBox = new TextBox
        {
            Location = new Point(172, y3 + 6),
            Size = new Size(60, 22),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "0"
        };
        tConf.Controls.Add(_targetMhzBox);

        var lblHint = new Label
        {
            Text = "(0 = deduite de l'origine)",
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(160, 18),
            Location = new Point(238, y3 + 10)
        };
        tConf.Controls.Add(lblHint);

        var btnPackage = MakeButton("Determiner le package optimal", 18, y3 + 38, 260);
        btnPackage.Click += (o, e) => PackageButton();
        tConf.Controls.Add(btnPackage);

        var btnReport = MakeButton("Ouvrir le rapport comparatif", 18, y3 + 74, 260);
        btnReport.Click += (o, e) => OpenComparativeReport();
        tConf.Controls.Add(btnReport);

        BuildSensorsTab(tSensors);

        var reportBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(28, 28, 28)
        };
        var btnRafraichir = MakeButton("Actualiser le rapport", 8, 6, 190);
        btnRafraichir.Click += (o, e) => RefreshReportTab();
        reportBar.Controls.Add(btnRafraichir);

        var lblReport = new Label
        {
            Text = "Rapport comparatif avant / apres",
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(280, 18),
            Location = new Point(212, 12)
        };
        reportBar.Controls.Add(lblReport);

        _reportView = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true,
            IsWebBrowserContextMenuEnabled = false
        };

        // ordre important : Fill d'abord, puis le bandeau Top
        tReport.Controls.Add(_reportView);
        tReport.Controls.Add(reportBar);

        tabs.SelectedIndexChanged += (o, e) =>
        {
            if (tabs.SelectedTab == tReport) RefreshReportTab();
        };

        int y4 = 8;
        Section(tLive, "Temps reel", y4, new[]
        {
            "Frequence GPU", "Frequence memoire", "Tension", "Puissance",
            "Temperature GPU", "Hotspot", "Temperature memoire",
            "Ventilateur 1", "Ventilateur 2", "Charge GPU"
        });
    }

    private Button MakeButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(118, 185, 0);
        return b;
    }

    // ------------------------------------------------------------------
    //  Execution d'une sequence d'etapes avec barre de progression
    // ------------------------------------------------------------------
    private void RunSteps(string titre, string[] etapes, Action<int> action)
    {
        if (_busy) return;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;

        var th = new Thread(() =>
        {
            bool ok = true;
            for (int i = 0; i < etapes.Length; i++)
            {
                int idx = i;
                Ui(() =>
                {
                    _step.Text = string.Format("{0} ({1}/{2}) : {3}", titre, idx + 1, etapes.Length, etapes[idx]);
                    _bar.Value = (int)(100.0 * idx / etapes.Length);
                });
                try { action(idx); }
                catch (Exception e)
                {
                    ok = false;
                    Ui(() => { _step.Text = "Echec : " + e.Message; });
                    break;
                }
            }

            Ui(() =>
            {
                _bar.Value = ok ? 100 : _bar.Value;
                if (ok) _step.Text = titre + " : termine.";
                _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = true;
                _busy = false;
            });
            Refresh(true);
        });
        th.IsBackground = true;
        th.Start();
    }

    private void Ui(Action a)
    {
        try { if (InvokeRequired) BeginInvoke(a); else a(); }
        catch { }
    }

    private static void Ps(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = Process.Start(psi)) { p.WaitForExit(60000); }
    }

    private void ApplyConfig()
    {
        RunSteps("Application de la configuration", new[]
        {
            "Application de la courbe V/F",
            "Reglage du power limit a 320 W",
            "Verification de la courbe",
            "Verification du service de ventilation"
        }, i =>
        {
            switch (i)
            {
                case 0:
                    Ps("& '" + Path.Combine(Root, "snu.exe") + "' --in-tuning-file '" +
                       Path.Combine(Root, "plateau-1845-831.json") + "' --no-persist | Out-Null");
                    break;
                case 1:
                    Ps("& nvidia-smi -pl 320 | Out-Null");
                    break;
                case 2:
                    Ps("& '" + Path.Combine(Root, "snu.exe") + "' status | Out-Null");
                    break;
                case 3:
                    Ps("Get-Service NvidiaFanCurve -ErrorAction SilentlyContinue | Out-Null");
                    break;
            }
        });
    }

    private void RestoreOriginal()
    {
        if (MessageBox.Show(
            "Restaurer la configuration d'origine ?\n\n" +
            "- courbe V/F stock\n- power limit 350 W\n- ventilateurs rendus au VBIOS\n" +
            "- service et tache planifiee supprimes",
            "Restauration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        RunSteps("Restauration de l'origine", new[]
        {
            "Arret du service de ventilation",
            "Suppression du service",
            "Ventilateurs rendus au VBIOS",
            "Courbe V/F remise au stock",
            "Power limit remis a 350 W",
            "Suppression de la tache planifiee",
            "Verification"
        }, i =>
        {
            switch (i)
            {
                case 0: Ps("& sc.exe stop NvidiaFanCurve | Out-Null"); break;
                case 1: Ps("& sc.exe delete NvidiaFanCurve | Out-Null"); break;
                case 2: Ps("& '" + Path.Combine(Root, "nvfan.exe") + "' auto | Out-Null"); break;
                case 3: Ps("& '" + Path.Combine(Root, "snu.exe") + "' clear | Out-Null"); break;
                case 4: Ps("& nvidia-smi -pl 350 | Out-Null"); break;
                case 5: Ps("Unregister-ScheduledTask -TaskName NvidiaUndervolt -Confirm:$false -ErrorAction SilentlyContinue"); break;
                case 6:
                    Ps("& '" + Path.Combine(Root, "snu.exe") + "' status | Out-Null");
                    break;
            }
        });
    }

    private void InstallPersistence()
    {
        RunSteps("Reapplication au demarrage", new[]
        {
            "Creation de la tache planifiee",
            "Installation du service de ventilation",
            "Application immediate de la configuration"
        }, i =>
        {
            switch (i)
            {
                case 0: Ps("& '" + Path.Combine(Root, "install-task.ps1") + "'"); break;
                case 1: Ps("& '" + Path.Combine(Root, "install-fan-service.ps1") + "'"); break;
                case 2:
                    Ps("& '" + Path.Combine(Root, "apply-undervolt.ps1") + "'");
                    break;
            }
        });
    }

    private int Section(Control parent, string title, int y, string[] keys)
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
        parent.Controls.Add(box);
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
                p.WaitForExit(10000);
                return o;
            }
        }
        catch { return ""; }
    }

    private void Refresh(bool full)
    {
        if (ShowRequested)
        {
            ShowRequested = false;
            LogMsg("traitement de la demande d'affichage");
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
        }

        // pendant un test, on evite de lancer nvidia-smi en concurrence
        if (_busy && !full) return;
        try
        {
            string q = Run(Smi, "--query-gpu=name,driver_version,vbios_version,pci.bus_id,pci.device_id,pci.sub_device_id,memory.total,memory.used,clocks.current.graphics,clocks.current.memory,clocks.max.graphics,clocks.max.memory,power.draw,power.limit,temperature.gpu,utilization.gpu,fan.speed,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits");
            if (!string.IsNullOrWhiteSpace(q))
            {
                string[] p = q.Trim().Split(',');
                for (int i = 0; i < p.Length; i++) p[i] = p[i].Trim();
                if (p.Length >= 21)
                {
                    _s.PcieGen = p[17] + " / max " + p[18];
                    _s.PcieWidth = p[19] + " / max " + p[20];
                }
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

            ReadFreqProfile();
            ReadVoltageInfo();
            DetectTorch();

            string dump = Run(Path.Combine(Root, "snu.exe"), "curve");
            var mh = System.Text.RegularExpressions.Regex.Match(dump, @"831 mV\s*->\s*(\d+)\s*MHz");
            if (mh.Success) _s.Curve831 = mh.Groups[1].Value + " MHz";
        }
        catch { }
    }

    private static int ParseInt(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? (int)d : 0; }
    private static long ParseLong(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? (long)d : 0; }
    private static double ParseDouble(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }

    private void DetectTorch()
    {
        try
        {
            string py = ResolvePython();
            if (py == null) { _s.Torch = "python absent"; return; }
            var psi = new ProcessStartInfo(py, "-c \"import torch;print(torch.__version__)\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(20000);
                _s.Torch = (p.ExitCode == 0 && o.Length > 0) ? o : "absent";
            }
        }
        catch { _s.Torch = "absent"; }
    }

    private void ReadVoltageInfo()
    {
        try
        {
            string stock = Path.Combine(Root, "courbe-stock.json");
            if (File.Exists(stock))
            {
                int min = int.MaxValue, max = int.MinValue;
                foreach (Match m in Regex.Matches(File.ReadAllText(stock), "\"mv\"\\s*:\\s*(\\d+)"))
                {
                    int v = int.Parse(m.Groups[1].Value);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                if (min != int.MaxValue) { _s.VoltMin = min + " mV"; _s.VoltMax = max + " mV"; }
            }

            string active = Path.Combine(Root, "plateau-active.json");
            if (File.Exists(active))
            {
                var m = Regex.Match(File.ReadAllText(active), "\"mv\"\\s*:\\s*(\\d+)");
                if (m.Success) _s.VoltPlateau = m.Groups[1].Value + " mV";
            }
        }
        catch { }
    }

    private void Render()
    {
        _lStatus.Text = _s.Name + "  |  " + _s.Driver;

        Set("Version de nvidiaconf", _s.AppVersion);
        Set("Nom", _s.Name);
        bool revOk = !string.IsNullOrEmpty(_s.DeviceId) && _s.DeviceId != "00000000";
        Set("Revision", revOk ? (_s.DeviceId + " / " + _s.SubId) : "n/a");
        Set("VBIOS", _s.Vbios);
        Set("Pilote", _s.Driver);
        Set("Bus PCI", _s.BusId);
        Set("Memoire video", (_s.VramTotal / 1024) + " Mo  (" + _s.VramUsed + " Mo utilises)");
        Set("PyTorch", _s.Torch);
        Set("Tension min (courbe)", _s.VoltMin);
        Set("Tension max (courbe)", _s.VoltMax);
        Set("Tension du plateau", _s.VoltPlateau);
        Set("Bus PCIe (version)", _s.PcieGen);
        Set("Bus PCIe (largeur)", _s.PcieWidth);

        Set("Frequence GPU max", _s.ClockMax + " MHz");
        Set("Frequence memoire max", _s.MemClockMax + " MHz");
        Set("Puissance min", _s.PowerMin + " W");
        Set("Puissance max", _s.PowerMax + " W");
        Set("Puissance par defaut", _s.PowerDefault + " W");
        Set("Coupure thermique", _s.TempShutdown + " C");
        Set("Ralentissement thermique", _s.TempSlowdown + " C");

        Set("Undervolt", _s.Undervolt);
        string plTxt = _s.PowerLimit.ToString("0") + " W";
        if (Math.Abs(_s.PowerLimit - 320) > 1) plTxt += "  (configuration : 320 W)";
        Set("Power limit", plTxt);
        Set("Profil ventilation", _s.Profile);
        if (_profileBox != null && !_profileBox.Focused)
        {
            int idx = _profileBox.Items.IndexOf(_s.Profile);
            if (idx >= 0 && idx != _profileBox.SelectedIndex)
            {
                _profileBox.SelectedIndexChanged -= (s2, e2) => { };
                _profileBox.SelectedIndex = idx;
            }
        }
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

        UpdateSensors();

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

    private void PromoteTrayIcon()
    {
        try
        {
            using (var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Control Panel\NotifyIconSettings", true))
            {
                if (baseKey == null)
                {
                    MessageBox.Show("Cle de notification introuvable.", "Icone",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                foreach (string sub in baseKey.GetSubKeyNames())
                {
                    using (var k = baseKey.OpenSubKey(sub, true))
                    {
                        if (k == null) continue;
                        string path = k.GetValue("ExecutablePath") as string;
                        if (path != null && path.ToLowerInvariant().Contains("nvidiaconf.exe"))
                            k.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
            }
            MessageBox.Show(
                "Icone epinglee dans la zone de notification.\n\n" +
                "Redemarrez la session (ou l'explorateur) pour que le changement soit applique.",
                "Icone", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show("Impossible d'epingler l'icone : " + e.Message, "Icone",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InstallTorch()
    {
        if (_busy) return;
        if (MessageBox.Show(
            "Installer ou mettre a jour PyTorch ?\n\n" +
            "Le telechargement peut representer plusieurs centaines de Mo.\n" +
            "Le moteur de test l'utilise en priorite (plus severe qu'OpenCL).",
            "PyTorch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        string py = ResolvePython();
        if (py == null)
        {
            MessageBox.Show("Python est introuvable sur cette machine.", "PyTorch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Style = ProgressBarStyle.Marquee;
        _step.Text = "Installation de PyTorch via pip (peut durer plusieurs minutes)...";

        var th = new Thread(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(py,
                    "-m pip install --upgrade torch --index-url https://download.pytorch.org/whl/cu124")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                int code = -1;
                using (var p = Process.Start(psi)) { p.WaitForExit(3600000); code = p.ExitCode; }
                LogMsg("pip torch termine, code " + code);
            }
            catch (Exception e) { LogMsg("InstallTorch : " + e.Message); }

            Ui(() =>
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = 0;
                _step.Text = "Installation de PyTorch terminee.";
                _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = true;
                _busy = false;
            });
            Refresh(true);
        });
        th.IsBackground = true;
        th.Start();
    }

    private void OnProfileChanged()
    {
        if (_busy || _profileBox == null) return;
        string name = _profileBox.SelectedItem as string;
        if (string.IsNullOrEmpty(name)) return;
        SetProfile(name);
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

    private static Mutex _mutex;
    private static EventWaitHandle _showEvent;

    private static void EnsureTasks(string root)
    {
        try
        {
            string inst = Path.Combine(root, "install-conf-task.ps1");
            if (!File.Exists(inst)) return;

            bool missing = false;
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                "/query /tn NvidiaConf")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);
                missing = p.ExitCode != 0;
            }

            if (missing)
            {
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + inst + "\"")
                { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(90000);
                Log("taches NvidiaConf/NvidiaUndervolt creees");
            }
        }
        catch (Exception e) { Log("EnsureTasks : " + e.Message); }
    }

    private static void EnsureConfigurationApplied(string root)
    {
        try
        {
            string snu = Path.Combine(root, "snu.exe");
            string curve = Path.Combine(root, "plateau-active.json");
            if (!File.Exists(snu) || !File.Exists(curve)) return;

            // power limit cible
            var psiPl = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                "--query-gpu=power.limit --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psiPl))
            {
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(8000);
                double pl;
                if (double.TryParse(o, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out pl) && Math.Abs(pl - 320) > 1)
                {
                    var psiSet = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"), "-pl 320")
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var ps = Process.Start(psiSet)) ps.WaitForExit(15000);
                    Log("power limit applique : 320 W");
                }
            }

            // courbe V/F
            var psiSt = new ProcessStartInfo(snu, "status")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psiSt))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                if (o.Contains("Core curve offset: stock"))
                {
                    var psiAp = new ProcessStartInfo(snu, "--in-tuning-file \"" + curve + "\" --no-persist")
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var pa = Process.Start(psiAp)) pa.WaitForExit(20000);
                    Log("courbe appliquee au demarrage");
                }
            }
        }
        catch (Exception e) { Log("EnsureConfigurationApplied : " + e.Message); }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (o, e) => Log("UNHANDLED " + e.ExceptionObject);
        Application.ThreadException += (o, e) => Log("THREAD " + e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Log("demarrage args=" + string.Join(" ", args));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool hidden = false, tune = false, check = false, report = false, freqTest = false, fanTest = false, packageTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--tray" || a == "/tray") hidden = true;
            if (a == "--autotune") tune = true;
            if (a == "--check") check = true;
            if (a == "--report") report = true;
            if (a == "--freqtest") freqTest = true;
            if (a == "--fantest") fanTest = true;
            if (a == "--package") packageTest = true;
            if (a == "--mhz" && i + 1 < args.Length)
            {
                int mv;
                if (int.TryParse(args[i + 1], out mv)) MainForm.RequestedMhz = mv;
            }
        }

        string root = File.Exists(@"C:\Nvidia\snu.exe") ? @"C:\Nvidia" : @"C:\ProgramData\NvidiaUndervolt";
        var compat = Compatibility.Check(root);
        Log("compatibilite : " + (compat.Ok ? "OK" : "NON") + " / " + compat.GpuName);

        if (freqTest || fanTest || packageTest)
        {
            try
            {
                var f = new MainForm(true, false);
                if (freqTest) f.CliFrequencyTest();
                if (fanTest) f.CliFanTest();
                if (packageTest) f.CliPackage();
            }
            catch (Exception e) { Log("CLI test erreur : " + e.Message); }
            return 0;
        }

        if (report)
        {
            try
            {
                var f = new MainForm(true, false);
                string path = f.GenerateReport();
                Log("rapport genere : " + path);
            }
            catch (Exception e) { Log("rapport erreur : " + e.Message); }
            return 0;
        }

        if (check)
        {
            MessageBox.Show(compat.Summary, "Compatibilite", MessageBoxButtons.OK,
                compat.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return compat.Ok ? 0 : 2;
        }

        if (MainForm.ConsumeCrashMarker(root))
        {
            Log("marqueur de test trouve : restauration d'origine");
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(root, "snu.exe"), "clear")
                { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(15000);
                Process.Start(new ProcessStartInfo("nvidia-smi", "-pl 350")
                { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(15000);
            }
            catch { }
            if (!tune && !hidden)
            {
                MessageBox.Show(
                    "Un test d'optimisation a ete interrompu (probable instabilite du pilote).\n\n" +
                    "La configuration d'origine a ete restauree automatiquement.\n" +
                    "L'optimisation reprendra a un palier plus sur.",
                    "Test interrompu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        if (!compat.Ok)
        {
            Log("refus : " + compat.Reason);
            MessageBox.Show(
                "Cette carte n'est pas compatible avec NVIDIA Configuration.\n\n" +
                compat.Summary + "\n\n" +
                "Le programme ne peut pas s'executer sur ce materiel.",
                "Materiel incompatible", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        bool createdNew;
        _mutex = new Mutex(true, "Local\\NvidiaConfSingleton", out createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting("NvidiaConf.Show").Set(); } catch { }
            return 0;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "NvidiaConf.Show");

        var form = new MainForm(hidden, tune);
        var wake = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                Log("signal d'affichage recu");
                MainForm.ShowRequested = true;
            }
        });
        wake.IsBackground = true;
        wake.Start();

        EnsureConfigurationApplied(root);
        EnsureTasks(root);

        // Si le service de ventilation existe mais est arrete (cas apres une
        // installation ou une desinstallation), on le redemarre.
        try
        {
            var sc = new System.ServiceProcess.ServiceController("NvidiaFanCurve");
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                sc.Start();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                Log("service NvidiaFanCurve demarre");
            }
        }
        catch (Exception e)
        {
            Log("service absent : " + e.Message);
            // le service n'existe pas : on le cree s'il est installe sur disque
            try
            {
                string inst = Path.Combine(root, "install-fan-service.ps1");
                if (File.Exists(inst))
                {
                    Process.Start(new ProcessStartInfo("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -File \"" + inst + "\"")
                    { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(60000);
                    Log("service NvidiaFanCurve cree");
                }
            }
            catch (Exception e2) { Log("creation service : " + e2.Message); }
        }

        try
        {
            if (hidden)
            {
                // fenetre invisible des le premier affichage, puis masquee.
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Shown += (o2, e2) =>
                {
                    form.Hide();
                    form.Opacity = 1;
                };
            }
            Application.Run(form);
            Log("arret normal");
            return 0;
        }
        catch (Exception e) { Log("FATAL " + e); return 1; }
    }
}
