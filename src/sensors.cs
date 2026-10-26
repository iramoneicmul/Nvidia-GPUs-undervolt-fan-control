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
