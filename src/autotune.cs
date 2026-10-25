// =====================================================================
//  autotune.cs  -  recherche automatique de la configuration optimale
//  (classe partielle de MainForm)
//
//  Principe :
//   - paliers de tension lus dans la courbe stock (pas de valeur inventee)
//   - frequence cible fixe (1845 MHz)
//   - pour chaque palier, test de stabilite avec verification bit a bit
//   - arret au premier echec, puis marge de securite d'un palier
//   - rapport Markdown horodate
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
    private int TargetMhz = 1845;
    private const int StabilitySeconds = 240;

    private bool _autoTuneRequested;

    private void ReadAutoTuneConfig()
    {
        try
        {
            string p = Path.Combine(Root, "autotune.ini");
            if (!File.Exists(p)) return;
            foreach (string line in File.ReadAllLines(p))
            {
                string l = line.Trim();
                if (l.StartsWith("#") || !l.Contains("=")) continue;
                string[] kv = l.Split(new[] { '=' }, 2);
                string k = kv[0].Trim().ToLowerInvariant();
                string v = kv[1].Trim();
                int n;
                if (!int.TryParse(v, out n)) continue;
                if (k == "target_mhz" && n > 0) TargetMhz = n;
            }
            LogMsg("autotune cible : " + TargetMhz + " MHz");
        }
        catch { }
    }

    private static string ResolvePython()
    {
        var candidates = new List<string>();
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(local, "Programs", "Python");
            if (Directory.Exists(dir))
            {
                foreach (string d in Directory.GetDirectories(dir, "Python*"))
                {
                    if (d.Contains("-32")) continue;
                    string exe = Path.Combine(d, "python.exe");
                    if (File.Exists(exe)) candidates.Add(exe);
                }
            }
        }
        catch { }

        foreach (string p in new[]
        {
            @"C:\\Python313\\python.exe", @"C:\\Python312\\python.exe",
            @"C:\\Python311\\python.exe", @"C:\\Python310\\python.exe"
        })
            if (File.Exists(p)) candidates.Add(p);

        candidates.Add("python.exe");

        foreach (string c in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(c, "-c \"import torch;print(torch.__version__)\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);
                    if (p.ExitCode == 0 && o.Trim().Length > 0) return c;
                }
            }
            catch { }
        }
        return null;
    }

    private static void LogMsg(string msg)
    {
        try
        {
            File.AppendAllText(@"C:\Nvidia\nvidiaconf.log",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    private void AutoTune()
    {
        if (_busy) return;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;
        _step.Text = "Preparation de la recherche...";

        var th = new Thread(() =>
        {
            try { DoAutoTune(); }
            catch (Exception e)
            {
                LogMsg("AUTOTUNE FATAL " + e);
                Ui(() => { _step.Text = "Echec : " + e.Message; });
            }
            finally
            {
                Ui(() =>
                {
                    _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = true;
                    _autoTuneRequested = false;
                    _busy = false;
                });
            }
        });
        th.IsBackground = true;
        th.Start();
    }

    // ------------------------------------------------------------------
    private void DoAutoTune()
    {
        ReadAutoTuneConfig();
        var stock = ReadStockCurve();
        if (stock.Count == 0)
        {
            Ui(() => { _step.Text = "Courbe de reference introuvable."; });
            return;
        }

        var cands = new List<int>();
        foreach (var kv in stock)
            if (kv.Key >= 806 && kv.Key <= 843) cands.Add(kv.Key);
        cands.Sort();
        cands.Reverse();

        if (cands.Count == 0)
        {
            Ui(() => { _step.Text = "Aucun palier de tension utilisable."; });
            return;
        }

        var lines = new List<string>();
        int lastOk = -1;
        int firstFail = -1;

        for (int i = 0; i < cands.Count; i++)
        {
            int mv = cands[i];
            int idx = i;
            Ui(() =>
            {
                _step.Text = string.Format("Test de stabilite : {0} mV ({1}/{2}) - {3} min",
                    mv, idx + 1, cands.Count, Math.Max(1, StabilitySeconds / 60));
                _bar.Value = (int)(90.0 * idx / cands.Count);
            });

            WriteMarker(mv);
            WritePlateau(mv, stock);
            ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

            string res = RunStability();
            int ecarts = ParseEcarts(res);

            lines.Add(string.Format("| {0} | {1} | {2} |", mv, res,
                ecarts == 0 ? "stable" : "**echec**"));

            if (ecarts == 0) { lastOk = mv; }
            else
            {
                firstFail = mv;
                if (res.Contains("CRASH PILOTE"))
                {
                    LogMsg("arret de l'optimisation : crash du pilote a " + mv + " mV");
                    EmergencyRestore();
                    ClearMarker();
                    Ui(() => { _step.Text = "Test interrompu : instabilite detectee a " + mv + " mV. Configuration d'origine restauree."; });
                    return;
                }
                break;
            }
        }

        // marge de securite : on retient le palier au-dessus du dernier stable
        int retained = lastOk;
        int okIndex = cands.IndexOf(lastOk);
        if (okIndex > 0) retained = cands[okIndex - 1];

        Ui(() =>
        {
            _bar.Value = 94;
            _step.Text = string.Format("Configuration retenue : {0} mV / {1} MHz", retained, TargetMhz);
        });

        ClearMarker();
        ApplyFinal(retained, stock);
        WriteReport(lines, retained, firstFail);
        Ui(() => { _bar.Value = 100; _step.Text = "Termine : " + retained + " mV / " + TargetMhz + " MHz"; });
    }

    // ------------------------------------------------------------------
    private Dictionary<int, int> ReadStockCurve()
    {
        var d = new Dictionary<int, int>();
        try
        {
            string p = Path.Combine(Root, "courbe-stock.json");
            if (!File.Exists(p)) return d;
            string s = File.ReadAllText(p);
            foreach (Match m in Regex.Matches(s, "\"mv\"\\s*:\\s*(\\d+)\\s*,\\s*\"mhz\"\\s*:\\s*(\\d+)"))
                d[int.Parse(m.Groups[1].Value)] = int.Parse(m.Groups[2].Value);
        }
        catch (Exception e) { LogMsg("ReadStock " + e.Message); }
        return d;
    }

    private void WritePlateau(int mv, Dictionary<int, int> stock)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"type\": \"tuning\",");
        sb.AppendLine("  \"formatVersion\": 1,");
        sb.AppendLine("  \"appVersion\": \"1.0.9+beace06d9\",");
        sb.AppendLine("  \"memoryOffset\": 0,");
        sb.AppendLine("  \"curve\": [");
        var keys = new List<int>(stock.Keys);
        keys.Sort();
        var entries = new List<string>();
        foreach (int k in keys)
            if (k >= mv)
                entries.Add(string.Format("    {{ \"mv\": {0}, \"offset\": {1}, \"mhz\": {2} }}",
                    k, TargetMhz - stock[k], TargetMhz));
        sb.AppendLine(string.Join(",\r\n", entries));
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(Root, "plateau-autotune.json"), sb.ToString());
    }

    private void ApplyTuning(string file)
    {
        RunExe(Path.Combine(Root, "snu.exe"),
            "--in-tuning-file \"" + file + "\" --no-persist", 60000);
    }

    private string RunStability()
    {
        // PyTorch offre la charge la plus severe (~300 W) : priorite.
        // OpenCL sert de repli quand Python/PyTorch n'est pas disponible.
        string py0 = ResolvePython();
        if (py0 == null)
        {
            if (OpenClTest.Available)
            {
                LogMsg("python absent : test via noyau OpenCL integre");
                string r = OpenClTest.Run(StabilitySeconds, 2048, m => LogMsg(m));
                LogMsg("resultat OpenCL : " + r);
                return r;
            }
            LogMsg("aucun moteur de test disponible");
            return "aucun moteur";
        }

        string outFile = Path.Combine(Root, "autotune-stability.txt");
        try { File.Delete(outFile); } catch { }

        string py = ResolvePython();
        LogMsg("python utilise : " + py);
        if (py == null) { LogMsg("python introuvable"); return "python introuvable"; }

        var psi = new ProcessStartInfo(py,
            "\"C:\\Nvidia\\stability.py\" " + StabilitySeconds + " \"" + outFile + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };

        Process p;
        try { p = Process.Start(psi); }
        catch (Exception e) { LogMsg("RunStability start : " + e.Message); return "demarrage impossible"; }

        var sw = Stopwatch.StartNew();
        bool driverLost = false;
        int echecs = 0;

        while (!p.HasExited && sw.Elapsed.TotalSeconds < StabilitySeconds + 90)
        {
            Thread.Sleep(3000);
            int t = SmiInt("temperature.gpu");
            // un echec isole peut venir de la charge : on tolere 3 echecs
            // consecutifs (9 s) avant de conclure a un probleme de pilote.
            if (t <= 0) echecs++;
            else echecs = 0;

            if (echecs >= 3)
            {
                driverLost = true;
                LogMsg("pilote injoignable 3 fois de suite");
                break;
            }
            if (t > 90)
            {
                driverLost = true;
                LogMsg("temperature excessive : " + t + " C");
                break;
            }
        }

        bool timedOut = !p.HasExited;
        try { if (!p.HasExited) p.Kill(); } catch { }

        if (driverLost || timedOut)
        {
            LogMsg("INCIDENT PILOTE detecte pendant le test (driverLost=" + driverLost + ", timeout=" + timedOut + ")");
            EmergencyRestore();
            return driverLost ? "CRASH PILOTE" : "test interrompu";
        }

        try { return File.ReadAllText(outFile).Trim(); }
        catch { return "aucun resultat"; }
    }

    /// <summary>Retour immediat a la configuration d'origine.</summary>
    private void EmergencyRestore()
    {
        try
        {
            Ps("& '" + Path.Combine(Root, "snu.exe") + "' clear | Out-Null");
            Ps("& nvidia-smi -pl 350 | Out-Null");
            Ps("& '" + Path.Combine(Root, "nvfan.exe") + "' auto | Out-Null");
            LogMsg("restauration d'urgence effectuee (stock, 350 W, ventilateurs VBIOS)");
        }
        catch (Exception e) { LogMsg("EmergencyRestore " + e.Message); }
    }

    private void WriteMarker(int mv)
    {
        try
        {
            File.WriteAllText(Path.Combine(Root, "autotune-inprogress.json"),
                "{\"mv\":" + mv + ",\"time\":\"" + DateTime.Now.ToString("s") + "\"}");
        }
        catch { }
    }

    private void ClearMarker()
    {
        try { File.Delete(Path.Combine(Root, "autotune-inprogress.json")); } catch { }
    }

    public static bool ConsumeCrashMarker(string root)
    {
        try
        {
            string p = Path.Combine(root, "autotune-inprogress.json");
            if (!File.Exists(p)) return false;
            File.Delete(p);
            return true;
        }
        catch { return false; }
    }

    private int ParseEcarts(string res)
    {
        var m = Regex.Match(res, @"ecarts=(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    private void ApplyFinal(int mv, Dictionary<int, int> stock)
    {
        // la courbe retenue devient la courbe active
        WritePlateauNamed(mv, stock, Path.Combine(Root, "plateau-active.json"));
        ApplyTuning(Path.Combine(Root, "plateau-active.json"));
        Ps("& nvidia-smi -pl 320 | Out-Null");
    }

    private void WritePlateauNamed(int mv, Dictionary<int, int> stock, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"type\": \"tuning\",");
        sb.AppendLine("  \"formatVersion\": 1,");
        sb.AppendLine("  \"appVersion\": \"1.0.9+beace06d9\",");
        sb.AppendLine("  \"memoryOffset\": 0,");
        sb.AppendLine("  \"curve\": [");
        var keys = new List<int>(stock.Keys);
        keys.Sort();
        var entries = new List<string>();
        foreach (int k in keys)
            if (k >= mv)
                entries.Add(string.Format("    {{ \"mv\": {0}, \"offset\": {1}, \"mhz\": {2} }}",
                    k, TargetMhz - stock[k], TargetMhz));
        sb.AppendLine(string.Join(",\r\n", entries));
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString());
    }

    private void WriteReport(List<string> lines, int retained, int failed)
    {
        try
        {
            string path = Path.Combine(Root, "rapport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md");
            var sb = new StringBuilder();
            sb.AppendLine("# Rapport d'optimisation - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine("Carte : " + _s.Name);
            sb.AppendLine();
            sb.AppendLine("Frequence cible : " + TargetMhz + " MHz");
            sb.AppendLine("Duree du test par palier : " + StabilitySeconds + " s");
            sb.AppendLine();
            sb.AppendLine("| Tension | Resultat | Verdict |");
            sb.AppendLine("|---|---|---|");
            foreach (string l in lines) sb.AppendLine(l);
            sb.AppendLine();
            sb.AppendLine("Palier retenu (avec marge) : **" + retained + " mV**");
            if (failed > 0) sb.AppendLine("Premier palier instable : " + failed + " mV");
            sb.AppendLine();
            sb.AppendLine("Power limit applique : 320 W");
            File.WriteAllText(path, sb.ToString());
            LogMsg("rapport ecrit : " + path);
        }
        catch (Exception e) { LogMsg("WriteReport " + e.Message); }
    }

    // ------------------------------------------------------------------
    private static void RunExe(string exe, string args, int timeoutMs)
    {
        RunExe(exe, args, timeoutMs, null);
    }

    private static void RunExe(string exe, string args, int timeoutMs, string redirect)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect != null,
                RedirectStandardError = false
            };
            using (var p = Process.Start(psi))
            {
                if (redirect != null)
                {
                    string o = p.StandardOutput.ReadToEnd();
                    File.WriteAllText(redirect, o);
                }
                p.WaitForExit(timeoutMs);
            }
        }
        catch (Exception e) { LogMsg("RunExe " + exe + " : " + e.Message); }
    }
}
