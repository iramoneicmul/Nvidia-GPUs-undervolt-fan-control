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
using System.Net;
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

    // NVAPI n'est pas reentrant : un seul appel a la fois, sinon interblocage
    // dans nvapi64.dll lorsque l'interface et la mesure interrogent la carte
    // simultanement.
    private static readonly object _sync = new object();

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
        lock (_sync)
        {
            if (!Init()) return;
            gpu = Sensor(0x01, 0);
            hotspot = Sensor(0x02, 1);
            mem = Sensor(0x200, 9);
        }
    }

    public static void ReadFans(out int rpm1, out int pct1, out int rpm2, out int pct2)
    {
        lock (_sync) { ReadFansCore(out rpm1, out pct1, out rpm2, out pct2); }
    }

    private static void ReadFansCore(out int rpm1, out int pct1, out int rpm2, out int pct2)
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
    private TextBox _targetMvBox;
    private TextBox _targetPlBox;
    private Label _boundsLabel;
    private Label _lblLanguage;
    private Label _targetError;
    private MiniGraph _graph;
    private bool _tempAlert;
    public static int RequestedMv;
    public static int RequestedPowerLimit;
    private Label _lProfile;
    private volatile bool _busy;
    public static volatile bool ShowRequested;
    public static volatile bool PackageRequested;
    public static volatile bool CompareRequested;

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

        menu.Items.Add(TrayItem("Afficher", (o, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); }));
        menu.Items.Add(TrayItem("Actualiser", (o, e) => Refresh(true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayItem("Appliquer la configuration", (o, e) => RunScript("apply-undervolt.ps1")));

        var prof = new ToolStripMenuItem(Lang.T("Profil de ventilation"));
        prof.Tag = "Profil de ventilation";
        var pSil = new ToolStripMenuItem(Lang.T("Silence")); pSil.Tag = "Silence";
        pSil.Click += (o, e) => SetProfile("silence");
        var pEqu = new ToolStripMenuItem(Lang.T("Equilibre")); pEqu.Tag = "Equilibre";
        pEqu.Click += (o, e) => SetProfile("equilibre");
        var pFro = new ToolStripMenuItem(Lang.T("Froid")); pFro.Tag = "Froid";
        pFro.Click += (o, e) => SetProfile("froid");
        prof.DropDownItems.Add(pSil);
        prof.DropDownItems.Add(pEqu);
        prof.DropDownItems.Add(pFro);
        menu.Items.Add(prof);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayItem("Epingler l'icone dans la barre", (o, e) => PromoteTrayIcon()));
        menu.Items.Add(TrayItem("Rapport comparatif", (o, e) => OpenComparativeReport()));
        menu.Items.Add(TrayItem("Ouvrir le dossier", (o, e) => Process.Start("explorer.exe", Root)));
        menu.Items.Add(TrayItem("Quitter", (o, e) => { _tray.Visible = false; Application.Exit(); }));

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

        var upd = new Thread(CheckForUpdates) { IsBackground = true };
        upd.Start();

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
            Text = Lang.T("Profil de ventilation"),
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
        _profileBox.Items.AddRange(new object[]
        {
            Lang.T("silence"), Lang.French ? "equilibre" : "equilibre", Lang.T("froid")
        });
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
            Text = Lang.T("Pret."),
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

        _btnPersist = MakeButton("Appliquer la configuration au demarrage", 364, 80, 176);
        _btnPersist.Click += (o, e) => InstallPersistence();
        bottom.Controls.Add(_btnPersist);

        var btnTune = MakeButton("Trouver la configuration optimale", 16, 114, 340);
        btnTune.Click += (o, e) => AutoTune();
        bottom.Controls.Add(btnTune);

        var btnFreq = MakeButton("Mesurer les frequences", 364, 114, 176);
        btnFreq.Click += (o, e) => MeasureFrequencies();
        bottom.Controls.Add(btnFreq);

        var btnFanTest = MakeButton("Optimisation de la ventilation", 16, 80, 240);
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

        var tIdent = new TabPage(Lang.T("Identite")) { Tag = "Identite", BackColor = Color.FromArgb(24, 24, 24) };
        var tFreq = new TabPage(Lang.T("Frequences")) { Tag = "Frequences", BackColor = Color.FromArgb(24, 24, 24) };
        var tConf = new TabPage(Lang.T("Configuration")) { Tag = "Configuration", BackColor = Color.FromArgb(24, 24, 24) };
        var tLive = new TabPage(Lang.T("Temps reel")) { Tag = "Temps reel", BackColor = Color.FromArgb(24, 24, 24) };
        var tSensors = new TabPage(Lang.T("Capteurs")) { Tag = "Capteurs", BackColor = Color.FromArgb(24, 24, 24) };
        var tReport = new TabPage(Lang.T("Rapport")) { Tag = "Rapport", BackColor = Color.FromArgb(24, 24, 24) };

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

        int yLang = y1 + 70;
        _lblLanguage = new Label
        {
            Text = Lang.T("Langue") + " : " + Lang.Names[Lang.IndexOf(Lang.Current)],
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(320, 18),
            Location = new Point(18, yLang)
        };
        tIdent.Controls.Add(_lblLanguage);

        var btnLang = MakeButton("Langue", 18, yLang + 24, 140);
        btnLang.Click += (o, e) => ChooseLanguage(btnLang);
        tIdent.Controls.Add(btnLang);

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
            "Undervolt", "Power limit", "Profil ventilation", "Courbe active"
        });

        _targetMhzBox = AddTargetRow(tConf, "Frequence cible (MHz) :", y3 + 10,
            "(0 = deduite de l'origine)", () => ApplyTargetField(0));
        _targetMvBox = AddTargetRow(tConf, "Tension cible (mV) :", y3 + 36,
            "(0 = recherche automatique)", () => ApplyTargetField(1));
        _targetPlBox = AddTargetRow(tConf, "Power limit max (W) :", y3 + 62,
            "(0 = consomme + 10 W)", () => ApplyTargetField(2));

        _boundsLabel = new Label
        {
            Text = "Limites de la carte : calcul en cours...",
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(430, 32),
            Location = new Point(18, y3 + 90)
        };
        tConf.Controls.Add(_boundsLabel);

        _targetError = new Label
        {
            ForeColor = Color.FromArgb(230, 170, 90),
            AutoSize = false,
            Size = new Size(520, 32),
            Location = new Point(18, y3 + 90 + 32)
        };
        tConf.Controls.Add(_targetError);

        var btnApplyTargets = MakeButton("Appliquer ces valeurs", 18, y3 + 128, 260);
        btnApplyTargets.Click += (o, e) => ApplyTargets();
        tConf.Controls.Add(btnApplyTargets);

        var btnPackage = MakeButton("Determiner le package optimal", 288, y3 + 128, 250);
        btnPackage.Click += (o, e) => PackageButton();
        tConf.Controls.Add(btnPackage);

        // validation en temps reel : message et couleur des le changement
        EventHandler live = (o, e) => ValidateTargetsLive();
        _targetMhzBox.TextChanged += live;
        _targetMvBox.TextChanged += live;
        _targetPlBox.TextChanged += live;

        var btnReport = MakeButton("Ouvrir le rapport", 18, y3 + 164, 260);
        // Ouvre l'onglet Rapport et y affiche le rapport comparatif.
        btnReport.Click += (o, e) => { tabs.SelectedTab = tReport; RefreshReportTab(); };
        tConf.Controls.Add(btnReport);

        var btnHistory = MakeButton("Historique des configurations", 288, y3 + 164, 250);
        btnHistory.Click += (o, e) => ShowHistory();
        tConf.Controls.Add(btnHistory);

        BuildSensorsTab(tSensors);

        var reportBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(28, 28, 28)
        };
        var btnRafraichir = MakeButton("Actualiser", 8, 6, 110);
        btnRafraichir.Click += (o, e) => RefreshReportTab();
        reportBar.Controls.Add(btnRafraichir);

        var btnCompare = MakeButton("Comparer origine / applique", 126, 6, 250);
        btnCompare.Click += (o, e) => CompareButton();
        reportBar.Controls.Add(btnCompare);

        var lblReport = new Label
        {
            Text = Lang.T("Rapport comparatif"),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(120, 18),
            Location = new Point(386, 12)
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
        BuildLiveGraph(tLive, 8 + 10 * 21);
    }

    // Ligne de saisie : libelle, zone de texte, aide. Retourne la zone de texte.
    private TextBox AddTargetRow(TabPage page, string label, int y, string hint, Action apply = null)
    {
        var lbl = new Label
        {
            Tag = label,
            Text = Lang.T(label),
            ForeColor = Color.Silver,
            AutoSize = false,
            Size = new Size(150, 18),
            Location = new Point(18, y)
        };
        page.Controls.Add(lbl);

        var box = new TextBox
        {
            Location = new Point(172, y - 4),
            Size = new Size(60, 22),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "0"
        };
        page.Controls.Add(box);

        var help = new Label
        {
            Tag = hint,
            Text = Lang.T(hint),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(190, 18),
            Location = new Point(238, y)
        };
        page.Controls.Add(help);

        if (apply != null)
        {
            var btn = new Button
            {
                Tag = "Appliquer",
                Text = Lang.T("Appliquer"),
                Location = new Point(434, y - 5),
                Size = new Size(110, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gainsboro
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(118, 185, 0);
            btn.Click += (o, e) => apply();
            page.Controls.Add(btn);
        }
        return box;
    }

    private Button MakeButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Tag = text,
            Text = Lang.T(text),
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
                    Ui(() => { _step.Text = Lang.T("Echec : ") + e.Message; });
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

    // Verification de version aupres des releases GitHub (aucune installation
    // automatique : simple notification).
    private void CheckForUpdates()
    {
        try
        {
            // .NET Framework ancien : TLS 1.2 doit etre demande explicitement.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            var req = (HttpWebRequest)WebRequest.Create(
                "https://api.github.com/repos/iramoneicmul/Nvidia-GPUs-undervolt-fan-control/releases/latest");
            req.UserAgent = "nvidiaconf";
            req.Timeout = 8000;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                string json = sr.ReadToEnd();
                var m = Regex.Match(json, @"""tag_name""\s*:\s*""v?([0-9.]+)""");
                if (!m.Success) return;

                string latest = m.Groups[1].Value;
                string current = Application.ProductVersion;
                LogMsg("version : actuelle " + current + ", derniere publiee " + latest);
                if (string.Compare(latest, current, true) > 0)
                {
                    LogMsg("mise a jour disponible : " + latest);
                    try
                    {
                        _tray.ShowBalloonTip(10000,
                            Lang.French ? "Mise a jour disponible" : "Update available",
                            Lang.French
                                ? "NVIDIA Configuration " + latest + " est disponible (version installee : " + current + ")."
                                : "NVIDIA Configuration " + latest + " is available (installed version: " + current + ").",
                            ToolTipIcon.Info);
                        menu_UpdateItem(latest);
                    }
                    catch { }
                }
            }
        }
        catch (Exception e) { LogMsg("verification de version : " + e.Message); }
    }

    private void menu_UpdateItem(string latest)
    {
        try
        {
            if (_tray.ContextMenuStrip == null) return;
            if (_tray.ContextMenuStrip.Items.ContainsKey("maj")) return;
            var item = new ToolStripMenuItem(Lang.T("Mise a jour ") + latest +
                (Lang.French ? " disponible" : " available"));
            item.Name = "maj";
            item.Click += (o, e) => Process.Start(
                "https://github.com/iramoneicmul/Nvidia-GPUs-undervolt-fan-control/releases/latest");
            _tray.ContextMenuStrip.Items.Insert(0, item);
        }
        catch { }
    }

    private ToolStripMenuItem TrayItem(string fr, EventHandler onClick)
    {
        var it = new ToolStripMenuItem(Lang.T(fr));
        it.Tag = fr;
        it.Click += onClick;
        return it;
    }

    // Retraduit toute l'interface en place, sans redemarrer l'application.
    private void RetranslateAll()
    {
        RetranslateControl(this);

        if (_tray != null && _tray.ContextMenuStrip != null)
            RetranslateItems(_tray.ContextMenuStrip.Items);

        if (_boundsLabel != null) UpdateTargetBounds();

        // profils de ventilation : libelles traduits, index conserve
        if (_profileBox != null)
        {
            int idx = _profileBox.SelectedIndex;
            _profileBox.Items.Clear();
            _profileBox.Items.AddRange(new object[]
            {
                Lang.T("silence"), "equilibre", Lang.T("froid")
            });
            if (idx >= 0) _profileBox.SelectedIndex = idx;
        }

        if (_lblLanguage != null)
            _lblLanguage.Text = Lang.T("Langue") + " : " +
                Lang.Names[Lang.IndexOf(Lang.Current)];
    }

    private void RetranslateControl(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.Tag is string) c.Text = Lang.T((string)c.Tag);
            if (c is DataGridView)
            {
                var g = (DataGridView)c;
                foreach (DataGridViewColumn col in g.Columns)
                    if (col.Tag is string) col.HeaderText = Lang.T((string)col.Tag);
            }
            RetranslateControl(c);
        }
    }

    private void RetranslateItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            if (it.Tag is string) it.Text = Lang.T((string)it.Tag);
            var mi = it as ToolStripMenuItem;
            if (mi != null) RetranslateItems(mi.DropDownItems);
        }
    }

    // Choix de la langue : menu, application immediate sans redemarrage.
    private void ChooseLanguage(Control anchor)
    {
        var menu = new ContextMenuStrip();
        for (int i = 0; i < Lang.Available.Length; i++)
        {
            string code = Lang.Available[i];
            var item = new ToolStripMenuItem(Lang.Names[i]);
            item.Checked = (code == Lang.Current);
            item.Click += (o, e) => ApplyLanguage(code);
            menu.Items.Add(item);
        }
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void ApplyLanguage(string code)
    {
        if (code == Lang.Current) return;
        if (_busy)
        {
            MessageBox.Show(Lang.T("Changement de langue impossible pendant un test.") +
                (Lang.French ? "\n\nAttendez la fin du test." : "\n\nWait for the test to finish."),
                Lang.T("Langue"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Lang.Set(code);
        LogMsg("langue : " + code);
        RetranslateAll();
        Refresh(true);
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
        using (var p = Process.Start(psi))
        {
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60000))
            {
                try { p.Kill(); } catch { }
                LogMsg("Ps timeout : " + command);
            }
            else { so.Wait(2000); se.Wait(2000); }
        }
    }

    // Point de fonctionnement de la courbe active (tension / frequence).
    private void ReadActivePoint(out int mhz, out int mv)
    {
        mhz = 0; mv = 0;
        try
        {
            string f = Path.Combine(Root, "plateau-active.json");
            if (!File.Exists(f)) return;
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f),
                "\"mv\"\\s*:\\s*(\\d+)\\s*,\\s*\"offset\"\\s*:\\s*-?\\d+\\s*,\\s*\"mhz\"\\s*:\\s*(\\d+)");
            if (m.Success)
            {
                mv = int.Parse(m.Groups[1].Value);
                mhz = int.Parse(m.Groups[2].Value);
            }
        }
        catch { }
    }

    public void CliApply()
    {
        ApplyConfig();
        Thread.Sleep(4000);
        LogMsg("apply : configuration appliquee par la ligne de commande");
    }

    private void ApplyConfig()
    {
        // Configuration active si elle existe, sinon la reference.
        string plateau = Path.Combine(Root, "plateau-active.json");
        if (!File.Exists(plateau)) plateau = Path.Combine(Root, "plateau-1845-831.json");

        // Power limit memorise (fichier utilise aussi par le watchdog).
        int pl = 320;
        try
        {
            string f = Path.Combine(Root, "power-limit.txt");
            if (File.Exists(f))
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f), @"(\d+)");
                if (m.Success) pl = int.Parse(m.Groups[1].Value);
            }
        }
        catch { }

        RunSteps(Lang.French ? "Application de la configuration" : "Applying configuration", new[]
        {
            Lang.French ? "Application de la courbe V/F" : "Applying the V/F curve",
            Lang.French ? "Reglage du power limit a " + pl + " W" : "Setting power limit to " + pl + " W",
            Lang.French ? "Verification de la courbe" : "Checking the curve",
            Lang.French ? "Verification du service de ventilation" : "Checking the fan service"
        }, i =>
        {
            switch (i)
            {
                case 0:
                    Ps("& '" + Path.Combine(Root, "snu.exe") + "' --in-tuning-file '" + plateau + "' --no-persist | Out-Null");
                    break;
                case 1:
                    Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + pl + " | Out-Null");
                    try { File.WriteAllText(Path.Combine(Root, "power-limit.txt"), pl + " W\n"); } catch { }
                    break;
                case 2:
                    Ps("& '" + Path.Combine(Root, "snu.exe") + "' status | Out-Null");
                    int histMhz, histMv;
                    ReadActivePoint(out histMhz, out histMv);
                    // Toujours le nom interne du profil : la restauration
                    // cherche profil-<nom>.txt, independamment de la langue.
                    AppendHistory(histMhz, histMv, pl, "equilibre");
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
            (Lang.French
                ? "Restaurer la configuration d'origine ?\n\n" +
                  "- courbe V/F stock\n- power limit d'origine (" + PlDefault + " W)\n- ventilateurs rendus au VBIOS\n" +
                  "- service et tache planifiee supprimes"
                : "Restore the stock configuration?\n\n" +
                  "- stock V/F curve\n- default power limit (" + PlDefault + " W)\n- fans returned to VBIOS\n" +
                  "- service and scheduled task removed"),
            Lang.French ? "Restauration" : "Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        RunSteps("Restauration de l'origine", new[]
        {
            "Arret du service de ventilation",
            "Suppression du service",
            "Ventilateurs rendus au VBIOS",
            "Courbe V/F remise au stock",
            "Power limit remis a " + PlDefault + " W",
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
                case 4: Ps("& nvidia-smi -pl " + PlDefault + " | Out-Null"); break;
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
            Tag = title,
            Text = Lang.T(title),
            ForeColor = Color.FromArgb(150, 200, 60),
            Location = new Point(14, y),
            Size = new Size(532, 26 + keys.Length * 18),
            Font = new Font("Segoe UI Semibold", 9f)
        };

        int ky = 20;
        foreach (string k in keys)
        {
            var lk = new Label { Tag = k, Text = Lang.T(k), ForeColor = Color.Gray, AutoSize = false, Size = new Size(210, 17), Location = new Point(12, ky) };
            var lv = new Label { Text = "-", ForeColor = Color.WhiteSmoke, AutoSize = false, Size = new Size(290, 17), Location = new Point(226, ky), TextAlign = ContentAlignment.MiddleRight };
            box.Controls.Add(lk);
            box.Controls.Add(lv);
            _v[k] = lv;
            ky += 18;
        }
        parent.Controls.Add(box);
        return y + box.Height + 8;
    }

    private static string FanText(int rpm, int pct)
    {
        string unit = Lang.French ? " tr/min" : " rpm";
        if (rpm > 0 && pct >= 0) return rpm + unit + "  (" + pct + " %)";
        if (rpm > 0) return rpm + unit;
        if (pct >= 0) return pct + " %";
        return "n/a";
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
                // Lecture asynchrone : ReadToEnd() bloquait indefiniment si le
                // processus ne fermait pas son flux (nvidia-smi sous charge).
                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(10000))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                return so.Wait(2000) ? so.Result : "";
            }
        }
        catch { return ""; }
    }

    private void Refresh(bool full)
    {
        if (PackageRequested)
        {
            PackageRequested = false;
            PackageButton();
        }

        if (CompareRequested)
        {
            CompareRequested = false;
            CompareButton();
        }

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

        // Pendant un test, on continue a rafraichir l'interface : le verrou
        // NVAPI interne empeche tout interblocage, et l'utilisateur garde des
        // valeurs a jour (tension, puissance, temperatures).
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
                    int fanPct = ParseInt(p[16]);
                    if (fanPct >= 0) _s.Fan1Pct = fanPct;
                }
            }

            int g, h, m;
            NvSensors.ReadTemps(out g, out h, out m);
            if (g > 0) _s.Temp = g;
            _s.Hotspot = h; _s.MemTemp = m;

            int r1, c1, r2, c2;
            NvSensors.ReadFans(out r1, out c1, out r2, out c2);
            if (r1 > 0) _s.Fan1Rpm = r1;
            if (c1 >= 0) _s.Fan1Pct = c1;
            if (r2 > 0) _s.Fan2Rpm = r2;
            if (c2 >= 0) _s.Fan2Pct = c2;

            string v = Run(Path.Combine(Root, "snu.exe"), "voltage");
            var mt = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)\s*mV");
            if (mt.Success) _s.VoltageMv = int.Parse(mt.Groups[1].Value);
            else if (_s.VoltageMv <= 0)
            {
                // nouvelle tentative unique : la premiere lecture au demarrage
                // peut arriver avant que le pilote ne reponde
                string v2 = Run(Path.Combine(Root, "snu.exe"), "voltage");
                var mt2 = System.Text.RegularExpressions.Regex.Match(v2, @"(\d+)\s*mV");
                if (mt2.Success) _s.VoltageMv = int.Parse(mt2.Groups[1].Value);
            }

            // graphique temps reel
            if (_graph != null) _graph.Add(_s.Temp, _s.PowerNow, _s.ClockNow);

            // alerte de temperature
            if (_s.TempSlowdown > 0 && _s.Temp >= _s.TempSlowdown - 5)
            {
                if (!_tempAlert)
                {
                    _tempAlert = true;
                    try
                    {
                        _tray.ShowBalloonTip(8000, "Temperature elevee",
                            "GPU a " + _s.Temp + " C (seuil de ralentissement : " + _s.TempSlowdown + " C).",
                            ToolTipIcon.Warning);
                    }
                    catch { }
                    LogMsg("alerte temperature : " + _s.Temp + " C");
                }
            }
            else if (_s.TempSlowdown > 0 && _s.Temp < _s.TempSlowdown - 12) _tempAlert = false;

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
                    UpdateTargetBounds();
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
            _lStatus.Text = Lang.T("Erreur : ") + e.Message;
        }
    }

    private void ReadAppliedConfig()
    {
        try
        {
            // La configuration appliquee se lit dans la courbe active : c'est
            // elle que les boutons d'application viennent de reecrire.
            int activeMhz, activeMv;
            ReadActivePoint(out activeMhz, out activeMv);
            if (activeMhz > 0 && activeMv > 0)
                _s.Undervolt = activeMhz + " MHz a " + activeMv + " mV";

            string log = Path.Combine(Root, "undervolt.log");
            if (string.IsNullOrEmpty(_s.Undervolt) && File.Exists(log))
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
            var mh = System.Text.RegularExpressions.Regex.Match(dump, @"(\d+) mV\s*->\s*(\d+)\s*MHz");
            _s.Curve831 = mh.Success
                ? mh.Groups[1].Value + " mV / " + mh.Groups[2].Value + " MHz"
                : (activeMv > 0 && activeMhz > 0 ? activeMv + " mV / " + activeMhz + " MHz" : "-");
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
                var so = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } _s.Torch = "absent"; return; }
                string o = so.Wait(2000) ? so.Result.Trim() : "";
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
        Set("Courbe active", _s.Curve831);

        Set("Frequence GPU", _s.ClockNow + " MHz");
        Set("Frequence memoire", _s.MemClockNow + " MHz");
        Set("Tension", _s.VoltageMv > 0 ? _s.VoltageMv + " mV" : "n/a");
        Set("Puissance", _s.PowerNow.ToString("0.0") + " W");
        Set("Temperature GPU", (_s.Temp > 0 ? _s.Temp + " C" : "n/a"));
        Set("Hotspot", (_s.Hotspot > 0 ? _s.Hotspot + " C" : "n/a"));
        Set("Temperature memoire", (_s.MemTemp > 0 ? _s.MemTemp + " C" : "n/a"));
        Set("Ventilateur 1", FanText(_s.Fan1Rpm, _s.Fan1Pct));
        Set("Ventilateur 2", FanText(_s.Fan2Rpm, _s.Fan2Pct));
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
                    MessageBox.Show(Lang.T("Cle de notification introuvable."), "Icone",
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
                Lang.French
                    ? "Icone epinglee dans la zone de notification.\n\nRedemarrez la session (ou l'explorateur) pour que le changement soit applique."
                    : "Icon pinned to the notification area.\n\nRestart the session (or Explorer) for the change to apply.",
                Lang.French ? "Icone" : "Icon", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show((Lang.French ? "Impossible d'epingler l'icone : " : "Unable to pin the icon: ") + e.Message,
                Lang.French ? "Icone" : "Icon", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        _step.Text = Lang.T("Installation de PyTorch via pip (peut durer plusieurs minutes)...");

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
                _step.Text = Lang.T("Installation de PyTorch terminee.");
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
        // Le texte affiche est traduit : on mappe l'index vers le nom interne
        // du profil, seul nom utilise pour lire profil-<nom>.txt.
        string[] internes = { "silence", "equilibre", "froid" };
        int i = _profileBox.SelectedIndex;
        if (i < 0 || i >= internes.Length) return;
        SetProfile(internes[i]);
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
        try
        {
            string path = @"C:\Nvidia\nvidiaconf.log";
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 2 * 1024 * 1024)
            {
                string old = path + ".1";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                try { File.Move(path, old); } catch { }
            }
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
        }
        catch { }
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
                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } missing = true; }
                else missing = p.ExitCode != 0;
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
            int targetPl = 320;
            try
            {
                string plFile = Path.Combine(root, "power-limit.txt");
                if (File.Exists(plFile))
                {
                    var mp = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(plFile), @"(\d+)");
                    if (mp.Success) targetPl = int.Parse(mp.Groups[1].Value);
                }
            }
            catch { }

            using (var p = Process.Start(psiPl))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                else
                {
                    string o = so.Wait(2000) ? so.Result.Trim() : "";
                    double pl;
                    if (double.TryParse(o, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out pl) && Math.Abs(pl - targetPl) > 1)
                    {
                        var psiSet = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                            "-pl " + targetPl)
                        { UseShellExecute = false, CreateNoWindow = true };
                        using (var ps = Process.Start(psiSet)) ps.WaitForExit(15000);
                        Log("power limit applique : " + targetPl + " W");
                    }
                }
            }

            // courbe V/F
            var psiSt = new ProcessStartInfo(snu, "status")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psiSt))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } }
                string o = so.Wait(2000) ? so.Result : "";
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
        Lang.Load();
        Log("demarrage args=" + string.Join(" ", args) + " langue=" + Lang.Current);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool hidden = false, tune = false, check = false, report = false, freqTest = false, fanTest = false, packageTest = false, compareTest = false, applyTest = false;
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
            if (a == "--compare") compareTest = true;
            if (a == "--apply") applyTest = true;
            if (a == "--mhz" && i + 1 < args.Length)
            {
                int v;
                if (int.TryParse(args[i + 1], out v)) MainForm.RequestedMhz = v;
            }
            if (a == "--mv" && i + 1 < args.Length)
            {
                int v;
                if (int.TryParse(args[i + 1], out v)) MainForm.RequestedMv = v;
            }
            if (a == "--pl" && i + 1 < args.Length)
            {
                int v;
                if (int.TryParse(args[i + 1], out v)) MainForm.RequestedPowerLimit = v;
            }
        }

        string root = File.Exists(@"C:\Nvidia\snu.exe") ? @"C:\Nvidia" : @"C:\ProgramData\NvidiaUndervolt";
        var compat = Compatibility.Check(root);
        Log("compatibilite : " + (compat.Ok ? "OK" : "NON") + " / " + compat.GpuName);

        if (freqTest || fanTest)
        {
            try
            {
                var f = new MainForm(true, false);
                if (freqTest) f.CliFrequencyTest();
                if (fanTest) f.CliFanTest();
            }
            catch (Exception e) { Log("CLI test erreur : " + e.Message); }
            return 0;
        }

        if (applyTest)
        {
            try
            {
                var af = new MainForm(true, false);
                af.CliApply();
            }
            catch (Exception e) { Log("apply CLI " + e.Message); }
            return 0;
        }

        if (compareTest)
        {
            bool sentC = false;
            try
            {
                using (var ev = EventWaitHandle.OpenExisting("NvidiaConf.Compare"))
                {
                    ev.Set();
                    sentC = true;
                }
            }
            catch { }

            if (sentC) { Log("compare : demande transmise a l'instance en cours"); return 0; }

            var cf = new MainForm(false, false);
            cf.StartPackageOnShown();   // la comparaison se lance depuis l'onglet Rapport
            try { Application.Run(cf); } catch (Exception e) { Log("compare erreur : " + e.Message); }
            return 0;
        }

        if (packageTest)
        {
            // On confie le test a l'instance deja en cours (icone de la zone de
            // notification) pour que la progression s'affiche dans SA fenetre.
            // Les consignes --mhz/--mv/--pl doivent suivre la demande :
            // l'evenement ne transporte pas de donnees.
            try
            {
                File.WriteAllText(Path.Combine(root, "package-request.txt"),
                    "mhz=" + MainForm.RequestedMhz + "\nmv=" + MainForm.RequestedMv +
                    "\npl=" + MainForm.RequestedPowerLimit + "\n");
            }
            catch { }

            bool sent = false;
            try
            {
                using (var ev = EventWaitHandle.OpenExisting("NvidiaConf.Package"))
                {
                    ev.Set();
                    sent = true;
                }
            }
            catch { }

            if (sent) { Log("package : demande transmise a l'instance en cours"); return 0; }

            // aucune instance : on lance une fenetre visible
            var pf = new MainForm(false, false);
            pf.StartPackageOnShown();
            try { Application.Run(pf); } catch (Exception e) { Log("package erreur : " + e.Message); }
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
                Process.Start(new ProcessStartInfo("nvidia-smi", "-pl " + MainForm.DefaultPowerLimit)
                { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(15000);
            }
            catch { }
            if (!tune && !hidden)
            {
                MessageBox.Show(
                    (Lang.French
                        ? "Un test d'optimisation a ete interrompu (probable instabilite du pilote).\n\n" +
                          "La configuration d'origine a ete restauree automatiquement.\n"
                        : "An optimisation test was interrupted (likely driver instability).\n\n" +
                          "The stock configuration was restored automatically.\n") +
                    "L'optimisation reprendra a un palier plus sur.",
                    "Test interrompu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        if (!compat.Ok)
        {
            Log("refus : " + compat.Reason);
            MessageBox.Show(
                (Lang.French ? "Cette carte n'est pas compatible avec NVIDIA Configuration.\n\n"
                             : "This card is not supported by NVIDIA Configuration.\n\n") +
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
        EventWaitHandle packageEvent = null;
        try { packageEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "NvidiaConf.Package"); } catch { }
        EventWaitHandle compareEvent = null;
        try { compareEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "NvidiaConf.Compare"); } catch { }

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

        if (packageEvent != null)
        {
            var pkg = new Thread(() =>
            {
                while (true)
                {
                    packageEvent.WaitOne();
                    MainForm.PackageRequested = true;
                    MainForm.ShowRequested = true;   // amene la fenetre au premier plan
                }
            });
            pkg.IsBackground = true;
            pkg.Start();
        }

        if (compareEvent != null)
        {
            var cmp = new Thread(() =>
            {
                while (true)
                {
                    compareEvent.WaitOne();
                    MainForm.CompareRequested = true;
                    MainForm.ShowRequested = true;
                }
            });
            cmp.IsBackground = true;
            cmp.Start();
        }

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
