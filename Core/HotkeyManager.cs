using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MyZoom.Interop;

namespace MyZoom.Core;

/// <summary>RegisterHotKey 기반 전역 단축키. 메시지 전용 창 하나로 받는다.</summary>
public sealed class HotkeyManager : IDisposable
{
    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 1;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("MyZoomHotkeySink")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>등록에 실패하면(다른 프로그램이 이미 사용 중) 0 을 돌려준다.</summary>
    public int Register(HotkeySpec spec, Action action)
    {
        if (spec.IsEmpty) return 0;
        uint mods = Native.MOD_NOREPEAT;
        if (spec.Ctrl) mods |= Native.MOD_CONTROL;
        if (spec.Alt) mods |= Native.MOD_ALT;
        if (spec.Shift) mods |= Native.MOD_SHIFT;
        if (spec.Win) mods |= Native.MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(spec.ParsedKey);
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods, vk)) return 0;
        _actions[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        if (id == 0 || !_actions.Remove(id)) return;
        Native.UnregisterHotKey(_source.Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToList()) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            // 메시지 처리 중 창을 띄우면 재진입 문제가 생길 수 있어 한 박자 늦춘다.
            Dispatcher.CurrentDispatcher.BeginInvoke(action, DispatcherPriority.Input);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
