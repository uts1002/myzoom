using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyZoom.Core;
using MyZoom.Features;
using MyZoom.UI;

namespace MyZoom;

/// <summary>
/// 트레이 상주 프로그램의 본체. 단축키 → 기능 연결, 기능 창들의 켜고 끄기를 관리한다.
/// </summary>
public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    HotkeyManager? _hotkeys;
    TrayIcon? _tray;
    LiveZoomController? _liveZoom;

    AnnotationWindow? _annot;      // 줌·그리기·영역 캡처 (화면 멈춤)
    AnnotationWindow? _liveDraw;   // 화면 위 그리기
    TimerWindow? _timer;
    SpotlightWindow? _spotlight;
    CursorHighlightWindow? _halo;
    KeystrokeWindow? _keys;
    SettingsWindow? _settingsWindow;
    bool _busy;

    Mutex? _mutex;
    EventWaitHandle? _showSettingsEvent;

    public bool IsCursorHighlightOn => _halo != null;
    public bool IsKeystrokeOn => _keys != null;
    public bool IsSpotlightOn => _spotlight != null;
    public bool IsLiveZoomOn => _liveZoom?.IsActive == true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            try { SelfTest.Run(e.Args[1]); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), ex.ToString()); }
            Shutdown();
            return;
        }
#endif

        // ---- 한 번만 실행 (두 번째 실행은 설정 창을 연다) ----
        string user = Environment.UserName;
        _mutex = new Mutex(true, "MyZoom.SingleInstance." + user, out bool created);
        if (!created)
        {
            // 이미 실행 중인 쪽이 설정 창을 맨 앞으로 띄울 수 있게 권한을 넘겨준다
            Interop.Native.AllowSetForegroundWindow(-1);
            try { EventWaitHandle.OpenExisting("MyZoom.ShowSettings." + user).Set(); } catch { }
            Shutdown();
            return;
        }
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "MyZoom.ShowSettings." + user);
        var waiter = new Thread(() =>
        {
            while (_showSettingsEvent.WaitOne())
                Dispatcher.BeginInvoke(ShowSettings);
        }) { IsBackground = true };
        waiter.Start();

        DispatcherUnhandledException += OnUnhandled;

        bool firstRun = !File.Exists(AppSettings.FilePath);
        Settings = AppSettings.Load();
        if (firstRun)
        {
            try { Settings.Save(); } catch { }
        }

        _liveZoom = new LiveZoomController(() => Settings);
        _liveZoom.Notify += Toast;
        _hotkeys = new HotkeyManager();
        _tray = new TrayIcon(this);
        HotkeyBox.CaptureStateChanged += capturing =>
        {
            if (capturing) _hotkeys.UnregisterAll();
            else RegisterHotkeys();
        };

        var failed = RegisterHotkeys();

        if (Settings.CursorHighlightOnStartup) ToggleCursorHighlight(quiet: true);
        if (Settings.KeystrokeOnStartup) ToggleKeystrokes(quiet: true);

        if (failed.Count > 0)
        {
            _tray.Balloon("일부 단축키를 쓸 수 없습니다",
                string.Join("\n", failed) + "\n다른 프로그램(ZoomIt 등)이 쓰고 있을 수 있어요. 설정에서 바꿔 주세요.");
        }
        else if (Settings.ShowStartupNotice)
        {
            _tray.Balloon("MyZoom 실행 중",
                $"{Settings.ZoomHotkey} 확대 · {Settings.DrawHotkey} 그리기 · {Settings.TimerHotkey} 타이머\n트레이 아이콘을 두 번 누르면 설정이 열립니다.");
        }

        if (firstRun || e.Args.Contains("--settings")) ShowSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    void Cleanup()
    {
        try { _liveZoom?.StopImmediately(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _hotkeys = null;
        _tray = null;
    }

    public void ExitApp()
    {
        Cleanup();
        Shutdown();
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch { }
        Toast("오류가 발생했습니다: " + e.Exception.Message);
        _busy = false;
    }

    public void Toast(string message) => ToastWindow.Show(message);

    // =====================================================================
    // 단축키
    // =====================================================================

    public List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        if (_hotkeys == null) return failed;
        _hotkeys.UnregisterAll();
        foreach (var (id, label) in AppSettings.HotkeyActions)
        {
            var spec = Settings.GetHotkey(id);
            if (spec.IsEmpty) continue;
            if (_hotkeys.Register(spec, ActionFor(id)) == 0) failed.Add($"{label}: {spec}");
        }
        _tray?.RefreshMenu();
        return failed;
    }

    public Action ActionFor(string id) => id switch
    {
        "Zoom" => DoZoom,
        "Draw" => DoDraw,
        "LiveDraw" => DoLiveDraw,
        "Timer" => DoTimer,
        "LiveZoom" => DoLiveZoom,
        "Spotlight" => DoSpotlight,
        "Snip" => DoSnip,
        "CursorHighlight" => () => ToggleCursorHighlight(),
        "Keystroke" => () => ToggleKeystrokes(),
        _ => () => { },
    };

    // =====================================================================
    // 기능
    // =====================================================================

    /// <summary>화면을 찍는다. 커서 강조·키 표시 같은 보조 창은 잠깐 숨겨서 사진에 안 찍히게 한다.</summary>
    async Task<BitmapSource> CaptureAsync(Int32Rect r)
    {
        var hidden = new List<Window>();
        foreach (var w in new Window?[] { _halo, _keys, _spotlight })
        {
            if (w is { Opacity: > 0 })
            {
                w.Opacity = 0;
                hidden.Add(w);
            }
        }
        if (hidden.Count > 0) await Task.Delay(70);
        try
        {
            return ScreenHelper.Capture(r);
        }
        finally
        {
            foreach (var w in hidden) w.Opacity = 1;
        }
    }

    async Task OpenAnnotation(AnnotationMode mode, double scale = 1, Vector? translatePx = null)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var mon = ScreenHelper.FromCursor();
            var shot = await CaptureAsync(mon.Bounds);
            var w = new AnnotationWindow(Settings, mode, mon, shot, scale, translatePx);
            w.Notify += Toast;
            w.Closed += (_, _) => { if (_annot == w) _annot = null; };
            _annot = w;
            w.Show();
        }
        finally
        {
            _busy = false;
        }
    }

    public async void DoZoom()
    {
        if (_liveZoom!.IsActive) { _liveZoom.Stop(); return; }
        if (_annot != null) { _annot.Close(); return; }
        await OpenAnnotation(AnnotationMode.Zoom);
    }

    public async void DoDraw()
    {
        if (_annot != null)
        {
            if (_annot.IsFollowing) _annot.LockForDrawing();
            else _annot.Close();
            return;
        }
        if (_liveZoom!.IsActive)
        {
            // 라이브 줌 중이면 지금 보이는 확대 화면 그대로 멈추고 그리기
            var (s, ox, oy) = _liveZoom.Current;
            _liveZoom.StopImmediately();
            await Task.Delay(90); // 돋보기 해제 후 화면이 다시 그려질 때까지
            var (cx, cy) = ScreenHelper.CursorPos();
            var mon = ScreenHelper.FromPoint(cx, cy);
            // 화면좌표 q = (c - off) * s  →  모니터 기준 이동량 T = monLeft*(s-1) - off*s (픽셀)
            var t = new Vector(mon.Bounds.X * (s - 1) - ox * s, mon.Bounds.Y * (s - 1) - oy * s);
            await OpenAnnotation(AnnotationMode.Draw, s, t);
            return;
        }
        await OpenAnnotation(AnnotationMode.Draw);
    }

    public async void DoSnip()
    {
        if (_annot != null)
        {
            bool wasSnip = _annot.Mode == AnnotationMode.Snip;
            _annot.Close();
            if (wasSnip) return;
            await Task.Delay(120);
        }
        await OpenAnnotation(AnnotationMode.Snip);
    }

    public void DoLiveDraw()
    {
        if (_liveDraw != null)
        {
            _liveDraw.TogglePassThrough();
            return;
        }
        var w = new AnnotationWindow(Settings, AnnotationMode.LiveDraw, ScreenHelper.FromCursor(), null);
        w.Notify += Toast;
        w.Closed += (_, _) => { if (_liveDraw == w) _liveDraw = null; };
        _liveDraw = w;
        w.Show();
    }

    public void CloseLiveDraw() => _liveDraw?.Close();
    public bool IsLiveDrawOn => _liveDraw != null;

    public async void DoTimer()
    {
        if (_timer != null) { _timer.Close(); return; }
        if (_busy) return;
        _busy = true;
        try
        {
            BitmapSource? shot = null;
            if (Settings.TimerBackground == TimerBackground.DimScreen)
                shot = await CaptureAsync(ScreenHelper.FromCursor().Bounds);
            var w = new TimerWindow(Settings, shot);
            w.Closed += (_, _) => { if (_timer == w) _timer = null; };
            _timer = w;
            w.Show();
        }
        finally
        {
            _busy = false;
        }
    }

    public void DoLiveZoom()
    {
        if (_annot != null) _annot.Close();
        _liveZoom!.Toggle();
    }

    public void DoSpotlight()
    {
        if (_spotlight != null) { _spotlight.Close(); return; }
        var w = new SpotlightWindow(Settings);
        w.Closed += (_, _) => { if (_spotlight == w) _spotlight = null; };
        _spotlight = w;
        w.Show();
    }

    public void ToggleCursorHighlight(bool quiet = false)
    {
        if (_halo != null)
        {
            _halo.Close();
            _halo = null;
            if (!quiet) Toast("커서·클릭 강조 끔");
        }
        else
        {
            _halo = new CursorHighlightWindow(Settings);
            _halo.Show();
            if (!quiet) Toast("커서·클릭 강조 켬");
        }
        _tray?.RefreshMenu();
    }

    public void ToggleKeystrokes(bool quiet = false)
    {
        if (_keys != null)
        {
            _keys.Close();
            _keys = null;
            if (!quiet) Toast("키 입력 표시 끔");
        }
        else
        {
            _keys = new KeystrokeWindow(Settings);
            _keys.Show();
            if (!quiet)
                Toast(Settings.KeystrokeMode == KeystrokeMode.ShortcutsOnly
                    ? "키 입력 표시 켬 (단축키만)"
                    : "키 입력 표시 켬 (모든 키)");
        }
        _tray?.RefreshMenu();
    }

    // =====================================================================
    // 설정
    // =====================================================================

    public void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            BringToFront(_settingsWindow);
            return;
        }
        _settingsWindow = new SettingsWindow(Settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        BringToFront(_settingsWindow);
    }

    /// <summary>다른 프로그램 뒤에 깔리지 않게 맨 앞으로 (트레이·두 번째 실행에서 열 때).</summary>
    static void BringToFront(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Topmost = true;
        w.Activate();
        Interop.Native.SetForegroundWindow(Interop.Native.Handle(w));
        w.Topmost = false;
        w.Focus();
    }

    /// <summary>설정 창에서 저장을 누르면 호출. 등록에 실패한 단축키 목록을 돌려준다.</summary>
    List<string> ApplySettings(AppSettings s)
    {
        Settings = s;
        Settings.Save();
        var failed = RegisterHotkeys();

        // 켜져 있는 보조 창은 새 설정으로 다시 띄운다
        if (_halo != null) { ToggleCursorHighlight(quiet: true); ToggleCursorHighlight(quiet: true); }
        if (_keys != null) { ToggleKeystrokes(quiet: true); ToggleKeystrokes(quiet: true); }
        if (_spotlight != null) { DoSpotlight(); DoSpotlight(); }
        return failed;
    }

    public void OpenSaveFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.SaveFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.SaveFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast("폴더를 열 수 없습니다: " + ex.Message);
        }
    }
}
