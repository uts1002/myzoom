using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MyZoom.Core;

/// <summary>
/// exe 하나로 설치·제거를 한다 (관리자 권한 없음, 이 사용자에게만).
///   MyZoom.exe --install    → %LOCALAPPDATA%\Programs\MyZoom 에 복사, 바로가기·자동 실행·'설치된 앱' 등록 후 실행
///   MyZoom.exe --uninstall  → 위의 것을 모두 지움 ('설치된 앱'에서 제거를 누르면 이게 실행됨)
/// </summary>
public static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MyZoom";

    static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MyZoom.lnk");
    static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MyZoom.lnk");

    public static void Install(bool quiet)
    {
        string src = AppInfo.CurrentExe;
        string dest = AppInfo.InstalledExe;
        try
        {
            if (!AppInfo.IsInstalledCopy)
            {
                StopInstalledInstances();
                Directory.CreateDirectory(AppInfo.InstallDir);
                CopyWithRetry(src, dest);
            }
            CreateShortcut(StartMenuShortcut, dest);
            CreateShortcut(DesktopShortcut, dest);
            StartupRegistration.Set(true, dest);
            RegisterAppEntry(dest);

            Process.Start(new ProcessStartInfo(dest, "--installed") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            if (!quiet)
                MessageBox.Show("설치하지 못했습니다.\n\n" + ex.Message, "MyZoom", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void Uninstall()
    {
        if (MessageBox.Show("MyZoom 을 이 PC에서 지울까요?", "MyZoom 제거",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool removeSettings = MessageBox.Show("설정(단축키·색 등)도 같이 지울까요?\n'아니요'를 누르면 다시 설치할 때 그대로 씁니다.",
            "MyZoom 제거", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        StopInstalledInstances();
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        try { StartupRegistration.Set(false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        if (removeSettings)
        {
            try { Directory.Delete(AppSettings.Folder, true); } catch { }
        }

        // 실행 중인 자기 자신은 지울 수 없으므로, 끝난 뒤 cmd 가 폴더를 지운다
        if (Directory.Exists(AppInfo.InstallDir))
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{AppInfo.InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        MessageBox.Show("MyZoom 을 지웠습니다.", "MyZoom 제거", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Windows '설정 > 앱 > 설치된 앱' 목록에 올린다. 켤 때마다 불러서 버전을 맞춘다.</summary>
    public static void RegisterAppEntry(string exe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "MyZoom");
            key.SetValue("DisplayVersion", AppInfo.VersionText);
            key.SetValue("DisplayIcon", exe + ",0");
            key.SetValue("Publisher", $"{AppInfo.CreditName} ({AppInfo.CreditUrl.Replace("https://", "")})");
            key.SetValue("URLInfoAbout", AppInfo.CreditUrl);
            key.SetValue("URLUpdateInfo", AppInfo.ReleasesUrl);
            key.SetValue("InstallLocation", Path.GetDirectoryName(exe)!);
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (File.Exists(exe))
                key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>설치 폴더의 MyZoom 이 켜져 있으면 끈다 (덮어쓰기·제거 전에).</summary>
    static void StopInstalledInstances()
    {
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("MyZoom"))
        {
            try
            {
                if (p.Id == me) continue;
                string? path = p.MainModule?.FileName;
                if (path != null && !string.Equals(path, AppInfo.InstalledExe, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill();
                p.WaitForExit(5000);
            }
            catch { }
        }
    }

    static void CopyWithRetry(string src, string dest)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                File.Copy(src, dest, true);
                return;
            }
            catch (IOException) when (i < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    static void CreateShortcut(string path, string target)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.IconLocation = target + ",0";
            lnk.Description = "MyZoom - 화면 확대·판서";
            lnk.Save();
        }
        catch { }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
