import AppKit
import ScreenCaptureKit

enum CaptureError: Error {
    case noPermission
    case noDisplay
}

/// 화면 한 장 찍기. '화면 및 시스템 오디오 기록' 권한이 필요하다.
enum ScreenCapture {
    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    /// 처음이면 시스템 권한 창을 띄우고 목록에 MyZoom 을 올린다. 켠 뒤에는 앱을 다시 시작해야 반영된다.
    static func requestPermission() {
        _ = CGRequestScreenCaptureAccess()
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID? {
        screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
    }

    /// 마우스가 있는 화면.
    static func screenUnderMouse() -> NSScreen {
        let p = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(p, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    static func capture(_ screen: NSScreen) async throws -> CGImage {
        guard hasPermission else { throw CaptureError.noPermission }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let id = displayID(of: screen),
              let display = content.displays.first(where: { $0.displayID == id }) else {
            throw CaptureError.noDisplay
        }
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let config = SCStreamConfiguration()
        let scale = screen.backingScaleFactor
        config.width = Int(CGFloat(display.width) * scale)
        config.height = Int(CGFloat(display.height) * scale)
        config.showsCursor = false
        return try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
    }
}
