import AppKit
import Carbon.HIToolbox

/// 타이머(⌃3). 쉬는 시간·활동 시간 카운트다운. 배경은 흐린 화면 / 검정 / 흰색.
final class TimerView: NSView {
    private(set) var total: TimeInterval
    private(set) var remaining: TimeInterval
    private(set) var running = true
    private var endDate: Date
    private var ticker: Timer?
    private var finishedBeeps = 0
    var background = Settings.timerBackground
    var message = Settings.timerMessage
    var onClose: (() -> Void)?
    var now: () -> Date = Date.init

    init(frame: NSRect, minutes: Int) {
        total = TimeInterval(minutes * 60)
        remaining = total
        endDate = Date().addingTimeInterval(total)
        super.init(frame: frame)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    func start() {
        ticker?.invalidate()
        ticker = Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in self?.tick() }
    }

    func stop() {
        ticker?.invalidate()
        ticker = nil
    }

    private func tick() {
        guard running else { return }
        let left = endDate.timeIntervalSince(now())
        if left <= 0 {
            if remaining > 0 { finish() }
            remaining = 0
        } else {
            remaining = left
        }
        needsDisplay = true
    }

    private func finish() {
        remaining = 0
        finishedBeeps = 0
        beep()
    }

    private func beep() {
        guard finishedBeeps < 3 else { return }
        finishedBeeps += 1
        NSSound(named: "Glass")?.play()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { [weak self] in
            guard let self, self.remaining == 0 else { return }
            self.beep()
        }
    }

    private func adjust(_ seconds: TimeInterval) {
        remaining = max(0, remaining + seconds)
        total = max(total, remaining)
        endDate = now().addingTimeInterval(remaining)
        needsDisplay = true
    }

    private func toggle() {
        if running {
            running = false
        } else {
            if remaining <= 0 { remaining = total }
            endDate = now().addingTimeInterval(remaining)
            running = true
        }
        needsDisplay = true
    }

    private func reset() {
        remaining = total
        endDate = now().addingTimeInterval(remaining)
        needsDisplay = true
    }

    /// 타이머를 닫을 때 다음에도 쓰도록 분 단위로 기억한다.
    var minutesToRemember: Int { max(1, Int((total / 60).rounded())) }

    // MARK: 그리기

    private var palette: (bg: NSColor, fg: NSColor, sub: NSColor) {
        switch background {
        case 1: return (NSColor(hex: 0x111111), .white, NSColor.white.withAlphaComponent(0.6))
        case 2: return (.white, NSColor(hex: 0x1C1C1E), NSColor(hex: 0x1C1C1E, alpha: 0.55))
        default: return (NSColor.black.withAlphaComponent(0.62), .white, NSColor.white.withAlphaComponent(0.7))
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        let (bg, fg, sub) = palette
        bg.setFill()
        bounds.fill()

        let done = remaining <= 0
        let secs = Int(remaining.rounded(.up))
        let big = done ? "끝!" : String(format: "%d:%02d", secs / 60, secs % 60)
        let bigSize = min(bounds.width * 0.26, bounds.height * 0.42)

        let center = NSMutableParagraphStyle()
        center.alignment = .center

        func line(_ text: String, size: CGFloat, weight: NSFont.Weight, color: NSColor, mono: Bool = false) -> NSAttributedString {
            let font = mono ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight)
                            : NSFont.systemFont(ofSize: size, weight: weight)
            return NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color, .paragraphStyle: center])
        }

        let title = line(message, size: bigSize * 0.22, weight: .semibold, color: sub)
        let clock = line(big, size: bigSize, weight: .semibold, color: done ? NSColor(hex: 0xFF6B5E) : fg, mono: true)
        let f = DateFormatter()
        f.dateFormat = "a h:mm"
        f.locale = Locale(identifier: "ko_KR")
        let status = done ? "시간이 다 됐어요" : running ? "\(f.string(from: endDate))에 끝나요" : "멈춤 — Space 를 누르면 이어서 가요"
        let foot = line(status, size: bigSize * 0.11, weight: .regular, color: sub)

        let h1 = title.size().height, h2 = clock.size().height, h3 = foot.size().height
        var y = (bounds.height - (h1 + h2 + h3)) / 2 - bounds.height * 0.04
        for (s, h) in [(title, h1), (clock, h2), (foot, h3)] {
            s.draw(in: CGRect(x: 0, y: y, width: bounds.width, height: h))
            y += h
        }

        let keys = line("Space 시작·멈춤  ·  ↑↓ 1분  ←→ 10초  ·  R 처음부터  ·  B 배경  ·  Esc 닫기",
                        size: 13, weight: .medium, color: sub)
        keys.draw(in: CGRect(x: 0, y: bounds.height - 56, width: bounds.width, height: 24))
    }

    // MARK: 입력

    override func keyDown(with e: NSEvent) {
        switch Int(e.keyCode) {
        case kVK_Escape: close()
        case kVK_Space: toggle()
        case kVK_UpArrow: adjust(60)
        case kVK_DownArrow: adjust(-60)
        case kVK_RightArrow: adjust(10)
        case kVK_LeftArrow: adjust(-10)
        case kVK_ANSI_R: reset()
        case kVK_ANSI_B:
            background = (background + 1) % 3
            Settings.timerBackground = background
            window?.isOpaque = background != 0
            needsDisplay = true
        default: super.keyDown(with: e)
        }
    }

    override func rightMouseDown(with e: NSEvent) { close() }

    func close() {
        stop()
        onClose?()
    }
}
