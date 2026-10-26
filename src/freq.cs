// =====================================================================
//  freq.cs  -  mesure des frequences moyennes par niveau de charge
//  (classe partielle de MainForm)
//
//  Quatre niveaux : legere (1024) / jeu (2048) / soutenue (4096) /
//  calcul lourd (8192). Mesure en configuration d'origine puis en
//  configuration optimisee, et ecrit freq-profile.json.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed partial class MainForm
{
    private static readonly int[] Levels = { 1024, 2048, 4096, 8192 };
    private static readonly string[] LevelNames = { "legere", "jeu", "soutenue", "lourde" };

    public void CliFrequencyTest()
    {
        try { DoMeasureFrequencies(); }
        catch (Exception e) { LogMsg("CliFrequencyTest " + e.Message); }
    }

    private void MeasureFrequencies()
    {
        if (_busy) return;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;

        var th = new Thread(() =>
        {
            try { DoMeasureFrequencies(); }
            catch (Exception e) { LogMsg("FREQ FATAL " + e); Ui(() => { _step.Text = "Echec : " + e.Message; }); }
            finally
            {
                Ui(() =>
                {
                    _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = true;
                    _busy = false;
                });
                Refresh(true);
            }
        });
        th.IsBackground = true;
        th.Start();
    }

    private void DoMeasureFrequencies()
    {
        string py = ResolvePython();
        if (py == null) { Ui(() => { _step.Text = "Python/PyTorch introuvable."; }); return; }

        var stock = new Dictionary<string, int>();
        var opti = new Dictionary<string, int>();
        int step = 0, total = 8;
        LogMsg("debut mesure des frequences (8 etapes, ~1 min chacune)");

        // --- 1) configuration optimisee (etat courant) ---
        for (int i = 0; i < Levels.Length; i++)
        {
            int idx = i;
            step++;
            Ui(() =>
            {
                _step.Text = string.Format("Frequence optimisee - charge {0} ({1}/8) - 1 min", LevelNames[idx], step);
                _bar.Value = (int)(100.0 * (step - 1) / total);
            });
            opti[LevelNames[i]] = MeasureLevel(py, Levels[i]);
            LogMsg("freq optimisee " + LevelNames[i] + " = " + opti[LevelNames[i]] + " MHz");
        }

        // --- 2) configuration d'origine ---
        Ps("& '" + Path.Combine(Root, "snu.exe") + "' clear | Out-Null");
        Ps("& nvidia-smi -pl " + PlDefault + " | Out-Null");

        for (int i = 0; i < Levels.Length; i++)
        {
            int idx = i;
            step++;
            Ui(() =>
            {
                _step.Text = string.Format("Frequence d'origine - charge {0} ({1}/8) - 1 min", LevelNames[idx], step);
                _bar.Value = (int)(100.0 * (step - 1) / total);
            });
            stock[LevelNames[i]] = MeasureLevel(py, Levels[i]);
            LogMsg("freq origine " + LevelNames[i] + " = " + stock[LevelNames[i]] + " MHz");
        }

        WriteFreqProfile(stock, opti);

        // --- 3) restaurer la configuration optimisee ---
        Ui(() => { _step.Text = "Restauration de la configuration optimisee..."; });
        ApplyTuning(Path.Combine(Root, "plateau-active.json"));
        Ps("& nvidia-smi -pl 320 | Out-Null");

        Ui(() => { _bar.Value = 100; _step.Text = "Frequences mesurees et enregistrees."; });
    }

    private int MeasureLevel(string py, int size)
    {
        // Charge representative : PyTorch (matmul dense) est plus severe
        // qu'OpenCL et evite de mesurer une carte non bridee.
        Thread load = null;
        if (py != null)
        {
            load = new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\stress-long.py\" 65 " + size)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit(75000);
                }
                catch { }
            });
            load.IsBackground = true;
            load.Start();
        }
        else if (OpenClTest.Available)
        {
            load = new Thread(() => { OpenClTest.Run(65, 2048, null); });
            load.IsBackground = true;
            load.Start();
        }
        else if (py != null)
        {
            load = new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\stress-long.py\" 65 " + size)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit(75000);
                }
                catch { }
            });
            load.IsBackground = true;
            load.Start();
        }
        else return 0;

        Thread.Sleep(22000);   // montee en regime

        long sum = 0; int n = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 40000)
        {
            int mhz = SmiInt("clocks.gr");
            if (mhz > 0) { sum += mhz; n++; }
            Thread.Sleep(1000);
        }
        return n > 0 ? (int)(sum / n) : 0;
    }

    /// <summary>Execute un programme et capture sa sortie avec un delai maximal.
    /// La lecture asynchrone evite tout blocage si le processus ne rend pas la main
    /// (cas observe avec nvidia-smi sous forte charge).</summary>
    private static string RunCapture(string exe, string args, int timeoutMs)
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
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    LogMsg("RunCapture timeout : " + Path.GetFileName(exe) + " " + args);
                    return "";
                }
                return outTask.Wait(3000) ? outTask.Result : "";
            }
        }
        catch (Exception ex) { LogMsg("RunCapture " + ex.Message); return ""; }
    }

    // ------------------------------------------------------------------
    //  Limites de puissance de la carte (aucune valeur codee en dur)
    // ------------------------------------------------------------------
    private static bool _plRead;
    private static int _plMin = 100, _plMax = 350, _plDef = 350;

    private static void ReadPowerLimits()
    {
        if (_plRead) return;
        _plRead = true;
        try
        {
            string o = RunCapture(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                "--query-gpu=power.min_limit,power.max_limit,power.default_limit --format=csv,noheader,nounits", 8000);
            var parts = o.Trim().Split(',');
            double a, b, c;
            if (parts.Length >= 3 &&
                double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out b) &&
                double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out c))
            {
                _plMin = (int)Math.Ceiling(a);
                _plMax = (int)Math.Floor(b);
                _plDef = (int)Math.Floor(c);
            }
        }
        catch { }
        if (_plDef <= 0) _plDef = _plMax > 0 ? _plMax : 350;
        if (_plMax <= 0) _plMax = _plDef;
        if (_plMin <= 0) _plMin = 100;
        LogMsg(string.Format("limites carte : power limit {0}..{1} W (defaut {2} W)", _plMin, _plMax, _plDef));
    }

    // Valeur d'origine du VBIOS : sert de plafond et de valeur de restauration.
    internal static int DefaultPowerLimit { get { ReadPowerLimits(); return _plDef; } }
    private static int PlDefault { get { ReadPowerLimits(); return _plDef; } }
    private static int PlMax { get { ReadPowerLimits(); return _plMax; } }
    private static int PlMin { get { ReadPowerLimits(); return _plMin; } }

    private static int SmiInt(string field)
    {
        try
        {
            string smiPath = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            string o = RunCapture(smiPath,
                "--query-gpu=" + field + " --format=csv,noheader,nounits", 8000).Trim();
            int v;
            if (int.TryParse(o, out v)) return v;
        }
        catch (Exception ex) { LogMsg("SmiInt " + field + " : " + ex.Message); }
        return 0;
    }

    private void WriteFreqProfile(Dictionary<string, int> stock, Dictionary<string, int> opti)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"date\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\",");
            sb.AppendLine("  \"stock\": {");
            for (int i = 0; i < LevelNames.Length; i++)
                sb.AppendLine("    \"" + LevelNames[i] + "\": " + stock[LevelNames[i]] + (i < LevelNames.Length - 1 ? "," : ""));
            sb.AppendLine("  },");
            sb.AppendLine("  \"optimise\": {");
            for (int i = 0; i < LevelNames.Length; i++)
                sb.AppendLine("    \"" + LevelNames[i] + "\": " + opti[LevelNames[i]] + (i < LevelNames.Length - 1 ? "," : ""));
            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(Root, "freq-profile.json"), sb.ToString());
            LogMsg("freq-profile.json ecrit");
        }
        catch (Exception e) { LogMsg("WriteFreqProfile " + e.Message); }
    }

    private void ReadFreqProfile()
    {
        try
        {
            string p = Path.Combine(Root, "freq-profile.json");
            if (!File.Exists(p)) return;
            string s = File.ReadAllText(p);

            foreach (string lvl in LevelNames)
            {
                var ms = Regex.Match(s, "\"stock\"\\s*:\\s*\\{[^}]*\"" + lvl + "\"\\s*:\\s*(\\d+)");
                var mo = Regex.Match(s, "\"optimise\"\\s*:\\s*\\{[^}]*\"" + lvl + "\"\\s*:\\s*(\\d+)");
                Set(Cap(lvl) + " - origine", ms.Success ? ms.Groups[1].Value + " MHz" : "-");
                Set(Cap(lvl) + " - optimise", mo.Success ? mo.Groups[1].Value + " MHz" : "-");
            }
        }
        catch { }
    }

    private static string Cap(string s)
    {
        return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
