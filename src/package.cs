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
        // suspendre le rafraichissement de l'interface : ses appels NVAPI
        // concurrents provoquaient un interblocage avec ceux de la mesure.
        if (_timer != null) _timer.Stop();
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
                    if (_timer != null) _timer.Start();
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
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + PlDefault + " | Out-Null");
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

        // Marge de securite sur la tension. Un test court ne peut pas detecter
        // une instabilite tres rare (2 ecarts sur 28 000 iterations mesurees
        // manuellement) : on applique donc 3 % de marge, soit ~4 pas de 6,25 mV.
        int margin = (int)Math.Ceiling(lastOk * 0.03 / 6.25) * 6;
        int safe = lastOk + Math.Max(6, margin);
        int hi = 0;
        foreach (int c in cands) if (c > hi) hi = c;
        if (safe > hi) safe = hi;

        // La courbe n'accepte que des paliers de 6,25 mV : on arrondit au
        // palier superieur ou egal, jamais en dessous de la marge.
        int rounded = hi;
        foreach (int c in cands) if (c >= safe && c < rounded) rounded = c;
        if (rounded > mvRetained) mvRetained = rounded;
        LogMsg(string.Format("package : derniere tension stable {0} mV -> marge 3 % -> {1} mV", lastOk, mvRetained));
        ClearMarker();
        LogMsg("package : tension retenue " + mvRetained + " mV");

        // application provisoire pour determiner le power limit
        WritePlateauNamed(mvRetained, curve, Path.Combine(Root, "plateau-active.json"));
        ApplyTuning(Path.Combine(Root, "plateau-active.json"));
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + PlDefault + " | Out-Null");
        Thread.Sleep(3000);

        // ---------- 4) power limit ----------
        Ui(() => { _step.Text = "Determination du power limit..."; _bar.Value = 80; });
        var jeu = MeasureDetailed(py, 2048, 45);

        // Marge de securite : +10 W au-dessus de la consommation reelle en jeu,
        // arrondie au pas de 10 W. Exemple valide : 309 W -> 320 W.
        // Plafond : valeur d'origine du VBIOS, lue sur la carte (pas de valeur
        // codee en dur : chaque modele a ses propres limites).
        int pl = (int)(Math.Ceiling((jeu.Watts + 10) / 10.0) * 10);
        int plOrigin = PlDefault;
        if (pl < 300) pl = 300;
        if (pl > plOrigin) pl = plOrigin;
        LogMsg(string.Format("package : conso jeu {0:0.0} W, marge +10 W -> {1} W (plafond carte {2} W)",
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

        // Le power limit provisoire vient d'une mesure courte ; on le recale sur
        // la consommation relevee pendant la mesure finale (plus longue) +10 W.
        if (opti.ContainsKey("jeu"))
        {
            int plFinal = (int)(Math.Ceiling((opti["jeu"].Watts + 10) / 10.0) * 10);
            if (plFinal < PlMin) plFinal = PlMin;
            if (plFinal > plOrigin) plFinal = plOrigin;
            if (plFinal != pl)
            {
                Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + plFinal + " | Out-Null");
                LogMsg("package : power limit recale sur la mesure finale " +
                       opti["jeu"].Watts.ToString("0.0") + " W -> " + plFinal + " W");
                pl = plFinal;
            }
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
        Process loadProc = null;

        if (py != null)
        {
            load = new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\stress-long.py\" " + seconds + " " + size)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi))
                    {
                        loadProc = p;
                        p.WaitForExit((seconds + 20) * 1000);
                    }
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

        LogMsg("mesure : charge " + size + " lancee (" + seconds + " s)");
        Thread.Sleep(Math.Max(15, seconds / 3) * 1000);

        long sumMhz = 0, sumMv = 0, sumRpm = 0, n = 0;
        double sumW = 0;
        int maxGpu = 0, maxMem = 0;
        string lastStep = "demarrage";
        object stateLock = new object();

        var sampler = new Thread(() =>
        {
            var sw = Stopwatch.StartNew();
            int sampleWindow = Math.Max(15, seconds / 2) * 1000;
            int k = 0;
            while (sw.ElapsedMilliseconds < sampleWindow)
            {
                k++;
                lastStep = "clocks.gr";
                int mhz = SmiInt("clocks.gr");
                lastStep = "power.draw";
                double w = SmiDouble("power.draw");
                lastStep = "fan.speed";
                int rpm = SmiInt("fan.speed");
                lastStep = "nvapi temperatures";
                int g, h, mm;
                NvSensors.ReadTemps(out g, out h, out mm);
                lastStep = "snu voltage";
                int mv = ReadVoltage();
                lastStep = "attente";

                lock (stateLock)
                {
                    if (mhz > 0) { sumMhz += mhz; n++; }
                    sumW += w;
                    sumRpm += rpm;
                    sumMv += mv;
                    if (g > maxGpu) maxGpu = g;
                    if (mm > maxMem) maxMem = mm;
                }
                LogMsg(string.Format("  echantillon {0} : {1} MHz / {2:0.0} W / {3} mV / {4} C",
                    k, mhz, w, mv, g));
                Thread.Sleep(1000);
            }
        });
        sampler.IsBackground = true;
        sampler.Start();

        // attente bornee : meme si un appel externe ne rend pas la main, le
        // package poursuit avec les mesures deja collectees.
        int budget = (seconds + 90) * 1000;
        if (!sampler.Join(budget))
        {
            LogMsg("mesure : echantillonnage bloque a l'etape '" + lastStep +
                   "' apres " + (budget / 1000) + " s - poursuite sans attendre");
        }

        try { if (loadProc != null && !loadProc.HasExited) loadProc.Kill(); } catch { }

        lock (stateLock)
        {
            if (n > 0)
            {
                m.Mhz = (int)(sumMhz / n);
                m.Rpm = (int)(sumRpm / n);
                m.TempGpu = maxGpu;
                m.TempMem = maxMem;
                m.Watts = sumW / n;
                m.Mv = (int)(sumMv / n);
            }
        }
        LogMsg("mesure : charge " + size + " terminee, " + n + " echantillons");
        return m;
    }

    private int ReadVoltage()
    {
        try
        {
            string o = RunCapture(Path.Combine(Root, "snu.exe"), "voltage", 6000);
            var m = Regex.Match(o, @"(\d+)\s*mV");
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        catch { }
        return 0;
    }

    private static double SmiDouble(string field)
    {
        try
        {
            string o = RunCapture(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                "--query-gpu=" + field + " --format=csv,noheader,nounits", 8000).Trim();
            double v;
            if (double.TryParse(o, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
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
                Cap(lvl), a.Line().Replace("|", " · "), b.Line().Replace("|", " · "),
                b.Mhz - a.Mhz, b.Watts - a.Watts, b.TempGpu - a.TempGpu));
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
