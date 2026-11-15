import AppKit
import Carbon.HIToolbox

// 발표·녹화 보조 기능 (Windows 판 Features 의 SpotlightWindow·CursorHighlightWindow·KeystrokeWindow 와 같은 동작)
//   ⌃5 스포트라이트      커서 주변만 밝게. ⌥+스크롤로 크기 (⌃+스크롤은 맥 확대/축소와 겹쳐서)
//   ⌃7 커서·클릭 강조    커서 둘레 노란 원 + 클릭할 때 퍼지는 고리
//   ⌃8 키 입력 표시      누른 단축키를 화면 아래에 (다른 앱의 키를 읽으므로 '손쉬운 사용' 권한)
// 모두 클릭이 그대로 아래 프로그램으로 가는 투명 창(PassThroughWindow)에 그린다.

/// 마우스·키보드를 받지 않는 투명 창. 확대·그리기 창보다 위에 뜬다.
final class PassThroughWindow: NSWindow {
    init(frame: NSRect) {
        super.init(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
        level = NSWindow.Level(rawValue: NSWindow.Level.screenSaver.rawValue + 1)
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        isReleasedWhenClosed = false
        hasShadow = false
        isOpaque = false
        backgroundColor = .clear
        ignoresMouseEvents = true
        setFrame(frame, display: false)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// 메뉴를 열어 둔 동안에도 도는 타이머.
private func commonTimer(_ interval: TimeInterval, _ block: @escaping () -> Void) -> Timer {
    let t = Timer(timeInterval: interval, repeats: true) { _ in block() }
    RunLoop.main.add(t, forMode: .common)
    return t
}

private let srgb = CGColorSpace(name: CGColorSpace.sRGB)!

// MARK: - ⌃5 스포트라이트

final class SpotlightView: NSView {
    /// 밝게 둘 곳 (뷰 좌표). nil 이면 전부 어둡게.
    var center: CGPoint? {
        didSet {
            if let oldValue { setNeedsDisplay(spot(oldValue)) }
            if let center { setNeedsDisplay(spot(center)) }
        }
    }
    var radius: CGFloat = Settings.spotlightRadius { didSet { needsDisplay = true } }
    var dim: CGFloat = 0.65

    private func spot(_ p: CGPoint) -> CGRect {
        CGRect(x: p.x - radius - 2, y: p.y - radius - 2, width: radius * 2 + 4, height: radius * 2 + 4)
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let shade = CGColor(colorSpace: srgb, components: [0, 0, 0, dim])!
        let clear = CGColor(colorSpace: srgb, components: [0, 0, 0, 0])!
        ctx.setFillColor(shade)
        ctx.fill(bounds)
        guard let c = center,
              let g = CGGradient(colorsSpace: srgb, colors: [clear, clear, shade] as CFArray, locations: [0, 0.86, 1])
        else { return }
        // 가장자리를 부드럽게 — 안쪽 86% 는 완전히 밝고 바깥으로 갈수록 어두워진다
        ctx.setBlendMode(.copy)
        ctx.drawRadialGradient(g, startCenter: c, startRadius: 0, endCenter: c, endRadius: radius, options: [])
    }
}

final class SpotlightController {
    private var window: PassThroughWindow?
    private var view: SpotlightView?
    private var timer: Timer?
    private var scrollMonitor: Any?
    private var last = CGPoint(x: -1, y: -1)

    var isOn: Bool { window != nil }

    func toggle() { isOn ? stop() : start() }

    private func start() {
        let screen = ScreenCapture.screenUnderMouse()
        let win = PassThroughWindow(frame: screen.frame)
        let v = SpotlightView(frame: NSRect(origin: .zero, size: screen.frame.size))
        win.contentView = v
        win.orderFrontRegardless()
        window = win
        view = v
        last = CGPoint(x: -1, y: -1)
        tick()
        timer = commonTimer(1.0 / 60) { [weak self] in self?.tick() }
        // ⌥+스크롤로 크기 (전역 마우스 이벤트 보기는 권한이 필요 없다)
        scrollMonitor = NSEvent.addGlobalMonitorForEvents(matching: .scrollWheel) { [weak self] e in
            guard e.modifierFlags.contains(.option), let v = self?.view else { return }
            let dy = e.hasPreciseScrollingDeltas ? e.scrollingDeltaY * 0.01 : e.scrollingDeltaY * 0.1
            v.radius = min(600, max(60, v.radius * (1 + dy)))
            Settings.spotlightRadius = v.radius
        }
        Toast.show("스포트라이트 — ⌥+스크롤로 크기 · ⌃5 로 끄기")
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        if let scrollMonitor { NSEvent.removeMonitor(scrollMonitor) }
        scrollMonitor = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
        view = nil
    }

    private func tick() {
        guard let window, let view else { return }
        let p = NSEvent.mouseLocation
        guard p != last else { return }
        last = p
        if !NSMouseInRect(p, window.frame, false) {
            let screen = ScreenCapture.screenUnderMouse()
            window.setFrame(screen.frame, display: false)
            view.frame = NSRect(origin: .zero, size: screen.frame.size)
            view.needsDisplay = true
        }
        view.center = CGPoint(x: p.x - window.frame.minX, y: p.y - window.frame.minY)
    }
}

// MARK: - ⌃7 커서·클릭 강조

final class CursorHighlightView: NSView {
    struct Ring {
        let start: CFTimeInterval
        let color: NSColor
    }

    static let size: CGFloat = 160
    var rings: [Ring] = []
    var now: () -> CFTimeInterval = CACurrentMediaTime
    private let ringSeconds = 0.48

    var hasRings: Bool {
        let t = now()
        rings.removeAll { t - $0.start > ringSeconds }
        return !rings.isEmpty
    }

    override func draw(_ dirtyRect: NSRect) {
        let c = CGPoint(x: bounds.midX, y: bounds.midY)
        let halo: CGFloat = 56
        NSColor(hex: 0xFFEB3B).withAlphaComponent(0.35).setFill()
        NSBezierPath(ovalIn: CGRect(x: c.x - halo / 2, y: c.y - halo / 2, width: halo, height: halo)).fill()

        let t = now()
        for r in rings {
            let k = min(1, max(0, (t - r.start) / ringSeconds))
            let eased = 1 - pow(1 - k, 3)
            let d = bounds.width * 0.8 * (0.15 + 0.45 * eased)
            let path = NSBezierPath(ovalIn: CGRect(x: c.x - d / 2, y: c.y - d / 2, width: d, height: d))
            path.lineWidth = 4
            r.color.withAlphaComponent(1 - k).setStroke()
            path.stroke()
        }
    }
}

final class CursorHighlightController {
    private var window: PassThroughWindow?
    private var view: CursorHighlightView?
    private var timer: Timer?
    private var clickMonitor: Any?
    private var last = CGPoint(x: -1, y: -1)

    var isOn: Bool { window != nil }

    func toggle() { isOn ? stop() : start() }

    private func start() {
        let s = CursorHighlightView.size
        let win = PassThroughWindow(frame: NSRect(x: 0, y: 0, width: s, height: s))
        let v = CursorHighlightView(frame: NSRect(x: 0, y: 0, width: s, height: s))
        win.contentView = v
        window = win
        view = v
        last = CGPoint(x: -1, y: -1)
        tick()
        win.orderFrontRegardless()
        timer = commonTimer(1.0 / 60) { [weak self] in self?.tick() }
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]) { [weak self] e in
            let color: NSColor
            switch e.type {
            case .rightMouseDown: color = NSColor(hex: 0x1E88E5)
            case .otherMouseDown: color = NSColor(hex: 0x9E9E9E)
            default: color = NSColor(hex: 0xE53935)
            }
            self?.view?.rings.append(.init(start: CACurrentMediaTime(), color: color))
            self?.view?.needsDisplay = true
        }
        Toast.show("커서·클릭 강조 켬 — ⌃7 로 끄기")
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        if let clickMonitor { NSEvent.removeMonitor(clickMonitor) }
        clickMonitor = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
        view = nil
    }

    private func tick() {
        guard let window, let view else { return }
        let p = NSEvent.mouseLocation
        if p != last {
            last = p
            let s = CursorHighlightView.size
            window.setFrameOrigin(CGPoint(x: p.x - s / 2, y: p.y - s / 2))
            // 다른 창이 맨 위로 올라와도 다시 위로
            window.orderFrontRegardless()
        }
        // 고리가 있으면 계속 그리고, 마지막 고리가 끝난 순간 한 번 더 그려 지운다
        let had = !view.rings.isEmpty
        _ = view.hasRings
        if had { view.needsDisplay = true }
    }
}

// MARK: - ⌃8 키 입력 표시

enum KeyNames {
    /// 화면에 보여 줄 글. 단축키(⌘·⌃·⌥ 조합)와 특수 키만, 일반 타자는 nil.
    static func describe(keyCode: Int, flags: NSEvent.ModifierFlags) -> String? {
        let f = flags.intersection(.deviceIndependentFlagsMask)
        let combo = f.contains(.command) || f.contains(.control) || f.contains(.option)
        let special = specials[keyCode]
        if !combo && special == nil { return nil }
        guard let name = special ?? plain[keyCode] else { return nil }
        var mods = ""
        if f.contains(.control) { mods += "⌃" }
        if f.contains(.option) { mods += "⌥" }
        if f.contains(.shift) { mods += "⇧" }
        if f.contains(.command) { mods += "⌘" }
        return mods.isEmpty ? name : "\(mods) \(name)"
    }

