// Draws the Lumi app icon (the glowing ghost on a dark tile) into an .iconset folder.
import AppKit

/// Lumi, the glowing ghost (the same grid as Mascot in main.swift): B body, E eyes and mouth.
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

    // A soft glow behind the ghost: the one place the icon is not flat pixels.
    let teal = NSColor(red: 93 / 255, green: 202 / 255, blue: 165 / 255, alpha: 1)
    NSGradient(colors: [teal.withAlphaComponent(0.35), teal.withAlphaComponent(0)])!
        .draw(in: NSBezierPath(ovalIn: tile.insetBy(dx: tile.width * 0.12, dy: tile.width * 0.12)), relativeCenterPosition: .zero)

    NSGraphicsContext.current?.shouldAntialias = false
    let u = (tile.width * 0.5 / 14).rounded(.down)
    let x0 = (s / 2 - u * 14 / 2).rounded()
    let top = (s / 2 + u * 11 / 2).rounded()
    let eye = NSColor(red: 0.12, green: 0.12, blue: 0.11, alpha: 1)
    for (r, line) in lumi.enumerated() {
        for (c, ch) in line.enumerated() where ch != "." {
            (ch == "E" ? eye : teal).setFill()
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
