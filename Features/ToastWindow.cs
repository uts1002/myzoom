using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>오른쪽 아래에 잠깐 떴다 사라지는 알림. 포커스를 뺏지 않고 클릭도 통과한다.</summary>
public sealed class ToastWindow : Window
{
    ToastWindow(string message)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; Top = -10000;

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 32, 33, 36)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(8),
            MaxWidth = 460,
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            },
        };

        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlayStyle(this, noActivate: true);
            Native.SetClickThrough(this, true);
        };
        Loaded += (_, _) =>
        {
            var mon = ScreenHelper.FromCursor();
            double s = ScreenHelper.Scale(this);
            int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
            int margin = (int)(16 * s);
            ScreenHelper.Place(this, new Int32Rect(
                mon.WorkArea.X + mon.WorkArea.Width - w - margin,
                mon.WorkArea.Y + mon.WorkArea.Height - h - margin, w, h));

            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15))));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.1))));
            anim.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, anim);
        };
        Opacity = 0;
    }

    public static void Show(string message) => new ToastWindow(message).Show();
}
