import AppKit
import Carbon.HIToolbox

/// `MyZoom --selftest <폴더>` — 권한 없이 그리기·확대·타이머 화면을 PNG 로 그리고, 계산 로직을 확인한다.
/// GitHub Actions 에서 돌려 결과 PNG 를 artifact 로 올린다 (맥 없이 화면을 눈으로 확인하는 용도).
enum SelfTest {
    private static var failures: [String] = []

    static func run(outputDir: String) -> Int32 {
        _ = NSApplication.shared
        let dir = URL(fileURLWithPath: outputDir)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        checkLogic()

        let size = CGSize(width: 1280, height: 800)
        guard let screen = fakeScreen(size: size, scale: 2) else {
            print("FAIL: 가짜 화면을 만들지 못함")
            return 1
        }
        let frame = CGRect(origin: .zero, size: size)

        let draw = AnnotationView(frame: frame, image: screen, zoomMode: false)
        draw.strokes = sampleStrokes()
        save(draw, dir, "01_draw.png")

        let zoom = AnnotationView(frame: frame, image: screen, zoomMode: true)
        save(zoom, dir, "02_zoom_follow.png")

        let zoomed = AnnotationView(frame: frame, image: screen, zoomMode: false)
        zoomed.strokes = sampleStrokes()
        zoomed.tool = .highlighter
        zoomed.colorIndex = 3
        zoomed.setZoom(2.5, keeping: CGPoint(x: 420, y: 330))
        save(zoomed, dir, "03_zoomed_with_drawing.png")

        let board = AnnotationView(frame: frame, image: screen, zoomMode: false)
        board.board = .black
        board.strokes = sampleStrokes()
        board.showHint = false
        save(board, dir, "04_chalkboard.png")

        if let img = draw.renderComposite(),
           let png = NSBitmapImageRep(cgImage: img).representation(using: .png, properties: [:]) {
            try? png.write(to: dir.appendingPathComponent("05_export_cmdS.png"))
            expect(img.width == Int(size.width * 2), "저장 이미지가 원래 해상도(2배)여야 함: \(img.width)")
        } else {
            failures.append("renderComposite 실패")
        }

        for (bg, name) in [(1, "06_timer_black.png"), (2, "07_timer_white.png")] {
            let t = TimerView(frame: frame, minutes: 10)
            t.background = bg
            t.message = "쉬는 시간"
            save(t, dir, name)
        }

        // 발표 보조 — 가짜 화면 위에 겹쳐 그린다
        let spot = SpotlightView(frame: frame)
        spot.center = CGPoint(x: 420, y: 520)
        save(layered(screen, size, spot), dir, "08_spotlight.png")

        let cursor = CursorHighlightView(frame: CGRect(x: 0, y: 0, width: CursorHighlightView.size, height: CursorHighlightView.size))
        let fixedNow: CFTimeInterval = 100
        cursor.now = { fixedNow }
        cursor.rings = [.init(start: fixedNow - 0.12, color: NSColor(hex: 0xE53935)),
                        .init(start: fixedNow - 0.3, color: NSColor(hex: 0x1E88E5))]
        cursor.setFrameOrigin(CGPoint(x: 560, y: 380))
        save(layered(screen, size, cursor), dir, "09_cursor_highlight.png")

        let keys = KeystrokeView(frame: CGRect(x: 0, y: 0, width: size.width, height: 110))
        keys.now = { fixedNow }
        keys.add("⌘ C")
        keys.add("⌘ V")
        keys.add("⌘ V")
        keys.add("esc")
        save(layered(screen, size, keys), dir, "10_keystrokes.png")

        let toast = ToastView(text: "클립보드에 복사했어요 — ⌘V 로 붙여넣기")
        toast.setFrameOrigin(CGPoint(x: (size.width - toast.frame.width) / 2, y: size.height * 0.18))
        save(layered(screen, size, toast), dir, "11_toast.png")

        if failures.isEmpty {
            let count = (try? FileManager.default.contentsOfDirectory(atPath: dir.path).count) ?? 0
            print("selftest OK — PNG \(count)장")
            return 0
        }
        failures.forEach { print("FAIL: \($0)") }
        return 1
    }

