using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyZoom.Core;

namespace MyZoom.UI;

/// <summary>
/// 단축키 입력 칸. 칸을 누르고 키 조합을 누르면 기록된다. Backspace/Delete = 사용 안 함.
/// 입력 중에는 전역 단축키를 잠시 해제해야 이미 쓰는 조합도 다시 입력할 수 있다.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    /// <summary>true: 입력 시작(전역 단축키 해제), false: 입력 끝(다시 등록).</summary>
    public static event Action<bool>? CaptureStateChanged;

    public HotkeySpec Spec { get; private set; }
    public event Action<HotkeySpec>? Changed;

    public HotkeyBox(HotkeySpec spec)
    {
        Spec = spec.Clone();
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Padding = new Thickness(6, 4, 6, 4);
        MinWidth = 200;
        InputMethod.SetIsInputMethodEnabled(this, false);
        Text = Spec.ToString();
        ToolTip = "눌러서 새 키 조합 입력 · Backspace 는 사용 안 함";
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));
        Text = "키 조합을 누르세요…";
        CaptureStateChanged?.Invoke(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ClearValue(BackgroundProperty);
        Text = Spec.ToString();
        CaptureStateChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

        if (key == Key.Tab && mods == ModifierKeys.None) return; // 다음 칸으로 이동은 허용
        e.Handled = true;

        if ((key is Key.Back or Key.Delete) && mods == ModifierKeys.None)
        {
            Set(new HotkeySpec());
            return;
        }
        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            Keyboard.ClearFocus();
            return;
        }

        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            Text = string.Join(" + ", parts) + " + …";
            return;
        }

        bool isFKey = key >= Key.F1 && key <= Key.F24;
        if (!ctrl && !alt && !win && !isFKey)
        {
            Text = "Ctrl·Alt·Win 과 함께 누르세요";
            return;
        }
        Set(new HotkeySpec(key, ctrl, shift, alt, win));
    }

    void Set(HotkeySpec spec)
    {
        Spec = spec;
        Text = spec.ToString();
        Changed?.Invoke(spec);
    }
}
