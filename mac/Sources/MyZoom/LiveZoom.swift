import AppKit
import ApplicationServices
import Carbon.HIToolbox

/// ⌃4 라이브 줌 — 직접 만들지 않고 맥 기본 '확대/축소'(손쉬운 사용)를 ⌥⌘8 로 켜고 끈다.
/// 맥 확대 기능은 확대된 화면에서도 클릭·입력이 되고, 그 설정은 다른 앱이 바꿀 수 없다.
/// 그래서 사용자가 '키보드 단축키를 사용하여 확대/축소'를 켜 두고, MyZoom 은 키만 대신 눌러 준다.
/// 다른 앱으로 키를 보내려면 '손쉬운 사용' 권한이 필요하다.
enum LiveZoom {
    static var canSendKeys: Bool { AXIsProcessTrusted() }

    /// 처음이면 시스템 권한 창을 띄우고 목록에 MyZoom 을 올린다.
    static func requestKeyPermission() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func openZoomSettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.universalaccess?Zoom")!)
    }

    static func openAccessibilityPermission() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
    }

    /// ⌥⌘8 을 보낸다. ⌃4 를 누른 손가락이 control 을 아직 잡고 있으면 ⌃⌥⌘8 이 되므로 뗄 때까지 잠깐 기다린다.
    static func toggle(waited: TimeInterval = 0) {
        let controlDown = CGEventSource.flagsState(.combinedSessionState).contains(.maskControl)
        if controlDown && waited < 1.5 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) { toggle(waited: waited + 0.05) }
            return
        }
        let src = CGEventSource(stateID: .hidSystemState)
        let key = CGKeyCode(kVK_ANSI_8)
        let flags: CGEventFlags = [.maskCommand, .maskAlternate]
        for isDown in [true, false] {
            guard let e = CGEvent(keyboardEventSource: src, virtualKey: key, keyDown: isDown) else { continue }
            e.flags = flags
            e.post(tap: .cghidEventTap)
        }
    }
}
