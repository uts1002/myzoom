using System.IO;
using System.Reflection;

namespace MyZoom.Core;

/// <summary>버전·설치 위치·만든 사람 정보.</summary>
public static class AppInfo
{
    public const string Name = "MyZoom";
    public const string Repo = "uts1002/myzoom";
    public const string RepoUrl = "https://github.com/" + Repo;
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string CreditName = "엄태식";
    public const string CreditUrl = "https://eompage.com";

    /// <summary>릴리즈 빌드는 태그 버전(예: 1.2.0)이 들어가고, 직접 빌드한 개발판은 0.0.0.</summary>
    public static Version Version { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version);

    public static string VersionText => IsDev ? "개발판" : Version.ToString(3);

    public static bool IsDev => Version == new Version(0, 0, 0);

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MyZoom");

    public static string InstalledExe => Path.Combine(InstallDir, "MyZoom.exe");

    public static string CurrentExe => Environment.ProcessPath ?? InstalledExe;

    /// <summary>지금 설치 폴더의 MyZoom 이 실행 중인지 (다운로드 폴더 등에서 바로 실행한 게 아니라).</summary>
    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>자동 업데이트 가능: 릴리즈판이고 설치 폴더에서 실행 중일 때만.</summary>
    public static bool CanSelfUpdate => !IsDev && IsInstalledCopy;

    public static Version Normalize(Version? v) =>
        v == null ? new Version(0, 0, 0) : new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
