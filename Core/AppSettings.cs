using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace MyZoom.Core;

/// <summary>단축키 한 개 (수식키 + 키). 키 이름은 WPF Key 열거형 이름으로 저장한다.</summary>
public sealed class HotkeySpec
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "None";

    public HotkeySpec() { }

    public HotkeySpec(Key key, bool ctrl = false, bool shift = false, bool alt = false, bool win = false)
    {
        Key = key.ToString(); Ctrl = ctrl; Shift = shift; Alt = alt; Win = win;
    }

    [JsonIgnore]
    public Key ParsedKey => Enum.TryParse<Key>(Key, out var k) ? k : System.Windows.Input.Key.None;

    [JsonIgnore]
    public bool IsEmpty => ParsedKey == System.Windows.Input.Key.None;

    public HotkeySpec Clone() => new() { Ctrl = Ctrl, Alt = Alt, Shift = Shift, Win = Win, Key = Key };

    public bool SameAs(HotkeySpec o) =>
        !IsEmpty && Ctrl == o.Ctrl && Alt == o.Alt && Shift == o.Shift && Win == o.Win && ParsedKey == o.ParsedKey;

    public override string ToString()
    {
        if (IsEmpty) return "(없음)";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyNames.Friendly(ParsedKey));
        return string.Join(" + ", parts);
    }
}

public static class KeyNames
{
    public static string Friendly(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(k - Key.NumPad0),
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Space => "Space",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Capital => "CapsLock",
        Key.Snapshot => "PrintScreen",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num .",
        Key.LWin or Key.RWin => "Win",
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.HangulMode => "한/영",
        Key.HanjaMode => "한자",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Apps => "Menu",
        _ => k.ToString(),
    };
}

public enum TimerBackground { DimScreen, Black, White }
public enum KeystrokeMode { ShortcutsOnly, AllKeys }

/// <summary>
/// 모든 설정. %AppData%\MyZoom\settings.json 에 저장된다.
/// 새 항목을 추가할 때는 기본값을 꼭 넣는다(예전 설정 파일과 호환).
/// </summary>
public sealed class AppSettings
{
    // ---- 단축키 (기본값은 ZoomIt 과 같은 배치) ----
    public HotkeySpec ZoomHotkey { get; set; } = new(Key.D1, ctrl: true);
    public HotkeySpec DrawHotkey { get; set; } = new(Key.D2, ctrl: true);
    public HotkeySpec LiveDrawHotkey { get; set; } = new(Key.D2, ctrl: true, shift: true);
    public HotkeySpec TimerHotkey { get; set; } = new(Key.D3, ctrl: true);
    public HotkeySpec LiveZoomHotkey { get; set; } = new(Key.D4, ctrl: true);
    public HotkeySpec SpotlightHotkey { get; set; } = new(Key.D5, ctrl: true);
    public HotkeySpec SnipHotkey { get; set; } = new(Key.D6, ctrl: true);
    public HotkeySpec CursorHighlightHotkey { get; set; } = new(Key.D7, ctrl: true);
    public HotkeySpec KeystrokeHotkey { get; set; } = new(Key.D8, ctrl: true);

    // ---- 줌 ----
    public double InitialZoom { get; set; } = 2.0;
    public double MaxZoom { get; set; } = 8.0;
    public bool AnimateZoom { get; set; } = true;
    public double LiveZoomInitial { get; set; } = 2.0;
    /// <summary>true: 라이브 줌 중 휠만으로 확대·축소. false: Ctrl+휠일 때만 (그냥 휠은 스크롤).</summary>
    public bool LiveZoomPlainWheel { get; set; } = true;

