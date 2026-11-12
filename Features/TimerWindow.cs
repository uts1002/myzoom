using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>
/// 쉬는 시간·활동 시간 타이머. 전체 화면 / 작은 창(M) 두 가지.
/// 타이머가 떠 있는 상태에서 제목 바꾸기·다른 모니터로 옮기기·시간 조절을 버튼이나 키로 바로 한다.
/// T 제목, Tab 다른 화면, ↑↓ 1분, ←→ 10초, 휠 1분, Space 일시정지, R 다시 시작, M 작게/크게, Esc 닫기.
/// </summary>
public sealed class TimerWindow : Window
{
    readonly AppSettings S;
    MonitorArea _mon;
    readonly DispatcherTimer _tick;
    TimeSpan _total;
    DateTime _endAt;
    TimeSpan _pausedRemaining;
    bool _paused;
    bool _finishedNotified;
    bool _mini;
    bool _editingTitle;

    // 전체 화면
    readonly Grid _full = new();
    Image? _bgImage;
    readonly TextBlock _message = new();
    readonly TextBox _messageEdit = new();
    readonly TextBlock _time = new();
    readonly TextBlock _sub = new();
    readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    TextBlock _pauseLabel = null!;
    // 작은 창
    readonly Border _miniBox = new();
    readonly TextBlock _miniTime = new();

    readonly Brush _fg;
    readonly bool _lightBg;
    // 화면 높이에 맞춰 글자·버튼 크기를 같은 비율로 (배율 100% 큰 모니터에서도 작아 보이지 않게)
    readonly ScaleTransform _contentScale = new(1, 1);

    public TimerWindow(AppSettings settings, BitmapSource? screenshot)
    {
        S = settings;
        _mon = ScreenHelper.FromCursor();
        _total = TimeSpan.FromMinutes(Math.Clamp(S.TimerMinutes, 1, 600));
        _endAt = DateTime.Now + _total;

        Title = "MyZoom 타이머";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _mon.Bounds.X; Top = _mon.Bounds.Y; Width = 800; Height = 600;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);

