// =====================================================================
//  compat.cs  -  detection de compatibilite de la carte
//
//  Une carte est consideree compatible si elle expose :
//    - NVAPI (pilote NVIDIA)
//    - au moins un ventilateur pilotable (ClientFanCoolers)
//    - la courbe tension/frequence (snu.exe repond)
//    - une plage de power limit modifiable (min < max)
// =====================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

internal sealed class CompatResult
{
    public bool NvApi;
    public bool Fans;
    public bool Curve;
    public bool PowerLimit;
    public string GpuName = "inconnu";
    public int FansCount;
    public int PowerMin, PowerMax;
    public string Reason = "";

    public bool Ok
    {
        get { return NvApi && Fans && Curve && PowerLimit; }
    }

    public string Summary
    {
        get
        {
            var s = new System.Text.StringBuilder();
            s.AppendLine("Carte : " + GpuName);
            s.AppendLine("NVAPI                     : " + (NvApi ? "OK" : "indisponible"));
            s.AppendLine("Ventilateurs pilotables   : " + (Fans ? "OK (" + FansCount + " canaux)" : "non exposes"));
            s.AppendLine("Courbe tension/frequence  : " + (Curve ? "OK" : "non accessible"));
            s.AppendLine("Power limit modifiable    : " + (PowerLimit ? "OK (" + PowerMin + "-" + PowerMax + " W)" : "plage non modifiable"));
            if (!string.IsNullOrEmpty(Reason)) s.AppendLine();
            if (!string.IsNullOrEmpty(Reason)) s.AppendLine("Cause : " + Reason);
            return s.ToString();
        }
    }
}

internal static class Compatibility
{
    public static CompatResult Check(string root)
    {
        var r = new CompatResult();

        // --- NVAPI + nom de la carte ---
        try
        {
            r.NvApi = NvSensors.Init();
            if (r.NvApi) r.GpuName = NvSensors.GpuName();
        }
        catch { r.NvApi = false; }

        // --- ventilateurs ---
        try
        {
            int rpm1, pct1, rpm2, pct2;
            NvSensors.ReadFans(out rpm1, out pct1, out rpm2, out pct2);
            if (rpm1 >= 0) { r.Fans = true; r.FansCount = 1; }
            if (rpm2 >= 0) { r.Fans = true; r.FansCount = 2; }
        }
        catch { }

        // --- courbe V/F : snu doit repondre ---
        try
        {
            string snu = Path.Combine(root, "snu.exe");
            if (File.Exists(snu))
            {
                var psi = new ProcessStartInfo(snu, "status")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    var so = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                    else
                    {
                        string o = so.Wait(2000) ? so.Result : "";
                        r.Curve = o.Contains("NVIDIA") && o.Contains("Core curve offset");
                    }
                }
            }
        }
        catch { }

        // --- power limit ---
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "-q -d POWER")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return r; }
                string o = so.Wait(2000) ? so.Result : "";
                var mn = Regex.Match(o, @"Min Power Limit\s*:\s*([\d.]+)");
                var mx = Regex.Match(o, @"Max Power Limit\s*:\s*([\d.]+)");
                if (mn.Success && mx.Success)
                {
                    r.PowerMin = (int)double.Parse(mn.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    r.PowerMax = (int)double.Parse(mx.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    r.PowerLimit = r.PowerMax > r.PowerMin;
                }
            }
        }
        catch { }

        if (!r.NvApi) r.Reason = "aucun pilote NVIDIA / NVAPI detecte.";
        else if (!r.Fans) r.Reason = "les ventilateurs ne sont pas exposes par le pilote (carte non supportee ou verrouillee par le constructeur).";
        else if (!r.Curve) r.Reason = "la courbe tension/frequence n'est pas accessible (snu.exe absent ou driver non compatible).";
        else if (!r.PowerLimit) r.Reason = "la plage de power limit n'est pas modifiable.";

        return r;
    }
}
