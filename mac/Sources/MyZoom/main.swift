import AppKit

// --selftest <폴더> : 화면 기록 권한 없이 그리기·타이머 화면을 PNG 로 그려 보고 끝낸다 (CI 확인용).
if let i = CommandLine.arguments.firstIndex(of: "--selftest") {
    let out = CommandLine.arguments.count > i + 1 ? CommandLine.arguments[i + 1] : "selftest"
    exit(SelfTest.run(outputDir: out))
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory) // Dock 아이콘 없이 메뉴 막대에만
app.run()
