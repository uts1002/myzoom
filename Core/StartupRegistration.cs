using Microsoft.Win32;

namespace MyZoom.Core;

/// <summary>Windows 시작 시 자동 실행 (HKCU\...\Run). 관리자 권한 불필요.</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "MyZoom";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else if (key.GetValue(Name) != null)
            key.DeleteValue(Name);
    }
}
