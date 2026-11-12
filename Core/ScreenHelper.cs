using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyZoom.Interop;
using WinForms = System.Windows.Forms;

namespace MyZoom.Core;

/// <summary>모니터 한 대. 좌표는 모두 실제 픽셀(물리 좌표).</summary>
public readonly record struct MonitorArea(Int32Rect Bounds, Int32Rect WorkArea, string Name)
{
    public bool Contains(int x, int y) =>
        x >= Bounds.X && y >= Bounds.Y && x < Bounds.X + Bounds.Width && y < Bounds.Y + Bounds.Height;
}

public static class ScreenHelper
{
    public static (int X, int Y) CursorPos()
    {
        Native.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static MonitorArea FromPoint(int x, int y)
    {
        var s = WinForms.Screen.FromPoint(new System.Drawing.Point(x, y));
        return new MonitorArea(
            new Int32Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height),
            new Int32Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height),
            s.DeviceName);
    }

    /// <summary>모든 모니터 (왼쪽 위 → 오른쪽 아래 순).</summary>
    public static List<MonitorArea> All() =>
        WinForms.Screen.AllScreens
            .OrderBy(s => s.Bounds.Y).ThenBy(s => s.Bounds.X)
            .Select(s => new MonitorArea(
                new Int32Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height),
                new Int32Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height),
                s.DeviceName))
            .ToList();

    public static MonitorArea FromCursor()
    {
        var (x, y) = CursorPos();
        return FromPoint(x, y);
    }

    /// <summary>화면 일부(물리 픽셀)를 캡처해 고정(Frozen)된 비트맵으로 돌려준다.</summary>
    public static BitmapSource Capture(Int32Rect r)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = Native.CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        IntPtr old = Native.SelectObject(memDc, hBmp);
        try
        {
            Native.BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(memDc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(hBmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            // 96 DPI 로 맞춰 둔다 (화면에 표시할 때는 Stretch 로 창 크기에 맞춤)
            var fixedDpi = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
            fixedDpi.Freeze();
            return fixedDpi;
        }
        finally
        {
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// 창을 물리 픽셀 영역에 정확히 맞춘다. 배율이 다른 모니터로 옮겨지면 WPF 가 크기를
    /// 한 번 바꿔 버리므로 두 번 호출해 확정한다.
    /// </summary>
    public static void Place(Window w, Int32Rect r, bool activate = false)
    {
        var h = Native.Handle(w);
        uint flags = activate ? 0 : Native.SWP_NOACTIVATE;
        Native.SetWindowPos(h, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, flags);
        Native.SetWindowPos(h, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, flags);
    }

    /// <summary>창이 놓인 모니터의 DIP→픽셀 배율.</summary>
    public static double Scale(Visual v) => VisualTreeHelper.GetDpi(v).DpiScaleX;

    public static string SavePng(BitmapSource bmp, string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"MyZoom_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    public static bool CopyToClipboard(BitmapSource bmp)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetImage(bmp);
                return true;
            }
            catch
            {
                Thread.Sleep(60); // 다른 프로그램이 클립보드를 잡고 있을 때 잠깐 기다렸다 재시도
            }
        }
        return false;
    }
}
