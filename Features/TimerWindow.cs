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
/// ↑↓ 1분, ←→ 10초, 휠 1분, Space 일시정지, R 다시 시작, M 작게/크게, Esc 닫기.
/// </summary>
public sealed class TimerWindow : Window
{
    readonly AppSettings S;
    readonly MonitorArea _mon;
    readonly DispatcherTimer _tick;
    TimeSpan _total;
    DateTime _endAt;
    TimeSpan _pausedRemaining;
    bool _paused;
    bool _finishedNotified;
    bool _mini;

    // 전체 화면
    readonly Grid _full = new();
    readonly TextBlock _message = new();
    readonly TextBlock _time = new();
    readonly TextBlock _sub = new();
    readonly TextBlock _keysHint = new();
    // 작은 창
    readonly Border _miniBox = new();
    readonly TextBlock _miniTime = new();

    readonly Brush _fg;

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

        _fg = S.TimerBackground == TimerBackground.White ? ColorUtil.Brush("#202124") : Brushes.White;
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
        PreviewKeyDown += OnKey;
        MouseWheel += (_, e) => Adjust(TimeSpan.FromMinutes(Math.Sign(e.Delta)));
        MouseLeftButtonDown += (_, e) =>
        {
            if (!_mini) return;
            if (e.ClickCount == 2) SetMini(false);
            else DragMove();
        };
    }

    void BuildFull(BitmapSource? shot)
    {
        switch (S.TimerBackground)
        {
            case TimerBackground.DimScreen when shot != null:
                var img = new Image
                {
                    Source = shot,
                    Stretch = Stretch.Fill,
                    Effect = new BlurEffect { Radius = 24, RenderingBias = RenderingBias.Performance },
                    Margin = new Thickness(-30), // 흐림 효과 가장자리가 비치지 않게
                };
                _full.Children.Add(img);
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

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _message.Text = S.TimerMessage;
        _message.Foreground = _fg;
        _message.FontSize = 48;
        _message.FontWeight = FontWeights.SemiBold;
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.Opacity = 0.9;

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

        stack.Children.Add(_message);
        stack.Children.Add(timeBox);
        stack.Children.Add(_sub);
        _full.Children.Add(stack);

        _keysHint.Text = "↑↓ 1분   ←→ 10초   Space 일시정지   R 다시 시작   M 작은 창   Esc 닫기";
        _keysHint.Foreground = _fg;
        _keysHint.Opacity = 0.45;
        _keysHint.FontSize = 16;
        _keysHint.HorizontalAlignment = HorizontalAlignment.Center;
        _keysHint.VerticalAlignment = VerticalAlignment.Bottom;
        _keysHint.Margin = new Thickness(0, 0, 0, 36);
        _full.Children.Add(_keysHint);
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
        _miniBox.ToolTip = "끌어서 옮기기 · 두 번 클릭하면 크게 · Esc 닫기";
        _miniBox.Cursor = Cursors.SizeAll;
    }

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

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Up: Adjust(TimeSpan.FromMinutes(1)); break;
            case Key.Down: Adjust(TimeSpan.FromMinutes(-1)); break;
            case Key.Right: Adjust(TimeSpan.FromSeconds(10)); break;
            case Key.Left: Adjust(TimeSpan.FromSeconds(-10)); break;
            case Key.Space:
                if (_paused) { _endAt = DateTime.Now + _pausedRemaining; _paused = false; }
                else { _pausedRemaining = Remaining > TimeSpan.Zero ? Remaining : TimeSpan.Zero; _paused = true; }
                UpdateDisplay();
                break;
            case Key.R:
                _paused = false;
                _endAt = DateTime.Now + _total;
                UpdateDisplay();
                break;
            case Key.M: SetMini(!_mini); break;
            default: e.Handled = false; break;
        }
    }

    void SetMini(bool mini)
    {
        _mini = mini;
        if (mini)
        {
            Content = _miniBox;
            double s = ScreenHelper.Scale(this);
            int w = (int)(250 * s), h = (int)(96 * s), m = (int)(24 * s);
            ScreenHelper.Place(this, new Int32Rect(
                _mon.WorkArea.X + _mon.WorkArea.Width - w - m,
                _mon.WorkArea.Y + _mon.WorkArea.Height - h - m, w, h));
        }
        else
        {
            Content = _full;
            ScreenHelper.Place(this, _mon.Bounds, activate: true);
            Activate();
        }
        Keyboard.Focus(this);
    }

#if DEBUG
    internal FrameworkElement TestFull { get { UpdateDisplay(); return _full; } }
    internal FrameworkElement TestMini { get { UpdateDisplay(); return _miniBox; } }
    internal void TestAdjust(TimeSpan d) => Adjust(d);
#endif
}
