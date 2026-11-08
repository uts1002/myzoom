using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MyZoom.Core;
using WinForms = System.Windows.Forms;

namespace MyZoom.UI;

/// <summary>설정 창. 화면을 코드로 만든다(항목 추가가 쉬움). 저장을 눌러야 적용된다.</summary>
public sealed class SettingsWindow : Window
{
    AppSettings _s;
    readonly Func<AppSettings, List<string>> _apply;
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 0) };
    TabControl _tabs = new();
    bool _runAtStartup;

    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68));
    static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x1E, 0x6E, 0x3A));
    static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));

    public SettingsWindow(AppSettings current, Func<AppSettings, List<string>> apply)
    {
        _s = current.Clone();
        _apply = apply;
        _runAtStartup = StartupRegistration.IsEnabled();

        Title = "MyZoom 설정";
        Width = 700;
        Height = 680;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("맑은 고딕");
        FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/myzoom.ico")); } catch { }

        Build();
    }

    // =====================================================================
    // 화면 구성
    // =====================================================================

    void Build()
    {
        int selected = _tabs.SelectedIndex < 0 ? 0 : _tabs.SelectedIndex;
        _tabs = new TabControl { Margin = new Thickness(12, 12, 12, 8), Padding = new Thickness(0) };
        _tabs.Items.Add(Tab("단축키", BuildHotkeys()));
        _tabs.Items.Add(Tab("줌·펜", BuildZoomPen()));
        _tabs.Items.Add(Tab("타이머", BuildTimer()));
        _tabs.Items.Add(Tab("발표 보조", BuildPresenter()));
        _tabs.Items.Add(Tab("일반", BuildGeneral()));
        _tabs.Items.Add(Tab("사용법", BuildUsage()));
        _tabs.SelectedIndex = selected;

        var reset = new Button { Content = "기본값으로", Padding = new Thickness(12, 5, 12, 5) };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "모든 설정을 기본값으로 되돌릴까요? (저장을 눌러야 적용)", "MyZoom",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _s = new AppSettings();
            Build();
            SetStatus("기본값을 불러왔습니다. 저장을 눌러 적용하세요.", Muted);
        };
        var save = new Button { Content = "저장", IsDefault = true, Padding = new Thickness(22, 5, 22, 5), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => Save();
        var close = new Button { Content = "닫기", IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
        close.Click += (_, _) => Close();

        var bottom = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(reset, Dock.Left);
        bottom.Children.Add(reset);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(save);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right);
        bottom.Children.Add(_status);

        var bar = new Border
        {
            Child = bottom,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDF, 0xE3)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
        };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_tabs);
        Content = root;
    }

    static TabItem Tab(string header, UIElement content) => new()
    {
        Header = new TextBlock { Text = header, Padding = new Thickness(8, 3, 8, 3) },
        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
        },
    };

    static StackPanel Page() => new() { Margin = new Thickness(20, 16, 20, 20) };

    static void Header(Panel p, string text) => p.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, p.Children.Count == 0 ? 0 : 18, 0, 8),
    });

    static void Note(Panel p, string text) => p.Children.Add(new TextBlock
    {
        Text = text,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    });

    static Grid Row(Panel p, string label, UIElement control, UIElement? extra = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        g.Children.Add(lb);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        if (extra != null)
        {
            Grid.SetColumn(extra, 2);
            g.Children.Add(extra);
        }
        p.Children.Add(g);
        return g;
    }

    static void Slider(Panel p, string label, double min, double max, double step, Func<double> get, Action<double> set, string format)
    {
        var value = new TextBlock { Width = 70, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var sl = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(get(), min, max),
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            SmallChange = step,
            LargeChange = step * 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.Text = string.Format(format, sl.Value);
        sl.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = string.Format(format, e.NewValue);
        };
        Row(p, label, sl, value);
    }

    static void Check(Panel p, string label, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 5, 0, 5) };
        cb.Checked += (_, _) => set(true);
        cb.Unchecked += (_, _) => set(false);
        p.Children.Add(cb);
    }

    static void TextField(Panel p, string label, Func<string> get, Action<string> set)
    {
        var tb = new TextBox { Text = get(), Padding = new Thickness(4, 3, 4, 3) };
        tb.TextChanged += (_, _) => set(tb.Text);
        Row(p, label, tb);
    }

    void ColorPick(Panel p, string label, Func<string> get, Action<string> set)
    {
        var swatch = new Border
        {
            Width = 44,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = ColorUtil.Brush(get()),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var btn = new Button { Content = swatch, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "눌러서 색 고르기" };
        var hex = new TextBlock { Text = get(), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        btn.Click += (_, _) =>
        {
            var c = ColorUtil.Parse(get());
            using var dlg = new WinForms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B),
                FullOpen = true,
            };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            string h = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            set(h);
            swatch.Background = ColorUtil.Brush(h);
            hex.Text = h;
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(btn);
        sp.Children.Add(hex);
        Row(p, label, sp);
    }

    static void Combo<T>(Panel p, string label, (T Value, string Text)[] items, Func<T> get, Action<T> set)
    {
        var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
        foreach (var it in items) cb.Items.Add(it.Text);
        cb.SelectedIndex = Math.Max(0, Array.FindIndex(items, i => EqualityComparer<T>.Default.Equals(i.Value, get())));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) set(items[cb.SelectedIndex].Value); };
        Row(p, label, cb);
    }

    // =====================================================================
    // 탭별 내용
    // =====================================================================

    UIElement BuildHotkeys()
    {
        var p = Page();
        Note(p, "칸을 누른 뒤 원하는 키 조합을 누르세요. Backspace 를 누르면 그 기능은 단축키 없이 씁니다.\n" +
                "기본값은 ZoomIt 과 같은 배치(Ctrl+숫자)라서 ZoomIt 이 켜져 있으면 겹칩니다. 브라우저의 Ctrl+숫자(탭 이동)도 막히니 불편하면 Ctrl+Alt+숫자 등으로 바꾸세요.");
        foreach (var (id, label) in AppSettings.HotkeyActions)
        {
            var box = new HotkeyBox(_s.GetHotkey(id));
            string capturedId = id;
            box.Changed += spec =>
            {
                _s.SetHotkey(capturedId, spec);
                var dup = FindDuplicate();
                SetStatus(dup ?? "", Bad);
            };
            Row(p, label, box);
        }

        Header(p, "기능별 안에서 쓰는 키");
        Note(p, "줌·그리기 화면 안의 키(R·G·B 색, 1~8 도구 등)는 '사용법' 탭에 정리되어 있습니다.");
        return p;
    }

    UIElement BuildZoomPen()
    {
        var p = Page();
        Header(p, "줌");
        Slider(p, "처음 확대 배율", 1.25, 6, 0.25, () => _s.InitialZoom, v => _s.InitialZoom = v, "{0:0.00}배");
        Slider(p, "최대 확대 배율", 2, 16, 1, () => _s.MaxZoom, v => _s.MaxZoom = v, "{0:0}배");
        Slider(p, "라이브 줌 처음 배율", 1.25, 6, 0.25, () => _s.LiveZoomInitial, v => _s.LiveZoomInitial = v, "{0:0.00}배");
        Check(p, "라이브 줌에서 휠만으로 확대·축소 (끄면 Ctrl+휠, 그냥 휠은 화면 스크롤)", () => _s.LiveZoomPlainWheel, v => _s.LiveZoomPlainWheel = v);
        Check(p, "확대·축소를 부드럽게 (애니메이션)", () => _s.AnimateZoom, v => _s.AnimateZoom = v);

        Header(p, "펜");
        Slider(p, "펜 굵기", 1, 30, 1, () => _s.PenWidth, v => _s.PenWidth = v, "{0:0}");
        Slider(p, "형광펜 굵기", 6, 80, 2, () => _s.HighlighterWidth, v => _s.HighlighterWidth = v, "{0:0}");
        Slider(p, "형광펜 투명도", 0.15, 0.8, 0.05, () => _s.HighlighterOpacity, v => _s.HighlighterOpacity = v, "{0:0%}");
        Slider(p, "글자 크기", 12, 96, 2, () => _s.TextSize, v => _s.TextSize = v, "{0:0}");
        TextField(p, "글꼴", () => _s.TextFont, v => _s.TextFont = v);

        Header(p, "색 (키보드 R·G·B·Y·O·P)");
        ColorPick(p, "R 빨강 자리", () => _s.ColorRed, v => _s.ColorRed = v);
        ColorPick(p, "G 초록 자리", () => _s.ColorGreen, v => _s.ColorGreen = v);
        ColorPick(p, "B 파랑 자리", () => _s.ColorBlue, v => _s.ColorBlue = v);
        ColorPick(p, "Y 노랑 자리", () => _s.ColorYellow, v => _s.ColorYellow = v);
        ColorPick(p, "O 주황 자리", () => _s.ColorOrange, v => _s.ColorOrange = v);
        ColorPick(p, "P 분홍 자리", () => _s.ColorPink, v => _s.ColorPink = v);
        ColorPick(p, "처음 펜 색", () => _s.DefaultColor, v => _s.DefaultColor = v);

        Header(p, "그리기 화면");
        Check(p, "도구 막대 보이기 (그리기 중 Space 로 숨김/보임)", () => _s.ShowToolbar, v => _s.ShowToolbar = v);
        Check(p, "모드가 바뀔 때 위쪽에 안내 문구 보이기", () => _s.ShowModeHint, v => _s.ShowModeHint = v);
        Check(p, "마우스 오른쪽 클릭으로 끝내기", () => _s.RightClickExits, v => _s.RightClickExits = v);

        Header(p, "저장");
        var folder = new TextBox { Text = _s.SaveFolder, Padding = new Thickness(4, 3, 4, 3) };
        folder.TextChanged += (_, _) => _s.SaveFolder = folder.Text;
        var browse = new Button { Content = "찾아보기", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { InitialDirectory = Directory.Exists(_s.SaveFolder) ? _s.SaveFolder : "" };
            if (dlg.ShowDialog(this) == true) folder.Text = dlg.FolderName;
        };
        Row(p, "그림 저장 폴더 (Ctrl+S)", folder, browse);
        Check(p, "영역 캡처할 때 파일로도 저장", () => _s.SnipAlsoSaveFile, v => _s.SnipAlsoSaveFile = v);
        return p;
    }

    UIElement BuildTimer()
    {
        var p = Page();
        Header(p, "타이머");
        Slider(p, "기본 시간", 1, 60, 1, () => _s.TimerMinutes, v => _s.TimerMinutes = (int)v, "{0:0}분");
        TextField(p, "위쪽 문구", () => _s.TimerMessage, v => _s.TimerMessage = v);
        Combo(p, "배경",
            new[] { (TimerBackground.DimScreen, "지금 화면을 흐리게"), (TimerBackground.Black, "검은 배경"), (TimerBackground.White, "흰 배경") },
            () => _s.TimerBackground, v => _s.TimerBackground = v);
        Check(p, "끝나는 시각 보이기", () => _s.TimerShowEndTime, v => _s.TimerShowEndTime = v);
        Check(p, "끝나면 알림음", () => _s.TimerSound, v => _s.TimerSound = v);
        Note(p, "타이머 화면에서:  ↑↓ 1분 · ←→ 10초 · 휠 1분 · Space 일시정지 · R 다시 시작 · M 작은 창(끌어서 이동) · Esc 닫기");
        return p;
    }

    UIElement BuildPresenter()
    {
        var p = Page();
        Header(p, "스포트라이트");
        Slider(p, "밝은 원 크기", 60, 500, 10, () => _s.SpotlightRadius, v => _s.SpotlightRadius = v, "{0:0}");
        Slider(p, "주변 어둡기", 0.3, 0.9, 0.05, () => _s.SpotlightDim, v => _s.SpotlightDim = v, "{0:0%}");
        Note(p, "켜져 있는 동안 Ctrl+휠로 원 크기를 바꿀 수 있습니다. 클릭은 그대로 아래 프로그램에 전달됩니다.");

        Header(p, "커서·클릭 강조 (영상 녹화용)");
        Check(p, "커서 둘레에 색 원 보이기", () => _s.ShowCursorHalo, v => _s.ShowCursorHalo = v);
        ColorPick(p, "커서 원 색", () => _s.HaloColor, v => _s.HaloColor = v);
        Slider(p, "커서 원 크기", 20, 140, 2, () => _s.HaloSize, v => _s.HaloSize = v, "{0:0}");
        Slider(p, "커서 원 진하기", 0.1, 0.8, 0.05, () => _s.HaloOpacity, v => _s.HaloOpacity = v, "{0:0%}");
        Check(p, "클릭할 때 퍼지는 고리 보이기", () => _s.ShowClickRings, v => _s.ShowClickRings = v);
        ColorPick(p, "왼쪽 클릭 고리 색", () => _s.LeftClickColor, v => _s.LeftClickColor = v);
        ColorPick(p, "오른쪽 클릭 고리 색", () => _s.RightClickColor, v => _s.RightClickColor = v);
        Check(p, "프로그램 시작할 때 바로 켜기", () => _s.CursorHighlightOnStartup, v => _s.CursorHighlightOnStartup = v);

        Header(p, "키 입력 표시 (영상 녹화용)");
        Combo(p, "보여 줄 키",
            new[] { (KeystrokeMode.ShortcutsOnly, "단축키만 (Ctrl·Alt·Win 조합, F키, Esc 등)"), (KeystrokeMode.AllKeys, "모든 키 (일반 타자 포함)") },
            () => _s.KeystrokeMode, v => _s.KeystrokeMode = v);
        Slider(p, "글자 크기", 14, 60, 2, () => _s.KeystrokeFontSize, v => _s.KeystrokeFontSize = v, "{0:0}");
        Slider(p, "보이는 시간", 0.6, 5, 0.2, () => _s.KeystrokeSeconds, v => _s.KeystrokeSeconds = v, "{0:0.0}초");
        Check(p, "프로그램 시작할 때 바로 켜기", () => _s.KeystrokeOnStartup, v => _s.KeystrokeOnStartup = v);
        Note(p, "'모든 키'로 두면 비밀번호도 화면에 보입니다. 녹화 중 로그인할 일이 있으면 '단축키만'을 쓰세요.");
        return p;
    }

    UIElement BuildGeneral()
    {
        var p = Page();
        Header(p, "실행");
        Check(p, "Windows 시작할 때 MyZoom 자동 실행", () => _runAtStartup, v => _runAtStartup = v);
        Check(p, "실행할 때 트레이 알림 보이기", () => _s.ShowStartupNotice, v => _s.ShowStartupNotice = v);

        Header(p, "파일");
        var openCfg = new Button { Content = "설정 폴더 열기", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        openCfg.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppSettings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Folder}\"") { UseShellExecute = true });
        };
        Row(p, "설정 파일 (settings.json)", openCfg);
        Note(p, AppSettings.FilePath);

        Header(p, "정보");
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Note(p, $"MyZoom {ver?.ToString(3)}\n실행 파일: {Environment.ProcessPath}");
        return p;
    }

    UIElement BuildUsage()
    {
        var p = Page();
        Header(p, "전역 단축키 (어느 프로그램에서나)");
        var sb = new System.Text.StringBuilder();
        foreach (var (id, label) in AppSettings.HotkeyActions)
            sb.AppendLine($"{_s.GetHotkey(id),-22}  {label}");
        p.Children.Add(new TextBlock { Text = sb.ToString(), FontFamily = new FontFamily("Consolas, 맑은 고딕"), LineHeight = 22 });

        Header(p, "줌");
        Note(p, "마우스를 움직이면 확대된 화면이 따라 움직입니다. 휠 또는 ↑↓ 로 배율 조절, 클릭하거나 그리기 단축키를 누르면 그 자리에서 화면이 멈추고 그리기로 바뀝니다. Esc·우클릭·같은 단축키로 끝.");

        Header(p, "그리기 화면 안에서");
        Note(p,
            "도구:  1 펜 · 2 형광펜 · 3 직선 · 4 화살표 · 5 사각형 · 6 원 · 7(T) 글자 · 8 지우개\n" +
            "색:  R 빨강 · G 초록 · B 파랑 · Y 노랑 · O 주황 · P 분홍   (Shift+색 = 그 색 형광펜)\n" +
            "펜으로 드래그할 때:  Shift 직선 · Ctrl 사각형 · Ctrl+Shift 화살표 · Alt 원\n" +
            "형광펜으로 Ctrl/Alt 드래그:  반투명하게 칠한 사각형/원 (영역 강조)\n" +
            "도형 도구에서 Shift:  45° 맞춤, 정사각형, 정원\n" +
            "굵기:  Ctrl+휠 또는 [ ]   ·   확대:  휠, ↑↓, 0 원래 크기, 가운데 버튼 드래그로 이동\n" +
            "판:  W 화이트보드 · K 칠판   ·   편집:  Ctrl+Z / Ctrl+Y, E 모두 지우기\n" +
            "내보내기:  Ctrl+S 파일 저장, Ctrl+C 클립보드 복사   ·   Space 도구 막대, F1 도움말, Esc 끝내기\n" +
            "글자:  글자 도구로 클릭 → 입력 → Enter(줄바꿈은 Shift+Enter) 또는 Esc 로 확정");

        Header(p, "화면 위에 그리기");
        Note(p, "화면을 멈추지 않고 그 위에 그립니다(동영상·움직이는 화면 위 판서). 같은 단축키를 한 번 더 누르면 그림은 남긴 채 아래 프로그램을 조작할 수 있고, 다시 누르면 이어서 그립니다. Esc 로 지우고 끝내기.");

        Header(p, "라이브 줌");
        Note(p, "움직이는 화면을 그대로 확대합니다(클릭·입력 가능). 휠로 배율 조절(설정에서 Ctrl+휠로 바꿀 수 있음). 라이브 줌 중에 그리기 단축키를 누르면 그 확대 화면 그대로 멈추고 판서합니다.");

        Header(p, "트레이 아이콘");
        Note(p, "작업 표시줄 오른쪽 아래 아이콘을 오른쪽 클릭하면 모든 기능과 설정, 종료가 있습니다. 두 번 클릭하면 이 설정 창이 열립니다.");
        return p;
    }

    // =====================================================================
    // 저장
    // =====================================================================

    string? FindDuplicate()
    {
        var list = AppSettings.HotkeyActions.Select(a => (a.Label, Spec: _s.GetHotkey(a.Id))).Where(x => !x.Spec.IsEmpty).ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (list[i].Spec.SameAs(list[j].Spec))
                    return $"단축키가 겹칩니다: '{list[i].Label}' 와 '{list[j].Label}' ({list[i].Spec})";
        return null;
    }

    void Save()
    {
        var dup = FindDuplicate();
        if (dup != null)
        {
            SetStatus(dup, Bad);
            _tabs.SelectedIndex = 0;
            return;
        }
        try
        {
            StartupRegistration.Set(_runAtStartup);
        }
        catch (Exception ex)
        {
            SetStatus("자동 실행 설정 실패: " + ex.Message, Bad);
        }

        List<string> failed;
        try
        {
            failed = _apply(_s.Clone());
        }
        catch (Exception ex)
        {
            SetStatus("저장 실패: " + ex.Message, Bad);
            return;
        }

        if (failed.Count > 0)
            SetStatus("저장했지만 다른 프로그램이 쓰는 단축키가 있습니다:\n" + string.Join("\n", failed), Bad);
        else
            SetStatus($"저장했습니다 ({DateTime.Now:HH:mm:ss})", Ok);
    }

    void SetStatus(string text, Brush color)
    {
        _status.Text = text;
        _status.Foreground = color;
    }
}
