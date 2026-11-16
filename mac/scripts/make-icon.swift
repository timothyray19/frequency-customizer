// Draws the app icon (a white sine wave on a blue-violet rounded square) into an .iconset folder.
// Usage: swift scripts/make-icon.swift build/AppIcon.iconset && iconutil -c icns build/AppIcon.iconset
import AppKit

let dir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset"
try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)

let sizes: [(String, Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32), ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256), ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024),
]

let space = CGColorSpaceCreateDeviceRGB()
for (name, px) in sizes {
    let s = CGFloat(px)
    guard let ctx = CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0,
                              space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { continue }

    let rect = CGRect(x: s * 0.1, y: s * 0.1, width: s * 0.8, height: s * 0.8)
    ctx.addPath(CGPath(roundedRect: rect, cornerWidth: s * 0.18, cornerHeight: s * 0.18, transform: nil))
    ctx.clip()
    let colors = [CGColor(red: 0.42, green: 0.55, blue: 1.0, alpha: 1),
                  CGColor(red: 0.56, green: 0.30, blue: 0.95, alpha: 1)] as CFArray
    let gradient = CGGradient(colorsSpace: space, colors: colors, locations: [0, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: s), end: CGPoint(x: s, y: 0), options: [])
    ctx.resetClip()

    ctx.setStrokeColor(CGColor(gray: 1, alpha: 1))
    ctx.setLineWidth(max(1, s * 0.06))
    ctx.setLineCap(.round)
    for i in 0...120 {
        let t = CGFloat(i) / 120
        let p = CGPoint(x: s * 0.24 + t * s * 0.52, y: s * 0.5 + sin(t * 2 * .pi) * s * 0.16)
        if i == 0 { ctx.move(to: p) } else { ctx.addLine(to: p) }
    }
    ctx.strokePath()

    guard let image = ctx.makeImage(),
          let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else { continue }
    try png.write(to: URL(fileURLWithPath: "\(dir)/\(name).png"))
}
print("Icon images written to \(dir)")
