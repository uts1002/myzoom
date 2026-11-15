import Foundation

/// 로그인할 때 자동 실행 — ~/Library/LaunchAgents 에 plist 를 둔다 (서명 없는 앱에서도 동작).
/// install-mac.sh 도 같은 파일을 만든다. 라벨·경로를 바꾸면 거기도 같이 바꿀 것.
enum LoginItem {
    static let label = "com.eompage.myzoom"

    static var plistURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }

    static var isEnabled: Bool { FileManager.default.fileExists(atPath: plistURL.path) }

    static func setEnabled(_ on: Bool) {
        if !on {
            try? FileManager.default.removeItem(at: plistURL)
            return
        }
        let plist: [String: Any] = [
            "Label": label,
            "ProgramArguments": ["/usr/bin/open", "-g", Bundle.main.bundlePath],
            "RunAtLoad": true,
        ]
        do {
            try FileManager.default.createDirectory(at: plistURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
            try data.write(to: plistURL)
        } catch {
            NSLog("MyZoom: 자동 실행 설정 실패 \(error)")
        }
    }
}
