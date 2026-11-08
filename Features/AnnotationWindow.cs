using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MyZoom.Core;
using MyZoom.Interop;
using Path = System.Windows.Shapes.Path;

namespace MyZoom.Features;

public enum AnnotationMode { Zoom, Draw, LiveDraw, Snip }
public enum DrawTool { Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Eraser }

/// <summary>
/// 확대·그리기·화면 위 그리기·영역 캡처를 모두 처리하는 전체 화면 창.
///  - Zoom/Draw/Snip : 누른 순간의 화면을 캡처해서 멈춘 화면 위에서 동작
///  - LiveDraw       : 투명 창이라 아래 화면이 계속 움직인다
/// 좌표계: _content 에 Scale→Translate 변환을 걸고, 그림(_ink)도 그 안에 있어 확대와 함께 움직인다.
/// </summary>
public sealed class AnnotationWindow : Window
{
    readonly AppSettings S;
    readonly MonitorArea _mon;
    readonly BitmapSource? _shot;
    public AnnotationMode Mode { get; }
    bool IsLive => Mode == AnnotationMode.LiveDraw;
    bool IsSnip => Mode == AnnotationMode.Snip;

    /// <summary>토스트로 띄울 메시지 (저장 경로 등).</summary>
    public event Action<string>? Notify;

    // ---- 화면 구성 ----
    readonly Grid _root = new();
    readonly Grid _viewport = new() { ClipToBounds = true };
    readonly Grid _content = new();
    readonly Rectangle _board = new() { Visibility = Visibility.Collapsed };
    readonly Canvas _ink = new();
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly Ellipse _cursorDot = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly ScaleTransform _scaleT = new();
    readonly TranslateTransform _transT = new();
    readonly TranslateTransform _toolbarOffset = new();
    Border _toolbar = null!;
    Border _hint = null!;
    TextBlock _hintText = null!;
    Border _zoomBadge = null!;
    TextBlock _zoomBadgeText = null!;
    Border? _help;
    readonly Dictionary<DrawTool, Border> _toolButtons = new();
    readonly List<(Color Color, Border Button)> _colorButtons = new();
    Ellipse _widthDot = null!;
    TextBlock _widthText = null!;

    // ---- 확대 상태 ----
    double _scale = 1, _target = 1;
    readonly Vector? _initTranslatePx;  // 라이브 줌에서 넘어올 때의 화면 이동량 (픽셀)
    bool _follow;                 // true: 마우스를 따라 화면이 움직이는 ZoomIt 방식
    Point _anchorView, _anchorContent;

    // ---- 그리기 상태 ----
    DrawTool _tool = DrawTool.Pen;
    Color _color;
    double _penWidth, _hlWidth, _textSize;
    bool _toolbarOn;
    bool _overToolbar;
    Path? _active;
    PathFigure? _fig;
    DrawTool _activeShape;
    bool _activeHl;
    Point _startPt, _lastPt;
    bool _erasing;
    UndoEntry? _eraseEntry;
    TextBox? _editingText;
    bool _panning;
    Point _panStart;
    Vector _panStartT;
    bool _passThrough;
    bool _draggingToolbar;
    Point _toolbarDragStart;
    Vector _toolbarDragOrigin;
    readonly Stack<UndoEntry> _undo = new();
    readonly Stack<UndoEntry> _redo = new();

