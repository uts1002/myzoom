import AppKit
import Carbon.HIToolbox

/// 화면 위에 떠서 키·마우스를 받는 테두리 없는 창.
final class OverlayWindow: NSWindow {
    init(screen: NSScreen) {
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        level = .screenSaver
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        isReleasedWhenClosed = false
        hasShadow = false
        acceptsMouseMovedEvents = true
        setFrame(screen.frame, display: false)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// 화면 확대(⌃1)와 그리기(⌃2)를 한 화면에서 처리한다 — Windows 판 AnnotationWindow 와 같은 동작.
///  - 확대: 찍어 둔 화면을 2배로 보여 주고 마우스를 따라 움직인다. 누르고 끌면 그 자리에서 판서.
///  - 그리기: 1배에서 바로 판서. 휠로 확대할 수 있다.
/// 좌표: 그림은 '1배 화면' 포인트(왼쪽 위 원점)로 저장하고, 보여 줄 때 zoom·origin 으로 바꾼다.
final class AnnotationView: NSView {
    enum Board { case screen, white, black }

    let image: CGImage?
    var zoom: CGFloat = 1
    var origin: CGPoint = .zero          // 보이는 영역의 왼쪽 위 (1배 좌표)
    var followMouse = false              // 확대 모드에서 첫 클릭 전까지 마우스를 따라감
    var strokes: [Stroke] = []
    var current: Stroke?
    var tool: Tool = .pen
    var colorIndex = 0
    var widthScale: CGFloat = 1
    var board: Board = .screen
    var showHint = true
    var onClose: (() -> Void)?

    private var undoStack: [[Stroke]] = []
    private var redoStack: [[Stroke]] = []
    private var mouse: CGPoint = .zero
    private var eraseSnapshot: [Stroke]?
    private var panStart: (mouse: CGPoint, origin: CGPoint)?
    private var flash: String?
    private var flashTimer: Timer?

    var color: NSColor { Settings.palette[colorIndex].color }

    init(frame: NSRect, image: CGImage?, zoomMode: Bool) {
        self.image = image
        super.init(frame: frame)
        if zoomMode {
            zoom = Settings.initialZoom
            followMouse = true
        }
        mouse = CGPoint(x: frame.width / 2, y: frame.height / 2)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    // MARK: 좌표

    private var visibleOrigin: CGPoint {
        followMouse ? CGPoint(x: mouse.x * (1 - 1 / zoom), y: mouse.y * (1 - 1 / zoom)) : origin
    }

    private func toImage(_ v: CGPoint) -> CGPoint {
        let o = visibleOrigin
        return CGPoint(x: o.x + v.x / zoom, y: o.y + v.y / zoom)
    }

    private func clampOrigin() {
        origin.x = max(0, min(origin.x, bounds.width - bounds.width / zoom))
        origin.y = max(0, min(origin.y, bounds.height - bounds.height / zoom))
    }

    private func freeze() {
        guard followMouse else { return }
        origin = visibleOrigin
        followMouse = false
    }

    /// 화면 위 점 v 를 고정한 채 배율을 바꾼다.
    func setZoom(_ z: CGFloat, keeping v: CGPoint) {
        let newZoom = max(1, min(Settings.maxZoom, z))
        if followMouse {
            zoom = newZoom
        } else {
            let p = toImage(v)
            zoom = newZoom
            origin = CGPoint(x: p.x - v.x / zoom, y: p.y - v.y / zoom)
            clampOrigin()
        }
        needsDisplay = true
    }

    // MARK: 그리기

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let o = visibleOrigin

        switch board {
        case .white:
            NSColor.white.setFill()
            bounds.fill()
        case .black:
            NSColor(hex: 0x1F2B26).setFill() // 칠판
            bounds.fill()
        case .screen:
            if let image {
                NSGraphicsContext.current?.imageInterpolation = zoom > 1 ? .high : .default
                let dest = CGRect(x: -o.x * zoom, y: -o.y * zoom, width: bounds.width * zoom, height: bounds.height * zoom)
                NSImage(cgImage: image, size: bounds.size)
                    .draw(in: dest, from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: nil)
            } else {
                NSColor.darkGray.setFill()
                bounds.fill()
            }
        }

        ctx.saveGState()
        ctx.translateBy(x: -o.x * zoom, y: -o.y * zoom)
        ctx.scaleBy(x: zoom, y: zoom)
        for s in strokes { StrokeRenderer.draw(s, in: ctx) }
        if let current { StrokeRenderer.draw(current, in: ctx) }
        ctx.restoreGState()

        if tool == .eraser && !followMouse {
            let r = eraserRadius * zoom
            NSColor.white.withAlphaComponent(0.9).setStroke()
            let path = NSBezierPath(ovalIn: CGRect(x: mouse.x - r, y: mouse.y - r, width: r * 2, height: r * 2))
            path.lineWidth = 1.5
            path.stroke()
        }

        if showHint { drawHint() }
        if let flash { drawBubble(flash, centerY: 48) }
    }

    private var eraserRadius: CGFloat { 10 * widthScale / zoom }

    private func drawHint() {
        let status: String
        if followMouse {
            status = String(format: "확대 ×%.1f", zoom)
        } else {
            let zoomText = zoom > 1.01 ? String(format: "  ·  ×%.1f", zoom) : ""
            status = "\(Settings.palette[colorIndex].name) \(tool.label)\(zoomText)"
        }
        let keys = followMouse
            ? "휠 배율  ·  누르고 끌어 그리기  ·  Space 안내 숨기기  ·  Esc 끝"
            : "1 펜  2 형광펜  3 직선  4 화살표  5 사각형  6 원  8 지우개  ·  R G B Y O P 색  ·  E 모두 지우기  ·  W 화이트보드  K 칠판\n"
            + "[ ] 굵기  ·  휠 확대  0 원래대로  ·  ⌘Z 되돌리기  ·  ⌘C 복사  ⌘S 저장  ·  Space 안내 숨기기  ·  Esc 끝"
        drawBubble(status + "\n" + keys, centerY: bounds.height - 70, swatch: followMouse ? nil : color)
    }

    private func drawBubble(_ text: String, centerY: CGFloat, swatch: NSColor? = nil) {
        let para = NSMutableParagraphStyle()
        para.alignment = .center
        para.lineSpacing = 3
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .medium),
            .foregroundColor: NSColor.white,
            .paragraphStyle: para,
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        let size = str.boundingRect(with: CGSize(width: bounds.width - 80, height: 400),
                                    options: [.usesLineFragmentOrigin]).size
        let pad: CGFloat = 14
        let box = CGRect(x: (bounds.width - size.width) / 2 - pad, y: centerY - size.height / 2 - pad / 1.5,
                         width: size.width + pad * 2, height: size.height + pad * 1.33)
        NSColor.black.withAlphaComponent(0.72).setFill()
        NSBezierPath(roundedRect: box, xRadius: 10, yRadius: 10).fill()
        str.draw(with: CGRect(x: box.minX + pad, y: box.minY + pad / 1.5, width: size.width, height: size.height),
                 options: [.usesLineFragmentOrigin])
        if let swatch {
            swatch.setFill()
            NSBezierPath(ovalIn: CGRect(x: box.minX + 10, y: box.minY + 10, width: 10, height: 10)).fill()
        }
    }

    func showFlash(_ text: String) {
        flash = text
        flashTimer?.invalidate()
        flashTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: false) { [weak self] _ in
            self?.flash = nil
            self?.needsDisplay = true
        }
        needsDisplay = true
    }