    // 한글 입력 중이어도 같은 글이 나오게 문자 대신 키 위치(kVK_*)로 이름을 정한다
    private static let plain: [Int: String] = {
        var m: [Int: String] = [
            kVK_ANSI_A: "A", kVK_ANSI_B: "B", kVK_ANSI_C: "C", kVK_ANSI_D: "D", kVK_ANSI_E: "E", kVK_ANSI_F: "F",
            kVK_ANSI_G: "G", kVK_ANSI_H: "H", kVK_ANSI_I: "I", kVK_ANSI_J: "J", kVK_ANSI_K: "K", kVK_ANSI_L: "L",
            kVK_ANSI_M: "M", kVK_ANSI_N: "N", kVK_ANSI_O: "O", kVK_ANSI_P: "P", kVK_ANSI_Q: "Q", kVK_ANSI_R: "R",
            kVK_ANSI_S: "S", kVK_ANSI_T: "T", kVK_ANSI_U: "U", kVK_ANSI_V: "V", kVK_ANSI_W: "W", kVK_ANSI_X: "X",
            kVK_ANSI_Y: "Y", kVK_ANSI_Z: "Z",
            kVK_ANSI_0: "0", kVK_ANSI_1: "1", kVK_ANSI_2: "2", kVK_ANSI_3: "3", kVK_ANSI_4: "4",
            kVK_ANSI_5: "5", kVK_ANSI_6: "6", kVK_ANSI_7: "7", kVK_ANSI_8: "8", kVK_ANSI_9: "9",
            kVK_ANSI_Minus: "-", kVK_ANSI_Equal: "=", kVK_ANSI_LeftBracket: "[", kVK_ANSI_RightBracket: "]",
            kVK_ANSI_Semicolon: ";", kVK_ANSI_Quote: "'", kVK_ANSI_Comma: ",", kVK_ANSI_Period: ".",
            kVK_ANSI_Slash: "/", kVK_ANSI_Backslash: "\\", kVK_ANSI_Grave: "`", kVK_Space: "Space",
        ]
        m[kVK_ANSI_KeypadEnter] = "Enter"
        return m
    }()

