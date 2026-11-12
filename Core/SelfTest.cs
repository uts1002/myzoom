#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyZoom.Features;
using MyZoom.UI;

namespace MyZoom.Core;

/// <summary>
/// 디버그 빌드 전용: MyZoom.exe --selftest &lt;폴더&gt;
/// 화면에 창을 띄우지 않고 주요 화면을 PNG 로 렌더링해서 모양과 그리기 로직을 확인한다.
/// </summary>
public static class SelfTest
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var s = new AppSettings();
        const double W = 1600, H = 900;
        var slide = FakeSlide(W, H);
        var mon = new MonitorArea(new Int32Rect(0, 0, (int)W, (int)H), new Int32Rect(0, 0, (int)W, (int)H), "test");

        // 0) 소개용 화면 (엄페이지 작품 사진): 수업에서 판서한 모습
        var show = new AnnotationWindow(s, AnnotationMode.Draw, mon, slide);
        show.TestLayout(W, H);
        show.TestStroke(DrawTool.Highlighter, ColorUtil.Parse(s.ColorYellow), new Point(172, 224), new Point(450, 224), new Point(738, 224));
        show.TestStroke(DrawTool.Pen, ColorUtil.Parse(s.ColorRed), Wave(318, 326, 262, 4, 24));
        show.TestStroke(DrawTool.Ellipse, ColorUtil.Parse(s.ColorBlue), new Point(522, 352), new Point(570, 380), new Point(612, 406));
        show.TestText(new Point(760, 455), ColorUtil.Parse(s.ColorRed), "잎에서 만들어져요");
        show.TestStroke(DrawTool.Arrow, ColorUtil.Parse(s.ColorRed), new Point(900, 505), new Point(980, 540), new Point(1066, 575));
        Save(show.TestRoot, W, H, Path.Combine(outDir, "00_showcase.png"));
        show.Close();

        // 1) 그리기 모드: 펜·형광펜·화살표·사각형·원·글자
        var a = new AnnotationWindow(s, AnnotationMode.Draw, mon, slide);
        a.TestLayout(W, H);
        a.TestStroke(DrawTool.Pen, Colors.Red, Wave(160, 330, 520, 20, 40));
        a.TestStroke(DrawTool.Highlighter, ColorUtil.Parse(s.ColorYellow), new Point(150, 228), new Point(420, 228), new Point(700, 228));
        a.TestStroke(DrawTool.Arrow, ColorUtil.Parse(s.ColorBlue), new Point(1150, 520), new Point(1000, 430), new Point(860, 360));
        a.TestStroke(DrawTool.Rectangle, ColorUtil.Parse(s.ColorGreen), new Point(140, 400), new Point(400, 470), new Point(660, 520));
        a.TestStroke(DrawTool.Ellipse, ColorUtil.Parse(s.ColorOrange), new Point(1050, 140), new Point(1300, 260), new Point(1450, 330));
        a.TestText(new Point(1000, 600), Colors.Red, "여기가 핵심!\n두 줄도 됩니다");
        a.TestStroke(DrawTool.Pen, Colors.Red, new Point(900, 760)); // 클릭만 = 점
        int before = a.TestInkCount;
        Save(a.TestRoot, W, H, Path.Combine(outDir, "01_draw.png"));
        log.Add($"draw: ink elements = {before}");

        // 2) 지우개·되돌리기
        a.TestEraseAt(new Point(161, 330)); // 펜 선 시작점 위
        int afterErase = a.TestInkCount;
        a.TestUndo();
        int afterUndo = a.TestInkCount;
        log.Add($"erase: {before} -> {afterErase}, undo -> {afterUndo}  ({(afterErase == before - 1 && afterUndo == before ? "OK" : "FAIL")})");

        // 3) 2.5배 확대 + 안내 문구 + 도움말
        a.TestZoom(2.5, new Point(1100, 450));
        a.TestHint("그리기 · F1 단축키 보기 · Esc 끝내기");
        Save(a.TestRoot, W, H, Path.Combine(outDir, "02_zoom.png"));
        a.TestZoom(1, new Point(800, 450));
        a.TestHelp();
        Save(a.TestRoot, W, H, Path.Combine(outDir, "03_help.png"));
        a.TestHelp();

        // 4) 칠판
        a.TestBoard(Color.FromRgb(0x1B, 0x2A, 0x22));
        a.TestStroke(DrawTool.Pen, Colors.White, Wave(200, 700, 500, 15, 30));
        Save(a.TestRoot, W, H, Path.Combine(outDir, "04_blackboard.png"));
        a.Close();

        // 5) 타이머
        var t = new TimerWindow(s, slide);
        t.TestAdjust(TimeSpan.FromSeconds(-23));
        Save(t.TestFull, W, H, Path.Combine(outDir, "05_timer.png"));
        Save(t.TestMini, 250, 96, Path.Combine(outDir, "06_timer_mini.png"));
        t.Close();

        // 6) 키 입력 표시
        var k = new KeystrokeWindow(s);
        k.TestChip("Ctrl + C");
        k.TestChip("Ctrl + Shift + S");
        k.TestChip("Alt + Tab  ×3");
        Save(k.TestRow, W, 150, Path.Combine(outDir, "07_keys.png"), Brushes.SlateGray);
        k.Close();

        // 7) 설정 창 각 탭
        var sw = new SettingsWindow(s, _ => new List<string>());
        var root = (FrameworkElement)sw.Content;
        var tabs = FindChild<TabControl>(root)!;
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            Save(root, 700, 680, Path.Combine(outDir, $"08_settings_{i}.png"), Brushes.White);
        }
        sw.Close();

        File.WriteAllLines(Path.Combine(outDir, "result.txt"), log);
    }

    static Point[] Wave(double x0, double y0, double width, double amp, int n)
    {
        var pts = new Point[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double tt = (double)i / n;
            pts[i] = new Point(x0 + width * tt, y0 + Math.Sin(tt * Math.PI * 3) * amp);
        }
        return pts;
    }

    static BitmapSource FakeSlide(double w, double h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x8A)), null, new Rect(0, 0, w, 110));
            var face = new Typeface(new FontFamily("맑은 고딕"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var face2 = new Typeface(new FontFamily("맑은 고딕"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            void Text(string t, double x, double y, double size, Brush b, Typeface f) =>
                dc.DrawText(new FormattedText(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, f, size, b, 1.0), new Point(x, y));
            Text("광합성은 어떻게 일어날까?", 60, 28, 44, Brushes.White, face);
            Text("• 식물은 빛 에너지를 이용해 양분을 만든다", 150, 200, 30, Brushes.Black, face2);
            Text("• 필요한 것: 빛, 물, 이산화 탄소", 150, 280, 30, Brushes.Black, face2);
            Text("• 만들어지는 것: 녹말(양분), 산소", 150, 360, 30, Brushes.Black, face2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), null, new Point(1200, 600), 140, 140);
            Text("잎", 1180, 580, 36, Brushes.White, face);
        }
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static void Save(FrameworkElement el, double w, double h, string path, Brush? bg = null)
    {
        el.Measure(new Size(w, h));
        el.Arrange(new Rect(0, 0, w, h));
        el.UpdateLayout();
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (bg != null) dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    static T? FindChild<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            var r = FindChild<T>(c);
            if (r != null) return r;
        }
        return null;
    }
}
#endif
