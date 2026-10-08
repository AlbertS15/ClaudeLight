// Draws the Lumi avatar (Telegram, GitHub, social profiles) and the GitHub social preview card.
// Usage: swift docs/brand/make_brand.swift docs/brand
import AppKit

let lumi = [
    "....BBBBBB....",
    "..BBBBBBBBBB..",
    ".BBBBBBBBBBBB.",
    ".BBBBBBBBBBBB.",
    "BBBEEBBBBEEBBB",
    "BBBEEBBBBEEBBB",
    "BBBBBBBBBBBBBB",
    "BBBBBBEEBBBBBB",
    "BBBBBBBBBBBBBB",
    "BBBBBBBBBBBBBB",
    "BB..BBBBBB..BB",
]
let teal = NSColor(red: 93 / 255, green: 202 / 255, blue: 165 / 255, alpha: 1)
let eye = NSColor(red: 0.12, green: 0.12, blue: 0.11, alpha: 1)

func canvas(_ w: Int, _ h: Int, _ draw: (CGFloat, CGFloat) -> Void) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw(CGFloat(w), CGFloat(h))
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

func background(_ rect: NSRect) {
    NSGradient(starting: NSColor(red: 0.20, green: 0.19, blue: 0.18, alpha: 1),
               ending: NSColor(red: 0.08, green: 0.08, blue: 0.07, alpha: 1))!.draw(in: rect, angle: -90)
}

/// The ghost centred on (cx, cy), `u` points per grid cell, with its soft glow.
func ghost(cx: CGFloat, cy: CGFloat, u: CGFloat) {
    let r = u * 11
    NSGradient(colors: [teal.withAlphaComponent(0.42), teal.withAlphaComponent(0.12), teal.withAlphaComponent(0)])!
        .draw(fromCenter: NSPoint(x: cx, y: cy), radius: 0, toCenter: NSPoint(x: cx, y: cy), radius: r, options: [])
    NSGraphicsContext.current?.shouldAntialias = false
    let x0 = (cx - u * 7).rounded(), top = (cy + u * 5.5).rounded()
    for (row, line) in lumi.enumerated() {
        for (col, ch) in line.enumerated() where ch != "." {
            (ch == "E" ? eye : teal).setFill()
            NSRect(x: x0 + CGFloat(col) * u, y: top - CGFloat(row + 1) * u, width: u, height: u).fill()
        }
    }
    NSGraphicsContext.current?.shouldAntialias = true
}

let out = CommandLine.arguments[1]

// Avatar: square, full-bleed, the ghost kept well inside the circle that Telegram and GitHub crop to.
let avatar = canvas(1024, 1024) { w, h in
    background(NSRect(x: 0, y: 0, width: w, height: h))
    ghost(cx: w / 2, cy: h / 2, u: 36)
}
try! avatar.write(to: URL(fileURLWithPath: "\(out)/avatar.png"))

// Social preview: the card GitHub, Telegram and messengers show for a link to the repository.
let preview = canvas(1280, 640) { w, h in
    background(NSRect(x: 0, y: 0, width: w, height: h))
    ghost(cx: 300, cy: h / 2, u: 26)
    func text(_ s: String, _ size: CGFloat, _ weight: NSFont.Weight, _ color: NSColor, y: CGFloat) {
        let font = NSFont.systemFont(ofSize: size, weight: weight)
        let rounded = font.fontDescriptor.withDesign(.rounded).flatMap { NSFont(descriptor: $0, size: size) } ?? font
        (s as NSString).draw(at: NSPoint(x: 560, y: y), withAttributes: [.font: rounded, .foregroundColor: color])
    }
    text("Lumi", 120, .bold, .white, y: 340)
    text("Spotlight + AI in one bar", 44, .semibold, teal, y: 270)
    text("Search apps and files, ask Claude, ChatGPT,", 30, .regular, NSColor(white: 0.78, alpha: 1), y: 200)
    text("Gemini or a local model. Free, open source.", 30, .regular, NSColor(white: 0.78, alpha: 1), y: 158)
    text("macOS  ·  Windows", 30, .medium, NSColor(white: 0.55, alpha: 1), y: 90)
}
try! preview.write(to: URL(fileURLWithPath: "\(out)/social-preview.png"))
print("wrote avatar.png and social-preview.png")
