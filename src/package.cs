// =====================================================================
//  package.cs  -  recherche et validation d'un package optimal
//                 (frequence GPU / tension / power limit / ventilation)
//
//  Objectif : ne pas perdre de performance par rapport a l'origine,
//  consommer moins et chauffer moins.
//
//  1. mesure complete de la configuration d'origine (4 niveaux de charge)
//  2. choix de la frequence cible (frequence d'origine en jeu)
//  3. recherche de la tension minimale stable pour cette cible
//  4. determination du power limit (consommation reelle + marge)
//  5. application, re-mesure et rapport comparatif
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private sealed class Measure
    {
        public int Mhz;
        public int Mv;
        public int Rpm;
        public int TempGpu;
        public int TempMem;
        public double Watts;

        public string Line()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} MHz | {1} mV | {2:0.0} W | {3} C | {4} C | {5} tr/min",
                Mhz, Mv, Watts, TempGpu, TempMem, Rpm);
        }
    }

    public static int RequestedMhz;

    private static int Round15(int mhz)
    {
        return (int)Math.Round(mhz / 15.0) * 15;
    }

    // ------------------------------------------------------------------
    public void CliPackage()
    {
        try { DoPackage(); }
        catch (Exception e) { LogMsg("CliPackage " + e); }
    }

    public void StartPackageOnShown()
    {
        Shown += (o, e) => PackageButton();
    }

    private void PackageButton()
    {
        if (_busy) return;
        int requested = 0;
        if (_targetMhzBox != null) int.TryParse(_targetMhzBox.Text.Trim(), out requested);
        MainForm.RequestedMhz = requested;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;
        _step.Text = "Preparation du package optimal...";

        var th = new Thread(() =>
        {
            try { DoPackage(); }
            catch (Exception e) { LogMsg("DoPackage " + e); Ui(() => { _step.Text = "Echec : " + e.Message; }); }
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

    // ------------------------------------------------------------------
    private void DoPackage()
    {
        ReadAutoTuneConfig();
        string py = ResolvePython();
        if (py == null)
        {
            LogMsg("package : PyTorch requis et introuvable");
            Ui(() => { _step.Text = "PyTorch est requis. Utilisez « Installer PyTorch »."; });
            MessageBox.Show(
                "La recherche de tension exige PyTorch (plus fiable qu'OpenCL).\n\n" +
                "Installez-le avec le bouton « Installer PyTorch », puis relancez.",
                "PyTorch requis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var stock = new Dictionary<string, Measure>();
        var opti = new Dictionary<string, Measure>();

        // ---------- 1) ligne de base d'origine ----------
        Ui(() => { _step.Text = "Ligne de base : configuration d'origine (4 charges)"; _bar.Value = 2; });
        Ps("& '" + Path.Combine(Root, "snu.exe") + "' clear | Out-Null");
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl 350 | Out-Null");
        Thread.Sleep(3000);

        for (int i = 0; i < Levels.Length; i++)
        {
            int idx = i;
            Ui(() => { _step.Text = string.Format("Origine - charge {0} ({1}/4)", LevelNames[idx], idx + 1); _bar.Value = 2 + idx * 5; });
            stock[LevelNames[i]] = MeasureDetailed(py, Levels[i], 60);
            LogMsg("package origine " + LevelNames[i] + " : " + stock[LevelNames[i]].Line());
        }

        // ---------- 2) frequence cible ----------
        int target;
        if (RequestedMhz > 0)
        {
            target = Round15(RequestedMhz);
            LogMsg("package : frequence cible imposee par l'utilisateur : " + target + " MHz");
        }
        else
        {
            target = Round15(stock["jeu"].Mhz);
            if (target < 1500) target = TargetMhz;
            LogMsg("package : frequence cible derivee de l'origine : " + target + " MHz");
        }
        TargetMhz = target;
        LogMsg("package : frequence cible " + target + " MHz");

        // ---------- 3) tension minimale stable ----------
        // verification du moteur de test avant toute chose
        string pyPath = ResolvePython();
        string stab = Path.Combine(Root, "stability.py");
        if (pyPath == null || !File.Exists(stab))
        {
            LogMsg("package : moteur de test incomplet (python=" + (pyPath ?? "absent") +
                   ", stability.py=" + File.Exists(stab) + ")");
            Ui(() => { _step.Text = "Moteur de test indisponible (PyTorch / stability.py)."; });
            MessageBox.Show(
                "Le moteur de test est incomplet :\n" +
                "  PyTorch : " + (pyPath == null ? "absent" : "present") + "\n" +
                "  stability.py : " + (File.Exists(stab) ? "present" : "absent") + "\n\n" +
                "Utilisez « Installer PyTorch » puis relancez.",
                "Moteur de test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var curve = ReadStockCurve();
        var cands = new List<int>();
        foreach (var kv in curve) if (kv.Key >= 806 && kv.Key <= 843) cands.Add(kv.Key);
        cands.Sort(); cands.Reverse();

        int lastOk = -1, firstFail = -1;
        var rows = new List<string>();

        for (int i = 0; i < cands.Count; i++)
        {
            int mv = cands[i];
            int idx = i;
            Ui(() => { _step.Text = string.Format("Tension {0} mV ({1}/{2}) - {3} min", mv, idx + 1, cands.Count, Math.Max(1, StabilitySeconds / 60)); _bar.Value = 22 + (int)(55.0 * idx / cands.Count); });

            WriteMarker(mv);
            WritePlateau(mv, curve);
            ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

            // PyTorch est plus severe qu'OpenCL : priorite pour la securite.
            string res = (ResolvePython() != null)
                ? RunPythonStability()
                : (OpenClTest.Available ? OpenClTest.Run(StabilitySeconds, 2048, null) : "aucun moteur");

            int ecarts = ParseEcarts(res);
            LogMsg(string.Format("package tension {0} mV -> {1}", mv, res.Replace("\n", " ")));
            rows.Add(string.Format("| {0} | {1} | {2} |", mv,
                res.Replace("|", "/"), ecarts == 0 ? "stable" : "**echec**"));

            if (res.Contains("aucun moteur") || res.Contains("aucun resultat"))
            {
                LogMsg("package : moteur muet, arret de la recherche");
                Ui(() => { _step.Text = "Moteur de test muet - recherche interrompue."; });
                ClearMarker();
                return;
            }

            if (ecarts == 0) lastOk = mv;
            else { firstFail = mv; break; }
        }

        int mvRetained = lastOk;
        int okIndex = cands.IndexOf(lastOk);
        if (okIndex > 0) mvRetained = cands[okIndex - 1];
        ClearMarker();
        LogMsg("package : tension retenue " + mvRetained + " mV");

        // application provisoire pour determiner le power limit
        WritePlateauNamed(mvRetained, curve, Path.Combine(Root, "plateau-active.json"));
        ApplyTuning(Path.Combine(Root, "plateau-active.json"));
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl 350 | Out-Null");
        Thread.Sleep(3000);

        // ---------- 4) power limit ----------
        Ui(() => { _step.Text = "Determination du power limit..."; _bar.Value = 80; });
        var jeu = MeasureDetailed(py, 2048, 45);

        // Marge de securite : 10 % au-dessus de la consommation reelle, afin de
        // ne jamais bridler en usage reel (les charges varient), sans jamais
        // depasser la valeur d'origine du VBIOS (350 W).
        int pl = (int)(Math.Ceiling(jeu.Watts * 1.10 / 10.0) * 10);
        int plOrigin = 350;
        if (pl < 300) pl = 300;
        if (pl > plOrigin) pl = plOrigin;
        LogMsg(string.Format("package : conso jeu {0:0.0} W, marge 10% -> {1} W (plafond {2} W)",
            jeu.Watts, pl, plOrigin));
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + pl + " | Out-Null");
        LogMsg("package : consommation en jeu " + jeu.Watts.ToString("0.0") + " W -> power limit " + pl + " W");

        // ventilation : profil equilibre
        try { File.Copy(Path.Combine(Root, "profil-equilibre.txt"), Path.Combine(Root, "fan-curve.txt"), true); } catch { }

        // ---------- 5) mesure de la configuration optimisee ----------
        for (int i = 0; i < Levels.Length; i++)
        {
            int idx = i;
            Ui(() => { _step.Text = string.Format("Package - charge {0} ({1}/4)", LevelNames[idx], idx + 1); _bar.Value = 82 + idx * 4; });
            opti[LevelNames[i]] = MeasureDetailed(py, Levels[i], 60);
            LogMsg("package optimise " + LevelNames[i] + " : " + opti[LevelNames[i]].Line());
        }

        // ---------- 6) rapport ----------
        WritePackageReport(stock, opti, target, mvRetained, pl, rows, firstFail);
        Ui(() => { _bar.Value = 100; _step.Text = "Package optimal applique et verifie."; });
    }

    // ------------------------------------------------------------------
    private Measure MeasureDetailed(string py, int size, int seconds)
    {
        var m = new Measure();
        Thread load = null;

        if (py != null)
        {
            load = new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\stress-long.py\" " + seconds + " " + size)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit((seconds + 20) * 1000);
                }
                catch { }
            });
            load.IsBackground = true;
            load.Start();
        }
        else if (OpenClTest.Available)
        {
            load = new Thread(() => { OpenClTest.Run(seconds, 2048, null); });
            load.IsBackground = true;
            load.Start();
        }
        else return m;

        Thread.Sleep(Math.Max(15, seconds / 3) * 1000);

        long sumMhz = 0, sumMv = 0, sumRpm = 0, n = 0;
        double sumW = 0;
        int maxGpu = 0, maxMem = 0;
        var sw = Stopwatch.StartNew();
        int sampleWindow = Math.Max(15, seconds / 2) * 1000;

        while (sw.ElapsedMilliseconds < sampleWindow)
        {
            int mhz = SmiInt("clocks.gr");
            if (mhz > 0) { sumMhz += mhz; n++; }
            sumW += SmiDouble("power.draw");
            sumRpm += SmiInt("fan.speed");

            int g, h, mm;
            NvSensors.ReadTemps(out g, out h, out mm);
            if (g > maxGpu) maxGpu = g;
            if (mm > maxMem) maxMem = mm;

            sumMv += ReadVoltage();
            Thread.Sleep(1000);
        }

        if (n > 0)
        {
            m.Mhz = (int)(sumMhz / n);
            m.Rpm = (int)(sumRpm / n);
            m.TempGpu = maxGpu;
            m.TempMem = maxMem;
            m.Watts = sumW / n;
            m.Mv = (int)(sumMv / n);
        }
        return m;
    }

    private int ReadVoltage()
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Root, "snu.exe"), "voltage")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                var m = Regex.Match(o, @"(\d+)\s*mV");
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
        }
        catch { }
        return 0;
    }

    private static double SmiDouble(string field)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                "--query-gpu=" + field + " --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(8000);
                double v;
                if (double.TryParse(o, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            }
        }
        catch { }
        return 0;
    }

    private string RunPythonStability()
    {
        try
        {
            string py = ResolvePython();
            if (py == null) return "aucun moteur";
            string outFile = Path.Combine(Root, "package-stability.txt");
            try { File.Delete(outFile); } catch { }
            var psi = new ProcessStartInfo(py,
                "\"C:\\Nvidia\\stability.py\" " + StabilitySeconds + " \"" + outFile + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) p.WaitForExit((StabilitySeconds + 40) * 1000);
            try { return File.ReadAllText(outFile).Trim(); } catch { return "aucun resultat"; }
        }
        catch { return "erreur"; }
    }

    // ------------------------------------------------------------------
    private void WritePackageReport(Dictionary<string, Measure> stock, Dictionary<string, Measure> opti,
                                    int target, int mv, int pl, List<string> rows, int firstFail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Package optimal - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();
        sb.AppendLine("Carte : " + _s.Name);
        sb.AppendLine();
        sb.AppendLine("## Package retenu");
        sb.AppendLine();
        sb.AppendLine("| Parametre | Valeur |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| Frequence cible | **" + target + " MHz** |");
        sb.AppendLine("| Tension | **" + mv + " mV** |");
        sb.AppendLine("| Power limit | **" + pl + " W** |");
        sb.AppendLine("| Ventilation | **equilibre** |");
        sb.AppendLine();
        sb.AppendLine("## Comparatif par niveau de charge");
        sb.AppendLine();
        sb.AppendLine("| Charge | Origine | Package | Gain MHz | Gain W | Gain C |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (string lvl in LevelNames)
        {
            var a = stock.ContainsKey(lvl) ? stock[lvl] : null;
            var b = opti.ContainsKey(lvl) ? opti[lvl] : null;
            if (a == null || b == null) continue;
            sb.AppendLine(string.Format("| {0} | {1} | {2} | {3:+0;-0;0} | {4:+0.0;-0.0;0.0} | {5:+0;-0;0} |",
                Cap(lvl), a.Line(), b.Line(), b.Mhz - a.Mhz, b.Watts - a.Watts, b.TempGpu - a.TempGpu));
        }

        sb.AppendLine();
        sb.AppendLine("## Paliers de tension testes");
        sb.AppendLine();
        sb.AppendLine("| Tension | Resultat | Verdict |");
        sb.AppendLine("|---|---|---|");
        foreach (string r in rows) sb.AppendLine(r);
        if (firstFail > 0) sb.AppendLine();
        if (firstFail > 0) sb.AppendLine("Premier palier instable : " + firstFail + " mV");

        try
        {
            string path = Path.Combine(Root, "package-optimal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md");
            File.WriteAllText(path, sb.ToString());
            LogMsg("rapport package : " + path);
        }
        catch (Exception e) { LogMsg("WritePackageReport " + e.Message); }
    }
}