    private static let specials: [Int: String] = [
        kVK_Escape: "esc", kVK_Return: "return ↩", kVK_Tab: "tab ⇥", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
        kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_Home: "home", kVK_End: "end", kVK_PageUp: "page up", kVK_PageDown: "page down",
        kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
        kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
    ]
}

final class KeystrokeView: NSView {
    struct Chip {
        var text: String
        var time: CFTimeInterval
        var count: Int
    }

    var chips: [Chip] = []
    var now: () -> CFTimeInterval = CACurrentMediaTime
    static let hold = 1.6
    static let fade = 0.4
    private let font = NSFont.systemFont(ofSize: 26, weight: .semibold)

    func add(_ text: String) {
        let t = now()
        if var last = chips.last, last.text == text, t - last.time < max(0.6, Self.hold * 0.7) {
            last.count += 1
            last.time = t
            chips[chips.count - 1] = last
        } else {
            chips.append(Chip(text: text, time: t, count: 1))
            if chips.count > 4 { chips.removeFirst() }
        }
        needsDisplay = true
    }

    /// 다 사라졌으면 false (그만 다시 그려도 됨)
    var isAnimating: Bool {
        let t = now()
        chips.removeAll { t - $0.time > Self.hold + Self.fade }
        return !chips.isEmpty
    }

