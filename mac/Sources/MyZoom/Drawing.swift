import AppKit

/// 그리기 도구. 숫자 키 번호는 Windows 판과 같다 (7 글자는 아직 없음).
enum Tool: Int, CaseIterable {
    case pen = 1, highlighter, line, arrow, rect, ellipse
    case eraser = 8

    var label: String {
        switch self {
        case .pen: return "펜"
        case .highlighter: return "형광펜"
        case .line: return "직선"
        case .arrow: return "화살표"
        case .rect: return "사각형"
        case .ellipse: return "원"
        case .eraser: return "지우개"
        }
    }
}

/// 한 번 그린 것. 좌표는 '확대 1배 화면' 기준(포인트, 왼쪽 위 원점).
struct Stroke {
    var tool: Tool
    var color: NSColor
    var width: CGFloat
    var points: [CGPoint]

    var start: CGPoint { points.first ?? .zero }
    var end: CGPoint { points.last ?? .zero }

    var boundsRect: CGRect {
        CGRect(x: min(start.x, end.x), y: min(start.y, end.y),
               width: abs(end.x - start.x), height: abs(end.y - start.y))
    }

    /// p 가 이 그림 위(반경 r 안)에 있는지 — 지우개용.
    func hit(_ p: CGPoint, radius r: CGFloat) -> Bool {
        let slack = r + width / 2
        switch tool {
        case .pen, .highlighter:
            if points.count == 1 { return hypot(p.x - start.x, p.y - start.y) <= slack }
            for i in 1..<points.count where distance(p, points[i - 1], points[i]) <= slack { return true }
            return false
        case .line, .arrow:
            return distance(p, start, end) <= slack
        case .rect:
            let b = boundsRect
            let corners = [CGPoint(x: b.minX, y: b.minY), CGPoint(x: b.maxX, y: b.minY),
                           CGPoint(x: b.maxX, y: b.maxY), CGPoint(x: b.minX, y: b.maxY)]
            for i in 0..<4 where distance(p, corners[i], corners[(i + 1) % 4]) <= slack { return true }
            return false
        case .ellipse:
            let b = boundsRect
            guard b.width > 0, b.height > 0 else { return hypot(p.x - b.midX, p.y - b.midY) <= slack }
            let nx = (p.x - b.midX) / (b.width / 2), ny = (p.y - b.midY) / (b.height / 2)
            let d = abs(hypot(nx, ny) - 1) * min(b.width, b.height) / 2
            return d <= slack
        case .eraser:
            return false
        }
    }
}

func distance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
    let dx = b.x - a.x, dy = b.y - a.y
    let len2 = dx * dx + dy * dy
    guard len2 > 0 else { return hypot(p.x - a.x, p.y - a.y) }
    let t = max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2))
    return hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy))
}

/// 그림 그리기. CGContext 만 쓰므로 화면·PNG 저장 양쪽에서 같은 결과가 나온다.
enum StrokeRenderer {
    static func draw(_ s: Stroke, in ctx: CGContext) {
        ctx.saveGState()
        defer { ctx.restoreGState() }
        ctx.setLineCap(.round)
        ctx.setLineJoin(.round)
        ctx.setLineWidth(s.width)
        let color = s.tool == .highlighter ? s.color.withAlphaComponent(Settings.highlighterOpacity) : s.color
        ctx.setStrokeColor(color.cgColor)
        ctx.setFillColor(color.cgColor)

        switch s.tool {
        case .pen, .highlighter:
            if s.points.count == 1 {
                let r = s.width / 2
                ctx.fillEllipse(in: CGRect(x: s.start.x - r, y: s.start.y - r, width: s.width, height: s.width))
                return
            }
            ctx.beginPath()
            ctx.move(to: s.points[0])
            for p in s.points.dropFirst() { ctx.addLine(to: p) }
            ctx.strokePath()
        case .line:
            ctx.strokeLineSegments(between: [s.start, s.end])
        case .arrow:
            ctx.strokeLineSegments(between: [s.start, s.end])
            let angle = atan2(s.end.y - s.start.y, s.end.x - s.start.x)
            let head = max(14, s.width * 4)
            let spread: CGFloat = .pi / 7
            ctx.beginPath()
            ctx.move(to: s.end)
            ctx.addLine(to: CGPoint(x: s.end.x - head * cos(angle - spread), y: s.end.y - head * sin(angle - spread)))
            ctx.addLine(to: CGPoint(x: s.end.x - head * cos(angle + spread), y: s.end.y - head * sin(angle + spread)))
            ctx.closePath()
            ctx.fillPath()
        case .rect:
            ctx.stroke(s.boundsRect)
        case .ellipse:
            ctx.strokeEllipse(in: s.boundsRect)
        case .eraser:
            break
        }
    }
}
