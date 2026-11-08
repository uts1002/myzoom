using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>
/// 커서 강조(노란 원)와 클릭 표시(퍼지는 고리). 영상 녹화용.
/// 전체 화면 창 대신 커서를 따라다니는 작은 창이라 가볍다.
/// </summary>
public sealed class CursorHighlightWindow : Window
{
    const double Size = 200; // DIP
    readonly AppSettings S;
    readonly Canvas _canvas = new() { Width = Size, Height = Size };
    readonly Ellipse _halo;
    IntPtr _hwnd;

    public CursorHighlightWindow(AppSettings settings)
    {
        S = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = Size; Height = Size;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas;

        double hs = Math.Clamp(S.HaloSize, 16, 180);
        _halo = new Ellipse
        {
            Width = hs,
            Height = hs,
            Fill = ColorUtil.Brush(S.HaloColor, Math.Clamp(S.HaloOpacity, 0.05, 1)),
            Visibility = S.ShowCursorHalo ? Visibility.Visible : Visibility.Collapsed,
        };
        Canvas.SetLeft(_halo, (Size - hs) / 2);
        Canvas.SetTop(_halo, (Size - hs) / 2);
        _canvas.Children.Add(_halo);

        SourceInitialized += (_, _) =>
        {
            _hwnd = Native.Handle(this);
            Native.MakeOverlayStyle(this, noActivate: true);
            Native.SetClickThrough(this, true);
        };
        Loaded += (_, _) =>
        {
            GlobalMouseHook.Acquire();
            GlobalMouseHook.Moved += OnMoved;
            GlobalMouseHook.ButtonDown += OnButtonDown;
            var (x, y) = ScreenHelper.CursorPos();
            OnMoved(x, y);
        };
        Closed += (_, _) =>
        {
            GlobalMouseHook.Moved -= OnMoved;
            GlobalMouseHook.ButtonDown -= OnButtonDown;
            GlobalMouseHook.Release();
        };
    }

    void OnMoved(int x, int y)
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x - w / 2, y - h / 2, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void OnButtonDown(MouseButtonKind button, int x, int y)
    {
        if (!S.ShowClickRings) return;
        OnMoved(x, y);
        string hex = button == MouseButtonKind.Right ? S.RightClickColor : S.LeftClickColor;
        if (button == MouseButtonKind.Middle) hex = "#9E9E9E";

        var ring = new Ellipse
        {
            Width = Size * 0.8,
            Height = Size * 0.8,
            Stroke = ColorUtil.Brush(hex),
            StrokeThickness = 4,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var st = new ScaleTransform(0.15, 0.15);
        ring.RenderTransform = st;
        Canvas.SetLeft(ring, Size * 0.1);
        Canvas.SetTop(ring, Size * 0.1);
        _canvas.Children.Add(ring);

        var dur = TimeSpan.FromMilliseconds(480);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.15, 0.6, dur) { EasingFunction = ease };
        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => _canvas.Children.Remove(ring);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }
}