    override func draw(_ dirtyRect: NSRect) {
        let t = now()
        let items: [(NSAttributedString, CGFloat, CGSize)] = chips.map { chip in
            let age = t - chip.time
            let alpha = age <= Self.hold ? 1 : max(0, 1 - (age - Self.hold) / Self.fade)
            let label = chip.count > 1 ? "\(chip.text)  ×\(chip.count)" : chip.text
            let s = NSAttributedString(string: label, attributes: [
                .font: font, .foregroundColor: NSColor.white.withAlphaComponent(alpha),
            ])
            let ts = s.size()
            return (s, alpha, CGSize(width: ts.width + 36, height: ts.height + 18))
        }
        let gap: CGFloat = 12
        let total = items.reduce(0) { $0 + $1.2.width } + gap * CGFloat(max(0, items.count - 1))
        var x = (bounds.width - total) / 2
        let bottom: CGFloat = 28
        for (s, alpha, size) in items {
            let r = CGRect(x: x, y: bottom, width: size.width, height: size.height)
            let path = NSBezierPath(roundedRect: r, xRadius: 10, yRadius: 10)
            NSColor(red: 28 / 255, green: 28 / 255, blue: 30 / 255, alpha: 225 / 255 * alpha).setFill()
            path.fill()
            NSColor.white.withAlphaComponent(90 / 255 * alpha).setStroke()
            path.lineWidth = 1
            path.stroke()
            s.draw(at: CGPoint(x: r.minX + 18, y: r.minY + 8))
            x += size.width + gap
        }
    }
}

final class KeystrokeController {
    private var window: PassThroughWindow?
    private var view: KeystrokeView?
    private var timer: Timer?
    private var keyMonitor: Any?

    var isOn: Bool { window != nil }

    func toggle() { isOn ? stop() : start() }

    private func start() {
        let win = PassThroughWindow(frame: .zero)
        let v = KeystrokeView(frame: .zero)
        win.contentView = v
        window = win
        view = v
        place()
        win.orderFrontRegardless()
        // 다른 앱의 키 보기 — '손쉬운 사용' 권한이 있어야 이벤트가 온다
        keyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let text = KeyNames.describe(keyCode: Int(e.keyCode), flags: e.modifierFlags) else { return }
            self?.show(text)
        }
        Toast.show("키 입력 표시 켬 — 단축키만 보여요 · ⌃8 로 끄기")
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
        view = nil
    }

    private func show(_ text: String) {
        guard let window, let view else { return }
        place()
        window.orderFrontRegardless()
        view.add(text)
        if timer == nil {
            timer = commonTimer(1.0 / 30) { [weak self] in
                guard let self, let view = self.view else { return }
                view.needsDisplay = true
                if !view.isAnimating {
                    self.timer?.invalidate()
                    self.timer = nil
                }
            }
        }
    }

    /// 마우스가 있는 화면 아래쪽에 둔다
    private func place() {
        guard let window, let view else { return }
        let vf = ScreenCapture.screenUnderMouse().visibleFrame
        let frame = NSRect(x: vf.minX, y: vf.minY, width: vf.width, height: 110)
        if window.frame != frame {
            window.setFrame(frame, display: false)
            view.frame = NSRect(origin: .zero, size: frame.size)
        }
    }
}

// MARK: - 짧은 알림

final class ToastView: NSView {
    let text: String

    init(text: String) {
        self.text = text
        super.init(frame: .zero)
        let s = attributed.size()
        setFrameSize(CGSize(width: s.width + 40, height: s.height + 22))
    }

    required init?(coder: NSCoder) { fatalError() }

    private var attributed: NSAttributedString {
        NSAttributedString(string: text, attributes: [
            .font: NSFont.systemFont(ofSize: 17, weight: .medium), .foregroundColor: NSColor.white,
        ])
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(white: 0.1, alpha: 0.88).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: bounds.height / 2, yRadius: bounds.height / 2).fill()
        attributed.draw(at: CGPoint(x: 20, y: 11))
    }
}

enum Toast {
    private static var window: PassThroughWindow?
    private static var hideWork: DispatchWorkItem?

    static func show(_ text: String, seconds: TimeInterval = 1.8) {
        window?.orderOut(nil)
        let v = ToastView(text: text)
        let vf = ScreenCapture.screenUnderMouse().visibleFrame
        let frame = NSRect(x: vf.midX - v.frame.width / 2, y: vf.minY + vf.height * 0.18,
                           width: v.frame.width, height: v.frame.height)
        let win = PassThroughWindow(frame: frame)
        win.contentView = v
        win.orderFrontRegardless()
        window = win
        hideWork?.cancel()
        let work = DispatchWorkItem {
            win.orderOut(nil)
            if window === win { window = nil }
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }
}
