import AppKit

/// 자동 업데이트 — Windows 판과 같은 릴리즈의 version.txt 를 보고, 새 버전이면 MyZoom-mac.zip 을 받아
/// 앱을 끈 뒤 바꿔 끼우고 다시 연다. 켜고 30초 뒤, 그 뒤 6시간마다. 개발판(0.0.0)은 하지 않는다.
enum Updater {
    static let base = URL(string: "https://github.com/uts1002/myzoom/releases/latest/download/")!

    /// 지금 화면 확대·그리기·타이머를 쓰는 중인지 (쓰는 중이면 다음 확인으로 미룬다).
    static var isBusy: () -> Bool = { false }

    private static var timer: Timer?

    static func startSchedule() {
        guard Settings.version != "0.0.0" else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 30) { check(manual: false) }
        timer = Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { _ in check(manual: false) }
    }

    static func isNewer(_ remote: String, than local: String) -> Bool {
        let r = remote.split(separator: ".").map { Int($0) ?? 0 }
        let l = local.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(r.count, l.count) {
            let a = i < r.count ? r[i] : 0, b = i < l.count ? l[i] : 0
            if a != b { return a > b }
        }
        return false
    }

    static func check(manual: Bool) {
        if !manual && (!Settings.autoUpdate || isBusy()) { return }
        Task {
            do {
                let (data, _) = try await URLSession.shared.data(from: base.appendingPathComponent("version.txt"))
                let remote = String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
                guard isNewer(remote, than: Settings.version) else {
                    if manual { await alert("최신 버전이에요", "지금 쓰는 MyZoom \(Settings.version) 이 최신이에요.") }
                    return
                }
                try await install(version: remote)
            } catch {
                if manual { await alert("업데이트를 확인하지 못했어요", "인터넷 연결을 확인해 주세요.\n(\(error.localizedDescription))") }
            }
        }
    }

    @MainActor
    private static func alert(_ title: String, _ text: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }

    private static func install(version: String) async throws {
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("myzoom-update-\(version)")
        try? fm.removeItem(at: work)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)

        let (tmp, _) = try await URLSession.shared.download(from: base.appendingPathComponent("MyZoom-mac.zip"))
        let zip = work.appendingPathComponent("MyZoom-mac.zip")
        try fm.moveItem(at: tmp, to: zip)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path])
        let newApp = work.appendingPathComponent("MyZoom.app")
        guard fm.fileExists(atPath: newApp.path) else { throw CocoaError(.fileNoSuchFile) }

        let oldApp = Bundle.main.bundlePath
        let pid = ProcessInfo.processInfo.processIdentifier
        let script = work.appendingPathComponent("swap.sh")
        try """
        #!/bin/sh
        while kill -0 \(pid) 2>/dev/null; do sleep 0.3; done
        rm -rf "\(oldApp)"
        mv "\(newApp.path)" "\(oldApp)"
        xattr -dr com.apple.quarantine "\(oldApp)" 2>/dev/null
        open "\(oldApp)"
        """.write(to: script, atomically: true, encoding: .utf8)

        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = [script.path]
        try p.run()
        await MainActor.run { NSApp.terminate(nil) }
    }

    private static func run(_ exe: String, _ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw CocoaError(.fileReadCorruptFile) }
    }
}
