// Draws the app icon (the Claude Code mascot in orange pixels on a dark tile) into an .iconset folder.
import AppKit

/// Clawd, from the pixel art inside Claude Code (the same grid as Mascot in main.swift): C body, E eyes.
let clawd = [
    "...CCCCCCCCCCCC...",
    "...CCCCCCCCCCCC...",
    "...CCECCCCCCECC...",
    "...CCECCCCCCECC...",
    ".CCCCCCCCCCCCCCCC.",
    ".CCCCCCCCCCCCCCCC.",
    "...CCCCCCCCCCCC...",
    "...CCCCCCCCCCCC...",
    "....C.C....C.C....",
    "....C.C....C.C....",
]

func icon(_ px: Int) -> Data {
    let s = CGFloat(px)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.shouldAntialias = false

    // macOS icon grid: the tile is ~80% of the canvas, with a corner radius of ~22.5% of the tile.
    let inset = s * 0.1
    let tile = NSRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
    NSGraphicsContext.current?.shouldAntialias = true
    let shape = NSBezierPath(roundedRect: tile, xRadius: tile.width * 0.225, yRadius: tile.width * 0.225)
    NSGradient(starting: NSColor(red: 0.20, green: 0.19, blue: 0.18, alpha: 1),
               ending: NSColor(red: 0.10, green: 0.10, blue: 0.09, alpha: 1))!.draw(in: shape, angle: -90)
    NSGraphicsContext.current?.shouldAntialias = false

    NSGraphicsContext.current?.shouldAntialias = false
    let u = (tile.width * 0.62 / 18).rounded(.down)
    let x0 = (s / 2 - u * 18 / 2).rounded()
    let top = (s / 2 + u * 10 / 2).rounded()
    let body = NSColor(red: 215 / 255, green: 119 / 255, blue: 87 / 255, alpha: 1)
    for (r, line) in clawd.enumerated() {
        for (c, ch) in line.enumerated() where ch != "." {
            (ch == "E" ? NSColor.black : body).setFill()
            NSRect(x: x0 + CGFloat(c) * u, y: top - CGFloat(r + 1) * u, width: u, height: u).fill()
        }
    }

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let out = CommandLine.arguments[1]
try? FileManager.default.createDirectory(atPath: out, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try! icon(base).write(to: URL(fileURLWithPath: "\(out)/icon_\(base)x\(base).png"))
    try! icon(base * 2).write(to: URL(fileURLWithPath: "\(out)/icon_\(base)x\(base)@2x.png"))
}
