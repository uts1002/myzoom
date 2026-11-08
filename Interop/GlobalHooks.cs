using System.Runtime.InteropServices;

namespace MyZoom.Interop;

public enum MouseButtonKind { Left, Right, Middle }

/// <summary>
/// 전역 마우스 훅. 필요한 기능이 켜졌을 때만 설치되고(참조 카운트), 모두 꺼지면 해제된다.
/// 콜백은 UI 스레드에서 호출되므로 WPF 객체를 바로 다뤄도 된다. 단 무거운 작업은 금지.
/// </summary>
public static class GlobalMouseHook
{
    public static event Action<int, int>? Moved;
    public static event Action<MouseButtonKind, int, int>? ButtonDown;

    /// <summary>Ctrl+휠 처리기. true 를 돌려주면 휠 입력을 다른 프로그램에 넘기지 않는다.</summary>
    public static Func<int, bool>? CtrlWheel;

    static IntPtr _hook;
    static Native.HookProc? _proc;
    static int _refs;

    public static void Acquire()
    {
        if (_refs++ > 0) return;
        _proc = Proc;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    public static void Release()
    {
        if (_refs == 0 || --_refs > 0) return;
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                switch (msg)
                {
                    case Native.WM_MOUSEMOVE:
                        Moved?.Invoke(info.pt.X, info.pt.Y);
                        break;
                    case Native.WM_LBUTTONDOWN:
                        ButtonDown?.Invoke(MouseButtonKind.Left, info.pt.X, info.pt.Y);
                        break;
                    case Native.WM_RBUTTONDOWN:
                        ButtonDown?.Invoke(MouseButtonKind.Right, info.pt.X, info.pt.Y);
                        break;
                    case Native.WM_MBUTTONDOWN:
                        ButtonDown?.Invoke(MouseButtonKind.Middle, info.pt.X, info.pt.Y);
                        break;
                    case Native.WM_MOUSEWHEEL:
                        if (CtrlWheel != null && Native.IsKeyDown(0x11))
                        {
                            int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                            if (CtrlWheel(delta)) return new IntPtr(1);
                        }
                        break;
                }
            }
            catch
            {
                // 훅 안에서 예외가 나면 입력 전체가 멈출 수 있으므로 삼킨다.
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

/// <summary>전역 키보드 훅(키 입력 표시용). 입력은 가로채지 않고 보기만 한다.</summary>
public static class GlobalKeyboardHook
{
    /// <summary>(가상키 코드) 키가 눌릴 때마다.</summary>
    public static event Action<int>? KeyDown;

    static IntPtr _hook;
    static Native.HookProc? _proc;
    static int _refs;

    public static void Acquire()
    {
        if (_refs++ > 0) return;
        _proc = Proc;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    public static void Release()
    {
        if (_refs == 0 || --_refs > 0) return;
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                try
                {
                    var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                    KeyDown?.Invoke((int)info.vkCode);
                }
                catch { }
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
