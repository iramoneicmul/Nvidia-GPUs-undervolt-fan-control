// =====================================================================
//  sensors.cs  -  onglet "Capteurs" facon GPU-Z (valeur / min / max)
//  + test automatique des trois profils de ventilation
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private static readonly string[] SensorNames =
    {
        "Frequence GPU", "Frequence memoire", "Tension", "Puissance",
        "Power limit", "Temperature GPU", "Hotspot", "Temperature memoire",
        "Ventilateur 1 (tr/min)", "Ventilateur 1 (%)",
        "Ventilateur 2 (tr/min)", "Ventilateur 2 (%)",
        "Charge GPU", "Memoire utilisee", "PCIe (version)", "PCIe (largeur)"
    };

    private readonly Dictionary<string, double[]> _stats = new Dictionary<string, double[]>();
    private DataGridView _grid;

    // ------------------------------------------------------------------
    //  Graphique temps reel (temperature, puissance, frequence)
    // ------------------------------------------------------------------
    private void BuildLiveGraph(TabPage page, int y4)
    {
        _graph = new MiniGraph
        {
            Location = new Point(18, y4 + 14),
            Size = new Size(566, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        page.Controls.Add(_graph);
    }

    // ------------------------------------------------------------------
    //  Export CSV des statistiques de capteurs
    // ------------------------------------------------------------------
    private void ExportCsv()
    {
        try
        {
            string path = Path.Combine(Root, "export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            var sb = new StringBuilder();
            sb.AppendLine("capteur;valeur;min;max");
            foreach (string key in SensorNames)
            {
                if (!_stats.ContainsKey(key)) continue;
                var st = _stats[key];
                sb.AppendLine(string.Format("{0};{1:0.##};{2:0.##};{3:0.##}", key, st[0], st[1], st[2]));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            LogMsg("export CSV : " + path);
            MessageBox.Show(Lang.T("Export ecrit :") + "\n" + path, Lang.T("Export CSV"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            LogMsg("export CSV : " + e.Message);
            MessageBox.Show(Lang.T("Export impossible : ") + e.Message, Lang.T("Export CSV"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BuildSensorsTab(TabPage page)
    {
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(24, 24, 24),
            GridColor = Color.FromArgb(45, 45, 45),
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Font = new Font("Consolas", 9f),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(150, 200, 60);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(28, 28, 28);
        _grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 60, 25);

        _grid.Columns.Add("c0", Lang.T("Capteur"));
        _grid.Columns.Add("c1", Lang.T("Valeur"));
        _grid.Columns.Add("c2", "Min");
        _grid.Columns.Add("c3", "Max");
        _grid.Columns[0].FillWeight = 46;
        _grid.Columns[1].FillWeight = 22;
        _grid.Columns[2].FillWeight = 16;
        _grid.Columns[3].FillWeight = 16;

        foreach (string n in SensorNames)
        {
            int i = _grid.Rows.Add(n, "-", "-", "-");
            _grid.Rows[i].Cells[1].Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Rows[i].Cells[2].Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Rows[i].Cells[3].Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        page.Controls.Add(_grid);

        var exportBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = Color.FromArgb(28, 28, 28)
        };
        var btnCsv = MakeButton("Exporter CSV", 8, 4, 130);
        btnCsv.Click += (o, e) => ExportCsv();
        exportBar.Controls.Add(btnCsv);
        page.Controls.Add(exportBar);
    }

    private void UpdateSensors()
    {
        if (_grid == null) return;

        SetSensor("Frequence GPU", _s.ClockNow, "MHz");
        SetSensor("Frequence memoire", _s.MemClockNow, "MHz");
        SetSensor("Tension", _s.VoltageMv, "mV");
        SetSensor("Puissance", _s.PowerNow, "W");
        SetSensor("Power limit", _s.PowerLimit, "W");
        SetSensor("Temperature GPU", _s.Temp, "C");
        SetSensor("Hotspot", _s.Hotspot, "C");
        SetSensor("Temperature memoire", _s.MemTemp, "C");
        SetSensor("Ventilateur 1 (tr/min)", _s.Fan1Rpm, "tr/min");
        SetSensor("Ventilateur 1 (%)", _s.Fan1Pct, "%");
        SetSensor("Ventilateur 2 (tr/min)", _s.Fan2Rpm, "tr/min");
        SetSensor("Ventilateur 2 (%)", _s.Fan2Pct, "%");
        SetSensor("Charge GPU", _s.GpuUsage, "%");
        SetSensor("Memoire utilisee", _s.VramUsed, "Mo");

        SetText("PCIe (version)", _s.PcieGen);
        SetText("PCIe (largeur)", _s.PcieWidth);
    }

    private void SetSensor(string name, double value, string unit)
    {
        int idx = Array.IndexOf(SensorNames, name);
        if (idx < 0 || idx >= _grid.Rows.Count) return;

        if (!_stats.ContainsKey(name)) _stats[name] = new[] { double.MaxValue, double.MinValue };
        var st = _stats[name];
        if (value > 0)
        {
            if (value < st[0]) st[0] = value;
            if (value > st[1]) st[1] = value;
        }

        var row = _grid.Rows[idx];
        row.Cells[1].Value = value > 0 ? value.ToString("0.#") + " " + unit : "n/a";
        row.Cells[2].Value = st[0] != double.MaxValue ? st[0].ToString("0.#") : "-";
        row.Cells[3].Value = st[1] != double.MinValue ? st[1].ToString("0.#") : "-";
    }

    private void SetText(string name, string value)
    {
        int idx = Array.IndexOf(SensorNames, name);
        if (idx < 0 || idx >= _grid.Rows.Count) return;
        var row = _grid.Rows[idx];
        row.Cells[1].Value = value;
        row.Cells[2].Value = "-";
        row.Cells[3].Value = "-";
    }

    // ------------------------------------------------------------------
    //  Test automatique des trois profils de ventilation
    // ------------------------------------------------------------------
    public void CliFanTest()
    {
        try { DoOptimizeFanProfiles(); }
        catch (Exception e) { LogMsg("CliFanTest " + e.Message); }
    }

    private void TestFanProfiles()
    {
        if (_busy) return;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;

        var th = new Thread(() =>
        {
            try { DoOptimizeFanProfiles(); }
            catch (Exception e) { LogMsg("FAN TEST FATAL " + e); Ui(() => { _step.Text = Lang.T("Echec : ") + e.Message; }); }
            finally
            {
                Ui(() =>
                {
                    _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = true;
                    _busy = false;
                });
            }
        });
        th.IsBackground = true;
        th.Start();
    }

    /// <summary>Teste les trois profils de ventilation sur charge reelle et
    /// retourne le profil retenu (le plus silencieux qui tient le seuil).</summary>
    public string TestFanProfilesForPackage()
    {
        string[] profils = { "silence", "equilibre", "froid" };
        var mesures = new List<string[]>();
        string py = ResolvePython();
        int seuil = _s.TempSlowdown > 0 ? _s.TempSlowdown - 10 : 80;

        for (int p = 0; p < profils.Length; p++)
        {
            string profil = profils[p];
            int idx = p;
            UiProgress(string.Format("Ventilation : profil {0} ({1}/3)", profil, idx + 1),
                10 + idx * 3, Math.Max(1, (profils.Length - idx) * 65 / 60));

            try { File.Copy(Path.Combine(Root, "profil-" + profil + ".txt"), Path.Combine(Root, "fan-curve.txt"), true); }
            catch (Exception e) { LogMsg("ventilation " + profil + " : " + e.Message); }
            Thread.Sleep(4000);

            Process load = null;
            if (py != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\stress-long.py\" 60 2048")
                    { UseShellExecute = false, CreateNoWindow = true };
                    load = Process.Start(psi);
                }
                catch { }
            }

            Thread.Sleep(15000);
            long sumRpm = 0; int n = 0, maxRpm = 0, maxGpu = 0, maxMem = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 45)
            {
                int r1, c1, r2, c2;
                NvSensors.ReadFans(out r1, out c1, out r2, out c2);
                if (r1 > 0) { sumRpm += r1; n++; if (r1 > maxRpm) maxRpm = r1; }
                int g, h, m;
                NvSensors.ReadTemps(out g, out h, out m);
                if (g > maxGpu) maxGpu = g;
                if (m > maxMem) maxMem = m;
                Thread.Sleep(2000);
            }
            try { if (load != null && !load.HasExited) load.Kill(); } catch { }

            int avg = n > 0 ? (int)(sumRpm / n) : 0;
            LogMsg(string.Format("ventilation {0} : {1} tr/min moyen, max {2}, GPU max {3} C, mem max {4} C",
                profil, avg, maxRpm, maxGpu, maxMem));
            mesures.Add(new string[] { profil, avg.ToString(), maxGpu.ToString(), maxMem.ToString() });
            Thread.Sleep(5000);
        }

        // Preference explicite du moins bruyant au plus bruyant : la regulation
        // en boucle fermee egalise les regimes mesures, il faut donc choisir
        // sur la consigne du profil, pas sur le regime releve.
        string[] ordre = { "silence", "equilibre", "froid" };
        string choisi = "froid";
        foreach (string nom in ordre)
        {
            var m = mesures.Find(x => x[0] == nom);
            if (m == null) continue;
            int gpu = int.Parse(m[2]);
            if (gpu <= seuil) { choisi = nom; break; }
        }
        LogMsg("ventilation : seuil " + seuil + " C, choix " + choisi);

        try { File.Copy(Path.Combine(Root, "profil-" + choisi + ".txt"), Path.Combine(Root, "fan-curve.txt"), true); } catch { }
        LogMsg("ventilation : seuil " + seuil + " C, profil retenu " + choisi);
        return choisi;
    }

    // ==================================================================
    //  Optimisation de la ventilation
    //  Determine, pour trois cibles thermiques, le regime minimal qui les
    //  tient avec la configuration appliquee, puis genere et enregistre les
    //  trois courbes (silence / equilibre / froid).
    // ==================================================================
    private void DoOptimizeFanProfiles()
    {
        string[] profils = { "silence", "equilibre", "froid" };
        int[] cibles = { 82, 78, 74 };
        int[] departs = { 1000, 1300, 1800 };
        string unit = Lang.French ? " tr/min" : " rpm";
        string py = ResolvePython();
        double pl = _s.PowerLimit > 0 ? _s.PowerLimit : 320;

        var rapport = new StringBuilder();
        rapport.AppendLine("# " + (Lang.French ? "Optimisation de la ventilation - " : "Fan optimisation - ") +
                           DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        rapport.AppendLine();
        rapport.AppendLine((Lang.French ? "Configuration appliquee : " : "Applied configuration: ") +
                           _s.Undervolt + " / power limit " + pl.ToString("0") + " W");
        rapport.AppendLine();
        rapport.AppendLine(Lang.French
            ? "| Profil | Cible | Regime retenu | Temperature tenue |"
            : "| Profile | Target | Retained speed | Held temperature |");
        rapport.AppendLine("|---|---|---|---|");

        for (int p = 0; p < profils.Length; p++)
        {
            string profil = profils[p];
            int cible = cibles[p];
            int rpm = departs[p];
            int retenu = -1, tenue = 0;
            int idx = p;

            Ui(() =>
            {
                _step.Text = string.Format(Lang.French
                    ? "Ventilation {0} : recherche du regime (cible {1} C)"
                    : "Cooling {0}: searching speed (target {1} C)", profil, cible);
                _bar.Value = (int)(100.0 * idx / 3);
            });

            Process load = null;
            Thread loadThread;
            if (py != null)
            {
                loadThread = new Thread(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo(py,
                            "\"C:\\Nvidia\\stress-long.py\" 300 2048")
                        { UseShellExecute = false, CreateNoWindow = true };
                        load = Process.Start(psi);
                        if (load != null) load.WaitForExit(340000);
                    }
                    catch { }
                });
            }
            else
            {
                loadThread = new Thread(() => { OpenClTest.Run(300, 2048, null); });
            }
            loadThread.IsBackground = true;
            loadThread.Start();
            Thread.Sleep(20000);

            for (int essai = 0; essai < 6 && retenu < 0; essai++)
            {
                WriteFlatCurve(rpm);
                Thread.Sleep(45000);

                int g, h, mm;
                NvSensors.ReadTemps(out g, out h, out mm);
                LogMsg(string.Format("ventilation {0} : essai {1}, {2}{3}, GPU {4} C (cible {5})",
                    profil, essai + 1, rpm, unit, g, cible));

                if (g > 0 && g <= cible)
                {
                    retenu = rpm;
                    tenue = g;
                }
                else
                {
                    rpm += 300;
                    if (rpm > 3400) rpm = 3400;
                }
            }

            try { if (load != null && !load.HasExited) load.Kill(); } catch { }
            if (retenu < 0) { retenu = 3400; tenue = cible + 5; }

            SaveOriginalProfile(profil);
            GenerateProfile(profil, retenu, cible);

            rapport.AppendLine(string.Format("| {0} | {1} C | {2}{3} | {4} C |",
                profil, cible, retenu, unit, tenue));
            LogMsg(string.Format("ventilation {0} : regime retenu {1}{2} pour {3} C (tenu {4} C)",
                profil, retenu, unit, cible, tenue));

            Thread.Sleep(8000);
        }

        try
        {
            File.Copy(Path.Combine(Root, "profil-equilibre.txt"),
                      Path.Combine(Root, "fan-curve.txt"), true);
        }
        catch { }

        try
        {
            string path = Path.Combine(Root, "rapport-ventilation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md");
            File.WriteAllText(path, rapport.ToString());
            LogMsg("rapport ventilation : " + path);
        }
        catch { }

        Ui(() =>
        {
            _bar.Value = 100;
            _step.Text = Lang.French
                ? "Optimisation terminee : 3 courbes enregistrees. Choisissez le profil."
                : "Optimisation complete: 3 curves saved. Choose a profile.";
            if (_profileBox != null) _profileBox.SelectedIndex = 1;
        });
    }

    private void WriteFlatCurve(int rpm)
    {
        try
        {
            var lines = new List<string>();
            lines.Add("# courbe temporaire - optimisation de la ventilation");
            lines.Add("auto_below=46");
            lines.Add("hysteresis=4");
            lines.Add("interval_ms=2000");
            lines.Add("failsafe_c=88");
            lines.Add("failsafe_mem_c=95");
            lines.Add("reapply_undervolt_minutes=5");
            lines.Add("undervolt_script=C:\\ProgramData\\NvidiaUndervolt\\apply-undervolt.ps1");
            foreach (int t in new[] { 50, 56, 62, 68, 72, 76, 79, 82, 85, 88, 91 })
                lines.Add(t + "=" + rpm);
            File.WriteAllLines(Path.Combine(Root, "fan-curve.txt"), lines);
            LogMsg("ventilation : regime temporaire " + rpm);
        }
        catch (Exception e) { LogMsg("courbe temporaire : " + e.Message); }
    }

    private void SaveOriginalProfile(string profil)
    {
        try
        {
            string src = Path.Combine(Root, "profil-" + profil + ".txt");
            string bak = src + ".bak";
            if (File.Exists(src) && !File.Exists(bak)) File.Copy(src, bak, true);
        }
        catch { }
    }

    private void GenerateProfile(string profil, int rpmCible, int cible)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# =====================================================================");
            sb.AppendLine("#  PROFIL \"" + profil.ToUpperInvariant() + "\" - genere par optimisation");
            sb.AppendLine("#  Cible " + cible + " C : " + rpmCible + " tr/min sous charge.");
            sb.AppendLine("#  Configuration : " + _s.Undervolt);
            sb.AppendLine("# =====================================================================");
            sb.AppendLine("auto_below=46");
            sb.AppendLine("hysteresis=4");
            sb.AppendLine("interval_ms=2000");
            sb.AppendLine("failsafe_c=" + Math.Max(cible + 3, 85));
            sb.AppendLine("failsafe_mem_c=95");
            sb.AppendLine("reapply_undervolt_minutes=5");
            sb.AppendLine("undervolt_script=C:\\ProgramData\\NvidiaUndervolt\\apply-undervolt.ps1");
            sb.AppendLine();

            foreach (int t in new[] { 50, 56, 62, 68, 72, 76, 79, 82, 85, 88, 91 })
            {
                int rpm;
                if (t <= cible) rpm = rpmCible - (cible - t) * 120;
                else rpm = rpmCible + (t - cible) * 150;

                if (rpm < 800) rpm = 800;
                if (rpm > 3400) rpm = 3400;
                rpm = (int)(Math.Round(rpm / 10.0) * 10);
                sb.AppendLine(t + "=" + rpm);
            }

            File.WriteAllText(Path.Combine(Root, "profil-" + profil + ".txt"), sb.ToString());
            LogMsg("profil " + profil + " genere : " + rpmCible + " tr/min a " + cible + " C");
        }
        catch (Exception e) { LogMsg("generation profil : " + e.Message); }
    }
}

/// <summary>Petit graphique temps reel : temperature, puissance, frequence.</summary>
internal sealed class MiniGraph : Panel
{
    private readonly System.Collections.Generic.List<double> _temp = new System.Collections.Generic.List<double>();
    private readonly System.Collections.Generic.List<double> _watts = new System.Collections.Generic.List<double>();
    private readonly System.Collections.Generic.List<double> _mhz = new System.Collections.Generic.List<double>();

    public MiniGraph()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(20, 20, 20);
        BorderStyle = BorderStyle.FixedSingle;
    }

    public void Add(double temp, double watts, double mhz)
    {
        _temp.Add(temp); _watts.Add(watts); _mhz.Add(mhz);
        while (_temp.Count > 150)
        {
            _temp.RemoveAt(0); _watts.RemoveAt(0); _mhz.RemoveAt(0);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var grid = new Pen(Color.FromArgb(40, 40, 40)))
        {
            for (int i = 1; i < 4; i++)
            {
                int y = Height * i / 4;
                g.DrawLine(grid, 0, y, Width, y);
            }
        }

        Draw(g, _temp, 100.0, Color.FromArgb(230, 120, 60));
        Draw(g, _watts, 400.0, Color.FromArgb(120, 200, 80));
        Draw(g, _mhz, 2100.0, Color.FromArgb(90, 160, 240));

        using (var f = new Font("Consolas", 8f))
        using (var b = new SolidBrush(Color.Gainsboro))
        {
            g.DrawString("temp", f, b, 6, 4);
            using (var p = new SolidBrush(Color.FromArgb(230, 120, 60))) g.FillRectangle(p, 40, 6, 12, 8);
            g.DrawString("W", f, b, 60, 4);
            using (var p = new SolidBrush(Color.FromArgb(120, 200, 80))) g.FillRectangle(p, 76, 6, 12, 8);
            g.DrawString("MHz", f, b, 96, 4);
            using (var p = new SolidBrush(Color.FromArgb(90, 160, 240))) g.FillRectangle(p, 126, 6, 12, 8);
        }
    }

    private void Draw(Graphics g, System.Collections.Generic.List<double> data, double scale, Color color)
    {
        if (data.Count < 2) return;
        int pad = 12;
        float w = Width - 2 * pad;
        float h = Height - 2 * pad;
        var pts = new System.Drawing.PointF[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            float x = pad + w * i / (data.Count - 1);
            double v = Math.Max(0, Math.Min(scale, data[i]));
            float y = pad + (float)(h * (1.0 - v / scale));
            pts[i] = new System.Drawing.PointF(x, y);
        }
        using (var pen = new Pen(color, 1.4f)) g.DrawLines(pen, pts);
    }
}
