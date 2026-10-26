import os, uuid, sys, argparse

parser = argparse.ArgumentParser()
parser.add_argument("-Src", default=r"C:\Nvidia")
parser.add_argument("-Out", default="nvidiaconf.wxs")
parser.add_argument("-Version", default="1.0.16")
a = parser.parse_args()
SRC = a.Src
OUT = a.Out

files = [
    ("nvidiaconf.exe", "AppExe"),
    ("nvfan.exe", "FanExe"),
    ("snu.exe", "UndervoltExe"),
    ("icon.ico", "AppIcon"),
    ("apply-undervolt.ps1", "ApplyScript"),
    ("install-task.ps1", "TaskScript"),
    ("install-fan-service.ps1", "ServiceScript"),
    ("uninstall-fan-service.ps1", "UninstallServiceScript"),
    ("set-fan-profile.ps1", "ProfileScript"),
    ("verify.ps1", "VerifyScript"),
    ("verify-fan.ps1", "VerifyFanScript"),
    ("rollback.ps1", "RollbackScript"),
    ("plateau-1845-831.json", "CurveActive"),
    ("plateau-1860.json", "Curve850"),
    ("courbe-stock.json", "CurveStock"),
    ("fan-curve.txt", "FanCurve"),
    ("profil-silence.txt", "ProfileSilence"),
    ("profil-equilibre.txt", "ProfileEquilibre"),
    ("profil-froid.txt", "ProfileFroid"),
    ("autotune.cs", "AutoTuneSource"),
    ("compat.cs", "CompatSource"),
    ("freq.cs", "FreqSource"),
    ("OpenClTest.cs", "OpenClSource"),
    ("sensors.cs", "SensorsSource"),
    ("report.cs", "ReportSource"),
    ("stability.py", "StabilityScript"),
    ("unlock-bench.py", "UnlockBenchScript"),
    ("stress-long.py", "StressLong"),
    ("plateau-active.json", "CurveNow"),
    ("README.txt", "Readme"),
    ("setup-choice.ps1", "SetupChoice"),
    ("install-conf-task.ps1", "ConfTask"),
    ("remove-conf-task.ps1", "RemoveConfTask"),
    ("restore-stock.ps1", "RestoreStockScript"),
    ("autotune.ini", "AutoTuneIni"),
]

upgrade = "8F3A2C41-5B7E-4D19-9C2A-6E4B1D8F7A30"

parts = []
parts.append('<?xml version="1.0" encoding="UTF-8"?>')
parts.append('<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">')
parts.append('  <Product Id="*" Name="NVIDIA Configuration" Language="1036" Version="%s.0"' % a.Version)
parts.append('           Manufacturer="iramone" UpgradeCode="%s">' % upgrade)
parts.append('    <Package InstallerVersion="500" Compressed="yes" InstallScope="perMachine" Platform="x64" />')
parts.append('    <MajorUpgrade DowngradeErrorMessage="Une version plus recente est deja installee." />')
parts.append('    <MediaTemplate EmbedCab="yes" />')
parts.append('')
parts.append('    <Property Id="NVLDDMKMINSTALLED">')
parts.append('      <RegistrySearch Id="SearchNvlddmkm" Root="HKLM" Key="SYSTEM\\CurrentControlSet\\Services\\nvlddmkm" Name="ImagePath" Type="raw" />')
parts.append('    </Property>')

