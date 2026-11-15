import AppKit
import Carbon.HIToolbox

/// 메뉴 막대 상주 본체. 단축키 → 기능 연결, 기능 창 켜고 끄기.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var overlay: NSWindow?
    private var failedHotkeys: [String] = []
    private var loginMenuItem: NSMenuItem!
    private var autoUpdateMenuItem: NSMenuItem!

    // 기본 단축키는 Windows 판(Ctrl+1/2/3)과 같은 ⌃1 ⌃2 ⌃3.
    private let shortcuts: [(id: UInt32, key: Int, label: String, action: Selector)] = [
        (1, kVK_ANSI_1, "화면 확대", #selector(startZoom)),
        (2, kVK_ANSI_2, "그리기", #selector(startDraw)),
        (3, kVK_ANSI_3, "타이머", #selector(startTimer)),
        (4, kVK_ANSI_4, "라이브 줌 (맥 확대/축소)", #selector(startLiveZoom)),
        (5, kVK_ANSI_5, "스포트라이트", #selector(toggleSpotlight)),
        (6, kVK_ANSI_6, "영역 캡처 (클립보드로)", #selector(startCapture)),
        (7, kVK_ANSI_7, "커서·클릭 강조", #selector(toggleCursorHighlight)),
        (8, kVK_ANSI_8, "키 입력 표시", #selector(toggleKeystrokes)),
    ]
    private let spotlight = SpotlightController()
    private let cursorHighlight = CursorHighlightController()
    private let keystrokes = KeystrokeController()

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()
        for s in shortcuts {
            let action = s.action
            let ok = Hotkeys.shared.register(keyCode: s.key, modifiers: controlKey, id: s.id) { [weak self] in
                self?.perform(action)
            }
            if !ok { failedHotkeys.append("⌃\(s.id) \(s.label)") }
        }
        Updater.isBusy = { [weak self] in
            guard let self else { return false }
            return self.overlay != nil || self.spotlight.isOn || self.cursorHighlight.isOn || self.keystrokes.isOn
        }
        Updater.startSchedule()
        if !Settings.shownWelcome {
            Settings.shownWelcome = true
            showWelcome()
        } else if !failedHotkeys.isEmpty {
            warnHotkeys()
        }
    }

    // MARK: 메뉴 막대

    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "plus.magnifyingglass", accessibilityDescription: "MyZoom")

        let menu = NSMenu()
        menu.delegate = self
        for s in shortcuts {
            let item = NSMenuItem(title: s.label, action: s.action, keyEquivalent: String(s.id))
            item.keyEquivalentModifierMask = .control
            item.target = self
            item.tag = Int(s.id)
            menu.addItem(item)
        }
        menu.addItem(.separator())
        menu.addItem(item("타이머 문구 바꾸기…", #selector(editTimerMessage)))
        menu.addItem(item("화면 기록 권한 확인…", #selector(checkPermission)))
        menu.addItem(item("라이브 줌(⌃4) 설정 안내…", #selector(showLiveZoomGuide)))
        loginMenuItem = item("로그인할 때 자동 실행", #selector(toggleLogin))
        menu.addItem(loginMenuItem)
        autoUpdateMenuItem = item("자동 업데이트", #selector(toggleAutoUpdate))
        menu.addItem(autoUpdateMenuItem)
        menu.addItem(item("지금 업데이트 확인", #selector(checkUpdate)))
        menu.addItem(.separator())
        menu.addItem(item("MyZoom \(Settings.version) 정보", #selector(showAbout)))
        menu.addItem(item("종료", #selector(quit)))
        statusItem.menu = menu
    }

    private func item(_ title: String, _ action: Selector) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: "")
        i.target = self
        return i
    }

    func menuWillOpen(_ menu: NSMenu) {
        loginMenuItem.state = LoginItem.isEnabled ? .on : .off
        autoUpdateMenuItem.state = Settings.autoUpdate ? .on : .off
        menu.item(withTag: 5)?.state = spotlight.isOn ? .on : .off
        menu.item(withTag: 7)?.state = cursorHighlight.isOn ? .on : .off
        menu.item(withTag: 8)?.state = keystrokes.isOn ? .on : .off
    }

    // MARK: 기능

    @objc func startZoom() { startAnnotation(zoomMode: true) }
    @objc func startDraw() { startAnnotation(zoomMode: false) }

    private func startAnnotation(zoomMode: Bool) {
        if let overlay {
            // 같은 단축키를 다시 누르면 닫기 (Windows 판과 같음)
            closeOverlay(overlay)
            return
        }
        guard ScreenCapture.hasPermission else {
            askForPermission()
            return
        }
        let screen = ScreenCapture.screenUnderMouse()
        Task { @MainActor in
            do {
                let image = try await ScreenCapture.capture(screen)
                self.showAnnotation(image: image, screen: screen, zoomMode: zoomMode)
            } catch CaptureError.noPermission {
                self.askForPermission()
            } catch {
                self.alert("화면을 찍지 못했어요", "\(error.localizedDescription)\n\n화면 기록 권한을 확인해 주세요.")
            }
        }
    }

    private func showAnnotation(image: CGImage, screen: NSScreen, zoomMode: Bool) {
        let win = OverlayWindow(screen: screen)
        let view = AnnotationView(frame: NSRect(origin: .zero, size: screen.frame.size), image: image, zoomMode: zoomMode)
        view.onClose = { [weak self, weak win] in if let win { self?.closeOverlay(win) } }
        win.contentView = view
        present(win, firstResponder: view)
    }

    @objc func startTimer() {
        if let overlay {
            closeOverlay(overlay)
            return
        }
        let screen = ScreenCapture.screenUnderMouse()
        let win = OverlayWindow(screen: screen)
        win.isOpaque = Settings.timerBackground != 0
        win.backgroundColor = .clear
        let view = TimerView(frame: NSRect(origin: .zero, size: screen.frame.size), minutes: Settings.timerMinutes)
        view.onClose = { [weak self, weak win, weak view] in
            if let view { Settings.timerMinutes = view.minutesToRemember }
            if let win { self?.closeOverlay(win) }
        }
        win.contentView = view
        view.start()
        present(win, firstResponder: view)
    }

    @objc func toggleSpotlight() { spotlight.toggle() }
    @objc func toggleCursorHighlight() { cursorHighlight.toggle() }

    @objc func toggleKeystrokes() {
        guard keystrokes.isOn || LiveZoom.canSendKeys else {
            askForKeyPermission(reason: "⌃8 키 입력 표시는 다른 프로그램에서 누른 키를 읽어야 해서 권한이 필요해요.", retry: "⌃8")
            return
        }
        keystrokes.toggle()
    }

    /// ⌃6 — 맥 기본 영역 캡처(screencapture -i -c)로 드래그한 곳을 클립보드에. Esc 로 취소.
    @objc func startCapture() {
        guard ScreenCapture.hasPermission else {
            askForPermission()
            return
        }
        let before = NSPasteboard.general.changeCount
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        p.arguments = ["-i", "-c"]
        p.terminationHandler = { _ in
            DispatchQueue.main.async {
                if NSPasteboard.general.changeCount != before {
                    Toast.show("클립보드에 복사했어요 — ⌘V 로 붙여넣기")
                }
            }
        }
        do { try p.run() } catch { alert("영역 캡처를 열지 못했어요", error.localizedDescription) }
    }

    /// ⌃4 — 맥 기본 확대/축소를 ⌥⌘8 로 켜고 끈다 (LiveZoom 참고).
    @objc func startLiveZoom() {
        guard LiveZoom.canSendKeys else {
            askForKeyPermission(reason: "⌃4 라이브 줌은 맥 확대 단축키(⌥⌘8)를 MyZoom 이 대신 눌러 주는 방식이라 권한이 필요해요.", retry: "⌃4")
            return
        }
        if !Settings.shownLiveZoomGuide {
            showLiveZoomGuide()
            return
        }
        LiveZoom.toggle()
    }

    @objc func showLiveZoomGuide() {
        let a = NSAlert()
        a.messageText = "라이브 줌은 맥 기본 확대 기능을 써요"
        a.informativeText = """
        ⌃4 를 누르면 MyZoom 이 맥 확대/축소 단축키(⌥⌘8)를 대신 눌러 줘요.
        확대된 화면에서도 클릭·입력이 그대로 돼요.

        처음 한 번만 설정해 주세요.
        1. [설정 열기]를 누르세요.
        2. '키보드 단축키를 사용하여 확대/축소'를 켜세요.
        3. 다시 ⌃4 를 누르면 확대, 한 번 더 누르면 원래대로.

        배율: ⌥⌘= 크게 · ⌥⌘- 작게
        control 을 누른 채 두 손가락 스크롤로도 확대돼요 ('보조 키와 함께 스크롤 동작…' 설정).
        """
        a.addButton(withTitle: "설정 열기")
        a.addButton(withTitle: "켜 두었어요")
        a.addButton(withTitle: "닫기")
        NSApp.activate(ignoringOtherApps: true)
        switch a.runModal() {
        case .alertFirstButtonReturn:
            Settings.shownLiveZoomGuide = true
            LiveZoom.openZoomSettings()
        case .alertSecondButtonReturn:
            Settings.shownLiveZoomGuide = true
            LiveZoom.toggle()
        default:
            break
        }
    }

    private func askForKeyPermission(reason: String, retry: String) {
        LiveZoom.requestKeyPermission()
        let a = NSAlert()
        a.messageText = "손쉬운 사용 권한이 필요해요"
        a.informativeText = """
        \(reason)

        1. [설정 열기]를 누르세요.
        2. 목록에서 MyZoom 을 켜세요. (없으면 + 를 눌러 홈 폴더 > 응용 프로그램의 MyZoom 을 추가)
        3. 다시 \(retry) 를 누르세요. (다시 시작은 필요 없어요)

        업데이트 뒤에 다시 물어보면, 목록에서 MyZoom 을 껐다가 다시 켜 주세요.
        """
        a.addButton(withTitle: "설정 열기")
        a.addButton(withTitle: "닫기")
        NSApp.activate(ignoringOtherApps: true)
        if a.runModal() == .alertFirstButtonReturn {
            LiveZoom.openAccessibilityPermission()
        }
    }

    private func present(_ win: NSWindow, firstResponder: NSView) {
        overlay = win
        NSApp.activate(ignoringOtherApps: true)
        win.makeKeyAndOrderFront(nil)
        win.makeFirstResponder(firstResponder)
    }

    private func closeOverlay(_ win: NSWindow) {
        (win.contentView as? TimerView)?.stop()
        win.orderOut(nil)
        win.close()
        if overlay === win { overlay = nil }
    }

    // MARK: 권한·안내

    private func askForPermission() {
        ScreenCapture.requestPermission()
        let a = NSAlert()
        a.messageText = "화면 기록 권한이 필요해요"
        a.informativeText = """
        화면을 확대하고 그 위에 그리려면 지금 화면을 찍어야 해서 권한이 필요해요.

        1. [설정 열기]를 누르세요.
        2. 목록에서 MyZoom 을 켜세요. (없으면 + 를 눌러 응용 프로그램 폴더의 MyZoom 을 추가)
        3. MyZoom 을 다시 시작하세요.

        업데이트 뒤에 다시 물어보면, 목록에서 MyZoom 을 껐다가 다시 켜 주세요.
        """
        a.addButton(withTitle: "설정 열기")
        a.addButton(withTitle: "MyZoom 다시 시작")
        a.addButton(withTitle: "닫기")
        NSApp.activate(ignoringOtherApps: true)
        switch a.runModal() {
        case .alertFirstButtonReturn:
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!)
        case .alertSecondButtonReturn:
            relaunch()
        default:
            break
        }
    }

    @objc func checkPermission() {
        if ScreenCapture.hasPermission {
            alert("화면 기록 권한이 켜져 있어요", "⌃1 화면 확대, ⌃2 그리기를 쓸 수 있어요.")
        } else {
            askForPermission()
        }
    }

    private func showWelcome() {
        var text = """
        화면 오른쪽 위 메뉴 막대의 돋보기 아이콘에 MyZoom 이 있어요.

        ⌃1 (control+1)  화면 확대 — 마우스를 따라 움직이고, 누르고 끌면 그 자리에 그려요
        ⌃2  그리기 — 지금 화면 위에 바로 판서
        ⌃3  타이머 — 쉬는 시간·활동 시간 카운트다운
        ⌃4  라이브 줌 — 맥 기본 확대/축소를 켜고 끄기 (클릭·입력 가능)
        ⌃5  스포트라이트 — 커서 주변만 밝게 (⌥+스크롤로 크기)
        ⌃6  영역 캡처 — 드래그한 곳을 클립보드로
        ⌃7  커서·클릭 강조 · ⌃8 키 입력 표시 (녹화용)

        확대·그리기·캡처는 '화면 기록' 권한을, 라이브 줌·키 입력 표시는 '손쉬운 사용' 권한을 처음에 물어봐요.
        """
        if !failedHotkeys.isEmpty {
            text += "\n\n다른 프로그램이 이미 쓰고 있어서 이 단축키는 등록하지 못했어요: \(failedHotkeys.joined(separator: ", "))\n메뉴 막대에서 눌러 쓸 수 있어요."
        }
        alert("MyZoom 을 켰어요", text)
    }

    private func warnHotkeys() {
        alert("단축키 일부를 쓸 수 없어요",
              "다른 프로그램이 이미 쓰고 있어요: \(failedHotkeys.joined(separator: ", "))\n메뉴 막대의 돋보기 아이콘에서 눌러 쓸 수 있어요.")
    }

    @objc func editTimerMessage() {
        let a = NSAlert()
        a.messageText = "타이머 문구"
        a.informativeText = "타이머 화면 위쪽에 보이는 글이에요."
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = Settings.timerMessage
        a.accessoryView = field
        a.addButton(withTitle: "저장")
        a.addButton(withTitle: "취소")
        NSApp.activate(ignoringOtherApps: true)
        a.window.initialFirstResponder = field
        if a.runModal() == .alertFirstButtonReturn {
            Settings.timerMessage = field.stringValue
        }
    }

    @objc func toggleLogin() { LoginItem.setEnabled(!LoginItem.isEnabled) }
    @objc func toggleAutoUpdate() { Settings.autoUpdate.toggle() }
    @objc func checkUpdate() { Updater.check(manual: true) }

    @objc func showAbout() {
        alert("MyZoom \(Settings.version)",
              "화면 확대·판서·타이머 — 연수와 수업 발표용.\n만든 사람: 엄태식 (eompage.com)\nhttps://github.com/uts1002/myzoom")
    }

    @objc func quit() { NSApp.terminate(nil) }

    private func relaunch() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 1; open \"\(Bundle.main.bundlePath)\""]
        try? p.run()
        NSApp.terminate(nil)
    }

    private func alert(_ title: String, _ text: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
}
