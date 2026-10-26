// =====================================================================
//  report.cs  -  rapport comparatif HTML avant/apres
// =====================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    public void RefreshReportTab()
    {
        try
        {
            if (_reportView == null) return;
            _reportView.DocumentText = BuildComparativeHtml();
        }
        catch (Exception e) { LogMsg("RefreshReportTab : " + e.Message); }
    }

    public string GenerateReport()
    {
        return BuildComparativeReport();
    }

    // ------------------------------------------------------------------
    //  Comparaison origine / reglages appliques, lancee par l'utilisateur
    // ------------------------------------------------------------------
    private void CompareButton()
    {
        if (_busy) return;
        _busy = true;
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;
        _step.Text = "Comparaison : preparation (~15 min)";

        var th = new Thread(() =>
        {
            try { RunComparison(); }
            catch (Exception e)
            {
                LogMsg("RunComparison " + e);
                Ui(() => { _step.Text = "Echec de la comparaison : " + e.Message; });
            }
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

    private void RunComparison()
    {
        string py = ResolvePython();
        if (py == null)
        {
            LogMsg("comparatif : PyTorch requis");
            Ui(() => { _step.Text = "Comparaison impossible : PyTorch est requis."; });
            return;
        }

        string[] levels = { "legere", "jeu", "soutenue", "lourde" };
        int[] sizes = { 1024, 2048, 4096, 8192 };
        var stock = new Dictionary<string, Measure>();
        var opti = new Dictionary<string, Measure>();

        int currentPl = (int)Math.Round(_s.PowerLimit);
        int total = levels.Length * 2 * 110;
        var sw = Stopwatch.StartNew();

        // 1) configuration d'origine
        UiProgress("Origine : restauration de la configuration d'usine", 2, total / 60);
        Ps("& '" + Path.Combine(Root, "snu.exe") + "' clear | Out-Null");
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + PlDefault + " | Out-Null");
        Thread.Sleep(3000);

        for (int i = 0; i < levels.Length; i++)
        {
            int idx = i;
            int remaining = Math.Max(1, (total - (int)sw.Elapsed.TotalSeconds) / 60);
            UiProgress(string.Format("Origine - charge {0} ({1}/4)", Cap(levels[idx]), idx + 1),
                2 + idx * 8, remaining);
            stock[levels[i]] = MeasureDetailed(py, sizes[i], 60);
            LogMsg("comparatif origine " + levels[i] + " : " + stock[levels[i]].Line());
        }

        // 2) reglages appliques
        UiProgress("Application des reglages optimises", 36, Math.Max(1, (total - (int)sw.Elapsed.TotalSeconds) / 60));
        try
        {
            string plateau = Path.Combine(Root, "plateau-active.json");
            if (File.Exists(plateau)) ApplyTuning(plateau);
        }
        catch { }
        if (currentPl > 0)
            Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + currentPl + " | Out-Null");
        Thread.Sleep(3000);

        for (int i = 0; i < levels.Length; i++)
        {
            int idx = i;
            int remaining = Math.Max(1, (total - (int)sw.Elapsed.TotalSeconds) / 60);
            UiProgress(string.Format("Applique - charge {0} ({1}/4)", Cap(levels[idx]), idx + 1),
                38 + idx * 8, remaining);
            opti[levels[i]] = MeasureDetailed(py, sizes[i], 60);
            LogMsg("comparatif applique " + levels[i] + " : " + opti[levels[i]].Line());
        }

        // 3) rapport HTML affiche dans la fenetre
        UiProgress("Generation du rapport comparatif", 96, 1);
        string path = Path.Combine(Root, "comparatif-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");
        File.WriteAllText(path, BuildComparisonHtml(stock, opti), Encoding.UTF8);
        LogMsg("comparatif HTML : " + path);

        Ui(() =>
        {
            try { _reportView.Navigate(new Uri(path)); } catch (Exception e) { LogMsg("affichage comparatif " + e.Message); }
            _bar.Value = 100;
            _step.Text = "Comparatif termine - rapport affiche dans l'onglet Rapport.";
        });
    }

    // Choix de libelle selon la langue active.
    private static string B(string fr, string en) { return Lang.French ? fr : en; }

    private string BuildComparisonHtml(Dictionary<string, Measure> stock, Dictionary<string, Measure> opti)
    {
        string[] levels = { "legere", "jeu", "soutenue", "lourde" };
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Comparatif origine / applique</title><style>");
        sb.AppendLine("body{background:#181818;color:#ddd;font-family:Segoe UI,sans-serif;margin:24px}");
        sb.AppendLine("h1{color:#76b900;font-size:22px;margin-bottom:4px}");
        sb.AppendLine("h2{color:#9ad32a;font-size:16px;margin-top:24px;border-bottom:1px solid #333;padding-bottom:4px}");
        sb.AppendLine("table{border-collapse:collapse;margin-top:10px;min-width:520px}");
        sb.AppendLine("th,td{padding:6px 12px;border-bottom:1px solid #2c2c2c;text-align:left}");
        sb.AppendLine("th{color:#aaa;font-weight:600}");
        sb.AppendLine("td.num{text-align:right;font-variant-numeric:tabular-nums}");
        sb.AppendLine(".gain{color:#76b900;font-weight:600}.loss{color:#e06060;font-weight:600}");
        sb.AppendLine(".meta{color:#888;font-size:12px}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>" + B("Comparatif origine / reglages appliques", "Stock / applied comparison") + "</h1>");
        sb.AppendLine("<div class=\"meta\">" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " &middot; " +
                      Esc(_s.Name) + " &middot; pilote " + Esc(_s.Driver) + "</div>");

        sb.AppendLine("<h2>" + B("Detail par niveau de charge", "Detail by load level") + "</h2>");
        sb.AppendLine("<table><tr><th>" + B("Charge", "Load") + "</th><th>" + B("Origine", "Stock") + "</th><th>" + B("Applique", "Applied") + "</th>" +
                      "<th>" + B("Gain MHz", "Gain MHz") + "</th><th>" + B("Gain W", "Gain W") + "</th><th>" + B("Gain C", "Gain C") + "</th></tr>");
        foreach (string lvl in levels)
        {
            if (!stock.ContainsKey(lvl) || !opti.ContainsKey(lvl)) continue;
            var a = stock[lvl];
            var b = opti[lvl];
            int dm = b.Mhz - a.Mhz;
            double dw = b.Watts - a.Watts;
            int dc = b.TempGpu - a.TempGpu;
            sb.AppendLine("<tr><td>" + LevelName(lvl) + "</td><td class=\"num\">" + FmtMeasure(a) +
                          "</td><td class=\"num\">" + FmtMeasure(b) + "</td>" +
                          "<td class=\"num " + Cls(-dm) + "\">" + Signed(dm) + " MHz</td>" +
                          "<td class=\"num " + Cls(dw < 0 ? -1 : (dw > 0 ? 1 : 0)) + "\">" + Signed(dw, 1) + " W</td>" +
                          "<td class=\"num " + Cls(dc) + "\">" + Signed(dc) + " C</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>" + B("Configuration appliquee", "Applied configuration") + "</h2><table>");
        sb.AppendLine("<tr><th>" + B("Parametre", "Parameter") + "</th><th>" + B("Valeur", "Value") + "</th></tr>");
        sb.AppendLine("<tr><td>Undervolt</td><td>" + Esc(_s.Undervolt) + "</td></tr>");
        sb.AppendLine("<tr><td>" + B("Tension du plateau", "Plateau voltage") + "</td><td>" + Esc(_s.VoltPlateau) + "</td></tr>");
        sb.AppendLine("<tr><td>Power limit</td><td>" + _s.PowerLimit.ToString("0") + " W</td></tr>");
        sb.AppendLine("<tr><td>" + B("Profil de ventilation", "Fan profile") + "</td><td>" + Esc(_s.Profile) + "</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<p class=\"meta\">" + B("Charges PyTorch de 60 s par niveau. Les gains positifs (vert) sont favorables a la configuration appliquee.",
                      "60 s PyTorch loads per level. Positive gains (green) favour the applied configuration.") + "</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string LevelName(string lvl)
    {
        if (Lang.French) return lvl.Substring(0, 1).ToUpperInvariant() + lvl.Substring(1);
        switch (lvl)
        {
            case "legere": return "Light";
            case "jeu": return "Gaming";
            case "soutenue": return "Sustained";
            case "lourde": return "Heavy";
            default: return lvl;
        }
    }

    private static string FmtMeasure(Measure m)
    {
        return string.Format("{0} MHz &middot; {1:0.0} W &middot; {2} C", m.Mhz, m.Watts, m.TempGpu);
    }

    private static string Signed(int v) { return (v > 0 ? "+" : "") + v; }
    private static string Signed(double v, int d) { return (v > 0 ? "+" : "") + v.ToString("0." + new string('0', d)); }
    private static string Cls(int v) { return v < 0 ? "gain" : (v > 0 ? "loss" : ""); }

    private void OpenComparativeReport()
    {
        try
        {
            string html = BuildComparativeReport();
            Process.Start(new ProcessStartInfo(html) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show(Lang.T("Impossible d'ouvrir le rapport : ") + e.Message, Lang.T("Rapport"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildComparativeReport()
    {
        string path = Path.Combine(Root, "rapport-comparatif-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");
        File.WriteAllText(path, BuildComparativeHtml(), Encoding.UTF8);
        LogMsg("rapport HTML ecrit : " + path);
        return path;
    }

    private string BuildComparativeHtml()
    {
        // --- donnees de frequences ---
        var stock = new System.Collections.Generic.Dictionary<string, int>();
        var opti = new System.Collections.Generic.Dictionary<string, int>();
        string date = "";

        string fp = Path.Combine(Root, "freq-profile.json");
        if (File.Exists(fp))
        {
            string s = File.ReadAllText(fp);
            var md = Regex.Match(s, "\"date\"\\s*:\\s*\"([^\"]+)\"");
            if (md.Success) date = md.Groups[1].Value;
            foreach (string lvl in new[] { "legere", "jeu", "soutenue", "lourde" })
            {
                var a = Regex.Match(s, "\"stock\"\\s*:\\s*\\{[^}]*\"" + lvl + "\"\\s*:\\s*(\\d+)");
                var b = Regex.Match(s, "\"optimise\"\\s*:\\s*\\{[^}]*\"" + lvl + "\"\\s*:\\s*(\\d+)");
                if (a.Success) stock[lvl] = int.Parse(a.Groups[1].Value);
                if (b.Success) opti[lvl] = int.Parse(b.Groups[1].Value);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Rapport NVIDIA Configuration</title><style>");
        sb.AppendLine("body{background:#181818;color:#ddd;font-family:Segoe UI,sans-serif;margin:24px}");
        sb.AppendLine("h1{color:#76b900;font-size:22px;margin-bottom:4px}");
        sb.AppendLine("h2{color:#9ad32a;font-size:16px;margin-top:26px;border-bottom:1px solid #333;padding-bottom:4px}");
        sb.AppendLine("table{border-collapse:collapse;margin-top:10px;min-width:420px}");
        sb.AppendLine("th,td{padding:6px 12px;border-bottom:1px solid #2c2c2c;text-align:left}");
        sb.AppendLine("th{color:#aaa;font-weight:600}");
        sb.AppendLine("td.num{text-align:right;font-variant-numeric:tabular-nums}");
        sb.AppendLine(".gain{color:#76b900;font-weight:600}.loss{color:#e06060;font-weight:600}");
        sb.AppendLine(".meta{color:#888;font-size:12px}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>NVIDIA Configuration - rapport comparatif</h1>");
        sb.AppendLine("<div class=\"meta\">" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                      " &middot; " + _s.Name + " &middot; pilote " + _s.Driver + "</div>");

        sb.AppendLine("<h2>" + B("Configuration appliquee", "Applied configuration") + "</h2><table>");
        sb.AppendLine("<tr><th>" + B("Parametre", "Parameter") + "</th><th>" + B("Valeur", "Value") + "</th></tr>");
        sb.AppendLine("<tr><td>Undervolt</td><td>" + Esc(_s.Undervolt) + "</td></tr>");
        sb.AppendLine("<tr><td>" + B("Tension du plateau", "Plateau voltage") + "</td><td>" + Esc(_s.VoltPlateau) + "</td></tr>");
        sb.AppendLine("<tr><td>Power limit</td><td>" + _s.PowerLimit.ToString("0") + " W</td></tr>");
        sb.AppendLine("<tr><td>" + B("Profil de ventilation", "Fan profile") + "</td><td>" + Esc(_s.Profile) + "</td></tr>");
        sb.AppendLine("<tr><td>Moteur de test</td><td>" + Esc(_s.Torch) + " (PyTorch) / OpenCL integre</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Frequences moyennes par charge</h2>");
        if (stock.Count == 0 && opti.Count == 0)
        {
            sb.AppendLine("<p>Aucune mesure disponible. Utilisez « Mesurer les frequences ».</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Charge</th><th>Origine</th><th>Optimise</th><th>Gain</th></tr>");
            foreach (string lvl in new[] { "legere", "jeu", "soutenue", "lourde" })
            {
                int a = stock.ContainsKey(lvl) ? stock[lvl] : 0;
                int b = opti.ContainsKey(lvl) ? opti[lvl] : 0;
                int d = b - a;
                string cls = d > 0 ? "gain" : (d < 0 ? "loss" : "");
                string sign = d > 0 ? "+" : "";
                sb.AppendLine("<tr><td>" + Cap(lvl) + "</td><td class=\"num\">" +
                              (a > 0 ? a + " MHz" : "-") + "</td><td class=\"num\">" +
                              (b > 0 ? b + " MHz" : "-") + "</td><td class=\"num " + cls + "\">" +
                              (a > 0 && b > 0 ? sign + d + " MHz" : "-") + "</td></tr>");
            }
            sb.AppendLine("</table>");
            if (!string.IsNullOrEmpty(date))
                sb.AppendLine("<div class=\"meta\">Mesures du " + Esc(date) + "</div>");
        }

        // --- dernier rapport d'optimisation (paliers) ---
        try
        {
            string dir = Root;
            var reports = Directory.GetFiles(dir, "rapport-*.md");
            Array.Sort(reports);
            if (reports.Length > 0)
            {
                string last = reports[reports.Length - 1];
                string txt = File.ReadAllText(last);
                sb.AppendLine("<h2>Paliers de tension testes</h2>");
                sb.AppendLine("<pre style=\"background:#111;padding:10px;color:#bbb;overflow:auto\">" +
                              Esc(txt) + "</pre>");
            }
        }
        catch { }

        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