parts.append('    <CustomAction Id="ChoiceInstall" Directory="NVIDIA_DIR"')
parts.append('                  ExeCommand="[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File &quot;[NVIDIA_DIR]setup-choice.ps1&quot; -Mode install -UiLevel [UILevel]"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="ignore" />')
parts.append('    <CustomAction Id="ChoiceUninstall" Directory="NVIDIA_DIR"')
parts.append('                  ExeCommand="[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File &quot;[NVIDIA_DIR]setup-choice.ps1&quot; -Mode uninstall -UiLevel [UILevel]"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="ignore" />')
parts.append('    <CustomAction Id="InstallTask" Directory="NVIDIA_DIR"')
parts.append('                  ExeCommand="[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy Bypass -File &quot;[NVIDIA_DIR]install-conf-task.ps1&quot;"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="ignore" />')
parts.append('    <CustomAction Id="RestoreStock" Directory="NVIDIA_DIR"')
parts.append('                  ExeCommand="[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy Bypass -File &quot;[NVIDIA_DIR]remove-conf-task.ps1&quot;"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="ignore" />')
parts.append('    <CustomAction Id="KillApp" Directory="NVIDIA_DIR" ExeCommand="[SystemFolder]cmd.exe /c taskkill /IM nvidiaconf.exe /F &amp; sc stop NvidiaFanCurve &amp; taskkill /IM nvfan.exe /F &amp; exit 0"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="ignore" />')
parts.append('    <CustomAction Id="LaunchApp" FileKey="fil_AppExe" ExeCommand="--tray"')
parts.append('                  Execute="immediate" Impersonate="yes" Return="asyncNoWait" />')
parts.append('    <InstallExecuteSequence>')
parts.append('      <Custom Action="KillApp" Before="InstallValidate">1</Custom>')
parts.append('      <Custom Action="ChoiceInstall" After="InstallFiles">NOT Installed</Custom>')
parts.append('      <Custom Action="InstallTask" After="ChoiceInstall">NOT Installed</Custom>')
parts.append('      <Custom Action="ChoiceUninstall" Before="RemoveFiles">Installed</Custom>')
parts.append('      <Custom Action="RestoreStock" After="ChoiceUninstall">Installed</Custom>')
parts.append('      <Custom Action="LaunchApp" After="InstallFinalize">NOT Installed</Custom>')
parts.append('    </InstallExecuteSequence>')
parts.append('    <Condition Message="NVIDIA Configuration necessite un pilote NVIDIA compatible (NVAPI) et une carte dont les tensions, frequences, power limit et ventilateurs sont pilotables. Installation annulee.">')
parts.append('      <![CDATA[NVLDDMKMINSTALLED]]>')
parts.append('    </Condition>')
parts.append('')
parts.append('    <Directory Id="TARGETDIR" Name="SourceDir">')
parts.append('      <Directory Id="WindowsVolume">')
parts.append('        <Directory Id="NVIDIA_DIR" Name="Nvidia">')

config_ids = set()   # plus de NeverOverwrite : le MSI installe les defauts,
                     # puis ChoiceInstall restaure la sauvegarde si elle existe

comp_refs = []
for i, (fn, cid) in enumerate(files):
    comp = "cmp_%s" % cid
    fileid = "fil_%s" % cid
    comp_refs.append(comp)
    nw = ' NeverOverwrite="yes"' if cid in config_ids else ''
    parts.append('          <Component Id="%s" Guid="*" Win64="yes"%s>' % (comp, nw))
    if fn == "nvidiaconf.exe":
        parts.append('            <File Id="%s" Source="%s\\%s" KeyPath="yes">' % (fileid, SRC, fn))
        parts.append('            </File>')
        parts.append('            <RemoveRegistryValue Root="HKLM" Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run" Name="NvidiaConf" />')
        parts.append('            <RegistryValue Root="HKLM" Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run"')
        parts.append('                           Name="NvidiaConf" Type="string"')
        parts.append('                           Value="&quot;[NVIDIA_DIR]nvidiaconf.exe&quot; --tray" />')
    else:
        parts.append('            <File Id="%s" Source="%s\\%s" KeyPath="yes" />' % (fileid, SRC, fn))
    parts.append('          </Component>')

parts.append('        </Directory>')
parts.append('      </Directory>')


parts.append('    </Directory>')
parts.append('')
parts.append('    <Feature Id="MainFeature" Title="NVIDIA Configuration" Level="1">')
for c in comp_refs:
    parts.append('      <ComponentRef Id="%s" />' % c)

parts.append('    </Feature>')
parts.append('  </Product>')
parts.append('</Wix>')

open(OUT, "w", encoding="utf-8").write("\n".join(parts))
print("ecrit", OUT, "fichiers:", len(files), "upgrade:", upgrade)