    // MARK: 마우스

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    private func point(_ e: NSEvent) -> CGPoint { convert(e.locationInWindow, from: nil) }

    override func mouseMoved(with e: NSEvent) {
        mouse = point(e)
        if followMouse || tool == .eraser { needsDisplay = true }
    }

    override func mouseDown(with e: NSEvent) {
        mouse = point(e)
        freeze()
        let p = toImage(mouse)
        if tool == .eraser {
            eraseSnapshot = strokes
            erase(at: p)
            return
        }
        var t = tool
        if tool == .pen {
            let m = e.modifierFlags
            if m.contains(.command) && m.contains(.shift) { t = .arrow }
            else if m.contains(.command) { t = .rect }
            else if m.contains(.option) { t = .ellipse }
            else if m.contains(.shift) { t = .line }
        }
        let base = tool == .highlighter ? Settings.highlighterWidth : Settings.penWidth
        current = Stroke(tool: t, color: color, width: base * widthScale / zoom, points: [p])
        needsDisplay = true
    }

    override func mouseDragged(with e: NSEvent) {
        mouse = point(e)
        let p = toImage(mouse)
        if eraseSnapshot != nil {
            erase(at: p)
            return
        }
        guard var s = current else { return }
        switch s.tool {
        case .pen, .highlighter:
            if let last = s.points.last, hypot(p.x - last.x, p.y - last.y) < 0.5 / zoom { return }
            s.points.append(p)
        default:
            s.points = [s.start, p]
        }
        current = s
        needsDisplay = true
    }

    override func mouseUp(with e: NSEvent) {
        if let snap = eraseSnapshot {
            if snap.count != strokes.count { pushUndo(snap) }
            eraseSnapshot = nil
        }
        if let s = current {
            pushUndo(strokes)
            strokes.append(s)
            current = nil
        }
        needsDisplay = true
    }

    private func erase(at p: CGPoint) {
        let before = strokes.count
        strokes.removeAll { $0.hit(p, radius: eraserRadius) }
        if strokes.count != before { needsDisplay = true }
    }

    override func rightMouseDown(with e: NSEvent) { close() }

    override func otherMouseDown(with e: NSEvent) {
        freeze()
        panStart = (point(e), origin)
    }

    override func otherMouseDragged(with e: NSEvent) {
        guard let start = panStart else { return }
        let m = point(e)
        origin = CGPoint(x: start.origin.x - (m.x - start.mouse.x) / zoom,
                         y: start.origin.y - (m.y - start.mouse.y) / zoom)
        clampOrigin()
        needsDisplay = true
    }

    override func otherMouseUp(with e: NSEvent) { panStart = nil }