    // ---- 펜 ----
    public double PenWidth { get; set; } = 4;
    public double HighlighterWidth { get; set; } = 22;
    public double HighlighterOpacity { get; set; } = 0.4;
    public double TextSize { get; set; } = 28;
    public string TextFont { get; set; } = "맑은 고딕";
    public string DefaultColor { get; set; } = "#E53935";
    public string ColorRed { get; set; } = "#E53935";
    public string ColorGreen { get; set; } = "#43A047";
    public string ColorBlue { get; set; } = "#1E88E5";
    public string ColorYellow { get; set; } = "#FDD835";
    public string ColorOrange { get; set; } = "#FB8C00";
    public string ColorPink { get; set; } = "#EC407A";
    public bool RightClickExits { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool ShowModeHint { get; set; } = true;
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MyZoom");

    // ---- 영역 캡처 ----
    public bool SnipAlsoSaveFile { get; set; } = false;

    // ---- 타이머 ----
    public int TimerMinutes { get; set; } = 10;
    public string TimerMessage { get; set; } = "쉬는 시간";
    public TimerBackground TimerBackground { get; set; } = TimerBackground.DimScreen;
    public bool TimerShowEndTime { get; set; } = true;
    public bool TimerSound { get; set; } = true;

    // ---- 스포트라이트 ----
    public double SpotlightRadius { get; set; } = 160;
    public double SpotlightDim { get; set; } = 0.65;

    // ---- 커서·클릭 강조 ----
    public bool CursorHighlightOnStartup { get; set; } = false;
    public bool ShowCursorHalo { get; set; } = true;
    public string HaloColor { get; set; } = "#FFEB3B";
    public double HaloSize { get; set; } = 56;
    public double HaloOpacity { get; set; } = 0.35;
    public bool ShowClickRings { get; set; } = true;
    public string LeftClickColor { get; set; } = "#E53935";
    public string RightClickColor { get; set; } = "#1E88E5";

    // ---- 키 입력 표시 ----
    public bool KeystrokeOnStartup { get; set; } = false;
    public KeystrokeMode KeystrokeMode { get; set; } = KeystrokeMode.ShortcutsOnly;
    public double KeystrokeFontSize { get; set; } = 26;
    public double KeystrokeSeconds { get; set; } = 1.6;

    // ---- 일반 ----
    public bool ShowStartupNotice { get; set; } = true;
    /// <summary>새 버전이 나오면 알아서 받아서 바꾼다 (켜고 30초 뒤, 그 뒤 6시간마다 확인).</summary>
    public bool AutoUpdate { get; set; } = true;

    // ===================================================================

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // 설정 파일을 열어 봐도 한글이 그대로 보이게 (쉬 같은 코드로 바꾸지 않음)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyZoom");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch
        {
            // 깨진 설정 파일은 백업해 두고 기본값으로 시작
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!;

    /// <summary>(이름, 단축키 getter/setter) 목록. 설정 화면과 등록 코드가 같이 쓴다.</summary>
    public static readonly (string Id, string Label)[] HotkeyActions =
    {
        ("Zoom", "화면 확대 (줌)"),
        ("Draw", "그리기 (화면 멈춤)"),
        ("LiveDraw", "화면 위에 그리기"),
        ("Timer", "타이머"),
        ("LiveZoom", "라이브 줌"),
        ("Spotlight", "스포트라이트"),
        ("Snip", "영역 캡처 → 클립보드"),
        ("CursorHighlight", "커서·클릭 강조 켜기/끄기"),
        ("Keystroke", "키 입력 표시 켜기/끄기"),
    };

    public HotkeySpec GetHotkey(string id) => id switch
    {
        "Zoom" => ZoomHotkey,
        "Draw" => DrawHotkey,
        "LiveDraw" => LiveDrawHotkey,
        "Timer" => TimerHotkey,
        "LiveZoom" => LiveZoomHotkey,
        "Spotlight" => SpotlightHotkey,
        "Snip" => SnipHotkey,
        "CursorHighlight" => CursorHighlightHotkey,
        "Keystroke" => KeystrokeHotkey,
        _ => throw new ArgumentException(id),
    };

    public void SetHotkey(string id, HotkeySpec spec)
    {
        switch (id)
        {
            case "Zoom": ZoomHotkey = spec; break;
            case "Draw": DrawHotkey = spec; break;
            case "LiveDraw": LiveDrawHotkey = spec; break;
            case "Timer": TimerHotkey = spec; break;
            case "LiveZoom": LiveZoomHotkey = spec; break;
            case "Spotlight": SpotlightHotkey = spec; break;
            case "Snip": SnipHotkey = spec; break;
            case "CursorHighlight": CursorHighlightHotkey = spec; break;
            case "Keystroke": KeystrokeHotkey = spec; break;
        }
    }
}
