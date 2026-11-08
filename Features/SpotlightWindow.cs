using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>
/// 스포트라이트: 커서 주변만 밝게 두고 나머지를 어둡게. 클릭은 아래 프로그램으로 그대로 통과한다.
/// Ctrl+휠로 원 크기 조절.
/// </summary>
public sealed class SpotlightWindow : Window
{
    readonly AppSettings S;
    readonly RadialGradientBrush _mask;
    MonitorArea _mon;
    double _radius;

    public SpotlightWindow(AppSettings settings)
    {
        S = settings;
        _radius = S.SpotlightRadius;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _mask = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
        _mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        _mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.86));
        _mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));

        Content = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(S.SpotlightDim, 0.1, 0.95) * 255), 0, 0, 0)),
            OpacityMask = _mask,
        };

        _mon = ScreenHelper.FromCursor();
        Left = _mon.Bounds.X; Top = _mon.Bounds.Y; Width = 400; Height = 300;

        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlayStyle(this, noActivate: true);
            Native.SetClickThrough(this, true);
            ScreenHelper.Place(this, _mon.Bounds);
        };
        Loaded += (_, _) =>
        {
            GlobalMouseHook.Acquire();
            GlobalMouseHook.Moved += OnMoved;
            GlobalMouseHook.CtrlWheel ??= OnCtrlWheel;
            var (x, y) = ScreenHelper.CursorPos();
            OnMoved(x, y);
        };
        Closed += (_, _) =>
        {
            GlobalMouseHook.Moved -= OnMoved;
            if (GlobalMouseHook.CtrlWheel == OnCtrlWheel) GlobalMouseHook.CtrlWheel = null;
            GlobalMouseHook.Release();
        };
    }

    void OnMoved(int x, int y)
    {
        if (!_mon.Contains(x, y))
        {
            _mon = ScreenHelper.FromPoint(x, y);
            ScreenHelper.Place(this, _mon.Bounds);
        }
        Point p;
        try { p = PointFromScreen(new Point(x, y)); }
        catch { return; }
        _mask.Center = p;
        _mask.GradientOrigin = p;
        _mask.RadiusX = _mask.RadiusY = _radius;
    }

    bool OnCtrlWheel(int delta)
    {
        _radius = Math.Clamp(_radius * Math.Pow(1.15, delta / 120.0), 40, 900);
        _mask.RadiusX = _mask.RadiusY = _radius;
        return true;
    }
}
