// =====================================================================
//  report.cs  -  rapport comparatif HTML avant/apres
// =====================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    private void OpenComparativeReport()
    {
        try
        {
            string html = BuildComparativeReport();
            Process.Start(new ProcessStartInfo(html) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show("Impossible d'ouvrir le rapport : " + e.Message, "Rapport",
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

        sb.AppendLine("<h2>Configuration appliquee</h2><table>");
        sb.AppendLine("<tr><th>Parametre</th><th>Valeur</th></tr>");
        sb.AppendLine("<tr><td>Undervolt</td><td>" + Esc(_s.Undervolt) + "</td></tr>");
        sb.AppendLine("<tr><td>Tension du plateau</td><td>" + Esc(_s.VoltPlateau) + "</td></tr>");
        sb.AppendLine("<tr><td>Power limit</td><td>" + _s.PowerLimit.ToString("0") + " W</td></tr>");
        sb.AppendLine("<tr><td>Profil de ventilation</td><td>" + Esc(_s.Profile) + "</td></tr>");
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