    override func scrollWheel(with e: NSEvent) {
        let dy = e.scrollingDeltaY
        guard dy != 0 else { return }
        let factor: CGFloat = e.hasPreciseScrollingDeltas ? pow(1.01, dy) : (dy > 0 ? 1.15 : 1 / 1.15)
        setZoom(zoom * factor, keeping: point(e))
    }

    // MARK: 키

    override func keyDown(with e: NSEvent) {
        let cmd = e.modifierFlags.contains(.command)
        let shift = e.modifierFlags.contains(.shift)
        let k = Int(e.keyCode)
        let center = CGPoint(x: bounds.midX, y: bounds.midY)

        if cmd {
            switch k {
            case kVK_ANSI_Z: shift ? redo() : undo()
            case kVK_ANSI_Y: redo()
            case kVK_ANSI_C: copyToClipboard()
            case kVK_ANSI_S: saveToPictures()
            default: super.keyDown(with: e)
            }
            return
        }

        switch k {
        case kVK_Escape: close()
        case kVK_ANSI_1: select(.pen)
        case kVK_ANSI_2: select(.highlighter)
        case kVK_ANSI_3: select(.line)
        case kVK_ANSI_4: select(.arrow)
        case kVK_ANSI_5: select(.rect)
        case kVK_ANSI_6: select(.ellipse)
        case kVK_ANSI_8: select(.eraser)
        case kVK_ANSI_R: pickColor(0, highlighter: shift)
        case kVK_ANSI_G: pickColor(1, highlighter: shift)
        case kVK_ANSI_B: pickColor(2, highlighter: shift)
        case kVK_ANSI_Y: pickColor(3, highlighter: shift)
        case kVK_ANSI_O: pickColor(4, highlighter: shift)
        case kVK_ANSI_P: pickColor(5, highlighter: shift)
        case kVK_ANSI_E:
            if !strokes.isEmpty { pushUndo(strokes); strokes.removeAll() }
        case kVK_ANSI_W: freeze(); board = board == .white ? .screen : .white
        case kVK_ANSI_K: freeze(); board = board == .black ? .screen : .black
        case kVK_ANSI_LeftBracket: widthScale = max(0.25, widthScale / 1.25)
        case kVK_ANSI_RightBracket: widthScale = min(6, widthScale * 1.25)
        case kVK_ANSI_0: followMouse = false; zoom = 1; origin = .zero
        case kVK_UpArrow: setZoom(zoom * 1.25, keeping: center)
        case kVK_DownArrow: setZoom(zoom / 1.25, keeping: center)
        case kVK_Space, kVK_F1: showHint.toggle()
        default: super.keyDown(with: e)
        }
        needsDisplay = true
    }

    private func select(_ t: Tool) {
        freeze()
        tool = t
    }

    private func pickColor(_ i: Int, highlighter: Bool) {
        freeze()
        colorIndex = i
        if highlighter { tool = .highlighter } else if tool == .eraser { tool = .pen }
    }

    private func pushUndo(_ snapshot: [Stroke]) {
        undoStack.append(snapshot)
        if undoStack.count > 200 { undoStack.removeFirst() }
        redoStack.removeAll()
    }

    private func undo() {
        guard let prev = undoStack.popLast() else { return }
        redoStack.append(strokes)
        strokes = prev
        needsDisplay = true
    }

    private func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append(strokes)
        strokes = next
        needsDisplay = true
    }

    func close() {
        onClose?()
    }

    // MARK: 저장·복사

    /// 1배 화면 전체 + 그림을 원래 해상도로 합친 이미지.
    func renderComposite() -> CGImage? {
        let scale = image.map { CGFloat($0.width) / bounds.width } ?? 2
        let w = Int(bounds.width * scale), h = Int(bounds.height * scale)
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let full = CGRect(x: 0, y: 0, width: w, height: h)
        switch board {
        case .white: ctx.setFillColor(NSColor.white.cgColor); ctx.fill(full)
        case .black: ctx.setFillColor(NSColor(hex: 0x1F2B26).cgColor); ctx.fill(full)
        case .screen:
            if let image { ctx.draw(image, in: full) } else { ctx.setFillColor(NSColor.darkGray.cgColor); ctx.fill(full) }
        }
        ctx.translateBy(x: 0, y: CGFloat(h))
        ctx.scaleBy(x: scale, y: -scale)
        for s in strokes { StrokeRenderer.draw(s, in: ctx) }
        return ctx.makeImage()
    }

    private func copyToClipboard() {
        guard let img = renderComposite() else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects([NSImage(cgImage: img, size: bounds.size)])
        showFlash("클립보드에 복사했어요")
    }

    private func saveToPictures() {
        guard let img = renderComposite(),
              let data = NSBitmapImageRep(cgImage: img).representation(using: .png, properties: [:]) else { return }
        let dir = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Pictures/MyZoom")
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd_HHmmss"
        let url = dir.appendingPathComponent("MyZoom_\(f.string(from: Date())).png")
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try data.write(to: url)
            showFlash("저장했어요: 사진/MyZoom/\(url.lastPathComponent)")
        } catch {
            showFlash("저장하지 못했어요: \(error.localizedDescription)")
        }
    }
}