    /// 가짜 화면 위에 투명한 보조 화면을 얹은 뷰
    private static func layered(_ screen: CGImage, _ size: CGSize, _ overlay: NSView) -> NSView {
        let root = NSView(frame: CGRect(origin: .zero, size: size))
        let iv = NSImageView(frame: root.bounds)
        iv.image = NSImage(cgImage: screen, size: size)
        iv.imageScaling = .scaleAxesIndependently
        root.addSubview(iv)
        root.addSubview(overlay)
        return root
    }

    private static func expect(_ ok: Bool, _ what: String) {
        if !ok { failures.append(what) }
    }

    private static func checkLogic() {
        expect(Updater.isNewer("1.2.0", than: "1.1.9"), "1.2.0 > 1.1.9")
        expect(Updater.isNewer("1.10.0", than: "1.9.0"), "1.10.0 > 1.9.0 (문자열 비교 아님)")
        expect(!Updater.isNewer("1.1.0", than: "1.1.0"), "같은 버전은 새 버전 아님")
        expect(!Updater.isNewer("1.1", than: "1.1.0"), "1.1 == 1.1.0")

        expect(KeyNames.describe(keyCode: kVK_ANSI_C, flags: [.command]) == "⌘ C", "⌘C 표시")
        expect(KeyNames.describe(keyCode: kVK_ANSI_Z, flags: [.command, .shift]) == "⇧⌘ Z", "⇧⌘Z 표시 (맥 순서)")
        expect(KeyNames.describe(keyCode: kVK_ANSI_C, flags: []) == nil, "일반 타자는 안 보임")
        expect(KeyNames.describe(keyCode: kVK_ANSI_C, flags: [.shift]) == nil, "Shift 글자도 안 보임")
        expect(KeyNames.describe(keyCode: kVK_Escape, flags: []) == "esc", "특수 키는 보임")
        expect(KeyNames.describe(keyCode: kVK_LeftArrow, flags: [.shift]) == "⇧ ←", "⇧← 표시")

        let line = Stroke(tool: .line, color: .red, width: 4, points: [CGPoint(x: 0, y: 0), CGPoint(x: 100, y: 0)])
        expect(line.hit(CGPoint(x: 50, y: 5), radius: 5), "직선 근처는 지우개에 걸림")
        expect(!line.hit(CGPoint(x: 50, y: 30), radius: 5), "직선에서 먼 곳은 안 걸림")
        let rect = Stroke(tool: .rect, color: .red, width: 4, points: [CGPoint(x: 0, y: 0), CGPoint(x: 100, y: 100)])
        expect(rect.hit(CGPoint(x: 100, y: 50), radius: 4), "사각형 테두리는 걸림")
        expect(!rect.hit(CGPoint(x: 50, y: 50), radius: 4), "사각형 가운데는 안 걸림")
        let oval = Stroke(tool: .ellipse, color: .red, width: 4, points: [CGPoint(x: 0, y: 0), CGPoint(x: 100, y: 60)])
        expect(oval.hit(CGPoint(x: 100, y: 30), radius: 4), "원 테두리는 걸림")
        expect(!oval.hit(CGPoint(x: 50, y: 30), radius: 4), "원 가운데는 안 걸림")
    }

