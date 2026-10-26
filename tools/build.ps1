# =====================================================================
#  build.ps1  -  recompilation de nvidiaconf.exe et nvidiaconf.msi
#
#  Sources      : C:\Nvidia (installation de reference)
#  Atelier      : D:\opencode\Nvidia\build
#  WiX          : D:\opencode\Nvidia\Nvidia_old\tools\wix
#  Sortie       : D:\opencode\Nvidia\build\nvidiaconf.msi
#
#  C:\Nvidia n'est jamais modifie par ce script (lecture seule).
# =====================================================================
param(
    [string]$Version = '1.0.16'
)

$ErrorActionPreference = 'Stop'

$src     = 'C:\Nvidia'
$work    = 'D:\opencode\Nvidia\build'
$stage   = Join-Path $work 'src'
$wix     = 'D:\opencode\Nvidia\Nvidia_old\tools\wix'
$csc     = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$gen     = 'D:\opencode\Nvidia\gen-wxs.py'
$python  = 'C:\Users\iramone\AppData\Local\Programs\Python\Python310\python.exe'

Write-Host "=== 1) preparation de l'atelier ==="
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "=== 2) copie des sources ==="
# 2a) restauration complete (sources + ressources)
Copy-Item (Join-Path 'D:\opencode\Nvidia\Nvidia_old' '*') $stage -Recurse -Force
# 2b) C:\Nvidia n'est PAS recopie : c'est l'installation, souvent plus
#     ancienne que les sources de reference (Nvidia_old).
(Get-ChildItem $stage -File | Measure-Object).Count.ToString() + ' fichiers copies'

Write-Host "=== 2bis) generation de la version ==="
$verFile = Join-Path $stage 'VersionInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyTitle("NVIDIA Configuration")]
[assembly: AssemblyCompany("iramone")]
[assembly: AssemblyProduct("nvidiaconf")]
"@ | Set-Content -Path $verFile -Encoding UTF8
'  ' + $verFile

Write-Host "=== 3) recompilation des binaires ==="
Push-Location $stage
& $csc /nologo /target:exe /platform:x64 /out:nvfan.exe /r:System.ServiceProcess.dll nvfan.cs
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "echec compilation nvfan.exe" }
& $csc /nologo /target:winexe /platform:x64 /out:nvidiaconf.exe /win32icon:icon.ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll `
    nvidiaconf.cs autotune.cs compat.cs freq.cs sensors.cs lang.cs OpenClTest.cs report.cs package.cs VersionInfo.cs
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "echec compilation nvidiaconf.exe" }
Pop-Location
'  binaires recompiles'

Write-Host "=== 3bis) signature Authenticode ==="
$cert = $null
try {
    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like '*NVIDIA Configuration*' } | Select-Object -First 1
    if (-not $cert) {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like '*NVIDIA Configuration*' } | Select-Object -First 1
    }
    if (-not $cert) {
        try {
            $cert = New-SelfSignedCertificate -Type CodeSigningCert `
                -Subject 'CN=NVIDIA Configuration (auto-signe)' `
                -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(5)
        }
        catch {
            $cert = New-SelfSignedCertificate -Type CodeSigningCert `
                -Subject 'CN=NVIDIA Configuration (auto-signe)' `
                -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
        }
        '  certificat auto-signe cree'
    }
    Export-Certificate -Cert $cert -FilePath (Join-Path $work 'nvidiaconf-selfsigned.cer') -Force | Out-Null
    foreach ($f in @('nvidiaconf.exe', 'nvfan.exe')) {
        $p = Join-Path $stage $f
        if (Test-Path $p) {
            $sig = Set-AuthenticodeSignature -FilePath $p -Certificate $cert
            '  ' + $f + ' : ' + $sig.Status
        }
    }
}
catch { '  signature ignoree : ' + $_.Exception.Message }

Write-Host "=== 4) generation du fichier WiX ==="
& $python $gen -Src $stage -Out (Join-Path $work 'nvidiaconf.wxs') -Version $Version
if ($LASTEXITCODE -ne 0) { throw "echec generation du .wxs" }

Write-Host "=== 5) compilation du MSI ==="
Push-Location $work
& (Join-Path $wix 'candle.exe') -nologo nvidiaconf.wxs
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "echec candle" }
& (Join-Path $wix 'light.exe') -nologo -sice:ICE99 -out (Join-Path $work 'nvidiaconf.msi') nvidiaconf.wixobj
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "echec light" }
Pop-Location

''
Write-Host "=== 6) resultat ==="
Get-ChildItem $work -Filter '*.msi' | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Get-FileHash (Join-Path $work 'nvidiaconf.msi') -Algorithm SHA256 | Select-Object -ExpandProperty Hash
