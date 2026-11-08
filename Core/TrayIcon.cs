using System.Windows;
using WinForms = System.Windows.Forms;

namespace MyZoom.Core;

/// <summary>알림 영역(트레이) 아이콘과 오른쪽 클릭 메뉴.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly App _app;
    readonly WinForms.NotifyIcon _icon;

    public TrayIcon(App app)
    {
        _app = app;
        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "MyZoom — 화면 확대·판서",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _app.ShowSettings();
        RefreshMenu();
    }

    static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/myzoom.ico"));
            if (res != null)
                return new System.Drawing.Icon(res.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>메뉴를 클릭하면 메뉴가 사라진 뒤에 기능을 실행 (메뉴가 캡처에 찍히지 않게).</summary>
    void Later(Action a) =>
        _app.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(250);
            a();
        });

    public void RefreshMenu()
    {
        var s = App.Settings;
        var menu = new WinForms.ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };

        var title = new WinForms.ToolStripLabel("MyZoom") { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(title);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        WinForms.ToolStripMenuItem Item(string text, HotkeySpec? hk, Action a, bool delayed = true)
        {
            var it = new WinForms.ToolStripMenuItem(text);
            if (hk is { IsEmpty: false }) it.ShortcutKeyDisplayString = hk.ToString();
            it.Click += (_, _) => { if (delayed) Later(a); else a(); };
            menu.Items.Add(it);
            return it;
        }

        Item("화면 확대", s.ZoomHotkey, _app.DoZoom);
        Item("그리기", s.DrawHotkey, _app.DoDraw);
        var live = Item("화면 위에 그리기", s.LiveDrawHotkey, _app.DoLiveDraw);
        Item("타이머", s.TimerHotkey, _app.DoTimer);
        var liveZoom = Item("라이브 줌", s.LiveZoomHotkey, _app.DoLiveZoom, delayed: false);
        var spot = Item("스포트라이트", s.SpotlightHotkey, _app.DoSpotlight, delayed: false);
        Item("영역 캡처", s.SnipHotkey, _app.DoSnip);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var halo = Item("커서·클릭 강조", s.CursorHighlightHotkey, () => _app.ToggleCursorHighlight(), delayed: false);
        var keys = Item("키 입력 표시", s.KeystrokeHotkey, () => _app.ToggleKeystrokes(), delayed: false);
        var clearLive = Item("화면 위 그림 지우고 닫기", null, _app.CloseLiveDraw, delayed: false);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        Item("저장 폴더 열기", null, _app.OpenSaveFolder, delayed: false);
        Item("설정...", null, _app.ShowSettings, delayed: false);
        Item("종료", null, _app.ExitApp, delayed: false);

        menu.Opening += (_, _) =>
        {
            halo.Checked = _app.IsCursorHighlightOn;
            keys.Checked = _app.IsKeystrokeOn;
            spot.Checked = _app.IsSpotlightOn;
            liveZoom.Checked = _app.IsLiveZoomOn;
            live.Checked = _app.IsLiveDrawOn;
            clearLive.Enabled = _app.IsLiveDrawOn;
        };

        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
