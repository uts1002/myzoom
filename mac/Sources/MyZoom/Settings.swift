import AppKit

/// 설정값. UserDefaults(com.eompage.myzoom) 에 저장한다. 기본값은 Windows 판 AppSettings 와 맞춘다.
enum Settings {
    private static let d = UserDefaults(suiteName: "com.eompage.myzoom") ?? .standard

    static var timerMinutes: Int {
        get { let v = d.integer(forKey: "timerMinutes"); return v > 0 ? v : 10 }
        set { d.set(max(1, newValue), forKey: "timerMinutes") }
    }

    static var timerMessage: String {
        get { d.string(forKey: "timerMessage") ?? "쉬는 시간" }
        set { d.set(newValue, forKey: "timerMessage") }
    }

    static var timerBackground: Int { // 0 흐린 화면, 1 검정, 2 흰색
        get { d.integer(forKey: "timerBackground") }
        set { d.set(newValue, forKey: "timerBackground") }
    }

    static var autoUpdate: Bool {
        get { d.object(forKey: "autoUpdate") as? Bool ?? true }
        set { d.set(newValue, forKey: "autoUpdate") }
    }

    static var shownWelcome: Bool {
        get { d.bool(forKey: "shownWelcome") }
        set { d.set(newValue, forKey: "shownWelcome") }
    }

    // 그리기 — Windows 판 기본값
    static let initialZoom: CGFloat = 2.0
    static let maxZoom: CGFloat = 8.0
    static let penWidth: CGFloat = 4
    static let highlighterWidth: CGFloat = 22
    static let highlighterOpacity: CGFloat = 0.4

    static let palette: [(name: String, color: NSColor)] = [
        ("빨강", NSColor(hex: 0xE53935)),
        ("초록", NSColor(hex: 0x43A047)),
        ("파랑", NSColor(hex: 0x1E88E5)),
        ("노랑", NSColor(hex: 0xFDD835)),
        ("주황", NSColor(hex: 0xFB8C00)),
        ("분홍", NSColor(hex: 0xEC407A)),
    ]

    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255,
                  alpha: alpha)
    }
}
