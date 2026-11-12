using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace MyZoom.Core;

/// <summary>
/// GitHub 릴리즈로 자동 업데이트.
///   https://github.com/uts1002/myzoom/releases/latest/download/version.txt   최신 버전 번호
///   https://github.com/uts1002/myzoom/releases/download/v{버전}/MyZoom.exe     프로그램
/// 실행 중인 exe 는 덮어쓸 수 없지만 이름은 바꿀 수 있다 → MyZoom.exe.old 로 바꾸고 새 파일을 그 자리에 둔 뒤 다시 시작.
/// </summary>
public static class Updater
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MyZoom-Updater");
        return c;
    }

    static string WorkDir => Path.Combine(Path.GetTempPath(), "MyZoom-update");

    public static async Task<Version?> GetLatestVersionAsync()
    {
        string text = await Http.GetStringAsync($"https://github.com/{AppInfo.Repo}/releases/latest/download/version.txt");
        return Version.TryParse(text.Trim().TrimStart('v'), out var v) ? AppInfo.Normalize(v) : null;
    }

    /// <summary>새 버전 exe 를 임시 폴더에 받아 확인까지 마친 경로. 문제가 있으면 예외.</summary>
    public static async Task<string> DownloadAsync(Version v)
    {
        Directory.CreateDirectory(WorkDir);
        string target = Path.Combine(WorkDir, $"MyZoom-{v.ToString(3)}.exe");
        if (File.Exists(target) && IsVersion(target, v)) return target; // 이미 받아 둠

        string part = target + ".part";
        string url = $"https://github.com/{AppInfo.Repo}/releases/download/v{v.ToString(3)}/MyZoom.exe";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(part);
            await resp.Content.CopyToAsync(fs);
        }
        // 받은 파일이 온전한지(크기·안에 적힌 버전) 확인한 뒤에만 쓴다
        if (new FileInfo(part).Length < 1_000_000) throw new InvalidDataException("받은 파일이 너무 작습니다");
        if (!IsVersion(part, v)) throw new InvalidDataException("받은 파일의 버전이 맞지 않습니다");
        File.Move(part, target, true);
        return target;
    }

    static bool IsVersion(string exe, Version v)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var fileVer = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            return fileVer == v;
        }
        catch { return false; }
    }

    /// <summary>받아 둔 새 exe 로 바꾸고 다시 시작한다. 성공하면 true (호출한 쪽은 바로 종료해야 함).</summary>
    public static bool ApplyAndRestart(string staged, string extraArgs)
    {
        string current = AppInfo.CurrentExe;
        string old = current + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try
            {
                File.Copy(staged, current, true);
            }
            catch
            {
                File.Move(old, current); // 되돌리기
                throw;
            }
            Process.Start(new ProcessStartInfo(current, $"--updated-from {AppInfo.Version.ToString(3)} {extraArgs}".Trim())
            {
                UseShellExecute = false,
            });
            try { File.Delete(staged); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>업데이트 뒤 남은 예전 exe 를 지운다 (켤 때마다).</summary>
    public static void CleanupOld()
    {
        try
        {
            string old = AppInfo.CurrentExe + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }
}