        _lightBg = S.TimerBackground == TimerBackground.White;
        _fg = _lightBg ? ColorUtil.Brush("#202124") : Brushes.White;
        BuildFull(screenshot);
        BuildMini();
        Content = _full;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => UpdateDisplay();

        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlayStyle(this, noActivate: false);
            ScreenHelper.Place(this, _mon.Bounds, activate: true);
        };
        Loaded += (_, _) =>
        {
            ScreenHelper.Place(this, _mon.Bounds, activate: true);
            Activate();
            Native.SetForegroundWindow(Native.Handle(this));
            Keyboard.Focus(this);
            UpdateDisplay();
            _tick.Start();
        };
        Closed += (_, _) => _tick.Stop();
        _full.SizeChanged += (_, _) =>
        {
            double k = Math.Clamp(_full.ActualHeight / 960.0, 0.7, 2.5); // 960 = 기준 화면 높이(DIP)
            _contentScale.ScaleX = _contentScale.ScaleY = k;
        };
        PreviewKeyDown += OnKey;
        MouseWheel += (_, e) => { if (!_editingTitle) Adjust(TimeSpan.FromMinutes(Math.Sign(e.Delta))); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!_mini) return;
            if (e.ClickCount == 2) SetMini(false);
            else DragMove();
        };
    }

    // =====================================================================
    // 화면 구성
    // =====================================================================

    void BuildFull(BitmapSource? shot)
    {
        switch (S.TimerBackground)
        {
            case TimerBackground.DimScreen when shot != null:
                _bgImage = new Image
                {
                    Source = shot,
                    Stretch = Stretch.Fill,
                    Effect = new BlurEffect { Radius = 24, RenderingBias = RenderingBias.Performance },
                    Margin = new Thickness(-30), // 흐림 효과 가장자리가 비치지 않게
                };
                _full.Children.Add(_bgImage);
                _full.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) });
                _full.ClipToBounds = true;
                break;
            case TimerBackground.White:
                _full.Background = Brushes.White;
                break;
            default:
                _full.Background = ColorUtil.Brush("#111214");
                break;
        }

        // ---- 제목 (누르면 그 자리에서 바꾸기) ----
        _message.Foreground = _fg;
        _message.FontSize = 48;
        _message.FontWeight = FontWeights.SemiBold;
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.Opacity = 0.9;
        _message.Cursor = Cursors.Hand;
        _message.ToolTip = "눌러서 제목 바꾸기 (T)";
        _message.Background = Brushes.Transparent; // 글자 사이 빈 곳도 눌리게
        _message.MinWidth = 200;
        _message.TextAlignment = TextAlignment.Center;
        _message.MouseLeftButtonUp += (_, e) => { e.Handled = true; BeginEditTitle(); };

        _messageEdit.FontSize = 48;
        _messageEdit.FontWeight = FontWeights.SemiBold;
        _messageEdit.Foreground = _fg;
        _messageEdit.CaretBrush = _fg;
        _messageEdit.Background = new SolidColorBrush(_lightBg ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255));
        _messageEdit.BorderBrush = _fg;
        _messageEdit.BorderThickness = new Thickness(0, 0, 0, 2);
        _messageEdit.MinWidth = 420;
        _messageEdit.MaxLength = 40;
        _messageEdit.HorizontalContentAlignment = HorizontalAlignment.Center;
        _messageEdit.HorizontalAlignment = HorizontalAlignment.Center;
        _messageEdit.Padding = new Thickness(12, 0, 12, 0);
        _messageEdit.Visibility = Visibility.Collapsed;
        _messageEdit.LostKeyboardFocus += (_, _) => EndEditTitle(commit: true);

        var titleHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        titleHost.Children.Add(_message);
        titleHost.Children.Add(_messageEdit);

        _time.Foreground = _fg;
        _time.FontFamily = new FontFamily("Segoe UI");
        _time.FontWeight = FontWeights.SemiBold;
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        Typography.SetNumeralAlignment(_time, FontNumeralAlignment.Tabular);
        var timeBox = new Viewbox { Child = _time, Height = 300, Margin = new Thickness(0, 4, 0, 4) };

        _sub.Foreground = _fg;
        _sub.FontSize = 26;
        _sub.Opacity = 0.75;
        _sub.HorizontalAlignment = HorizontalAlignment.Center;

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, LayoutTransform = _contentScale };
        stack.Children.Add(titleHost);
        stack.Children.Add(timeBox);
        stack.Children.Add(_sub);
        _full.Children.Add(stack);

        // ---- 아래쪽 조작 버튼 ----
        _bar.Children.Add(BarButton("제목 바꾸기", "T", BeginEditTitle));
        if (ScreenHelper.All().Count > 1)
            _bar.Children.Add(BarButton("다른 화면으로", "Tab", MoveToNextMonitor));
        _bar.Children.Add(BarButton("−1분", "↓", () => Adjust(TimeSpan.FromMinutes(-1))));
        _bar.Children.Add(BarButton("+1분", "↑", () => Adjust(TimeSpan.FromMinutes(1))));
        var pause = BarButton("일시정지", "Space", TogglePause);
        _pauseLabel = (TextBlock)((StackPanel)pause.Child).Children[0];
        _bar.Children.Add(pause);
        _bar.Children.Add(BarButton("다시 시작", "R", Restart));
        _bar.Children.Add(BarButton("작은 창", "M", () => SetMini(true)));
        _bar.Children.Add(BarButton("닫기", "Esc", Close));

        var hint = new TextBlock
        {
            Text = "←→ 10초 · 휠 1분",
            Foreground = _fg,
            Opacity = 0.4,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 32), LayoutTransform = _contentScale };
        bottom.Children.Add(_bar);
        bottom.Children.Add(hint);
        _full.Children.Add(bottom);
    }

    Border BarButton(string label, string key, Action onClick)
    {
        var normal = new SolidColorBrush(_lightBg ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(34, 255, 255, 255));
        var hover = new SolidColorBrush(_lightBg ? Color.FromArgb(34, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255));
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = label, Foreground = _fg, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = key, Foreground = _fg, Opacity = 0.5, FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            Child = content,
            Background = normal,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(5, 0, 5, 0),
            Cursor = Cursors.Hand,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    void BuildMini()
    {
        _miniTime.Foreground = Brushes.White;
        _miniTime.FontSize = 40;
        _miniTime.FontWeight = FontWeights.SemiBold;
        _miniTime.HorizontalAlignment = HorizontalAlignment.Center;
        _miniTime.VerticalAlignment = VerticalAlignment.Center;
        Typography.SetNumeralAlignment(_miniTime, FontNumeralAlignment.Tabular);
        _miniBox.Child = _miniTime;
        _miniBox.Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 30));
        _miniBox.CornerRadius = new CornerRadius(14);
        _miniBox.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        _miniBox.BorderThickness = new Thickness(1);
        _miniBox.ToolTip = "끌어서 옮기기 (다른 모니터로도) · 두 번 클릭하면 크게 · Tab 다른 화면 · Esc 닫기";
        _miniBox.Cursor = Cursors.SizeAll;
    }

    // =====================================================================
    // 시간
    // =====================================================================

    TimeSpan Remaining => _paused ? _pausedRemaining : _endAt - DateTime.Now;

    void UpdateDisplay()
    {
        var rem = Remaining;
        string text;
        if (rem > TimeSpan.Zero)
        {
            var r = TimeSpan.FromSeconds(Math.Ceiling(rem.TotalSeconds));
            text = r.TotalHours >= 1 ? r.ToString(@"h\:mm\:ss") : r.ToString(@"mm\:ss");
            _time.Foreground = _fg;
            _miniTime.Foreground = Brushes.White;
            _message.Text = _paused ? "일시정지" : S.TimerMessage;
            _sub.Text = _paused ? "Space 를 누르면 계속" : (S.TimerShowEndTime ? $"{_endAt:HH:mm} 에 끝나요" : "");
            _finishedNotified = false;
        }
        else
        {
            var over = -rem;
            text = "+" + over.ToString(@"mm\:ss");
            var red = ColorUtil.Brush("#EF5350");
            _time.Foreground = red;
            _miniTime.Foreground = red;
            _message.Text = "시간이 다 됐어요";
            _sub.Text = "지난 시간";
            if (!_finishedNotified)
            {
                _finishedNotified = true;
                if (S.TimerSound) SystemSounds.Exclamation.Play();
                if (_mini) SetMini(false); // 끝나면 크게 보여 준다
            }
        }
        _time.Text = text;
        _miniTime.Text = text;
        if (_pauseLabel != null) _pauseLabel.Text = _paused ? "계속" : "일시정지";
    }

    void Adjust(TimeSpan delta)
    {
        if (_paused)
        {
            _pausedRemaining += delta;
            if (_pausedRemaining < TimeSpan.Zero) _pausedRemaining = TimeSpan.Zero;
        }
        else
        {
            var rem = _endAt - DateTime.Now;
            if (rem < TimeSpan.Zero) rem = TimeSpan.Zero; // 시간 종료 후 ↑ 를 누르면 새로 시작
            rem += delta;
            if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
            _endAt = DateTime.Now + rem;
        }
        UpdateDisplay();
    }

    void TogglePause()
    {
        if (_paused) { _endAt = DateTime.Now + _pausedRemaining; _paused = false; }
        else { _pausedRemaining = Remaining > TimeSpan.Zero ? Remaining : TimeSpan.Zero; _paused = true; }
        UpdateDisplay();
    }

    void Restart()
    {
        _paused = false;
        _endAt = DateTime.Now + _total;
        UpdateDisplay();
    }

    // =====================================================================
    // 제목 바꾸기
    // =====================================================================

    void BeginEditTitle()
    {
        if (_mini) SetMini(false);
        if (_editingTitle) return;
        _editingTitle = true;
        _messageEdit.Text = S.TimerMessage;
        _message.Visibility = Visibility.Hidden;
        _messageEdit.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            _messageEdit.Focus();
            Keyboard.Focus(_messageEdit);
            _messageEdit.SelectAll();
        }, DispatcherPriority.Input);
    }

    void EndEditTitle(bool commit)
    {
        if (!_editingTitle) return;
        _editingTitle = false;
        if (commit)
        {
            S.TimerMessage = _messageEdit.Text.Trim();
            try { S.Save(); } catch { } // 다음 타이머에도 이 제목으로
        }
        _messageEdit.Visibility = Visibility.Collapsed;
        _message.Visibility = Visibility.Visible;
        Keyboard.Focus(this);
        UpdateDisplay();
    }

    // =====================================================================
    // 다른 모니터로
    // =====================================================================

    void MoveToNextMonitor()
    {
        var all = ScreenHelper.All();
        if (all.Count < 2) return;
        int idx = all.FindIndex(m => m.Name == _mon.Name);
        MoveTo(all[(idx + 1) % all.Count]);
    }

    void MoveTo(MonitorArea target)
    {
        if (target.Name != _mon.Name && _bgImage != null)
        {
            // 흐린 배경은 옮겨 갈 모니터 화면으로 다시 찍는다
            try { _bgImage.Source = ScreenHelper.Capture(target.Bounds); } catch { }
        }
        _mon = target;
        if (_mini) PlaceMini();
        else ScreenHelper.Place(this, _mon.Bounds, activate: true);
        Activate();
        Keyboard.Focus(this);
    }

    // =====================================================================
    // 키보드
    // =====================================================================

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_editingTitle)
        {
            if (e.Key == Key.Enter) { EndEditTitle(commit: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEditTitle(commit: false); e.Handled = true; }
            return; // 나머지는 글자 입력
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Up: Adjust(TimeSpan.FromMinutes(1)); break;
            case Key.Down: Adjust(TimeSpan.FromMinutes(-1)); break;
            case Key.Right: Adjust(TimeSpan.FromSeconds(10)); break;
            case Key.Left: Adjust(TimeSpan.FromSeconds(-10)); break;
            case Key.Space: TogglePause(); break;
            case Key.R: Restart(); break;
            case Key.M: SetMini(!_mini); break;
            case Key.T:
            case Key.F2:
                BeginEditTitle();
                break;
            case Key.Tab: MoveToNextMonitor(); break;
            default: e.Handled = false; break;
        }
    }

    // =====================================================================
    // 작은 창
    // =====================================================================

    void SetMini(bool mini)
    {
        if (mini && _editingTitle) EndEditTitle(commit: true);
        _mini = mini;
        if (mini)
        {
            Content = _miniBox;
            PlaceMini();
        }
        else
        {
            // 작은 창을 다른 모니터로 끌어다 놓았으면 그 모니터에서 크게
            Native.GetWindowRect(Native.Handle(this), out var r);
            var here = ScreenHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            Content = _full;
            if (here.Name != _mon.Name) MoveTo(here);
            else ScreenHelper.Place(this, _mon.Bounds, activate: true);
            Activate();
        }
        Keyboard.Focus(this);
    }

    void PlaceMini()
    {
        // 배율이 다른 모니터로 옮기면 첫 배치 후 배율이 바뀌므로 한 번 더 계산해서 놓는다
        for (int i = 0; i < 2; i++)
        {
            double s = ScreenHelper.Scale(this);
            int w = (int)(250 * s), h = (int)(96 * s), m = (int)(24 * s);
            ScreenHelper.Place(this, new Int32Rect(
                _mon.WorkArea.X + _mon.WorkArea.Width - w - m,
                _mon.WorkArea.Y + _mon.WorkArea.Height - h - m, w, h));
        }
    }

#if DEBUG
    internal FrameworkElement TestFull { get { UpdateDisplay(); return _full; } }
    internal FrameworkElement TestMini { get { UpdateDisplay(); return _miniBox; } }
    internal void TestAdjust(TimeSpan d) => Adjust(d);
#endif
}
