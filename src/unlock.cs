// =====================================================================
//  unlock.cs - onglet GPU-Unlock
//  Activation / desactivation reelle du chargeur UEFI (port Windows de
//  gpu-unlock), verification des prerequis, benchmark FP16/BF16/TF32 et
//  calcul du gain observe.
// =====================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private const string UnlockRepo = "https://github.com/Zagitta/gpu-unlock";
    private const string UnlockWinRepo = "https://github.com/impacifist/Windows-GPU-UNLOCK";

    private sealed class UnlockInfo
    {
        public bool Enabled;          // activation demandee
        public bool Pending;          // activation en attente de redemarrage
        public string PortPath = "";  // dossier du port Windows (Headless.ps1)
        public double RefFp16, RefBf16, RefTf32;
        public double LastFp16, LastBf16, LastTf32;
        public string LastDate = "";
    }

    private UnlockInfo _unlock = new UnlockInfo();
    private Label _unlockState;
    private Label _unlockPrereq;
    private Label _unlockGain;
    private Button _unlockBtnOn;
    private Button _unlockBtnOff;

    private string UnlockFile { get { return Path.Combine(Root, "gpu-unlock.txt"); } }

    private string UnlockArchitecture()
    {
        string n = (_s.Name ?? "").ToUpperInvariant();
        if (n.Contains("3080 TI") || n.Contains("3090") || n.Contains("3080") || n.Contains("3070")) return "GA102/GA104";
        if (n.Contains("3060")) return "GA104/GA106";
        if (n.Contains("2080") || n.Contains("2070") || n.Contains("2060")) return "Turing";
        if (n.Contains("4070") || n.Contains("4080") || n.Contains("4090")) return "Ada";
        return "inconnue";
    }

    // ------------------------------------------------------------------
    //  Etat persistant
    // ------------------------------------------------------------------
    private void LoadUnlock()
    {
        var info = new UnlockInfo();
        try
        {
            if (File.Exists(UnlockFile))
            {
                foreach (string line in File.ReadAllLines(UnlockFile))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim(), v = kv[1].Trim();
                    double d;
                    switch (k)
                    {
                        case "enabled": info.Enabled = v == "1"; break;
                        case "pending": info.Pending = v == "1"; break;
                        case "port": info.PortPath = v; break;
                        case "last": info.LastDate = v; break;
                        case "ref_fp16": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.RefFp16 = d; break;
                        case "ref_bf16": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.RefBf16 = d; break;
                        case "ref_tf32": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.RefTf32 = d; break;
                        case "last_fp16": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.LastFp16 = d; break;
                        case "last_bf16": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.LastBf16 = d; break;
                        case "last_tf32": if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) info.LastTf32 = d; break;
                    }
                }
            }
        }
        catch { }
        if (string.IsNullOrEmpty(info.PortPath)) info.PortPath = Path.Combine(Root, "gpu-unlock");
        _unlock = info;
    }

    private void SaveUnlock()
    {
        try
        {
            var lines = new string[]
            {
                "enabled=" + (_unlock.Enabled ? "1" : "0"),
                "pending=" + (_unlock.Pending ? "1" : "0"),
                "port=" + _unlock.PortPath,
                "last=" + _unlock.LastDate,
                "ref_fp16=" + _unlock.RefFp16.ToString("0.00", CultureInfo.InvariantCulture),
                "ref_bf16=" + _unlock.RefBf16.ToString("0.00", CultureInfo.InvariantCulture),
                "ref_tf32=" + _unlock.RefTf32.ToString("0.00", CultureInfo.InvariantCulture),
                "last_fp16=" + _unlock.LastFp16.ToString("0.00", CultureInfo.InvariantCulture),
                "last_bf16=" + _unlock.LastBf16.ToString("0.00", CultureInfo.InvariantCulture),
                "last_tf32=" + _unlock.LastTf32.ToString("0.00", CultureInfo.InvariantCulture)
            };
            File.WriteAllLines(UnlockFile, lines);
        }
        catch (Exception e) { LogMsg("gpu-unlock : sauvegarde " + e.Message); }
    }

    // ------------------------------------------------------------------
    //  Prerequis
    // ------------------------------------------------------------------
    private bool SecureBootEnabled()
    {
        try
        {
            string o = RunCapture("powershell.exe",
                "-NoProfile -Command \"try { (Confirm-SecureBootUEFI).ToString() } catch { 'inconnu' }\"", 10000).Trim();
            return o.StartsWith("True", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private string BitLockerStatus()
    {
        try
        {
            string o = RunCapture("powershell.exe",
                "-NoProfile -Command \"try { (Get-BitLockerVolume -MountPoint C:).ProtectionStatus } catch { 'inconnu' }\"", 12000).Trim();
            return string.IsNullOrEmpty(o) ? "inconnu" : o;
        }
        catch { return "inconnu"; }
    }

    private bool BootEntryPresent()
    {
        try
        {
            string o = RunCapture("bcdedit.exe", "/enum firmware", 15000);
            if (string.IsNullOrEmpty(o)) o = RunCapture("bcdedit.exe", "/enum all", 15000);
            return o.IndexOf("UNLOCK", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    private string PortScript()
    {
        string s = Path.Combine(_unlock.PortPath, "internal-boot", "Headless.ps1");
        return File.Exists(s) ? s : null;
    }

    private bool PrereqsOk(out string report)
    {
        bool secure = SecureBootEnabled();
        string bitlocker = BitLockerStatus();
        string script = PortScript();
        bool entry = BootEntryPresent();
        string arch = UnlockArchitecture();

        report =
            Ligne("Secure Boot", secure ? "ACTIF - doit etre desactive" : "desactive", !secure) +
            Ligne("BitLocker C:", bitlocker, !bitlocker.Equals("On", StringComparison.OrdinalIgnoreCase)) +
            Ligne("Port Windows", script != null ? "present" : "absent (" + _unlock.PortPath + ")", script != null) +
            Ligne("Entree de demarrage", entry ? "presente" : "absente", true) +
            Ligne("Architecture", arch, true);

        return !secure && script != null;
    }

    private static string Ligne(string label, string value, bool ok)
    {
        return (ok ? "  [OK]   " : "  [----] ") + label.PadRight(20) + ": " + value + Environment.NewLine;
    }

    // ------------------------------------------------------------------
    //  Interface
    // ------------------------------------------------------------------
    private void BuildUnlockTab(TabPage page)
    {
        LoadUnlock();
        int y = 8;

        var title = new Label
        {
            Text = "GPU-Unlock - deblocage FP/BF16 (GA102/103/104/106)",
            ForeColor = Color.FromArgb(150, 200, 60),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(570, 24),
            Location = new Point(18, y)
        };
        page.Controls.Add(title);
        y += 30;

        _unlockState = new Label
        {
            ForeColor = Color.Gainsboro,
            AutoSize = false,
            Size = new Size(570, 46),
            Location = new Point(18, y)
        };
        page.Controls.Add(_unlockState);
        y += 50;

        _unlockPrereq = new Label
        {
            ForeColor = Color.Gray,
            Font = new Font("Consolas", 8.5f),
            AutoSize = false,
            Size = new Size(570, 108),
            Location = new Point(18, y)
        };
        page.Controls.Add(_unlockPrereq);
        y += 112;

        _unlockBtnOn = MakeButton("Activer gpu-unlock", 18, y, 180);
        _unlockBtnOn.Click += (o, e) => UnlockActivate();
        page.Controls.Add(_unlockBtnOn);

        _unlockBtnOff = MakeButton("Desactiver", 206, y, 120);
        _unlockBtnOff.Click += (o, e) => UnlockDeactivate();
        page.Controls.Add(_unlockBtnOff);

        var btnBench = MakeButton("Mesurer", 334, y, 110);
        btnBench.Click += (o, e) => UnlockBenchmark(false);
        page.Controls.Add(btnBench);

        var btnRef = MakeButton("Definir reference", 452, y, 132);
        btnRef.Click += (o, e) => UnlockBenchmark(true);
        page.Controls.Add(btnRef);
        y += 40;

        var btnCheck = MakeButton("Verifier les prerequis", 18, y, 200);
        btnCheck.Click += (o, e) => { string r; PrereqsOk(out r); RefreshUnlockUi(); };
        page.Controls.Add(btnCheck);

        var btnDoc = MakeButton("Projet gpu-unlock", 226, y, 170);
        btnDoc.Click += (o, e) => Process.Start(UnlockRepo);
        page.Controls.Add(btnDoc);

        var btnWin = MakeButton("Port Windows", 404, y, 180);
        btnWin.Click += (o, e) => Process.Start(UnlockWinRepo);
        page.Controls.Add(btnWin);
        y += 40;

        var btnFolder = MakeButton("Choisir le dossier du port...", 18, y, 260);
        btnFolder.Click += (o, e) => UnlockPickPort();
        page.Controls.Add(btnFolder);
        y += 42;

        _unlockGain = new Label
        {
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 8.5f),
            AutoSize = false,
            Size = new Size(570, 150),
            Location = new Point(18, y)
        };
        page.Controls.Add(_unlockGain);

        RefreshUnlockUi();
    }

    private void RefreshUnlockUi()
    {
        if (_unlockState == null) return;

        string script = PortScript();
        string etat;
        if (_unlock.Pending)
            etat = "ACTIVE - en attente de redemarrage";
        else if (_unlock.Enabled)
            etat = "ACTIVE";
        else
            etat = "desactive";

        _unlockState.Text =
            "Etat              : " + etat + Environment.NewLine +
            "Chargeur UEFI     : " + (script != null ? "present" : "absent") +
            "   (module Linux d'origine : non applicable sous Windows)";

        string report;
        bool ok = PrereqsOk(out report);
        _unlockPrereq.Text = report;

        _unlockBtnOn.Enabled = !_unlock.Enabled;
        _unlockBtnOff.Enabled = _unlock.Enabled;

        _unlockGain.Text = BuildGainText();
        _unlockGain.ForeColor = Color.Gainsboro;
        _unlockState.ForeColor = ok ? Color.Gainsboro : Color.FromArgb(230, 170, 90);
    }

    private string BuildGainText()
    {
        string head = "Debits FP (8192^3, 5 s par test)" + Environment.NewLine +
                      "  precision   reference     actuel      gain" + Environment.NewLine;

        string body = GainLine("FP16", _unlock.RefFp16, _unlock.LastFp16) + Environment.NewLine +
                      GainLine("BF16", _unlock.RefBf16, _unlock.LastBf16) + Environment.NewLine +
                      GainLine("TF32", _unlock.RefTf32, _unlock.LastTf32) + Environment.NewLine +
                      Environment.NewLine;

        if (_unlock.RefFp16 > 0 && _unlock.LastFp16 > 0)
        {
            double g = (_unlock.LastFp16 / _unlock.RefFp16 - 1.0) * 100.0;
            if (g > 50) body += "Verdict : deblocage detecte (" + g.ToString("0") + " % en FP16).";
            else if (g > 10) body += "Verdict : gain partiel (" + g.ToString("0") + " %), a confirmer.";
            else body += "Verdict : pas de deblocage detecte.";
        }
        else
        {
            body += "Definissez une reference (carte verrouillee) puis mesurez apres activation.";
        }

        body += Environment.NewLine + "Rappel : gain public x1.88 a x1.95 ; jeux (FP32) non concernes.";
        if (_s.PowerLimit > 0)
            body += Environment.NewLine + string.Format("Power limit {0:0} W : facteur limitant possible.", _s.PowerLimit);

        return head + body;
    }

    private static string GainLine(string name, double r, double a)
    {
        if (r <= 0 || a <= 0)
            return string.Format("  {0,-10}  {1,8}  {2,8}  {3,9}", name,
                r > 0 ? r.ToString("0.0") : "-", a > 0 ? a.ToString("0.0") : "-", "-");

        double gain = (a / r - 1.0) * 100.0;
        return string.Format("  {0,-10}  {1,8:0.0}  {2,8:0.0}  {3,+8:0.0} %", name, r, a, gain);
    }

    // ------------------------------------------------------------------
    //  Activation / desactivation
    // ------------------------------------------------------------------
    private void UnlockActivate()
    {
        string report;
        bool ok = PrereqsOk(out report);
        string script = PortScript();

        if (script == null)
        {
            LogMsg("gpu-unlock : activation refusee, port Windows absent");
            MessageBox.Show(
                "Le chargeur UEFI n'est pas present.\n\n" +
                "Le projet d'origine (Zagitta/gpu-unlock) est un module noyau LINUX :\n" +
                "il ne peut pas etre active sous Windows.\n\n" +
                "Pour Windows, il faut le port UEFI experimental :\n" +
                "  " + UnlockWinRepo + "\n\n" +
                "1. cloner le depot ;\n" +
                "2. compiler localement (clang/lld 20.1.8, Python 3.9+, 5 firmwares) ;\n" +
                "3. copier le dossier dans :\n   " + _unlock.PortPath + "\n\n" +
                "L'onglet activera alors l'entree de demarrage UEFI.\n\n" +
                "Profils valides a ce jour : RTX 3060, 3070 Ti, 3090 FE, 3090 Ti.\n" +
                "Cette carte : " + (_s.Name ?? "inconnue") + ".",
                "GPU-Unlock - prerequis manquant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (SecureBootEnabled())
        {
            MessageBox.Show(
                "Secure Boot est actif.\n\nLe chargeur UEFI exige Secure Boot desactive.\n" +
                "Desactivez-le dans le BIOS, puis relancez l'activation.",
                "GPU-Unlock - Secure Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
            "Activer le deblocage GPU au prochain demarrage ?\n\n" +
            "Cette operation enregistre une entree de demarrage UEFI experimentale.\n" +
            "En cas d'echec, il faudra demarrer sur Windows Boot Manager (F8).\n\n" +
            "Carte : " + (_s.Name ?? "inconnue") + "\n" +
            "Profil valide : " + (UnlockArchitecture() == "GA102/GA104" && _s.Name != null && _s.Name.Contains("3080 Ti")
                ? "NON (RTX 3080 Ti non validee)" : "a verifier") + "\n\n" +
            "Continuer ?",
            "GPU-Unlock - activation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        LogMsg("gpu-unlock : activation demandee via " + script);
        UiProgress("Activation de gpu-unlock (entree UEFI)", 30, 1);
        try
        {
            string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -EnableAutomatic -ConfirmedSuccess";
            var psi = new ProcessStartInfo("powershell.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } }
                string outp = so.Wait(2000) ? so.Result : "";
                if (outp.Length > 0) LogMsg("gpu-unlock : " + outp.Trim().Replace("\n", " "));
            }

            _unlock.Enabled = true;
            _unlock.Pending = true;
            _unlock.LastDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveUnlock();
            LogMsg("gpu-unlock : active, redemarrage requis");

            MessageBox.Show(
                "Activation enregistree.\n\nRedemarrez pour appliquer le deblocage.\n" +
                "Au retour, lancez « Mesurer » : le gain doit apparaitre par rapport a la reference.",
                "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            LogMsg("gpu-unlock : echec activation - " + e.Message);
            MessageBox.Show("Activation impossible : " + e.Message, "GPU-Unlock",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UiProgress("Etat gpu-unlock actualise", 100, 0);
            RefreshUnlockUi();
        }
    }

    private void UnlockDeactivate()
    {
        string script = PortScript();
        if (script == null)
        {
            MessageBox.Show(
                "Aucun chargeur UEFI a desactiver.\n\n" +
                "Sous Linux : retirez gpu_unlock.targets= de la ligne de commande du noyau,\n" +
                "ou desinstallez le paquet.",
                "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
            "Desactiver le deblocage et remettre Windows en premier au demarrage ?",
            "GPU-Unlock - desactivation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        LogMsg("gpu-unlock : desactivation demandee");
        try
        {
            string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -RestoreWindowsFirst";
            var psi = new ProcessStartInfo("powershell.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } }
                string outp = so.Wait(2000) ? so.Result : "";
                if (outp.Length > 0) LogMsg("gpu-unlock : " + outp.Trim().Replace("\n", " "));
            }

            _unlock.Enabled = false;
            _unlock.Pending = false;
            SaveUnlock();
            LogMsg("gpu-unlock : desactive");

            MessageBox.Show("Desactivation enregistree. Windows redemarre normalement.",
                "GPU-Unlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            LogMsg("gpu-unlock : echec desactivation - " + e.Message);
            MessageBox.Show("Desactivation impossible : " + e.Message, "GPU-Unlock",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshUnlockUi();
        }
    }

    private void UnlockPickPort()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Dossier contenant internal-boot\\Headless.ps1";
            dlg.SelectedPath = Directory.Exists(_unlock.PortPath) ? _unlock.PortPath : Root;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _unlock.PortPath = dlg.SelectedPath;
            SaveUnlock();
            LogMsg("gpu-unlock : dossier du port = " + _unlock.PortPath);
            RefreshUnlockUi();
        }
    }

    // ------------------------------------------------------------------
    //  Points d'entree CLI
    // ------------------------------------------------------------------
    public void CliUnlockStatus()
    {
        LoadUnlock();
        string report;
        bool ok = PrereqsOk(out report);
        LogMsg("gpu-unlock etat : " + (_unlock.Enabled ? "active" : "desactive") +
               (_unlock.Pending ? " (attente redemarrage)" : ""));
        foreach (string line in report.Split('\n'))
            if (line.Trim().Length > 0) LogMsg("gpu-unlock " + line.Trim());
        LogMsg("gpu-unlock prerequis : " + (ok ? "OK" : "manquants"));
    }

    public void CliUnlockBench(bool reference)
    {
        LoadUnlock();
        string py = ResolvePython();
        if (py == null) { LogMsg("gpu-unlock : PyTorch absent"); return; }

        string outFile = Path.Combine(Root, "unlock-bench.txt");
        try { File.Delete(outFile); } catch { }
        var psi = new ProcessStartInfo(py, "\"C:\\Nvidia\\unlock-bench.py\" 8192 \"" + outFile + "\"")
        { UseShellExecute = false, CreateNoWindow = true };
        using (var p = Process.Start(psi)) p.WaitForExit(180000);

        string res = "";
        try { res = File.ReadAllText(outFile).Trim(); } catch { }
        double fp16 = 0, bf16 = 0, tf32 = 0;
        foreach (string line in res.Split('\n'))
        {
            var kv = line.Split('=');
            if (kv.Length != 2) continue;
            double v;
            if (!double.TryParse(kv[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) continue;
            if (kv[0].Trim() == "FP16") fp16 = v;
            if (kv[0].Trim() == "BF16") bf16 = v;
            if (kv[0].Trim() == "TF32") tf32 = v;
        }
        if (reference) { _unlock.RefFp16 = fp16; _unlock.RefBf16 = bf16; _unlock.RefTf32 = tf32; }
        else { _unlock.LastFp16 = fp16; _unlock.LastBf16 = bf16; _unlock.LastTf32 = tf32; }
        SaveUnlock();
        LogMsg(string.Format("gpu-unlock benchmark ({0}) : FP16={1:0.0} BF16={2:0.0} TF32={3:0.0}",
            reference ? "reference" : "actuel", fp16, bf16, tf32));
    }

    // ------------------------------------------------------------------
    //  Benchmark et gain
    // ------------------------------------------------------------------
    private void UnlockBenchmark(bool asReference)
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
        UiProgress(asReference ? "Mesure de reference (carte verrouillee)" : "Mesure des debits FP", 5, 1);

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

                double fp16 = 0, bf16 = 0, tf32 = 0;
                foreach (string line in res.Split('\n'))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    double v;
                    if (!double.TryParse(kv[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) continue;
                    if (kv[0].Trim() == "FP16") fp16 = v;
                    if (kv[0].Trim() == "BF16") bf16 = v;
                    if (kv[0].Trim() == "TF32") tf32 = v;
                }

                if (asReference)
                {
                    _unlock.RefFp16 = fp16; _unlock.RefBf16 = bf16; _unlock.RefTf32 = tf32;
                    LogMsg("gpu-unlock : reference enregistree");
                }
                else
                {
                    _unlock.LastFp16 = fp16; _unlock.LastBf16 = bf16; _unlock.LastTf32 = tf32;
                }
                SaveUnlock();

                Ui(() =>
                {
                    RefreshUnlockUi();
                    _bar.Value = 100;
                    _step.Text = asReference ? "Reference enregistree." : "Mesure terminee.";
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
}