    // ---- 영역 캡처 ----
    bool _snipping;
    Point _snipStart;
    readonly Rectangle _snipRect = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Visibility = Visibility.Collapsed };
    readonly Path _snipShade = new() { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) };
    readonly TextBlock _snipLabel = new() { Foreground = Brushes.White, FontSize = 13, Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed };

    sealed class UndoEntry
    {
        public List<UIElement> Added { get; } = new();
        public List<(UIElement El, int Index)> Removed { get; } = new();
    }

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public AnnotationWindow(AppSettings settings, AnnotationMode mode, MonitorArea mon, BitmapSource? shot,
        double initScale = 1, Vector? initTranslatePx = null)
    {
        S = settings;
        Mode = mode;
        _mon = mon;
        _shot = shot;

        Title = "MyZoom";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = mon.Bounds.X; Top = mon.Bounds.Y; Width = 800; Height = 600; // 실제 위치는 SourceInitialized 에서 픽셀 단위로 맞춘다
        if (IsLive)
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
        }
        else
        {
            Background = Brushes.Black;
        }
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);

        _color = ColorUtil.Parse(S.DefaultColor);
        _penWidth = S.PenWidth;
        _hlWidth = S.HighlighterWidth;
        _textSize = S.TextSize;
        _toolbarOn = S.ShowToolbar;

        // 시작 배율
        if (mode == AnnotationMode.Zoom)
        {
            _follow = true;
            _target = Math.Clamp(S.InitialZoom, 1, S.MaxZoom);
            _scale = S.AnimateZoom ? 1 : _target;
        }
        else
        {
            _scale = _target = Math.Max(1, initScale);
        }

        _initTranslatePx = initTranslatePx;
        BuildVisuals();
        _scaleT.ScaleX = _scaleT.ScaleY = _scale;

        SourceInitialized += (_, _) =>
        {
            ScreenHelper.Place(this, _mon.Bounds, activate: true);
            Native.MakeOverlayStyle(this, noActivate: false);
        };
        Loaded += OnLoaded;
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnMouseWheel;
        _root.PreviewMouseMove += (_, e) => MoveCursorDot(e.GetPosition(_overlay));
        _root.MouseLeave += (_, _) => _cursorDot.Visibility = Visibility.Collapsed;
        _viewport.MouseDown += OnViewportMouseDown;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseUp += OnViewportMouseUp;
        _viewport.LostMouseCapture += (_, _) => FinishActiveStroke();
    }

    // =====================================================================
    // 화면 구성
    // =====================================================================

    void BuildVisuals()
    {
        Content = _root;
        _root.Children.Add(_viewport);
        _viewport.Background = IsLive ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        _viewport.Children.Add(_content);

        var tg = new TransformGroup();
        tg.Children.Add(_scaleT);
        tg.Children.Add(_transT);
        _content.RenderTransform = tg;

        if (_shot != null)
        {
            var img = new Image { Source = _shot, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _content.Children.Add(img);
        }
        _content.Children.Add(_board);
        _content.Children.Add(_ink);

        _root.Children.Add(_overlay);
        _overlay.Children.Add(_cursorDot);

        if (IsSnip)
        {
            _overlay.Children.Add(_snipShade);
            _overlay.Children.Add(_snipRect);
            _overlay.Children.Add(_snipLabel);
        }

        // 확대 배율 표시 (오른쪽 위)
        _zoomBadgeText = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
        _zoomBadge = new Border
        {
            Child = _zoomBadgeText,
            Background = new SolidColorBrush(Color.FromArgb(200, 32, 33, 36)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 24, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_zoomBadge);

        // 안내 말풍선 (위쪽 가운데)
        _hintText = new TextBlock { Foreground = Brushes.White, FontSize = 15, TextAlignment = TextAlignment.Center };
        _hint = new Border
        {
            Child = _hintText,
            Background = new SolidColorBrush(Color.FromArgb(215, 32, 33, 36)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_hint);

        if (!IsSnip)
        {
            _toolbar = BuildToolbar();
            _root.Children.Add(_toolbar);
        }
        else
        {
            _toolbar = new Border();
        }
    }

    Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var grip = new TextBlock
        {
            Text = "⋮⋮",
            Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
            Cursor = Cursors.SizeAll,
            ToolTip = "끌어서 도구 막대 옮기기",
            Background = Brushes.Transparent,
        };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            _draggingToolbar = true;
            _toolbarDragStart = e.GetPosition(_root);
            _toolbarDragOrigin = new Vector(_toolbarOffset.X, _toolbarOffset.Y);
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (!_draggingToolbar) return;
            var d = e.GetPosition(_root) - _toolbarDragStart;
            _toolbarOffset.X = _toolbarDragOrigin.X + d.X;
            _toolbarOffset.Y = _toolbarDragOrigin.Y + d.Y;
        };
        grip.MouseLeftButtonUp += (_, _) => { _draggingToolbar = false; grip.ReleaseMouseCapture(); };
        panel.Children.Add(grip);

        void AddTool(DrawTool tool, UIElement icon, string tip)
        {
            var b = MakeButton(icon, tip, () => SetTool(tool));
            _toolButtons[tool] = b;
            panel.Children.Add(b);
        }

        AddTool(DrawTool.Pen, Glyph(""), "펜 (1)");
        AddTool(DrawTool.Highlighter, Glyph(""), "형광펜 (2)  ·  Shift+색 키로도 선택");
        AddTool(DrawTool.Line, IconPath("M3,13 L13,3"), "직선 (3)  ·  펜으로 Shift+드래그");
        AddTool(DrawTool.Arrow, IconPath("M3,13 L12.5,3.5 M6.5,3.5 L12.5,3.5 L12.5,9.5"), "화살표 (4)  ·  펜으로 Ctrl+Shift+드래그");
        AddTool(DrawTool.Rectangle, IconPath("M2.5,4 L13.5,4 L13.5,12 L2.5,12 Z"), "사각형 (5)  ·  펜으로 Ctrl+드래그");
        AddTool(DrawTool.Ellipse, new Ellipse { Width = 14, Height = 11, Stroke = Brushes.White, StrokeThickness = 1.6 }, "원 (6)  ·  펜으로 Alt+드래그");
        AddTool(DrawTool.Text, new TextBlock { Text = "가", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("맑은 고딕") }, "글자 (7 또는 T)");
        AddTool(DrawTool.Eraser, Glyph(""), "지우개 (8)  ·  선을 문지르면 지워짐");

        panel.Children.Add(Separator());

        void AddColor(string hex, string tip)
        {
            var c = ColorUtil.Parse(hex);
            var swatch = new Ellipse { Width = 16, Height = 16, Fill = ColorUtil.Brush(c), Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), StrokeThickness = 1 };
            var b = MakeButton(swatch, tip, () => PickColor(c, false), width: 28);
            _colorButtons.Add((c, b));
            panel.Children.Add(b);
        }
        AddColor(S.ColorRed, "빨강 (R)");
        AddColor(S.ColorGreen, "초록 (G)");
        AddColor(S.ColorBlue, "파랑 (B)");
        AddColor(S.ColorYellow, "노랑 (Y)");
        AddColor(S.ColorOrange, "주황 (O)");
        AddColor(S.ColorPink, "분홍 (P)");
        AddColor("#111111", "검정");
        AddColor("#FFFFFF", "흰색");

        panel.Children.Add(Separator());

        panel.Children.Add(MakeButton(Glyph("", 12), "가늘게  ( [ 또는 Ctrl+휠 )", () => ChangeWidth(-1), width: 26));
        var widthBox = new Grid { Width = 30, Height = 34 };
        _widthDot = new Ellipse { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _widthText = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -1) };
        widthBox.Children.Add(_widthDot);
        widthBox.Children.Add(_widthText);
        widthBox.ToolTip = "현재 굵기";
        panel.Children.Add(widthBox);
        panel.Children.Add(MakeButton(Glyph("", 12), "굵게  ( ] 또는 Ctrl+휠 )", () => ChangeWidth(1), width: 26));

        panel.Children.Add(Separator());

        panel.Children.Add(MakeButton(new Rectangle { Width = 17, Height = 12, Fill = Brushes.White, RadiusX = 2, RadiusY = 2 }, "화이트보드 (W)", () => ToggleBoard(Colors.White)));
        panel.Children.Add(MakeButton(new Rectangle { Width = 17, Height = 12, Fill = ColorUtil.Brush("#1B1B1B"), Stroke = Brushes.Gray, StrokeThickness = 1, RadiusX = 2, RadiusY = 2 }, "칠판 (K)", () => ToggleBoard(Color.FromRgb(0x1B, 0x2A, 0x22))));
        panel.Children.Add(MakeButton(Glyph(""), "되돌리기 (Ctrl+Z)", Undo));
        panel.Children.Add(MakeButton(Glyph(""), "다시 실행 (Ctrl+Y)", Redo));
        panel.Children.Add(MakeButton(Glyph(""), "모두 지우기 (E)", ClearAll));

        panel.Children.Add(Separator());

        panel.Children.Add(MakeButton(Glyph(""), "그림 저장 (Ctrl+S)", SaveImage));
        panel.Children.Add(MakeButton(Glyph(""), "클립보드 복사 (Ctrl+C)", CopyImage));
        panel.Children.Add(MakeButton(Glyph(""), "단축키 보기 (F1)", ToggleHelp));
        panel.Children.Add(MakeButton(Glyph(""), "끝내기 (Esc)", Close));

        var border = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromArgb(235, 32, 33, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 32),
            Cursor = Cursors.Arrow,
            RenderTransform = _toolbarOffset,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35 },
        };
        border.MouseEnter += (_, _) => { _overToolbar = true; _cursorDot.Visibility = Visibility.Collapsed; };
        border.MouseLeave += (_, _) => _overToolbar = false;
        // 도구 막대 위 클릭이 그림판으로 새지 않게
        border.MouseDown += (_, e) => e.Handled = true;
        border.MouseUp += (_, e) => e.Handled = true;
        return border;
    }

    static TextBlock Glyph(string glyph, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static Path IconPath(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Brushes.White,
        StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 16,
        Height = 16,
    };

    static UIElement Separator() => new Rectangle
    {
        Width = 1,
        Height = 22,
        Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
        Margin = new Thickness(5, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    static readonly Brush HoverBrush = ColorUtil.Brush(Color.FromArgb(45, 255, 255, 255));
    static readonly Brush SelectedBrush = ColorUtil.Brush(Color.FromArgb(85, 255, 255, 255));

    Border MakeButton(UIElement icon, string tip, Action onClick, double width = 34)
    {
        var b = new Border
        {
            Width = width,
            Height = 34,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = icon,
            ToolTip = tip,
            Margin = new Thickness(1, 0, 1, 0),
        };
        if (icon is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        b.MouseEnter += (_, _) => { if (b.Tag is not true) b.Background = HoverBrush; };
        b.MouseLeave += (_, _) => { if (b.Tag is not true) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    static void SetSelected(Border b, bool on)
    {
        b.Tag = on;
        b.Background = on ? SelectedBrush : Brushes.Transparent;
    }

    // =====================================================================
    // 시작·프레임
    // =====================================================================

    void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ScreenHelper.Place(this, _mon.Bounds, activate: true);
        if (_initTranslatePx is { } t)
        {
            double sc = ScreenHelper.Scale(this);
            UpdateLayout();
            ApplyTransform(t.X / sc, t.Y / sc);
        }
        CompositionTarget.Rendering += OnFrame;
        Activate();
        Native.SetForegroundWindow(Native.Handle(this));
        Keyboard.Focus(this);

        if (IsSnip) UpdateSnipShade(Rect.Empty);
        ApplyToolUi();

        string hint = Mode switch
        {
            AnnotationMode.Zoom => "확대 · 휠로 배율 조절 · 클릭하면 그리기 · Esc 끝내기",
            AnnotationMode.Draw => "그리기 · F1 단축키 보기 · Esc 끝내기",
            AnnotationMode.LiveDraw => "화면 위 그리기 · 같은 단축키를 다시 누르면 그림을 남긴 채 화면 조작 · Esc 끝내기",
            _ => "드래그해서 캡처할 영역을 고르세요 · Esc 취소",
        };
        if (S.ShowModeHint || IsSnip) ShowHint(hint);
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;
        double w = _viewport.ActualWidth, h = _viewport.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double prev = _scale;
        if (Math.Abs(_target - _scale) > 0.0005)
        {
            _scale = S.AnimateZoom ? _scale + (_target - _scale) * 0.22 : _target;
            if (Math.Abs(_target - _scale) < 0.002) _scale = _target;
        }

        double tx, ty;
        if (_follow)
        {
            Point p;
            try
            {
                var (cx, cy) = ScreenHelper.CursorPos();
                p = _viewport.PointFromScreen(new Point(cx, cy));
            }
            catch { return; }
            p.X = Math.Clamp(p.X, 0, w);
            p.Y = Math.Clamp(p.Y, 0, h);
            tx = -p.X * (_scale - 1);
            ty = -p.Y * (_scale - 1);
        }
        else if (prev != _scale)
        {
            tx = _anchorView.X - _anchorContent.X * _scale;
            ty = _anchorView.Y - _anchorContent.Y * _scale;
        }
        else
        {
            return;
        }
        ApplyTransform(tx, ty);
    }

    void ApplyTransform(double tx, double ty)
    {
        double w = _viewport.ActualWidth, h = _viewport.ActualHeight;
        _scaleT.ScaleX = _scaleT.ScaleY = _scale;
        _transT.X = Math.Clamp(tx, w - w * _scale, 0);
        _transT.Y = Math.Clamp(ty, h - h * _scale, 0);
    }

    // =====================================================================
    // 확대
    // =====================================================================

    public bool IsFollowing => _follow;
    public bool IsPassThrough => _passThrough;

    /// <summary>확대 모드에서 그리기 모드로 (화면 고정).</summary>
    public void LockForDrawing()
    {
        Lock();
        if (S.ShowModeHint) ShowHint("그리기 · F1 단축키 보기 · Esc 끝내기");
    }

    void Lock()
    {
        if (!_follow) return;
        _follow = false;
        _target = _scale;
        ApplyToolUi();
    }

    void ZoomBy(double factor, Point? viewAnchor = null)
    {
        if (IsLive || IsSnip) return;
        double nt = Math.Clamp(_target * factor, 1, Math.Max(1, S.MaxZoom));
        if (Math.Abs(nt - _target) < 0.0001) return;
        if (!_follow)
        {
            var a = viewAnchor ?? new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
            _anchorView = a;
            _anchorContent = new Point((a.X - _transT.X) / _scale, (a.Y - _transT.Y) / _scale);
        }
        _target = nt;
        ShowZoomBadge($"{_target:0.0}×");
    }

    void ZoomReset()
    {
        if (IsLive || IsSnip) return;
        ZoomBy(1 / _target, Mouse.GetPosition(_viewport));
    }

    void ShowZoomBadge(string text)
    {
        _zoomBadgeText.Text = text;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3))));
        _zoomBadge.BeginAnimation(OpacityProperty, anim);
    }

    public void ShowHint(string text, double seconds = 2.8)
    {
        _hintText.Text = text;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15))));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + 0.5))));
        _hint.BeginAnimation(OpacityProperty, anim);
    }

    // =====================================================================
    // 도구·색·굵기
    // =====================================================================

    void SetTool(DrawTool tool)
    {
        CommitText();
        _tool = tool;
        Lock();
        ApplyToolUi();
    }

    void PickColor(Color c, bool highlighter)
    {
        _color = c;
        if (highlighter) _tool = DrawTool.Highlighter;
        else if (_tool is DrawTool.Eraser or DrawTool.Highlighter) _tool = DrawTool.Pen;
        if (_editingText != null)
        {
            _editingText.Foreground = ColorUtil.Brush(c);
            _editingText.CaretBrush = ColorUtil.Brush(c);
        }
        Lock();
        ApplyToolUi();
    }

    void ChangeWidth(int dir)
    {
        switch (_tool)
        {
            case DrawTool.Highlighter:
                _hlWidth = Math.Clamp(_hlWidth + dir * 4, 6, 90);
                break;
            case DrawTool.Text:
                _textSize = Math.Clamp(_textSize + dir * 4, 12, 160);
                if (_editingText != null) _editingText.FontSize = _textSize / _scale;
                break;
            default:
                double step = _penWidth < 6 ? 1 : 2;
                _penWidth = Math.Clamp(_penWidth + dir * step, 1, 40);
                break;
        }
        ApplyToolUi();
        if (_follow) return;
        ShowZoomBadge(_tool == DrawTool.Text ? $"글자 {_textSize:0}" : $"굵기 {CurrentWidth():0}");
    }

    double CurrentWidth() => _tool == DrawTool.Highlighter ? _hlWidth : _penWidth;

    void ApplyToolUi()
    {
        foreach (var (tool, b) in _toolButtons) SetSelected(b, tool == _tool);
        foreach (var (c, b) in _colorButtons) SetSelected(b, c == _color);

        if (_widthDot != null)
        {
            double shown = _tool == DrawTool.Text ? Math.Min(18, _textSize / 3) : Math.Min(20, Math.Max(3, CurrentWidth()));
            _widthDot.Width = _widthDot.Height = shown;
            _widthDot.Fill = ColorUtil.Brush(_color, _tool == DrawTool.Highlighter ? S.HighlighterOpacity + 0.2 : 1);
            _widthDot.Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
            _widthText.Text = _tool == DrawTool.Text ? $"{_textSize:0}" : $"{CurrentWidth():0}";
        }

        UpdateToolbarVisibility();
        UpdateCursorVisual();
    }

    void UpdateToolbarVisibility()
    {
        if (IsSnip) return;
        _toolbar.Visibility = (!_follow && !_passThrough && _toolbarOn) ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateCursorVisual()
    {
        if (IsSnip)
        {
            Cursor = Cursors.Cross;
            return;
        }
        if (_follow || _passThrough)
        {
            Cursor = Cursors.Arrow;
            _cursorDot.Visibility = Visibility.Collapsed;
            return;
        }
        if (_tool == DrawTool.Text)
        {
            Cursor = Cursors.IBeam;
            _cursorDot.Visibility = Visibility.Collapsed;
            return;
        }

        Cursor = Cursors.None;
        if (_tool == DrawTool.Eraser)
        {
            _cursorDot.Width = _cursorDot.Height = 20;
            _cursorDot.Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            _cursorDot.Stroke = Brushes.White;
            _cursorDot.StrokeThickness = 1.5;
        }
        else
        {
            double d = Math.Max(5, CurrentWidth());
            _cursorDot.Width = _cursorDot.Height = d;
            _cursorDot.Fill = ColorUtil.Brush(_color, _tool == DrawTool.Highlighter ? S.HighlighterOpacity + 0.15 : 1);
            _cursorDot.Stroke = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
            _cursorDot.StrokeThickness = 1;
        }
        var p = Mouse.GetPosition(_overlay);
        MoveCursorDot(p);
    }

    void MoveCursorDot(Point p)
    {
        bool show = !IsSnip && !_follow && !_passThrough && !_overToolbar && _tool != DrawTool.Text && IsMouseOver;
        _cursorDot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        Canvas.SetLeft(_cursorDot, p.X - _cursorDot.Width / 2);
        Canvas.SetTop(_cursorDot, p.Y - _cursorDot.Height / 2);
    }

    void ToggleBoard(Color c)
    {
        if (_board.Visibility == Visibility.Visible && _board.Fill is SolidColorBrush b && b.Color == c)
        {
            _board.Visibility = Visibility.Collapsed;
        }
        else
        {
            _board.Fill = ColorUtil.Brush(c);
            _board.Visibility = Visibility.Visible;
            // 흰 판에 흰 펜, 검은 판에 검은 펜이면 안 보이므로 자동으로 바꿔 준다
            if (c == Colors.White && _color == Colors.White) _color = ColorUtil.Parse(S.ColorRed);
            if (c != Colors.White && _color.R < 40 && _color.G < 40 && _color.B < 40) _color = Colors.White;
        }
        Lock();
        ApplyToolUi();
    }

    // =====================================================================
    // 키보드
    // =====================================================================

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (_editingText != null)
        {
            if (key == Key.Escape || (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None))
            {
                CommitText();
                e.Handled = true;
            }
            return; // 글자 입력 중에는 단축키를 쓰지 않는다
        }

        if (IsSnip)
        {
            if (key == Key.Escape) Close();
            e.Handled = true;
            return;
        }
        if (_passThrough) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        e.Handled = true;

        switch (key)
        {
            case Key.Escape:
                if (_help?.Visibility == Visibility.Visible) ToggleHelp();
                else Close();
                break;
            case Key.F1:
            case Key.OemQuestion:
                ToggleHelp();
                break;
            case Key.Space:
                _toolbarOn = !_toolbarOn;
                UpdateToolbarVisibility();
                break;

            case Key.Z when ctrl: if (shift) Redo(); else Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.S when ctrl: SaveImage(); break;
            case Key.C when ctrl: CopyImage(); break;

            case Key.R when !ctrl: PickColor(ColorUtil.Parse(S.ColorRed), shift); break;
            case Key.G when !ctrl: PickColor(ColorUtil.Parse(S.ColorGreen), shift); break;
            case Key.B when !ctrl: PickColor(ColorUtil.Parse(S.ColorBlue), shift); break;
            case Key.Y when !ctrl: PickColor(ColorUtil.Parse(S.ColorYellow), shift); break;
            case Key.O when !ctrl: PickColor(ColorUtil.Parse(S.ColorOrange), shift); break;
            case Key.P when !ctrl: PickColor(ColorUtil.Parse(S.ColorPink), shift); break;

            case Key.W when !ctrl: ToggleBoard(Colors.White); break;
            case Key.K when !ctrl: ToggleBoard(Color.FromRgb(0x1B, 0x2A, 0x22)); break;
            case Key.E when !ctrl:
            case Key.Delete:
                ClearAll();
                break;
            case Key.T when !ctrl: SetTool(DrawTool.Text); break;

            case Key.D1: case Key.NumPad1: SetTool(DrawTool.Pen); break;
            case Key.D2: case Key.NumPad2: SetTool(DrawTool.Highlighter); break;
            case Key.D3: case Key.NumPad3: SetTool(DrawTool.Line); break;
            case Key.D4: case Key.NumPad4: SetTool(DrawTool.Arrow); break;
            case Key.D5: case Key.NumPad5: SetTool(DrawTool.Rectangle); break;
            case Key.D6: case Key.NumPad6: SetTool(DrawTool.Ellipse); break;
            case Key.D7: case Key.NumPad7: SetTool(DrawTool.Text); break;
            case Key.D8: case Key.NumPad8: SetTool(DrawTool.Eraser); break;
            case Key.D0: case Key.NumPad0: ZoomReset(); break;

            case Key.Up: case Key.Add: case Key.OemPlus:
                ZoomBy(1.25, Mouse.GetPosition(_viewport));
                break;
            case Key.Down: case Key.Subtract: case Key.OemMinus:
                ZoomBy(0.8, Mouse.GetPosition(_viewport));
                break;
            case Key.OemOpenBrackets: ChangeWidth(-1); break;
            case Key.OemCloseBrackets: ChangeWidth(1); break;

            default:
                e.Handled = false;
                break;
        }
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (IsSnip || _passThrough) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl || IsLive)
            ChangeWidth(Math.Sign(e.Delta));
        else
            ZoomBy(Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(_viewport));
    }

    // =====================================================================
    // 마우스·그리기
    // =====================================================================

    void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            e.Handled = true; // 닫기는 버튼을 뗄 때 (아래 프로그램에 우클릭 메뉴가 뜨지 않게)
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            if (IsLive || IsSnip) return;
            Lock();
            _panning = true;
            _panStart = e.GetPosition(_viewport);
            _panStartT = new Vector(_transT.X, _transT.Y);
            _viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;

        if (IsSnip)
        {
            _snipping = true;
            _snipStart = e.GetPosition(_root);
            _viewport.CaptureMouse();
            UpdateSnip(_snipStart);
            return;
        }

        var p = e.GetPosition(_ink);
        bool wasFollowing = _follow;
        Lock();
        if (wasFollowing && S.ShowModeHint) ShowHint("그리기 · F1 단축키 보기 · Esc 끝내기");

        if (_tool == DrawTool.Text)
        {
            BeginText(p);
            return;
        }
        CommitText();

        if (_tool == DrawTool.Eraser)
        {
            _erasing = true;
            _eraseEntry = new UndoEntry();
            EraseAt(p);
            _viewport.CaptureMouse();
            return;
        }

        StartStroke(p);
        _viewport.CaptureMouse();
    }

    void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            var d = e.GetPosition(_viewport) - _panStart;
            ApplyTransform(_panStartT.X + d.X, _panStartT.Y + d.Y);
            return;
        }
        if (_snipping)
        {
            UpdateSnip(e.GetPosition(_root));
            return;
        }
        if (_erasing)
        {
            EraseAt(e.GetPosition(_ink));
            return;
        }
        if (_active != null) UpdateStroke(e.GetPosition(_ink));
    }

    void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            e.Handled = true;
            if (S.RightClickExits && _active == null && !_snipping) Close();
            return;
        }
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (_panning)
            {
                _panning = false;
                _viewport.ReleaseMouseCapture();
            }
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (_snipping)
        {
            _snipping = false;
            _viewport.ReleaseMouseCapture();
            FinishSnip(e.GetPosition(_root));
            return;
        }
        if (_active != null) UpdateStroke(e.GetPosition(_ink));
        _viewport.ReleaseMouseCapture(); // LostMouseCapture → FinishActiveStroke
        FinishActiveStroke();
    }

    void StartStroke(Point p)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0 || Keyboard.IsKeyDown(Key.Tab);

        _activeHl = _tool == DrawTool.Highlighter;
        _activeShape = _tool;
        if (_tool is DrawTool.Pen or DrawTool.Highlighter)
        {
            // ZoomIt 와 같은 수식키 + 드래그 도형
            if (ctrl && shift) _activeShape = DrawTool.Arrow;
            else if (ctrl) _activeShape = DrawTool.Rectangle;
            else if (shift) _activeShape = DrawTool.Line;
            else if (alt) _activeShape = DrawTool.Ellipse;
        }

        _startPt = _lastPt = p;
        var brush = ColorUtil.Brush(_color);
        double th = (_activeHl ? _hlWidth : _penWidth) / _scale;
        _active = new Path
        {
            Stroke = brush,
            StrokeThickness = th,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        if (_activeHl) _active.Opacity = S.HighlighterOpacity;

        switch (_activeShape)
        {
            case DrawTool.Pen:
            case DrawTool.Highlighter:
                _fig = new PathFigure { StartPoint = p, IsFilled = false, IsClosed = false };
                _fig.Segments.Add(new LineSegment(new Point(p.X + 0.05 / _scale, p.Y), true)); // 클릭만 해도 점이 찍히게
                var g = new PathGeometry();
                g.Figures.Add(_fig);
                _active.Data = g;
                break;
            case DrawTool.Arrow:
                _active.Fill = brush;
                _active.Data = Geometry.Empty;
                break;
            case DrawTool.Rectangle:
            case DrawTool.Ellipse:
                if (_activeHl)
                {
                    // 형광펜 + 도형 = 반투명하게 칠한 영역 강조
                    _active.Fill = brush;
                    _active.Stroke = null;
                }
                _active.Data = Geometry.Empty;
                break;
            default:
                _active.Data = Geometry.Empty;
                break;
        }
        _ink.Children.Add(_active);
    }

    void UpdateStroke(Point p)
    {
        if (_active == null) return;
        bool constrain = _tool is DrawTool.Line or DrawTool.Arrow or DrawTool.Rectangle or DrawTool.Ellipse
                         && (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (_activeShape)
        {
            case DrawTool.Pen:
            case DrawTool.Highlighter:
                if ((p - _lastPt).Length < 0.6 / _scale) return;
                var mid = new Point((_lastPt.X + p.X) / 2, (_lastPt.Y + p.Y) / 2);
                _fig!.Segments.Add(new QuadraticBezierSegment(_lastPt, mid, true));
                _lastPt = p;
                break;
            case DrawTool.Line:
                _lastPt = constrain ? Snap45(_startPt, p) : p;
                _active.Data = new LineGeometry(_startPt, _lastPt);
                break;
            case DrawTool.Arrow:
                _lastPt = constrain ? Snap45(_startPt, p) : p;
                _active.Data = ArrowGeometry(_startPt, _lastPt, _active.StrokeThickness);
                break;
            case DrawTool.Rectangle:
                _lastPt = p;
                var r = RectFrom(_startPt, p, constrain);
                double rr = Math.Min(4 / _scale, Math.Min(r.Width, r.Height) / 4);
                _active.Data = new RectangleGeometry(r, rr, rr);
                break;
            case DrawTool.Ellipse:
                _lastPt = p;
                _active.Data = new EllipseGeometry(RectFrom(_startPt, p, constrain));
                break;
        }
    }

    void FinishActiveStroke()
    {
        if (_erasing)
        {
            _erasing = false;
            if (_eraseEntry is { Removed.Count: > 0 }) PushUndo(_eraseEntry);
            _eraseEntry = null;
        }
        if (_active == null) return;
        var path = _active;
        _active = null;

        if (_activeShape is DrawTool.Pen or DrawTool.Highlighter)
        {
            _fig?.Segments.Add(new LineSegment(_lastPt, true));
            _fig = null;
        }
        else if ((_lastPt - _startPt).Length < 3 / _scale)
        {
            _ink.Children.Remove(path); // 클릭만 하고 뗀 도형은 버린다
            return;
        }
        PushUndo(new UndoEntry { Added = { path } });
    }

    static Point Snap45(Point a, Point b)
    {
        var v = b - a;
        double len = v.Length;
        if (len < 0.001) return b;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    static Rect RectFrom(Point a, Point b, bool square)
    {
        if (!square) return new Rect(a, b);
        double side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        return new Rect(a, new Point(a.X + Math.Sign(b.X - a.X) * side, a.Y + Math.Sign(b.Y - a.Y) * side));
    }

    Geometry ArrowGeometry(Point a, Point b, double thickness)
    {
        var v = b - a;
        double len = v.Length;
        if (len < 0.01) return new LineGeometry(a, b);
        v /= len;
        double head = Math.Min(Math.Max(thickness * 4.2, 14 / _scale), len * 0.8);
        var n = new Vector(-v.Y, v.X);
        var basePt = b - v * head;
        double half = head * 0.5;

        var g = new PathGeometry();
        var shaft = new PathFigure { StartPoint = a, IsFilled = false };
        shaft.Segments.Add(new LineSegment(basePt + v * (thickness * 0.5), true));
        g.Figures.Add(shaft);

        var tip = new PathFigure { StartPoint = b, IsFilled = true, IsClosed = true };
        tip.Segments.Add(new LineSegment(basePt + n * half, true));
        tip.Segments.Add(new LineSegment(basePt - n * half, true));
        g.Figures.Add(tip);
        return g;
    }

    // ---- 지우개 ----

    void EraseAt(Point p)
    {
        if (_eraseEntry == null) return;
        double r = 10 / _scale;
        var hits = new List<UIElement>();
        VisualTreeHelper.HitTest(_ink, null, res =>
        {
            var top = TopLevelChild(res.VisualHit);
            if (top != null && !hits.Contains(top)) hits.Add(top);
            return HitTestResultBehavior.Continue;
        }, new GeometryHitTestParameters(new EllipseGeometry(p, r, r)));

        foreach (var el in hits)
        {
            if (el == _editingText) continue;
            int idx = _ink.Children.IndexOf(el);
            if (idx < 0) continue;
            _eraseEntry.Removed.Add((el, idx));
            _ink.Children.RemoveAt(idx);
        }
    }

    UIElement? TopLevelChild(DependencyObject? d)
    {
        while (d != null)
        {
            var parent = VisualTreeHelper.GetParent(d);
            if (parent == _ink) return d as UIElement;
            d = parent;
        }
        return null;
    }

    // ---- 글자 ----

    void BeginText(Point p)
    {
        CommitText();
        double fs = _textSize / _scale;
        var brush = ColorUtil.Brush(_color);
        var tb = new TextBox
        {
            FontSize = fs,
            FontFamily = new FontFamily(S.TextFont),
            FontWeight = FontWeights.SemiBold,
            Foreground = brush,
            CaretBrush = brush,
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            BorderThickness = new Thickness(1 / _scale),
            AcceptsReturn = true,
            MinWidth = fs * 1.5,
            Padding = new Thickness(0),
            Cursor = Cursors.IBeam,
        };
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y - fs * 0.7);
        _ink.Children.Add(tb);
        _editingText = tb;
        UpdateCursorVisual();
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
        }, DispatcherPriority.Input);
    }

    void CommitText()
    {
        var tb = _editingText;
        if (tb == null) return;
        _editingText = null;
        int idx = _ink.Children.IndexOf(tb);
        _ink.Children.Remove(tb);
        Keyboard.Focus(this);
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var block = new TextBlock
        {
            Text = tb.Text,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            FontWeight = tb.FontWeight,
            Foreground = tb.Foreground,
        };
        // TextBox 안쪽 여백(테두리 + 기본 2px)만큼 옮겨서 글자 위치가 그대로 보이게
        Canvas.SetLeft(block, Canvas.GetLeft(tb) + tb.BorderThickness.Left + 2);
        Canvas.SetTop(block, Canvas.GetTop(tb) + tb.BorderThickness.Top);
        _ink.Children.Insert(Math.Clamp(idx, 0, _ink.Children.Count), block);
        PushUndo(new UndoEntry { Added = { block } });
    }

    // ---- 되돌리기 ----

    void PushUndo(UndoEntry u)
    {
        _undo.Push(u);
        _redo.Clear();
    }

    void Undo()
    {
        CommitText();
        if (_undo.Count == 0) return;
        var u = _undo.Pop();
        foreach (var a in u.Added) _ink.Children.Remove(a);
        for (int i = u.Removed.Count - 1; i >= 0; i--)
        {
            var (el, idx) = u.Removed[i];
            _ink.Children.Insert(Math.Min(idx, _ink.Children.Count), el);
        }
        _redo.Push(u);
    }

    void Redo()
    {
        CommitText();
        if (_redo.Count == 0) return;
        var u = _redo.Pop();
        foreach (var (el, _) in u.Removed) _ink.Children.Remove(el);
        foreach (var a in u.Added) _ink.Children.Add(a);
        _undo.Push(u);
    }

    void ClearAll()
    {
        CommitText();
        if (_ink.Children.Count == 0) return;
        var u = new UndoEntry();
        for (int i = _ink.Children.Count - 1; i >= 0; i--)
        {
            u.Removed.Add((_ink.Children[i], i));
            _ink.Children.RemoveAt(i);
        }
        PushUndo(u);
    }

    // =====================================================================
    // 도움말
    // =====================================================================

    void ToggleHelp()
    {
        if (_help == null)
        {
            var rows = new (string Label, string Text)[]
            {
                ("도구", "1 펜 · 2 형광펜 · 3 직선 · 4 화살표 · 5 사각형 · 6 원 · 7(T) 글자 · 8 지우개"),
                ("색", "R 빨강 · G 초록 · B 파랑 · Y 노랑 · O 주황 · P 분홍   (Shift+색 = 그 색 형광펜)"),
                ("펜 드래그", "Shift 직선 · Ctrl 사각형 · Ctrl+Shift 화살표 · Alt 원"),
                ("형광펜 드래그", "Ctrl 또는 Alt = 반투명하게 칠한 사각형/원 (영역 강조)"),
                ("도형 도구", "Shift = 45° 맞춤 / 정사각형 / 정원"),
                ("굵기", "Ctrl+휠 또는 [ ]   (글자 도구일 때는 글자 크기)"),
                ("확대", "휠 또는 ↑ ↓ · 0 원래 크기 · 가운데 버튼 드래그로 화면 옮기기"),
                ("판", "W 화이트보드 · K 칠판 (한 번 더 누르면 원래 화면)"),
                ("편집", "Ctrl+Z 되돌리기 · Ctrl+Y 다시 · E 모두 지우기"),
                ("내보내기", "Ctrl+S 파일로 저장 · Ctrl+C 클립보드 복사"),
                ("기타", "Space 도구 막대 숨기기/보이기 · Esc 또는 우클릭 끝내기"),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lb = new TextBlock { Text = rows[i].Label, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), FontSize = 15, Margin = new Thickness(0, 4, 20, 4) };
                var tx = new TextBlock { Text = rows[i].Text, Foreground = Brushes.White, FontSize = 15, Margin = new Thickness(0, 4, 0, 4) };
                Grid.SetRow(lb, i);
                Grid.SetRow(tx, i);
                Grid.SetColumn(tx, 1);
                grid.Children.Add(lb);
                grid.Children.Add(tx);
            }
            var title = new TextBlock { Text = "그리기 단축키", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(grid);
            stack.Children.Add(new TextBlock { Text = "F1 또는 Esc 로 닫기", Foreground = Brushes.Gray, FontSize = 13, Margin = new Thickness(0, 12, 0, 0) });
            _help = new Border
            {
                Child = stack,
                Background = new SolidColorBrush(Color.FromArgb(235, 25, 26, 29)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(28, 22, 28, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            _root.Children.Add(_help);
        }
        _help.Visibility = _help.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // =====================================================================
    // 저장·복사
    // =====================================================================

    async Task<BitmapSource?> RenderCompositeAsync()
    {
        CommitText();
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = _viewport.ActualWidth, h = _viewport.ActualHeight;
        int pw = (int)Math.Round(w * dpi.DpiScaleX), ph = (int)Math.Round(h * dpi.DpiScaleY);
        if (pw <= 0 || ph <= 0) return null;

        var ink = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        ink.Render(_viewport);
        if (!IsLive)
        {
            ink.Freeze();
            return ink;
        }

        // 화면 위 그리기: 창을 잠깐 숨기고 아래 화면을 찍은 뒤 그림과 합친다
        Opacity = 0;
        await Task.Delay(150);
        var screen = ScreenHelper.Capture(_mon.Bounds);
        Opacity = 1;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(screen, new Rect(0, 0, w, h));
            dc.DrawImage(ink, new Rect(0, 0, w, h));
        }
        var result = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        result.Render(dv);
        result.Freeze();
        return result;
    }

    async void SaveImage()
    {
        try
        {
            var bmp = await RenderCompositeAsync();
            if (bmp == null) return;
            string path = ScreenHelper.SavePng(bmp, S.SaveFolder);
            ShowHint("저장했습니다\n" + path, 2.5);
        }
        catch (Exception ex)
        {
            ShowHint("저장 실패: " + ex.Message, 3);
        }
    }

    async void CopyImage()
    {
        var bmp = await RenderCompositeAsync();
        if (bmp == null) return;
        ShowHint(ScreenHelper.CopyToClipboard(bmp) ? "클립보드에 복사했습니다" : "클립보드 복사에 실패했습니다", 1.8);
    }

    // =====================================================================
    // 화면 위 그리기: 그림 유지한 채 클릭 통과
    // =====================================================================

    public void TogglePassThrough() => SetPassThrough(!_passThrough);

    void SetPassThrough(bool on)
    {
        if (!IsLive) return;
        CommitText();
        FinishActiveStroke();
        _passThrough = on;
        Native.SetClickThrough(this, on);
        if (_help != null) _help.Visibility = Visibility.Collapsed;
        ApplyToolUi();
        if (on)
        {
            ShowHint("그림을 남겨 두고 화면 조작 중 · 단축키를 다시 누르면 이어서 그리기");
        }
        else
        {
            Activate();
            Native.SetForegroundWindow(Native.Handle(this));
            Keyboard.Focus(this);
            ShowHint("다시 그리기 · Esc 끝내기");
        }
    }

    // =====================================================================
    // 영역 캡처
    // =====================================================================

    void UpdateSnip(Point p)
    {
        var r = new Rect(_snipStart, p);
        _snipRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(_snipRect, r.X);
        Canvas.SetTop(_snipRect, r.Y);
        _snipRect.Width = r.Width;
        _snipRect.Height = r.Height;
        UpdateSnipShade(r);

        double s = ScreenHelper.Scale(this);
        _snipLabel.Text = $"{Math.Round(r.Width * s)} × {Math.Round(r.Height * s)}";
        _snipLabel.Visibility = Visibility.Visible;
        Canvas.SetLeft(_snipLabel, r.X);
        Canvas.SetTop(_snipLabel, Math.Max(0, r.Y - 26));
    }

    void UpdateSnipShade(Rect hole)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, _root.ActualWidth), Math.Max(1, _root.ActualHeight)));
        _snipShade.Data = hole.IsEmpty
            ? full
            : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
    }

    void FinishSnip(Point end)
    {
        var r = new Rect(_snipStart, end);
        if (_shot == null || r.Width < 4 || r.Height < 4)
        {
            _snipRect.Visibility = Visibility.Collapsed;
            _snipLabel.Visibility = Visibility.Collapsed;
            UpdateSnipShade(Rect.Empty);
            return; // 너무 작으면 다시 고르게 둔다
        }
        double sx = _shot.PixelWidth / _root.ActualWidth;
        double sy = _shot.PixelHeight / _root.ActualHeight;
        int x = (int)Math.Clamp(Math.Round(r.X * sx), 0, _shot.PixelWidth - 1);
        int y = (int)Math.Clamp(Math.Round(r.Y * sy), 0, _shot.PixelHeight - 1);
        int w = (int)Math.Clamp(Math.Round(r.Width * sx), 1, _shot.PixelWidth - x);
        int h = (int)Math.Clamp(Math.Round(r.Height * sy), 1, _shot.PixelHeight - y);
        var crop = new CroppedBitmap(_shot, new Int32Rect(x, y, w, h));
        crop.Freeze();

        bool ok = ScreenHelper.CopyToClipboard(crop);
        string msg = ok ? "영역을 클립보드에 복사했습니다" : "클립보드 복사에 실패했습니다";
        if (S.SnipAlsoSaveFile)
        {
            try { msg += "\n" + ScreenHelper.SavePng(crop, S.SaveFolder); }
            catch (Exception ex) { msg += "\n저장 실패: " + ex.Message; }
        }
        Close();
        Notify?.Invoke(msg);
    }

