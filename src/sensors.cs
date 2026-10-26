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
            MessageBox.Show("Export ecrit :\n" + path, "Export CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            LogMsg("export CSV : " + e.Message);
            MessageBox.Show("Export impossible : " + e.Message, "Export CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==================================================================
    //  Onglet GPU-Unlock : etat, activation, mesure des gains
    // ==================================================================
    private Label _unlockState;
    private Label _unlockGain;

    private void BuildUnlockTab(TabPage page)
    {
        int y = 8;

        var title = new Label
        {
            Text = "GPU-Unlock (Zagitta) - debit FP/BF16",
            ForeColor = Color.FromArgb(150, 200, 60),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(560, 26),
            Location = new Point(18, y)
        };
        page.Controls.Add(title);
        y += 34;

        _unlockState = new Label
        {
            ForeColor = Color.Gainsboro,
            AutoSize = false,
            Size = new Size(566, 150),
            Location = new Point(18, y)
        };
        page.Controls.Add(_unlockState);
        y += 158;

        var btnOn = MakeButton("Activer gpu-unlock", 18, y, 190);
        btnOn.Click += (o, e) => UnlockActivate();
        page.Controls.Add(btnOn);

        var btnOff = MakeButton("Desactiver", 216, y, 120);
        btnOff.Click += (o, e) => UnlockDeactivate();
        page.Controls.Add(btnOff);

        var btnBench = MakeButton("Mesurer les performances", 344, y, 240);
        btnBench.Click += (o, e) => UnlockBenchmark();
        page.Controls.Add(btnBench);
        y += 42;

        var btnDoc = MakeButton("Ouvrir le projet gpu-unlock", 18, y, 280);
        btnDoc.Click += (o, e) => Process.Start("https://github.com/Zagitta/gpu-unlock");
        page.Controls.Add(btnDoc);

        var btnWin = MakeButton("Port Windows (UEFI)", 306, y, 200);
        btnWin.Click += (o, e) => Process.Start("https://github.com/impacifist/Windows-GPU-UNLOCK");
        page.Controls.Add(btnWin);
        y += 46;

        _unlockGain = new Label
        {
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(566, 150),
            Location = new Point(18, y)
        };
        page.Controls.Add(_unlockGain);

        RefreshUnlockState();
    }

    private string UnlockArchitecture()
    {
        string n = (_s.Name ?? "").ToUpperInvariant();
        if (n.Contains("3080 TI") || n.Contains("3090") || n.Contains("3080") || n.Contains("3070")) return "GA102/GA104";
        if (n.Contains("3060")) return "GA104/GA106";
        if (n.Contains("2080") || n.Contains("2070") || n.Contains("2060")) return "TU104/TU106 (Turing)";
        if (n.Contains("4070") || n.Contains("4080") || n.Contains("4090")) return "AD102/AD103/AD104 (Ada)";
        return "inconnue";
    }

    private void RefreshUnlockState()
    {
        if (_unlockState == null) return;

        string arch = UnlockArchitecture();
        bool windowsPort = _s.Name != null &&
            (_s.Name.ToUpperInvariant().Contains("3090") || _s.Name.ToUpperInvariant().Contains("3070 TI") ||
             _s.Name.ToUpperInvariant().Contains("3060"));

        _unlockState.Text =
            "Carte          : " + (_s.Name ?? "inconnue") + Environment.NewLine +
            "Architecture   : " + arch + Environment.NewLine +
            "Moteur gpu-unlock : module noyau Linux (initramfs, avant le pilote NVIDIA)" + Environment.NewLine +
            "Windows        : non applicable tel quel - aucun service ni pilote a activer" + Environment.NewLine +
            "Port Windows   : chargeur UEFI experimental (impacifist/Windows-GPU-UNLOCK)" + Environment.NewLine +
            "Profils valides: RTX 3060, 3070 Ti, 3090 FE, 3090 Ti" + Environment.NewLine +
            "Cette carte    : " + (windowsPort ? "profil existant (resultat variable)" : "AUCUN profil valide a ce jour") + Environment.NewLine +
            Environment.NewLine +
            "Effet attendu  : x2 sur le debit FP/BF16 (issue rate), ~x1.9 mesure en pratique.";

        _unlockState.ForeColor = windowsPort ? Color.Gainsboro : Color.FromArgb(230, 170, 90);
    }

    private void UnlockActivate()
    {
        RefreshUnlockState();
        LogMsg("gpu-unlock : demande d'activation");
        MessageBox.Show(
            "gpu-unlock ne peut pas etre active depuis Windows.\n\n" +
            "1. Le projet de reference (Zagitta/gpu-unlock) est un MODULE NOYAU LINUX,\n" +
            "   charge dans l'initramfs avant le pilote NVIDIA. Il n'existe ni\n" +
            "   service ni interface Windows.\n\n" +
            "2. Le port Windows (impacifist/Windows-GPU-UNLOCK) est un chargeur UEFI\n" +
            "   experimental : Secure Boot desactive, BitLocker desactive, partition\n" +
            "   EFI dediee, build local (clang/lld, 5 firmwares), profil materiel precis.\n\n" +
            "3. Profils valides : RTX 3060, 3070 Ti, 3090 FE, 3090 Ti.\n" +
            "   La RTX 3080 Ti n'a aucun profil valide.\n\n" +
            "Utilisez « Mesurer les performances » pour chiffrer le gain potentiel\n" +
            "sur cette carte avant toute manipulation.",
            "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UnlockDeactivate()
    {
        LogMsg("gpu-unlock : demande de desactivation");
        MessageBox.Show(
            "Rien a desactiver : gpu-unlock n'est pas actif sous Windows.\n\n" +
            "Sur une installation Linux, il suffit de retirer gpu_unlock.targets=\n" +
            "de la ligne de commande du noyau, ou de desinstaller le paquet.",
            "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UnlockBenchmark()
    {
        if (_busy) return;
        string py = ResolvePython();
        if (py == null)
        {
            MessageBox.Show("PyTorch est requis pour mesurer les debits FP16/BF16.",
                "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _busy = true;
        if (_timer != null) _timer.Stop();
        UiProgress("Mesure des debits FP16 / BF16 / TF32 / FP32", 5, 1);

        var th = new Thread(() =>
        {
            try
            {
                string outFile = Path.Combine(Root, "unlock-bench.txt");
                try { File.Delete(outFile); } catch { }
                var psi = new ProcessStartInfo(py,
                    "\"C:\\Nvidia\\unlock-bench.py\" 8192 \"" + outFile + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(180000);

                string res = "";
                try { res = File.ReadAllText(outFile).Trim(); } catch { }
                LogMsg("gpu-unlock benchmark : " + res.Replace("\n", " "));

                double fp16 = 0, bf16 = 0, tf32 = 0, fp32 = 0;
                foreach (string line in res.Split('\n'))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    double v;
                    if (!double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                    if (kv[0].Trim() == "FP16") fp16 = v;
                    if (kv[0].Trim() == "BF16") bf16 = v;
                    if (kv[0].Trim() == "TF32") tf32 = v;
                    if (kv[0].Trim() == "FP32") fp32 = v;
                }

                string gain =
                    "Debit mesure (8192 x 8192, 5 s par test)" + Environment.NewLine +
                    string.Format("  FP16 : {0,7:0.0} TFLOPS   potentiel deverrouille {1,7:0.0} (+100 %)\n", fp16, fp16 * 2) +
                    string.Format("  BF16 : {0,7:0.0} TFLOPS   potentiel deverrouille {1,7:0.0} (+100 %)\n", bf16, bf16 * 2) +
                    string.Format("  TF32 : {0,7:0.0} TFLOPS   potentiel deverrouille {1,7:0.0} (+100 %)\n", tf32, tf32 * 2) +
                    string.Format("  FP32 : {0,7:0.0} TFLOPS   non concerne par le deblocage\n", fp32) +
                    Environment.NewLine +
                    "Gain mesure dans les rapports publics : x1.88 a x1.95 (FP16/BF16/TF32).\n" +
                    "Concerne uniquement l'IA et le calcul FP16/BF16.\n" +
                    "Jeux (FP32) : aucun gain attendu.\n" +
                    Environment.NewLine +
                    string.Format("Power limit actuel : {0:0} W. A cette puissance, la carte est deja\n", _s.PowerLimit) +
                    "bridee par l'energie avant de l'etre par l'issue rate : le gain reel\n" +
                    "serait donc inferieur a +100 %, sauf a augmenter le power limit.";

                Ui(() =>
                {
                    _unlockGain.Text = gain;
                    _bar.Value = 100;
                    _step.Text = "Mesure terminee.";
                });
            }
            catch (Exception e)
            {
                LogMsg("gpu-unlock benchmark : " + e.Message);
                Ui(() => { _step.Text = "Mesure impossible : " + e.Message; });
            }
            finally
            {
                Ui(() =>
                {
                    if (_timer != null) _timer.Start();
                    _busy = false;
                });
            }
        });
        th.IsBackground = true;
        th.Start();
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

        _grid.Columns.Add("c0", "Capteur");
        _grid.Columns.Add("c1", "Valeur");
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
        try { DoTestFanProfiles(); }
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
            try { DoTestFanProfiles(); }
            catch (Exception e) { LogMsg("FAN TEST FATAL " + e); Ui(() => { _step.Text = "Echec : " + e.Message; }); }
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

        string choisi = "froid";
        int bestRpm = int.MaxValue;
        foreach (var m in mesures)
        {
            int rpm = int.Parse(m[1]);
            int gpu = int.Parse(m[2]);
            if (gpu <= seuil && rpm > 0 && rpm < bestRpm) { bestRpm = rpm; choisi = m[0]; }
        }

        try { File.Copy(Path.Combine(Root, "profil-" + choisi + ".txt"), Path.Combine(Root, "fan-curve.txt"), true); } catch { }
        LogMsg("ventilation : seuil " + seuil + " C, profil retenu " + choisi);
        return choisi;
    }

    private void DoTestFanProfiles()
    {
        string[] profils = { "silence", "equilibre", "froid" };
        var rapport = new StringBuilder();
        rapport.AppendLine("# Test des profils de ventilation - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        rapport.AppendLine();
        rapport.AppendLine("| Profil | Regime moyen | Regime max | GPU max | Memoire max |");
        rapport.AppendLine("|---|---|---|---|---|");

        for (int p = 0; p < profils.Length; p++)
        {
            string profil = profils[p];
            int idx = p;
            Ui(() =>
            {
                _step.Text = string.Format("Test du profil {0} ({1}/3) - charge 1 min", profil, idx + 1);
                _bar.Value = (int)(100.0 * idx / 3);
            });

            try
            {
                File.Copy(Path.Combine(Root, "profil-" + profil + ".txt"),
                          Path.Combine(Root, "fan-curve.txt"), true);
            }
            catch (Exception e) { LogMsg("profil " + profil + " : " + e.Message); }
            Thread.Sleep(4000);

            long sumRpm = 0; int n = 0, maxRpm = 0, maxGpu = 0, maxMem = 0;
            var sw = Stopwatch.StartNew();
            var load = new Thread(() => { OpenClTest.Run(70, 2048, null); });
            load.IsBackground = true;
            load.Start();

            Thread.Sleep(15000);   // montee en regime
            while (sw.Elapsed.TotalSeconds < 70)
            {
                int rpm = SmiInt("fan.speed");
                int gpu = SmiInt("temperature.gpu");
                int r1, c1, r2, c2;
                NvSensors.ReadFans(out r1, out c1, out r2, out c2);
                if (r1 > 0) { sumRpm += r1; n++; if (r1 > maxRpm) maxRpm = r1; }

                int gpuNv, hNv, memNv;
                NvSensors.ReadTemps(out gpuNv, out hNv, out memNv);
                if (gpuNv > gpu) gpu = gpuNv;
                if (gpu > maxGpu) maxGpu = gpu;
                if (memNv > maxMem) maxMem = memNv;
                Thread.Sleep(2000);
            }

            int avg = n > 0 ? (int)(sumRpm / n) : 0;
            rapport.AppendLine(string.Format("| {0} | {1} tr/min | {2} tr/min | {3} C | {4} C |",
                profil, avg, maxRpm, maxGpu, maxMem));
            LogMsg(string.Format("profil {0} : {1} tr/min moyen, GPU max {2} C, mem max {3} C",
                profil, avg, maxGpu, maxMem));
            Thread.Sleep(5000);
        }

        // retour au profil equilibre
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
            _step.Text = "Test des profils termine (profil equilibre restaure).";
        });
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
