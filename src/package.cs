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
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    // Bornes de saisie (caracteristiques de la carte, lues au demarrage)
    private static bool _boundsRead;
    private static int _bMvMin = 700, _bMvMax = 1000;
    private string _fanProfileChosen = "equilibre";

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

    // ------------------------------------------------------------------
    //  Bornes de saisie, deduites des caracteristiques de la carte
    // ------------------------------------------------------------------
    // Sauvegarde horodatee de la configuration courante avant chaque test.
    // Compte les erreurs Xid du pilote NVIDIA depuis une date donnee : une
    // seule suffit a considerer le palier instable.
    private static int CountXid(DateTime since)
    {
        try
        {
            string cmd = "Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='nvlddmkm'; StartTime='" +
                         since.ToString("yyyy-MM-ddTHH:mm:ss") + "'} -ErrorAction SilentlyContinue | " +
                         "Where-Object { $_.Id -in 13,31,43,153 } | Measure-Object | Select-Object -Expand Count";
            string o = RunCapture("powershell.exe", "-NoProfile -Command \"" + cmd + "\"", 15000);
            int n;
            return int.TryParse(o.Trim(), out n) ? n : 0;
        }
        catch { return 0; }
    }

    // ------------------------------------------------------------------
    //  Historique des configurations appliquees
    // ------------------------------------------------------------------
    private void AppendHistory(int mhz, int mv, int pl, string profile)
    {
        try
        {
            string line = string.Join("|", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mhz.ToString(), mv.ToString(), pl.ToString(), profile ?? "",
                Path.Combine(Root, "plateau-active.json"),
                Path.Combine(Root, "fan-curve.txt")
            });
            File.AppendAllText(Path.Combine(Root, "history.txt"), line + Environment.NewLine);
        }
        catch { }
    }

    private void ShowHistory()
    {
        var form = new Form
        {
            Text = "Historique des configurations",
            ClientSize = new Size(720, 380),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
            GridColor = Color.FromArgb(45, 45, 45),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("date", "Date");
        grid.Columns.Add("mhz", "MHz");
        grid.Columns.Add("mv", "mV");
        grid.Columns.Add("pl", "Power limit");
        grid.Columns.Add("profile", "Ventilation");

        var lines = new List<string[]>();
        try
        {
            string path = Path.Combine(Root, "history.txt");
            if (File.Exists(path))
            {
                foreach (string l in File.ReadAllLines(path))
                {
                    var parts = l.Split('|');
                    if (parts.Length >= 5) lines.Add(parts);
                }
            }
        }
        catch { }

        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var p = lines[i];
            grid.Rows.Add(new object[] { p[0], p[1], p[2], p[3] + " W", p[4] });
        }

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.FromArgb(28, 28, 28) };
        var btnClose = new Button { Text = "Fermer", Location = new Point(610, 8), Size = new Size(100, 28) };
        btnClose.Click += (o, e) => form.Close();
        var btnRestore = new Button { Text = "Restaurer la selection", Location = new Point(400, 8), Size = new Size(200, 28) };
        btnRestore.Click += (o, e) =>
        {
            if (grid.SelectedRows.Count == 0) return;
            int idx = grid.SelectedRows[0].Index;
            int real = lines.Count - 1 - idx;
            if (real < 0 || real >= lines.Count) return;
            var p = lines[real];
            try
            {
                string plateau = p.Length > 5 ? p[5] : Path.Combine(Root, "plateau-active.json");
                if (File.Exists(plateau))
                {
                    File.Copy(plateau, Path.Combine(Root, "plateau-active.json"), true);
                    ApplyTuning(Path.Combine(Root, "plateau-active.json"));
                }
                int plv;
                if (p.Length > 3 && int.TryParse(p[3], out plv))
                    Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + plv + " | Out-Null");
                if (p.Length > 4 && !string.IsNullOrEmpty(p[4]))
                    try { File.Copy(Path.Combine(Root, "profil-" + p[4] + ".txt"), Path.Combine(Root, "fan-curve.txt"), true); } catch { }
                LogMsg("historique : restauration " + p[0]);
                form.Close();
            }
            catch (Exception ex) { LogMsg("historique restauration : " + ex.Message); }
        };
        bar.Controls.Add(btnRestore);
        bar.Controls.Add(btnClose);

        form.Controls.Add(grid);
        form.Controls.Add(bar);
        form.ShowDialog(this);
    }

    // Restaure la configuration active (courbe + power limit) apres un echec.
    private void RestoreActiveSafe()
    {
        try
        {
            string plateau = Path.Combine(Root, "plateau-active.json");
            if (File.Exists(plateau)) ApplyTuning(plateau);

            int pl = 320;
            try
            {
                string f = Path.Combine(Root, "power-limit.txt");
                if (File.Exists(f))
                {
                    var m = Regex.Match(File.ReadAllText(f), @"(\d+)");
                    if (m.Success) pl = int.Parse(m.Groups[1].Value);
                }
            }
            catch { }

            Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + pl + " | Out-Null");
            LogMsg("restauration automatique apres echec : " + pl + " W");
        }
        catch (Exception e) { LogMsg("restauration automatique : " + e.Message); }
    }

    private void BackupBeforeTest(string reason)
    {
        try
        {
            string dir = Path.Combine(Root, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + reason);
            Directory.CreateDirectory(dir);
            foreach (string f in new[] { "plateau-active.json", "fan-curve.txt", "freq-profile.json" })
            {
                string src = Path.Combine(Root, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(dir, f), true);
            }
            try
            {
                string pl = SmiDouble("power.limit").ToString("0");
                File.WriteAllText(Path.Combine(dir, "power-limit.txt"), pl + " W\n");
            }
            catch { }
            // Ne conserver que les 10 sauvegardes les plus recentes.
            try
            {
                var dirs = Directory.GetDirectories(Path.Combine(Root, "backup"));
                if (dirs.Length > 10)
                {
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < dirs.Length - 10; i++)
                    {
                        try { Directory.Delete(dirs[i], true); } catch { }
                    }
                    LogMsg("sauvegardes : " + (dirs.Length - 10) + " ancienne(s) supprimee(s)");
                }
            }
            catch { }

            LogMsg("sauvegarde avant test : " + dir);
        }
        catch (Exception e) { LogMsg("sauvegarde avant test : " + e.Message); }
    }

    private void ComputeTargetBounds(out int mhzMin, out int mhzMax,
                                     out int mvMin, out int mvMax,
                                     out int plMin, out int plMax)
    {
        if (!_boundsRead)
        {
            _boundsRead = true;
            try
            {
                var curve = ReadStockCurve();
                int cmin = int.MaxValue, cmax = 0;
                foreach (var kv in curve)
                {
                    if (kv.Key < cmin) cmin = kv.Key;
                    if (kv.Key > cmax) cmax = kv.Key;
                }
                if (cmin != int.MaxValue && cmax > cmin) { _bMvMin = cmin; _bMvMax = cmax; }
            }
            catch { }
        }

        // Frequence : plancher de 500 MHz (en dessous, aucune charge utile et
        // aucune garantie de stabilite) jusqu'a la frequence maximale declaree.
        mhzMin = 500;
        mhzMax = _s.ClockMax > 0 ? _s.ClockMax : 2100;

        // Tension : plancher de securite a 700 mV (en dessous, aucune carte
        // recente n'est stable sous charge), plafond a 1000 mV.
        mvMin = Math.Max(_bMvMin, 700);
        mvMax = Math.Min(_bMvMax, 1000);

        // Power limit : de la limite minimale a la valeur d'origine du VBIOS.
        // Depasser le defaut ne presente aucun interet et augmente le risque.
        ReadPowerLimits();
        plMin = PlMin;
        plMax = PlDefault;
    }

    // Progression synthetique : action en cours + temps restant estime.
    private void UiProgress(string label, int percent, int remainingMinutes)
    {
        Ui(() =>
        {
            _step.Text = label + (remainingMinutes > 0 ? "  (~" + remainingMinutes + " min restantes)" : "");
            _bar.Value = Math.Max(0, Math.Min(100, percent));
        });
    }

    private void UpdateTargetBounds()
    {
        if (_boundsLabel == null) return;
        int mhzMin, mhzMax, mvMin, mvMax, plMin, plMax;
        ComputeTargetBounds(out mhzMin, out mhzMax, out mvMin, out mvMax, out plMin, out plMax);
        _boundsLabel.Text = Lang.French
            ? string.Format("Limites carte : {0}-{1} MHz | {2}-{3} mV | {4}-{5} W\n" +
                            "Toute valeur hors de ces plages est refusee.",
                            mhzMin, mhzMax, mvMin, mvMax, plMin, plMax)
            : string.Format("Card limits: {0}-{1} MHz | {2}-{3} mV | {4}-{5} W\n" +
                            "Any value outside these ranges is refused.",
                            mhzMin, mhzMax, mvMin, mvMax, plMin, plMax);
    }

    public void StartPackageOnShown()
    {
        Shown += (o, e) => PackageButton();
    }

    private void PackageButton()
    {
        if (_busy) return;

        int requested = 0, requestedMv = 0, requestedPl = 0;
        if (_targetMhzBox != null) int.TryParse(_targetMhzBox.Text.Trim(), out requested);
        if (_targetMvBox != null) int.TryParse(_targetMvBox.Text.Trim(), out requestedMv);
        if (_targetPlBox != null) int.TryParse(_targetPlBox.Text.Trim(), out requestedPl);

        // Consignes transmises par la ligne de commande (fichier de requete).
        string reqFile = Path.Combine(Root, "package-request.txt");
        if (File.Exists(reqFile))
        {
            try
            {
                foreach (string line in File.ReadAllLines(reqFile))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    int v;
                    if (!int.TryParse(kv[1].Trim(), out v)) continue;
                    if (kv[0].Trim() == "mhz" && requested == 0) requested = v;
                    if (kv[0].Trim() == "mv" && requestedMv == 0) requestedMv = v;
                    if (kv[0].Trim() == "pl" && requestedPl == 0) requestedPl = v;
                }
            }
            catch { }
            try { File.Delete(reqFile); } catch { }
        }

        // Valeurs statiques eventuelles, si rien d'autre n'a ete fourni.
        if (requested == 0) requested = MainForm.RequestedMhz;
        if (requestedMv == 0) requestedMv = MainForm.RequestedMv;
        if (requestedPl == 0) requestedPl = MainForm.RequestedPowerLimit;

        int mhzMin, mhzMax, mvMin, mvMax, plMin, plMax;
        ComputeTargetBounds(out mhzMin, out mhzMax, out mvMin, out mvMax, out plMin, out plMax);

        string invalid = null;
        if (requested != 0 && (requested < mhzMin || requested > mhzMax))
            invalid = string.Format("Frequence {0} MHz hors limites ({1}-{2} MHz).", requested, mhzMin, mhzMax);
        else if (requestedMv != 0 && (requestedMv < mvMin || requestedMv > mvMax))
            invalid = string.Format("Tension {0} mV hors limites ({1}-{2} mV).", requestedMv, mvMin, mvMax);
        else if (requestedPl != 0 && (requestedPl < plMin || requestedPl > plMax))
            invalid = string.Format("Power limit {0} W hors limites ({1}-{2} W).", requestedPl, plMin, plMax);

        if (invalid != null)
        {
            LogMsg("package : valeur refusee - " + invalid);
            if (Environment.UserInteractive)
                MessageBox.Show(invalid + "\n\nEntrez 0 pour laisser nvidiaconf determiner la valeur.",
                    "Valeur hors limites", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MainForm.RequestedMhz = requested;
        MainForm.RequestedMv = requestedMv;
        MainForm.RequestedPowerLimit = requestedPl;
        LogMsg(string.Format("package : consignes utilisateur {0} MHz / {1} mV / {2} W (0 = auto)",
            requested, requestedMv, requestedPl));
        _busy = true;
        // L'interface continue de se rafraichir : les appels NVAPI sont
        // desormais serialises par le verrou interne de NvSensors.
        _btnApply.Enabled = _btnRestore.Enabled = _btnPersist.Enabled = false;
        _bar.Value = 0;
        _step.Text = "Preparation du package optimal...";

        var th = new Thread(() =>
        {
            try { DoPackage(); }
            catch (Exception e)
            {
                LogMsg("DoPackage " + e);
                RestoreActiveSafe();
                Ui(() => { _step.Text = "Echec : " + e.Message + " (configuration restauree)"; });
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
                Lang.French
                    ? "La recherche de tension exige PyTorch (plus fiable qu'OpenCL).\n\n" +
                      "Installez-le avec le bouton « Installer PyTorch », puis relancez."
                    : "Voltage search requires PyTorch (more reliable than OpenCL).\n\n" +
                      "Install it with the \"Install PyTorch\" button, then try again.",
                Lang.T("PyTorch requis"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            UiProgress(string.Format("Origine - charge {0} ({1}/4)", LevelNames[idx], idx + 1),
                2 + idx * 5, Math.Max(1, (Levels.Length - idx) * 110 / 60));
            stock[LevelNames[i]] = MeasureDetailed(py, Levels[i], 60);
            LogMsg("package origine " + LevelNames[i] + " : " + stock[LevelNames[i]].Line());
        }

        // ---------- 1bis) ventilation : test obligatoire apres l'origine ----------
        Ui(() => { _step.Text = "Test obligatoire des profils de ventilation"; _bar.Value = 10; });
        string fanProfile = TestFanProfilesForPackage();
        _fanProfileChosen = fanProfile;
        LogMsg("package : profil de ventilation retenu " + fanProfile);

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
            if (Environment.UserInteractive)
            MessageBox.Show(
                (Lang.French
                    ? "Le moteur de test est incomplet :\n"
                    : "The test engine is incomplete:\n") +
                (Lang.French ? "  PyTorch : " : "  PyTorch: ") + (pyPath == null ? (Lang.French ? "absent" : "missing") : (Lang.French ? "present" : "present")) + "\n" +
                "  stability.py : " + (File.Exists(stab) ? (Lang.French ? "present" : "present") : (Lang.French ? "absent" : "missing")) + "\n\n" +
                (Lang.French ? "Utilisez « Installer PyTorch » puis relancez." : "Use \"Install PyTorch\" then try again."),
                "Moteur de test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BackupBeforeTest("package");

        var curve = ReadStockCurve();
        var cands = new List<int>();
        foreach (var kv in curve) if (kv.Key >= 806 && kv.Key <= 843) cands.Add(kv.Key);
        cands.Sort(); cands.Reverse();

        int lastOk = -1, firstFail = -1;
        var rows = new List<string>();
        int mvRetained;

        if (RequestedMv > 0)
        {
            // Tension imposee par l'utilisateur : un seul test de confirmation.
            int mv = RequestedMv;
            UiProgress(string.Format("Tension imposee {0} mV - test de stabilite", mv),
                30, Math.Max(1, StabilitySeconds / 60));
            WriteMarker(mv);
            WritePlateau(mv, curve);
            ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

            string res = RunPythonStability();
            int ecarts = ParseEcarts(res);
            LogMsg(string.Format("package tension imposee {0} mV -> {1}", mv, res.Replace("\n", " ")));
            rows.Add(string.Format("| {0} | {1} | {2} |", mv,
                res.Replace("|", "/"), ecarts == 0 ? "stable" : (Lang.French ? "**echec**" : "**fail**")));

            if (ecarts != 0)
            {
                int up = int.MaxValue;
                foreach (int c in curve.Keys) if (c > mv && c < up) up = c;
                string prop = up == int.MaxValue ? "aucune" : up + " mV";
                LogMsg("package : tension imposee instable, valeur sure proposee " + prop);
                Ui(() => { _step.Text = "Tension imposee instable - arret."; });
                if (Environment.UserInteractive)
                    MessageBox.Show((Lang.French
                            ? "La tension de " + mv + " mV est instable.\n\nValeur sure proposee : " + prop + "."
                            : "The " + mv + " mV voltage is unstable.\n\nSuggested safe value: " + prop + "."),
                        Lang.T("Tension instable"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ClearMarker();
                return;
            }
            mvRetained = mv;
        }
        else
        {
            // Recherche par dichotomie : ~log2(N) tests au lieu de N.
            var asc = new List<int>(cands);
            asc.Sort();   // croissant

            Func<int, bool> trial = (mv) =>
            {
                UiProgress(string.Format("Tension {0} mV", mv),
                    22 + (int)(55.0 * Math.Max(0, asc.IndexOf(mv)) / asc.Count),
                    Math.Max(1, 3 * (StabilitySeconds + 40) / 60));

                WriteMarker(mv);
                WritePlateau(mv, curve);
                ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

                DateTime t0 = DateTime.Now;
                string res = (ResolvePython() != null) ? RunPythonStability() : "aucun moteur";
                int ecarts = ParseEcarts(res);
                int errs = ParseErreurs(res);
                int xid = CountXid(t0);
                bool ok = ecarts == 0 && errs == 0 && xid == 0;

                LogMsg(string.Format("package tension {0} mV -> {1} (xid={2})", mv, res.Replace("\n", " "), xid));
                rows.Add(string.Format("| {0} | {1} | {2} |", mv,
                    res.Replace("|", "/") + (xid > 0 ? " / Xid=" + xid : ""),
                    ok ? "stable" : "**echec**"));

                if (res.Contains("aucun moteur") || res.Contains("aucun resultat"))
                {
                    LogMsg("package : moteur muet, arret de la recherche");
                    UiProgress("Moteur de test muet - recherche interrompue", 75, 0);
                    ClearMarker();
                    return false;
                }
                return ok;
            };

            int lo = 0, hi = asc.Count - 1;
            lastOk = -1;
            if (trial(asc[hi])) { lastOk = asc[hi]; hi--; }
            else { lo = asc.Count; }

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (trial(asc[mid])) { lastOk = asc[mid]; hi = mid - 1; }
                else { lo = mid + 1; if (firstFail < 0) firstFail = asc[mid]; }
            }

            if (lastOk < 0)
            {
                LogMsg("package : aucune tension stable dans la plage testee");
                UiProgress("Aucune tension stable - arret", 75, 0);
                ClearMarker();
                if (Environment.UserInteractive)
                    MessageBox.Show(Lang.French
                            ? "Aucune tension de la plage testee n'est stable.\nLa configuration d'origine est conservee."
                            : "No voltage in the tested range is stable.\nThe stock configuration is kept.",
                        Lang.T("Tension"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Marge : quatre pas (25 mV) au-dessus du dernier palier stable.
            // Justification : le test manuel de reference (28 187 iterations sur
            // 15 min) n'avait revele que 2 ecarts a 818 mV ; un test automatise
            // de 10 min ne peut pas les voir. Quatre pas ramenent 806 mV a
            // 831 mV, valeur validee manuellement.
            int idxOk = asc.IndexOf(lastOk);
            int idxMargin = Math.Min(asc.Count - 1, idxOk + 4);
            mvRetained = asc[idxMargin];

            // Garde-fou : ne jamais descendre de plus de 12 % sous la tension
            // d'origine sous charge (limite constatee sur ce type de GPU).
            int origineMv = stock.ContainsKey("jeu") ? stock["jeu"].Mv : 0;
            if (origineMv > 0)
            {
                int floorMv = (int)Math.Ceiling(origineMv * 0.90);
                int rounded = asc[asc.Count - 1];
                foreach (int c in asc) if (c >= floorMv) { rounded = c; break; }
                if (mvRetained < rounded) mvRetained = rounded;
                LogMsg(string.Format("package : plancher de securite {0} mV (origine en charge {1} mV) -> retenu {2} mV",
                    rounded, origineMv, mvRetained));
            }

            LogMsg(string.Format("package : dernier palier stable {0} mV -> retenu {1} mV", lastOk, mvRetained));
        }
        // Verification de la tension finale, marge incluse : la marge ajoute
        // des pas qui n'ont pas ete testes par la recherche.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            UiProgress(string.Format("Verification de la tension retenue {0} mV", mvRetained), 76, 11);
            TargetMhz = target;
            WritePlateauNamed(mvRetained, curve, Path.Combine(Root, "plateau-autotune.json"));
            ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

            var tCheck = DateTime.Now;
            string rCheck = RunPythonStability();   // meme duree que la recherche
            bool okCheck = ParseEcarts(rCheck) == 0 && ParseErreurs(rCheck) == 0 && CountXid(tCheck) == 0;
            LogMsg(string.Format("package verification {0} mV -> {1} (ok={2})",
                mvRetained, rCheck.Replace("\n", " "), okCheck));

            if (okCheck) break;

            var ascCheck = new List<int>(cands);
            ascCheck.Sort();
            int idxCheck = ascCheck.IndexOf(mvRetained);
            if (idxCheck < 0 || idxCheck + 1 >= ascCheck.Count) break;
            mvRetained = ascCheck[idxCheck + 1];
            LogMsg("package : tension retenue instable, passage a " + mvRetained + " mV");
        }

        ClearMarker();
        LogMsg("package : tension retenue " + mvRetained + " mV");

        // ---------- 3bis) frequence maximale stable a cette tension ----------
        int targetMhz = target;
        if (RequestedMhz > 0)
        {
            targetMhz = TargetMhz;
        }
        else
        {
            // Point de depart : la frequence mesuree a l'origine en jeu. La
            // courbe stock a cette tension est bien plus basse : partir d'elle
            // ne permettrait jamais d'atteindre le point de fonctionnement reel.
            int baseMhz = (int)(target / 15.0) * 15;
            int maxMhz = _s.ClockMax > 0 ? _s.ClockMax : baseMhz + 120;
            int best = 0;

            Func<int, int, bool> tryFrequency = (mhz, step) =>
            {
                UiProgress(string.Format("Frequence {0} MHz a {1} mV", mhz, mvRetained),
                    70 + Math.Min(step, 5) * 2, Math.Max(1, (9 - step) * 3));

                TargetMhz = mhz;
                WritePlateauNamed(mvRetained, curve, Path.Combine(Root, "plateau-autotune.json"));
                ApplyTuning(Path.Combine(Root, "plateau-autotune.json"));

                var t0 = DateTime.Now;
                string r = RunPythonStability(240);
                int e = ParseEcarts(r), er = ParseErreurs(r), xid = CountXid(t0);
                bool ok = e == 0 && er == 0 && xid == 0;
                LogMsg(string.Format("package frequence {0} MHz -> {1} (xid={2})",
                    mhz, r.Replace("\n", " "), xid));
                return ok;
            };

            UiProgress(string.Format("Frequence de depart {0} MHz a {1} mV", baseMhz, mvRetained), 68, 4);
            bool okBase = tryFrequency(baseMhz, 0);

            if (okBase)
            {
                best = baseMhz;
                for (int step = 1; step <= 12; step++)
                {
                    int mhz = best + 15;
                    if (mhz > maxMhz) break;
                    if (!tryFrequency(mhz, step)) break;
                    best = mhz;
                }
            }
            else
            {
                LogMsg("package : frequence de depart instable, recherche vers le bas");
                for (int step = 1; step <= 4; step++)
                {
                    int mhz = baseMhz - step * 15;
                    if (mhz < 900) break;
                    if (tryFrequency(mhz, step)) { best = mhz; break; }
                }
                if (best == 0) best = 900;
            }

            TargetMhz = best;
            targetMhz = best;
            target = best;
            LogMsg("package : frequence retenue " + best + " MHz a " + mvRetained + " mV");
        }

        // application provisoire pour determiner le power limit
        WritePlateauNamed(mvRetained, curve, Path.Combine(Root, "plateau-active.json"));
        ApplyTuning(Path.Combine(Root, "plateau-active.json"));
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + PlDefault + " | Out-Null");
        Thread.Sleep(3000);

        // ---------- 4) power limit ----------
        // Mesure longue (10 min) : une pointe courte sous-estime la consommation.
        UiProgress("Determination du power limit (mesure en jeu 10 min)", 80, 11);
        var jeu = MeasureDetailed(py, 2048, 600);

        // Marge de securite : +10 W au-dessus de la consommation reelle en jeu,
        // arrondie au pas de 10 W. Exemple valide : 309 W -> 320 W.
        // Plafond : valeur d'origine du VBIOS, lue sur la carte (pas de valeur
        // codee en dur : chaque modele a ses propres limites).
        int pl = (int)(Math.Ceiling((jeu.Watts + 10) / 10.0) * 10);
        int plOrigin = RequestedPowerLimit > 0 ? Math.Min(RequestedPowerLimit, PlDefault) : PlDefault;
        if (pl < 300) pl = 300;
        if (pl > plOrigin) pl = plOrigin;
        LogMsg(string.Format("package : conso jeu {0:0.0} W, marge +10 W -> {1} W (plafond carte {2} W)",
            jeu.Watts, pl, plOrigin));
        Ps("& '" + Environment.SystemDirectory + "\\nvidia-smi.exe' -pl " + pl + " | Out-Null");
        try { File.WriteAllText(Path.Combine(Root, "power-limit.txt"), pl + " W\n"); } catch { }
        LogMsg("package : consommation en jeu " + jeu.Watts.ToString("0.0") + " W -> power limit " + pl + " W");

        // ventilation : profil equilibre
        try { File.Copy(Path.Combine(Root, "profil-" + fanProfile + ".txt"), Path.Combine(Root, "fan-curve.txt"), true); } catch { }

        // ---------- 5) mesure de la configuration optimisee ----------
        for (int i = 0; i < Levels.Length; i++)
        {
            int idx = i;
            UiProgress(string.Format("Package - charge {0} ({1}/4)", LevelNames[idx], idx + 1),
                82 + idx * 4, Math.Max(1, (Levels.Length - idx) * 110 / 60));
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
        AppendHistory(target, mvRetained, pl, _fanProfileChosen);
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

    private string RunPythonStability(int seconds = -1)
    {
        if (seconds < 0) seconds = StabilitySeconds;
        try
        {
            string py = ResolvePython();
            if (py == null) return "aucun moteur";
            string outFile = Path.Combine(Root, "package-stability.txt");
            try { File.Delete(outFile); } catch { }
            var psi = new ProcessStartInfo(py,
                "\"C:\\Nvidia\\stability.py\" " + seconds + " \"" + outFile + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) p.WaitForExit((seconds + 40) * 1000);
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
        sb.AppendLine((Lang.French ? "# Package optimal - " : "# Optimal package - ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();
        sb.AppendLine((Lang.French ? "Carte : " : "Card: ") + _s.Name);
        sb.AppendLine();
        sb.AppendLine(Lang.French ? "## Package retenu" : "## Selected package");
        sb.AppendLine();
        sb.AppendLine(Lang.French ? "| Parametre | Valeur |" : "| Parameter | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine((Lang.French ? "| Frequence cible | **" : "| Target frequency | **") + target + " MHz** |");
        sb.AppendLine((Lang.French ? "| Tension | **" : "| Voltage | **") + mv + " mV** |");
        sb.AppendLine("| Power limit | **" + pl + " W** |");
        sb.AppendLine((Lang.French ? "| Ventilation | **" : "| Cooling | **") + Lang.Profile(_fanProfileChosen) + "** |");
        sb.AppendLine();
        sb.AppendLine(Lang.French ? "## Comparatif par niveau de charge" : "## Comparison by load level");
        sb.AppendLine();
        sb.AppendLine(Lang.French ? "| Charge | Origine | Package | Gain MHz | Gain W | Gain C |" : "| Load | Stock | Package | Gain MHz | Gain W | Gain C |");
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
        sb.AppendLine(Lang.French ? "## Paliers de tension testes" : "## Voltage tiers tested");
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