#if DEBUG
    // ---- 자체 테스트(--selftest)용: 창을 띄우지 않고 그림만 렌더링 ----
    internal FrameworkElement TestRoot => _root;

    internal void TestLayout(double w, double h)
    {
        _root.Measure(new Size(w, h));
        _root.Arrange(new Rect(0, 0, w, h));
        _root.UpdateLayout();
        _follow = false;
        ApplyToolUi();
    }

    internal void TestStroke(DrawTool tool, Color color, params Point[] pts)
    {
        _tool = tool;
        _color = color;
        StartStroke(pts[0]);
        for (int i = 1; i < pts.Length; i++) UpdateStroke(pts[i]);
        FinishActiveStroke();
        ApplyToolUi();
    }

    internal void TestText(Point p, Color color, string text)
    {
        _tool = DrawTool.Text;
        _color = color;
        BeginText(p);
        _editingText!.Text = text;
        CommitText();
        _tool = DrawTool.Pen;
        ApplyToolUi();
    }

    internal void TestZoom(double s, Point anchor)
    {
        _anchorView = anchor;
        _anchorContent = new Point((anchor.X - _transT.X) / _scale, (anchor.Y - _transT.Y) / _scale);
        _scale = _target = s;
        ApplyTransform(_anchorView.X - _anchorContent.X * s, _anchorView.Y - _anchorContent.Y * s);
    }

    internal void TestUndo() => Undo();
    internal void TestEraseAt(Point p) { _erasing = true; _eraseEntry = new UndoEntry(); EraseAt(p); FinishActiveStroke(); }
    internal int TestInkCount => _ink.Children.Count;
    internal void TestHelp() => ToggleHelp();
    internal void TestBoard(Color c) => ToggleBoard(c);
    internal void TestHint(string text)
    {
        _hintText.Text = text;
        _hint.BeginAnimation(OpacityProperty, null);
        _hint.Opacity = 1;
    }
#endif
}
