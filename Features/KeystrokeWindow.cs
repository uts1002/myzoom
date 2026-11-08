using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>
/// 누른 키를 화면 아래쪽에 보여준다 (영상 녹화·연수용).
/// 기본은 "단축키만" 모드 — Ctrl/Alt/Win 조합과 F키·Esc 등만 보여 주고 일반 타자는 숨긴다(비밀번호 노출 방지).
/// </summary>
public sealed class KeystrokeWindow : Window
{
    const double PanelHeight = 150; // DIP
    readonly AppSettings S;
    readonly StackPanel _row = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 48),
    };
    MonitorArea _mon;

    // 연속 입력 묶기
    Border? _lastChip;
    TextBlock? _lastText;
    string _lastCombo = "";
    int _repeat;
    bool _lastWasTyping;
    DateTime _lastTime;

    static readonly HashSet<Key> ModifierKeySet = new()
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt,
        Key.LWin, Key.RWin, Key.System,
    };

    static readonly HashSet<Key> SpecialKeys = new()
    {
        Key.Escape, Key.Tab, Key.Return, Key.Back, Key.Delete, Key.Insert, Key.Home, Key.End,
        Key.PageUp, Key.PageDown, Key.Left, Key.Right, Key.Up, Key.Down, Key.Snapshot,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        Key.HangulMode, Key.HanjaMode,
    };

    public KeystrokeWindow(AppSettings settings)
    {
        S = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _row;

        _mon = ScreenHelper.FromCursor();
        Left = _mon.Bounds.X; Top = _mon.Bounds.Y; Width = 400; Height = PanelHeight;

        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlayStyle(this, noActivate: true);
            Native.SetClickThrough(this, true);
            PlaceOn(_mon);
        };
        Loaded += (_, _) =>
        {
            PlaceOn(_mon);
            GlobalKeyboardHook.Acquire();
            GlobalKeyboardHook.KeyDown += OnKey;
        };
        Closed += (_, _) =>
        {
            GlobalKeyboardHook.KeyDown -= OnKey;
            GlobalKeyboardHook.Release();
        };
    }

    void PlaceOn(MonitorArea mon)
    {
        _mon = mon;
        double s = IsLoaded ? ScreenHelper.Scale(this) : 1;
        int h = (int)(PanelHeight * s);
        ScreenHelper.Place(this, new Int32Rect(mon.WorkArea.X, mon.WorkArea.Y + mon.WorkArea.Height - h, mon.WorkArea.Width, h));
        // 배율이 다른 모니터로 옮긴 경우 한 번 더 (높이 재계산)
        double s2 = ScreenHelper.Scale(this);
        if (Math.Abs(s2 - s) > 0.01)
        {
            h = (int)(PanelHeight * s2);
            ScreenHelper.Place(this, new Int32Rect(mon.WorkArea.X, mon.WorkArea.Y + mon.WorkArea.Height - h, mon.WorkArea.Width, h));
        }
    }

    void OnKey(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        if (ModifierKeySet.Contains(key)) return;

        bool ctrl = Native.IsKeyDown(0x11);
        bool alt = Native.IsKeyDown(0x12);
        bool shift = Native.IsKeyDown(0x10);
        bool win = Native.IsKeyDown(0x5B) || Native.IsKeyDown(0x5C);
        bool combo = ctrl || alt || win;
        bool special = SpecialKeys.Contains(key);

        if (S.KeystrokeMode == KeystrokeMode.ShortcutsOnly && !combo && !special) return;

        // 키를 누른 모니터(커서가 있는 곳)로 따라간다
        var (cx, cy) = ScreenHelper.CursorPos();
        if (!_mon.Contains(cx, cy)) PlaceOn(ScreenHelper.FromPoint(cx, cy));

        var now = DateTime.Now;
        bool recent = (now - _lastTime).TotalSeconds < Math.Max(0.6, S.KeystrokeSeconds * 0.7);
        _lastTime = now;

        // 일반 타자(모든 키 모드)는 한 칩에 이어 붙인다
        if (!combo && !special)
        {
            string ch = TypedText(key, shift);
            if (_lastWasTyping && recent && _lastText != null && _lastChip != null && _row.Children.Contains(_lastChip))
            {
                string t = _lastText.Text + ch;
                _lastText.Text = t.Length > 28 ? "…" + t[^27..] : t;
                Refresh(_lastChip);
                return;
            }
            AddChip(ch, typing: true);
            return;
        }

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(KeyNames.Friendly(key));
        string text = string.Join(" + ", parts);

        if (text == _lastCombo && recent && _lastChip != null && _lastText != null && _row.Children.Contains(_lastChip))
        {
            _repeat++;
            _lastText.Text = $"{text}  ×{_repeat}";
            Refresh(_lastChip);
            return;
        }
        AddChip(text, typing: false);
        _lastCombo = text;
        _repeat = 1;
    }

    static string TypedText(Key key, bool shift)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            string c = key.ToString();
            return shift ? c : c.ToLowerInvariant();
        }
        if (key == Key.Space) return "␣";
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        return KeyNames.Friendly(key);
    }

    void AddChip(string text, bool typing)
    {
        // 줌·그리기 창보다 위에 보이도록 맨 위로 다시 올린다
        Native.SetWindowPos(Native.Handle(this), Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        AddChipCore(text, typing);
    }

    void AddChipCore(string text, bool typing)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = Math.Clamp(S.KeystrokeFontSize, 12, 72),
            FontWeight = FontWeights.SemiBold,
        };
        var chip = new Border
        {
            Child = tb,
            Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 8, 18, 10),
            Margin = new Thickness(6, 0, 6, 0),
        };
        _row.Children.Add(chip);
        while (_row.Children.Count > 4) _row.Children.RemoveAt(0);

        _lastChip = chip;
        _lastText = tb;
        _lastWasTyping = typing;
        if (typing) _lastCombo = "";
        Refresh(chip);
    }

    void Refresh(Border chip)
    {
        double hold = Math.Clamp(S.KeystrokeSeconds, 0.5, 10);
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + 0.4))));
        anim.Completed += (_, _) =>
        {
            if (chip.Opacity <= 0.01) _row.Children.Remove(chip);
        };
        chip.BeginAnimation(OpacityProperty, anim);
    }

#if DEBUG
    internal FrameworkElement TestRow => _row;
    internal void TestChip(string text) => AddChipCore(text, typing: false);
#endif
}