    private static func save(_ view: NSView, _ dir: URL, _ name: String) {
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            failures.append("\(name): 비트맵 생성 실패")
            return
        }
        view.cacheDisplay(in: view.bounds, to: rep)
        guard let data = rep.representation(using: .png, properties: [:]) else {
            failures.append("\(name): PNG 변환 실패")
            return
        }
        try? data.write(to: dir.appendingPathComponent(name))
    }

    private static func sampleStrokes() -> [Stroke] {
        let p = Settings.palette.map(\.color)
        var wave: [CGPoint] = []
        for i in 0...60 {
            let x = 120 + CGFloat(i) * 6
            wave.append(CGPoint(x: x, y: 520 + sin(CGFloat(i) / 5) * 40))
        }
        return [
            Stroke(tool: .pen, color: p[0], width: 4, points: wave),
            Stroke(tool: .highlighter, color: p[3], width: 22, points: [CGPoint(x: 150, y: 214), CGPoint(x: 620, y: 214)]),
            Stroke(tool: .line, color: p[2], width: 4, points: [CGPoint(x: 560, y: 600), CGPoint(x: 820, y: 640)]),
            Stroke(tool: .arrow, color: p[0], width: 5, points: [CGPoint(x: 900, y: 600), CGPoint(x: 760, y: 380)]),
            Stroke(tool: .rect, color: p[1], width: 4, points: [CGPoint(x: 700, y: 170), CGPoint(x: 1120, y: 340)]),
            Stroke(tool: .ellipse, color: p[5], width: 4, points: [CGPoint(x: 140, y: 300), CGPoint(x: 420, y: 420)]),
        ]
    }

    /// 수업 자료 화면처럼 보이는 가짜 스크린샷.
    private static func fakeScreen(size: CGSize, scale: CGFloat) -> CGImage? {
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width * scale),
                                         pixelsHigh: Int(size.height * scale), bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                         bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        rep.size = size // 포인트 크기 — 컨텍스트를 만들기 전에 정해야 2배로 그려진다
        guard let gc = NSGraphicsContext(bitmapImageRep: rep) else { return nil }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = gc
        // 위쪽을 0 으로 두고 그린다
        let flip = NSAffineTransform()
        flip.translateX(by: 0, yBy: size.height)
        flip.scaleX(by: 1, yBy: -1)
        flip.concat()

        NSGradient(starting: NSColor(hex: 0x5B7FA6), ending: NSColor(hex: 0x2E4057))?
            .draw(in: CGRect(origin: .zero, size: size), angle: 90)

        let win = CGRect(x: 90, y: 70, width: size.width - 180, height: size.height - 150)
        NSColor.white.setFill()
        NSBezierPath(roundedRect: win, xRadius: 12, yRadius: 12).fill()
        NSColor(hex: 0xECECEC).setFill()
        NSBezierPath(rect: CGRect(x: win.minX, y: win.minY, width: win.width, height: 36)).fill()

        func text(_ s: String, _ p: CGPoint, _ size: CGFloat, _ weight: NSFont.Weight = .regular, _ c: NSColor = NSColor(hex: 0x222222)) {
            let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: size, weight: weight), .foregroundColor: c]
            // 뒤집힌 좌표에서 글자가 거꾸로 나오지 않게 한 번 더 뒤집는다
            NSGraphicsContext.saveGraphicsState()
            let t = NSAffineTransform()
            t.translateX(by: p.x, yBy: p.y + size)
            t.scaleX(by: 1, yBy: -1)
            t.concat()
            NSAttributedString(string: s, attributes: attrs).draw(at: .zero)
            NSGraphicsContext.restoreGraphicsState()
        }

        text("6학년 과학 — 식물의 구조와 기능", CGPoint(x: win.minX + 60, y: win.minY + 70), 34, .bold)
        text("광합성: 빛 에너지를 이용해 이산화 탄소와 물로 양분을 만드는 과정", CGPoint(x: win.minX + 60, y: win.minY + 130), 20)
        text("• 잎의 기공으로 이산화 탄소가 들어온다", CGPoint(x: win.minX + 80, y: win.minY + 180), 18)
        text("• 뿌리에서 흡수한 물이 줄기를 따라 잎으로 간다", CGPoint(x: win.minX + 80, y: win.minY + 214), 18)
        text("• 만들어진 녹말은 체관을 통해 이동한다", CGPoint(x: win.minX + 80, y: win.minY + 248), 18)

        let bars: [CGFloat] = [120, 200, 160, 260, 220]
        for (i, h) in bars.enumerated() {
            NSColor(hex: 0x1E88E5).withAlphaComponent(0.75).setFill()
            NSBezierPath(rect: CGRect(x: win.minX + 640 + CGFloat(i) * 70, y: win.maxY - 60 - h, width: 44, height: h)).fill()
        }
        text("빛의 세기에 따른 산소 발생량", CGPoint(x: win.minX + 640, y: win.maxY - 46), 15, .medium, NSColor(hex: 0x555555))

        NSGraphicsContext.restoreGraphicsState()
        return rep.cgImage
    }
}
