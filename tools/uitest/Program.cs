// MyZoom 자리 비움 UI 테스트.
// 사용: UiTest.exe <출력폴더> [필요 유휴초=120] [최대 대기분=240]
// - 사용자가 idle 초 이상 입력이 없을 때 시작
// - 실행 중 사람이 만든(주입되지 않은) 입력이 감지되면 즉시 중단·정리 후 다시 유휴를 기다림
// - 키/클릭 전에는 항상 MyZoom 창이 포그라운드/커서 아래인지 확인 (다른 프로그램에 입력 안 들어가게)
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class UserInterrupt : Exception { }
class StepFail : Exception { public StepFail(string m) : base(m) { } }

static class P
{
    static string Out = "";
    static StreamWriter Log = null!;
    static volatile bool UserActive;
    static int ZoomPid;
    static int Pass, Fail;
    static string ExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MyZoom\MyZoom.exe");
    static bool HaloOn, KeysOn, SpotOn, LiveZoomOn;

    const int CX = 1280, CY = 640; // 주 모니터(2560x1440) 가운데 근처

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4)); // 물리 픽셀 좌표로 창 크기를 읽기 위해
        Out = args.Length > 0 ? args[0] : "uitest_out";
        int idleSec = args.Length > 1 ? int.Parse(args[1]) : 120;
        double maxWaitMin = args.Length > 2 ? double.Parse(args[2]) : 240;
        if (args.Length > 3) Only = args[3].Split(',').ToHashSet(); // 예: "8,8,8" 또는 "7,8"
        Directory.CreateDirectory(Out);
        Log = new StreamWriter(Path.Combine(Out, "report.txt"), false, Encoding.UTF8) { AutoFlush = true };
        StartHookThread();

        var deadline = DateTime.Now.AddMinutes(maxWaitMin);
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            L($"== 시도 {attempt}: 사용자 유휴 {idleSec}초 대기 ({DateTime.Now:HH:mm:ss})");
            if (!WaitIdle(idleSec, deadline)) { L("대기 시간 초과 — 테스트 못 함"); return 2; }
            UserActive = false;
            Pass = Fail = 0;
            L($"== 시작 {DateTime.Now:HH:mm:ss}");
            try
            {
                RunSuite();
                L($"== 끝 {DateTime.Now:HH:mm:ss}  PASS {Pass} / FAIL {Fail}");
                return 0;
            }
            catch (UserInterrupt)
            {
                L($"!! 사용자 입력 감지 — 중단하고 정리 ({DateTime.Now:HH:mm:ss})");
                Cleanup();
                Thread.Sleep(3000);
            }
            catch (Exception ex)
            {
                L("!! 예외: " + ex);
                Cleanup();
                return 1;
            }
        }
        return 3;
    }

    // ================================================================ 시나리오

    static void RunSuite()
    {
        var p = Process.GetProcessesByName("MyZoom").FirstOrDefault();
        if (p == null) { Process.Start(ExePath); Thread.Sleep(3000); p = Process.GetProcessesByName("MyZoom").First(); }
        ZoomPid = p.Id;
        L($"MyZoom pid {ZoomPid}");
        var picDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MyZoom");
        var picsBefore = Directory.Exists(picDir) ? Directory.GetFiles(picDir).ToHashSet() : new HashSet<string>();
        var clip = BackupClipboard();

        try
        {
            Group("1 줌", TestZoom);
            Group("2 그리기", TestDraw);
            Group("3 화면 위 그리기", TestLiveDraw);
            Group("4 타이머", TestTimer);
            Group("5 스포트라이트", TestSpotlight);
            Group("6 커서강조+키표시", TestHaloKeys);
            Group("7 라이브 줌", TestLiveZoom);
            Group("8 영역 캡처", TestSnip);
            if (Only != null && Only.Contains("8"))
                for (int i = 0; i < 4; i++) Group("8 영역 캡처(반복)", TestSnip);
            Group("9 두 번째 실행 → 설정 창", TestSecondInstance);
            Group("10 타이머 안에서 제목·화면 이동", TestTimerControls);
        }
        finally
        {
            RestoreClipboard(clip);
            if (Directory.Exists(picDir))
                foreach (var f in Directory.GetFiles(picDir).Where(f => !picsBefore.Contains(f)))
                {
                    L($"  (테스트 저장 파일 확인 후 삭제: {Path.GetFileName(f)})");
                    try { File.Copy(f, Path.Combine(Out, "saved_" + Path.GetFileName(f)), true); File.Delete(f); } catch { }
                }
        }

        var errLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MyZoom\error.log");
        if (File.Exists(errLog) && File.GetLastWriteTime(errLog) > Process.GetCurrentProcess().StartTime)
        {
            L("!! error.log 갱신됨:");
            L(string.Join("\n", File.ReadAllLines(errLog).TakeLast(30)));
            Fail++;
        }
        else Ok("error.log 새 오류 없음");
    }

    static HashSet<string> Only;

    static void Group(string name, Action a)
    {
        if (Only != null && !Only.Contains(name.Split(' ')[0])) return;
        L($"--- {name}");
        try { a(); }
        catch (StepFail f) { Bad(name + ": " + f.Message); Cleanup(); }
        Thread.Sleep(600);
        if (ZoomWindows().Any(w => w.Full)) { Bad(name + ": 끝났는데 전체화면 창이 남아 있음"); Cleanup(); }
    }

    static void TestZoom()
    {
        Move(CX, CY);
        Shot("00_baseline");
        Key("ctrl+1"); Thread.Sleep(1200);
        RequireFg("줌 창이 포그라운드가 아님");
        var w = ZoomWindows().FirstOrDefault(x => x.Full);
        Expect(w != null && w.Rect.Left == 0 && w.Rect.Top == 0 && w.Rect.Right == 2560 && w.Rect.Bottom == 1440, $"줌 창이 주 모니터 전체를 덮음 ({w?.Rect})");
        Shot("01_zoom_2x");
        Wheel(CX, CY, 240); Thread.Sleep(700);
        Shot("02_zoom_wheel");
        SlowMove(CX, CY, 500, 300, 20); Thread.Sleep(500);
        Shot("03_zoom_follow_topleft");
        RequireUnderCursor();
        Wave(500, 300, 900, 40);
        Thread.Sleep(300);
        Shot("04_zoom_then_draw");
        Key("esc"); Thread.Sleep(700);
        Expect(!ZoomWindows().Any(x => x.Full), "Esc 로 줌 종료");
    }

    static void TestDraw()
    {
        Move(CX, CY);
        Key("ctrl+2"); Thread.Sleep(1000);
        RequireFg("그리기 창이 포그라운드가 아님");
        RequireUnderCursor();
        Wave(300, 300, 700, 30);                       // 빨간 펜
        Key("2"); Drag(300, 420, 1000, 420);            // 형광펜
        Key("1"); Key("b"); Drag(300, 520, 1000, 600, "shift");     // 파랑 직선 (Shift)
        Key("g"); Drag(1200, 250, 1600, 500, "ctrl");               // 초록 사각형 (Ctrl)
        Key("o"); Drag(1900, 700, 1500, 450, "ctrl+shift");         // 주황 화살표
        Key("p"); Key("6"); Drag(1800, 200, 2300, 450);              // 분홍 원 (도구 6)
        Key("y"); Key("shift+y"); Drag(1200, 650, 1600, 760, "ctrl"); // 노랑 형광 사각 영역
        Key("r"); Key("7"); Click(300, 800); Thread.Sleep(300);
        Type("123 ok"); Key("enter"); Thread.Sleep(200);
        Key("1");
        Shot("05_draw_all");
        Key("ctrl+z"); Thread.Sleep(200);
        Shot("06_draw_undo_text");
        Key("ctrl+y"); Thread.Sleep(200);
        Key("8"); Drag(250, 300, 1050, 300); Thread.Sleep(200); // 지우개로 펜 선 지우기
        Shot("07_draw_erased");
        Key("1");
        Key("w"); Thread.Sleep(300); Shot("08_whiteboard");
        Key("w"); Key("k"); Thread.Sleep(300); Shot("09_blackboard");
        Key("k");
        Key("f1"); Thread.Sleep(300); Shot("10_help"); Key("f1");
        Key("space"); Thread.Sleep(300); Shot("11_toolbar_hidden"); Key("space");
        Key("ctrl+s"); Thread.Sleep(1200); Shot("12_after_save");
        Wheel(1400, 500, 360); Thread.Sleep(800); Shot("13_draw_zoomed");
        Key("0"); Thread.Sleep(600);
        RightClick(CX, CY); Thread.Sleep(700);
        Expect(!ZoomWindows().Any(x => x.Full), "우클릭으로 그리기 종료");
    }

    static void TestLiveDraw()
    {
        Move(CX, CY);
        Key("ctrl+shift+2"); Thread.Sleep(900);
        RequireFg("화면 위 그리기 창이 포그라운드가 아님");
        RequireUnderCursor();
        Key("r"); Wave(600, 500, 900, 50);
        Key("g"); Drag(700, 700, 1400, 900, "ctrl+shift");
        Thread.Sleep(300);
        Shot("14_livedraw");
        Key("ctrl+shift+2"); Thread.Sleep(700);    // 클릭 통과 모드
        var w = ZoomWindows().FirstOrDefault(x => x.Full);
        Expect(w != null && (w.ExStyle & 0x20) != 0, "다시 누르면 클릭 통과(WS_EX_TRANSPARENT)");
        Shot("15_livedraw_passthrough");
        Key("ctrl+shift+2"); Thread.Sleep(700);    // 다시 그리기
        w = ZoomWindows().FirstOrDefault(x => x.Full);
        Expect(w != null && (w.ExStyle & 0x20) == 0, "한 번 더 누르면 다시 그리기 모드");
        RequireFg("다시 그리기 모드인데 포그라운드 아님");
        Key("esc"); Thread.Sleep(600);
        Expect(!ZoomWindows().Any(x => x.Full), "Esc 로 화면 위 그리기 종료");
    }

    static void TestTimer()
    {
        Move(CX, CY);
        Key("ctrl+3"); Thread.Sleep(1200);
        RequireFg("타이머가 포그라운드가 아님");
        Shot("16_timer");
        Key("up"); Key("up"); Thread.Sleep(400);
        Shot("17_timer_plus2");
        Key("space"); Thread.Sleep(400); Shot("18_timer_paused"); Key("space");
        Key("m"); Thread.Sleep(700);
        var mini = ZoomWindows().FirstOrDefault();
        Expect(mini != null && !mini.Full && mini.Rect.Width < 600, $"M 으로 작은 창 ({mini?.Rect})");
        Shot("19_timer_mini");
        RequireFg("작은 타이머가 포그라운드 아님");
        Key("esc"); Thread.Sleep(600);
        Expect(!ZoomWindows().Any(), "Esc 로 타이머 닫힘");
    }

    static void TestSpotlight()
    {
        Move(CX, CY);
        Key("ctrl+5"); SpotOn = true; Thread.Sleep(900);
        Expect(ZoomWindows().Any(x => x.Full && (x.ExStyle & 0x20) != 0), "스포트라이트 창(클릭 통과)");
        Shot("20_spotlight");
        SlowMove(CX, CY, 700, 400, 15); Thread.Sleep(300);
        Shot("21_spotlight_moved");
        Key("ctrl+5"); SpotOn = false; Thread.Sleep(600);
        Expect(!ZoomWindows().Any(x => x.Full), "스포트라이트 끔");
    }

    static void TestHaloKeys()
    {
        Move(CX, CY);
        Key("ctrl+2"); Thread.Sleep(1000);       // 클릭·키가 다른 프로그램으로 가지 않도록 그리기 화면 위에서
        RequireFg("그리기 창이 포그라운드가 아님");
        Key("ctrl+7"); HaloOn = true; Thread.Sleep(700);
        Key("ctrl+8"); KeysOn = true; Thread.Sleep(700);
        RequireFg("토글 후 그리기 창 포그라운드 아님");
        Move(900, 600); Thread.Sleep(200);
        RequireUnderCursor();
        Click(900, 600); Thread.Sleep(150);
        Shot("22_halo_click_ring");
        Key("ctrl+z"); Key("ctrl+z"); Thread.Sleep(250);
        Key("ctrl+shift+z"); Thread.Sleep(250);
        Shot("23_keystrokes");
        Key("ctrl+7"); HaloOn = false;
        Key("ctrl+8"); KeysOn = false;
        Thread.Sleep(400);
        if (IsZoomFg()) Key("esc");
        Thread.Sleep(500);
    }

    static void TestLiveZoom()
    {
        Move(CX, CY);
        Key("ctrl+4"); LiveZoomOn = true; Thread.Sleep(1500);
        Shot("24_livezoom");                   // 캡처에 확대가 찍히는지 자체도 확인 대상
        RawWheel(CX, CY, 240); Thread.Sleep(900);           // 휠만으로 확대 (2배 → 약 2.9배)
        Shot("24b_livezoom_wheel_in");
        RawWheel(CX, CY, -240); Thread.Sleep(900);          // 다시 축소
        Shot("24c_livezoom_wheel_out");
        SlowMove(CX, CY, 600, 400, 15); Thread.Sleep(600);
        Shot("25_livezoom_moved");
        Key("ctrl+2"); LiveZoomOn = false; Thread.Sleep(1300);   // 확대 화면 그대로 그리기로
        RequireFg("라이브 줌→그리기 창 포그라운드 아님");
        Shot("26_livezoom_to_draw");
        RequireUnderCursor();
        Key("r"); Wave(500, 500, 600, 30);
        Shot("27_livezoom_draw");
        Key("esc"); Thread.Sleep(700);
        Shot("28_after_livezoom");             // 원래 배율로 돌아왔는지
        Expect(!ZoomWindows().Any(x => x.Full), "라이브 줌 → 그리기 → 종료");
    }

    static void TestSnip()
    {
        Move(CX, CY);
        Key("ctrl+6"); Thread.Sleep(1000);
        RequireFg("영역 캡처 창 포그라운드 아님");
        Shot("29_snip_start");
        RequireUnderCursor();
        Drag(800, 400, 1400, 800);
        Thread.Sleep(900);
        Expect(!ZoomWindows().Any(x => x.Full), "드래그 후 캡처 창 닫힘");
        Image img = null;
        try { if (Clipboard.ContainsImage()) img = Clipboard.GetImage(); } catch { }
        Expect(img != null && Math.Abs(img.Width - 600) <= 3 && Math.Abs(img.Height - 400) <= 3, $"클립보드 이미지 600x400 ({img?.Width}x{img?.Height})");
        img?.Save(Path.Combine(Out, "30_snip_clipboard.png"), ImageFormat.Png);
        Shot("31_snip_toast");
    }

    static void TestTimerControls()
    {
        var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MyZoom\settings.json");
        string original = File.ReadAllText(cfg);
        try
        {
            Move(CX, CY);
            Key("ctrl+3"); Thread.Sleep(1200);
            RequireFg("타이머가 포그라운드가 아님");
            Shot("40_timer_bar");

            // 제목 바꾸기: T → 입력 → Enter
            Key("t"); Thread.Sleep(400);
            RequireFg("제목 입력 중 포그라운드 아님");
            Key("ctrl+a");
            TypeUnicode("모둠 활동 시간");
            Thread.Sleep(200);
            Shot("41_timer_title_editing");
            Key("enter"); Thread.Sleep(500);
            Shot("42_timer_title_done");
            string saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg)).RootElement.GetProperty("TimerMessage").GetString();
            Expect(saved == "모둠 활동 시간", $"제목이 설정 파일에 저장됨 (다음 타이머에도 유지) ('{saved}')");

            // 다른 화면으로: Tab
            var before = ZoomWindows().FirstOrDefault(x => x.Full)?.Rect;
            Key("tab"); Thread.Sleep(1200);
            var after = ZoomWindows().FirstOrDefault(x => x.Full)?.Rect;
            Expect(before != null && after != null && after.Value.Top != before.Value.Top,
                $"Tab 으로 다른 모니터로 이동 ({before} → {after})");
            ShotRect("43_timer_on_other_monitor", after ?? Rectangle.Empty);
            RequireFg("다른 모니터에서 포그라운드 아님");
            Key("up"); Thread.Sleep(300);  // 옮긴 뒤에도 키가 먹는지
            Key("tab"); Thread.Sleep(1200);
            var back = ZoomWindows().FirstOrDefault(x => x.Full)?.Rect;
            Expect(back != null && before != null && back.Value == before.Value, $"Tab 한 번 더 → 원래 모니터 ({back})");

            // 작은 창 → 다른 화면 → 다시 크게
            Key("m"); Thread.Sleep(700);
            var mini = ZoomWindows().FirstOrDefault();
            Key("tab"); Thread.Sleep(900);
            var mini2 = ZoomWindows().FirstOrDefault();
            Expect(mini != null && mini2 != null && !mini2.Full && mini2.Rect.Top != mini.Rect.Top, $"작은 창도 Tab 으로 다른 모니터 ({mini?.Rect} → {mini2?.Rect})");
            ShotRect("44_timer_mini_other", mini2?.Rect ?? Rectangle.Empty);
            Key("m"); Thread.Sleep(1000);
            var big = ZoomWindows().FirstOrDefault(x => x.Full);
            Expect(big != null && mini2 != null && big.Rect.Contains(mini2.Rect.Location), $"크게 하면 작은 창이 있던 모니터에서 ({big?.Rect})");
            RequireFg("다시 크게 했는데 포그라운드 아님");
            Key("esc"); Thread.Sleep(600);
            Expect(!ZoomWindows().Any(), "Esc 로 닫힘");
        }
        finally
        {
            // 설정의 타이머 제목을 원래대로 (MyZoom 은 다음 저장 때 메모리 값을 다시 쓰므로 타이머로 되돌린다)
            if (!File.ReadAllText(cfg).Equals(original))
            {
                L("  (타이머 제목을 원래대로 되돌림)");
                RestoreTimerTitle(original);
            }
        }
    }

    static void RestoreTimerTitle(string originalJson)
    {
        string title = "쉬는 시간";
        try { title = System.Text.Json.JsonDocument.Parse(originalJson).RootElement.GetProperty("TimerMessage").GetString() ?? title; } catch { }
        Move(CX, CY);
        RawKey("ctrl+3"); Thread.Sleep(1200);
        if (!IsZoomFg()) { L("  !! 제목 복원 실패 — 설정에서 직접 바꿔 주세요"); return; }
        RawKey("t"); Thread.Sleep(400);
        RawKey("ctrl+a");
        TypeUnicodeRaw(title);
        RawKey("enter"); Thread.Sleep(400);
        RawKey("esc"); Thread.Sleep(500);
    }

    static void TestSecondInstance()
    {
        Process.Start(ExePath);
        Thread.Sleep(2500);
        var fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint pid);
        var sb = new StringBuilder(200); GetWindowText(fg, sb, 200);
        Expect(pid == ZoomPid && sb.ToString().Contains("설정"), $"설정 창이 맨 앞에 뜸 (fg='{sb}', pid={pid})");
        Shot("32_settings_front");
        if (IsZoomFg()) Key("esc");
        Thread.Sleep(500);
        Expect(Process.GetProcessesByName("MyZoom").Length == 1, "두 번째 실행은 바로 종료(하나만 실행)");
    }

    // ================================================================ 정리

    static void Cleanup()
    {
        UserActive = false; // 정리 동작은 막지 않음
        try
        {
            for (int i = 0; i < 4 && IsZoomFg(); i++) { RawKey("esc"); Thread.Sleep(400); }
            if (LiveZoomOn) { RawKey("ctrl+4"); LiveZoomOn = false; Thread.Sleep(800); }
            if (SpotOn) { RawKey("ctrl+5"); SpotOn = false; }
            if (HaloOn) { RawKey("ctrl+7"); HaloOn = false; }
            if (KeysOn) { RawKey("ctrl+8"); KeysOn = false; }
        }
        catch { }
    }

    // ================================================================ 확인

    static void Ok(string m) { Pass++; L("  PASS " + m); }
    static void Bad(string m) { Fail++; L("  FAIL " + m); }
    static void Expect(bool c, string m) { if (c) Ok(m); else Bad(m); }
    static void L(string m) { Log.WriteLine(m); Console.WriteLine(m); }

    static bool IsZoomFg()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == ZoomPid;
    }

    static void RequireFg(string why) { if (!IsZoomFg()) throw new StepFail(why); }

    static void RequireUnderCursor()
    {
        GetCursorPos(out var pt);
        var h = GetAncestor(WindowFromPoint(pt), 2);
        GetWindowThreadProcessId(h, out uint pid);
        if (pid != ZoomPid) throw new StepFail("커서 아래가 MyZoom 창이 아님 — 클릭 생략");
    }

    record Win(IntPtr H, Rectangle Rect, int ExStyle, string Title) { public bool Full => Rect.Width >= 2000; }

    static List<Win> ZoomWindows()
    {
        var res = new List<Win>();
        EnumWindows((h, l) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == ZoomPid && IsWindowVisible(h))
            {
                GetWindowRect(h, out var r);
                var sb = new StringBuilder(200); GetWindowText(h, sb, 200);
                var rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                if (rect.Width > 0 && rect.Height > 0)
                    res.Add(new Win(h, rect, GetWindowLong(h, -20), sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return res;
    }

    // ================================================================ 입력

    static void Ensure() { if (UserActive) throw new UserInterrupt(); }

    static readonly Dictionary<string, byte> Vk = new()
    {
        ["ctrl"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["esc"] = 0x1B, ["space"] = 0x20, ["enter"] = 0x0D,
        ["f1"] = 0x70, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27, ["tab"] = 0x09,
    };

    static byte VkOf(string k) => Vk.TryGetValue(k, out var v) ? v : (byte)char.ToUpperInvariant(k[0]);

    static void Key(string combo)
    {
        Ensure();
        // 전역 단축키(Ctrl+1~8, Ctrl+Shift+2)가 아니면 반드시 MyZoom 창에만 보낸다
        bool global = System.Text.RegularExpressions.Regex.IsMatch(combo, @"^ctrl\+(shift\+)?[1-8]$");
        if (!global) RequireFg($"'{combo}' 키를 보낼 창이 MyZoom 이 아님");
        RawKey(combo);
        Thread.Sleep(90);
        Ensure();
    }

    static void RawKey(string combo)
    {
        var parts = combo.ToLowerInvariant().Split('+');
        foreach (var p in parts) { keybd_event(VkOf(p), 0, 0, UIntPtr.Zero); Thread.Sleep(15); }
        for (int i = parts.Length - 1; i >= 0; i--) { keybd_event(VkOf(parts[i]), 0, 2, UIntPtr.Zero); Thread.Sleep(15); }
    }

    static void Type(string text)
    {
        foreach (char c in text)
        {
            RequireFg("글자 입력 대상이 MyZoom 이 아님");
            if (c == ' ') RawKey("space"); else RawKey(c.ToString());
            Thread.Sleep(40);
        }
    }

    static void Move(int x, int y) { Ensure(); SetPos(x, y); Thread.Sleep(60); }

    static void SlowMove(int x1, int y1, int x2, int y2, int n)
    {
        for (int i = 1; i <= n; i++) { Ensure(); SetPos(x1 + (x2 - x1) * i / n, y1 + (y2 - y1) * i / n); Thread.Sleep(25); }
    }

    static void Mods(string mods, bool down)
    {
        if (string.IsNullOrEmpty(mods)) return;
        foreach (var m in mods.Split('+')) { keybd_event(VkOf(m), 0, down ? 0u : 2u, UIntPtr.Zero); Thread.Sleep(20); }
    }

    static void Drag(int x1, int y1, int x2, int y2, string mods = "")
    {
        Move(x1, y1);
        RequireUnderCursor();
        Mods(mods, true);
        try
        {
            mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            for (int i = 1; i <= 25; i++) { SetPos(x1 + (x2 - x1) * i / 25, y1 + (y2 - y1) * i / 25); Thread.Sleep(12); }
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        }
        finally { Mods(mods, false); }
        Thread.Sleep(120);
        Ensure();
    }

    static void Wave(int x1, int y, int width, int amp)
    {
        Move(x1, y);
        RequireUnderCursor();
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        for (int i = 1; i <= 50; i++)
        {
            double t = i / 50.0;
            SetPos(x1 + (int)(width * t), y + (int)(Math.Sin(t * Math.PI * 4) * amp));
            Thread.Sleep(10);
        }
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(120);
        Ensure();
    }

    static void Click(int x, int y)
    {
        Move(x, y); RequireUnderCursor();
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(50); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100); Ensure();
    }

    static void RightClick(int x, int y)
    {
        Move(x, y); RequireUnderCursor();
        mouse_event(8, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(50); mouse_event(16, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100); Ensure();
    }

    static void Wheel(int x, int y, int delta)
    {
        Move(x, y); RequireUnderCursor();
        for (int d = delta; d > 0; d -= 120) { mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); Thread.Sleep(80); }
        Ensure();
    }

    // 창 확인 없이 휠 (라이브 줌은 창이 없음 — MyZoom 훅이 가로채야 정상)
    static void RawWheel(int x, int y, int delta)
    {
        Move(x, y);
        int step = delta > 0 ? 120 : -120;
        for (int d = Math.Abs(delta); d > 0; d -= 120) { mouse_event(0x800, 0, 0, step, UIntPtr.Zero); Thread.Sleep(80); }
        Ensure();
    }

    static void ShotRect(string name, Rectangle r)
    {
        Ensure();
        if (r.Width <= 0) return;
        using var full = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
        int w = Math.Min(1280, r.Width), h = r.Height * w / r.Width;
        using var half = new Bitmap(w, h);
        using (var g = Graphics.FromImage(half)) g.DrawImage(full, 0, 0, w, h);
        half.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
    }

    // IME(한/영) 상태와 상관없이 글자를 그대로 입력
    static void TypeUnicode(string text)
    {
        foreach (char c in text) { RequireFg("글자 입력 대상이 MyZoom 이 아님"); SendChar(c); Thread.Sleep(30); }
        Ensure();
    }

    static void TypeUnicodeRaw(string text) { foreach (char c in text) { SendChar(c); Thread.Sleep(30); } }

    static void SendChar(char c)
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; inputs[0].ki.wScan = c; inputs[0].ki.dwFlags = 0x0004;            // KEYEVENTF_UNICODE
        inputs[1].type = 1; inputs[1].ki.wScan = c; inputs[1].ki.dwFlags = 0x0004 | 0x0002;   // + KEYUP
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    static void Shot(string name)
    {
        Ensure();
        using var full = new Bitmap(2560, 1440);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(0, 0, 0, 0, full.Size);
        using var half = new Bitmap(1280, 720);
        using (var g = Graphics.FromImage(half))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, 1280, 720);
        }
        half.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
    }

    // ================================================================ 클립보드 백업

    static object BackupClipboard()
    {
        try
        {
            if (Clipboard.ContainsText()) return Clipboard.GetText();
            if (Clipboard.ContainsImage()) return Clipboard.GetImage();
        }
        catch { }
        return null;
    }

    static void RestoreClipboard(object o)
    {
        try
        {
            if (o is string s) Clipboard.SetText(s);
            else if (o is Image i) Clipboard.SetImage(i);
            L("  (클립보드 원래 내용 복원)");
        }
        catch { }
    }

    // ================================================================ 유휴 감지 / 사람 입력 감지

    static bool WaitIdle(int sec, DateTime deadline)
    {
        while (DateTime.Now < deadline)
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            GetLastInputInfo(ref lii);
            uint idleMs = (uint)Environment.TickCount - lii.dwTime;
            if (idleMs >= sec * 1000) return true;
            Thread.Sleep(5000);
        }
        return false;
    }

    static HookProc _mouseProc, _kbProc;

    static void StartHookThread()
    {
        var t = new Thread(() =>
        {
            _mouseProc = (n, w, l) =>
            {
                if (n >= 0)
                {
                    var info = Marshal.PtrToStructure<MSLL>(l);
                    if ((info.flags & 1) == 0) UserActive = true; // LLMHF_INJECTED 아님 = 사람
                }
                return CallNextHookEx(IntPtr.Zero, n, w, l);
            };
            _kbProc = (n, w, l) =>
            {
                if (n >= 0)
                {
                    var info = Marshal.PtrToStructure<KBLL>(l);
                    if ((info.flags & 0x10) == 0) UserActive = true; // LLKHF_INJECTED 아님
                }
                return CallNextHookEx(IntPtr.Zero, n, w, l);
            };
            SetWindowsHookEx(14, _mouseProc, GetModuleHandle(null), 0);
            SetWindowsHookEx(13, _kbProc, GetModuleHandle(null), 0);
            Application.Run();
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ================================================================ Win32

    delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KBLL { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    // SetCursorPos 는 훅에서 '사람 입력'처럼 보일 수 있어 주입 이벤트로 움직인다
    static void SetPos(int x, int y) => mouse_event(0x8001, (int)Math.Round(x * 65535.0 / 2559), (int)Math.Round(y * 65535.0 / 1439), 0, UIntPtr.Zero);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
}
