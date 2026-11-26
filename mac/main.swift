// SnapNote for macOS: snap it, mark it, share it with AI or save it.
// One-file AppKit app. "Set up SnapNote.command" builds it into SnapNote.app with Apple's swiftc.
// Port of the Windows version (C# WinForms).

import AppKit
import Carbon
import CoreImage
import ImageIO
import ApplicationServices
import ScreenCaptureKit

let appVersion = "0.2"
let tagline = "Snap it, mark it, share it with AI or save it."

// MARK: - Colours and fonts (Linework tokens)

func rgb(_ hex: String, _ alpha: CGFloat = 1) -> NSColor {
    var s = hex.trimmingCharacters(in: .whitespaces)
    if s.hasPrefix("#") { s.removeFirst() }
    let v = UInt64(s, radix: 16) ?? 0
    let r = CGFloat((v >> 16) & 0xff) / 255
    let g = CGFloat((v >> 8) & 0xff) / 255
    let b = CGFloat(v & 0xff) / 255
    return NSColor(srgbRed: r, green: g, blue: b, alpha: alpha)
}

enum Theme {
    static var dark = false
    static var surface = rgb("#ffffff"), sunken = rgb("#f5f5f6"), hover = rgb("#efeff1")
    static var ink = rgb("#0b0b0c"), inkMuted = rgb("#6b6b72"), inkFaint = rgb("#a3a3ab"), onPrimary = rgb("#ffffff")
    static var line = rgb("#e8e8eb"), lineControl = rgb("#8a8a92"), danger = rgb("#c2362b")

    static func load(_ pref: String) {
        let p = pref.lowercased()
        if p == "dark" { dark = true }
        else if p == "light" { dark = false }
        else { dark = NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua }
        if dark {
            surface = rgb("#131315"); sunken = rgb("#1a1a1d"); hover = rgb("#222226")
            ink = rgb("#f2f2f3"); inkMuted = rgb("#9d9da6"); inkFaint = rgb("#5c5c64"); onPrimary = rgb("#0b0b0c")
            line = rgb("#232327"); lineControl = rgb("#66666f"); danger = rgb("#ff7b70")
        } else {
            surface = rgb("#ffffff"); sunken = rgb("#f5f5f6"); hover = rgb("#efeff1")
            ink = rgb("#0b0b0c"); inkMuted = rgb("#6b6b72"); inkFaint = rgb("#a3a3ab"); onPrimary = rgb("#ffffff")
            line = rgb("#e8e8eb"); lineControl = rgb("#8a8a92"); danger = rgb("#c2362b")
        }
    }
}

enum Fonts {
    static func sans(_ size: CGFloat, medium: Bool = false) -> NSFont {
        if let f = NSFont(name: medium ? "HankenGrotesk-Medium" : "HankenGrotesk-Regular", size: size) { return f }
        return NSFont.systemFont(ofSize: size, weight: medium ? .medium : .regular)
    }
    static func mono(_ size: CGFloat, bold: Bool = false) -> NSFont {
        if let f = NSFont(name: bold ? "JetBrainsMono-Bold" : "JetBrainsMono-Regular", size: size) { return f }
        return NSFont.monospacedSystemFont(ofSize: size, weight: bold ? .bold : .regular)
    }
}

func textSize(_ s: String, _ font: NSFont) -> CGSize {
    if s.isEmpty { return CGSize(width: 0, height: font.ascender - font.descender) }
    return (s as NSString).size(withAttributes: [.font: font])
}

func drawText(_ s: String, at p: CGPoint, font: NSFont, color: NSColor) {
    (s as NSString).draw(at: p, withAttributes: [.font: font, .foregroundColor: color])
}

// Draws a CGImage the right way up in flipped views and contexts.
func drawCG(_ img: CGImage, in r: CGRect) {
    let ns = NSImage(cgImage: img, size: NSSize(width: img.width, height: img.height))
    ns.draw(in: r, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true,
            hints: [NSImageRep.HintKey.interpolation: NSNumber(value: NSImageInterpolation.high.rawValue)])
}

// The SnapNote logo (same drawing as AppIcon.icns): ink tile, light crop marks, red note bubble.
// Draws into a flipped context at rect r.
func drawLogo(_ r: CGRect, ring: Bool) {
    guard let ctx = NSGraphicsContext.current?.cgContext else { return }
    ctx.saveGState()
    ctx.translateBy(x: r.minX, y: r.minY)
    let k = r.width / 32
    ctx.scaleBy(x: k, y: k)
    let tile = NSBezierPath(roundedRect: CGRect(x: 1, y: 1, width: 30, height: 30), xRadius: 7, yRadius: 7)
    rgb("#0b0b0c").setFill(); tile.fill()
    if ring { NSColor(white: 1, alpha: 0.35).setStroke(); tile.lineWidth = 1.2 / k; tile.stroke() }
    let m = NSBezierPath()
    let a: CGFloat = 7, z: CGFloat = 25, L: CGFloat = 6
    m.move(to: CGPoint(x: a, y: a + L)); m.line(to: CGPoint(x: a, y: a)); m.line(to: CGPoint(x: a + L, y: a))
    m.move(to: CGPoint(x: z - L, y: a)); m.line(to: CGPoint(x: z, y: a)); m.line(to: CGPoint(x: z, y: a + L))
    m.move(to: CGPoint(x: a, y: z - L)); m.line(to: CGPoint(x: a, y: z)); m.line(to: CGPoint(x: a + L, y: z))
    m.lineWidth = max(2.2, 1.3 / k); m.lineCapStyle = .round; m.lineJoinStyle = .round
    rgb("#f2f2f3").setStroke(); m.stroke()
    let bub = NSBezierPath(roundedRect: CGRect(x: 15, y: 15.5, width: 12, height: 9), xRadius: 2.5, yRadius: 2.5)
    bub.move(to: CGPoint(x: 17.5, y: 24)); bub.line(to: CGPoint(x: 22, y: 24)); bub.line(to: CGPoint(x: 17.5, y: 28)); bub.close()
    rgb("#e5484d").setFill(); bub.fill()
    let w = NSBezierPath()
    w.move(to: CGPoint(x: 18, y: 19)); w.line(to: CGPoint(x: 24, y: 19))
    w.move(to: CGPoint(x: 18, y: 21.6)); w.line(to: CGPoint(x: 22, y: 21.6))
    w.lineWidth = max(1.4, 1 / k); w.lineCapStyle = .round
    NSColor.white.setStroke(); w.stroke()
    ctx.restoreGState()
}

func rtrim(_ s: String) -> String {
    var t = s
    while t.hasSuffix(" ") { t.removeLast() }
    return t
}

// MARK: - Settings (settings.ini next to SnapNote.app)

final class Settings {
    let url: URL
    var hotkey = "Ctrl+Shift+S"
    var smartPaste = true
    var smartPasteDelayMs = 400
    var smartPasteTimeoutSec = 120
    var noteInImage = false
    var annotationColor = rgb("#0b0b0c")
    var theme = "auto"
    var saveFolder = ""
    var alsoSaveOnCopy = false
    var animations = true

    static let defaultText = """
# SnapNote settings. After editing: menu bar icon > Quit SnapNote, then open SnapNote.app again.

# Global hotkey. Combine Ctrl, Option, Shift, Cmd with one letter or digit, e.g. Ctrl+Shift+S, Ctrl+Option+S
# If it is taken by another app, SnapNote falls back to Ctrl+Option+S, Ctrl+Shift+X or Ctrl+Option+A and tells you.
Hotkey=Ctrl+Shift+S

# SmartPaste: after "Copy for chat", your next Cmd+V pastes the screenshot, then the app pastes your text for you.
# Needs SnapNote to be allowed under System Settings > Privacy & Security > Accessibility.
# false = image and text go on the clipboard together (some chat apps then paste only one of them).
SmartPaste=true

# Milliseconds between the image paste and the text paste. Raise it if the text sometimes replaces the image.
SmartPasteDelayMs=400

# Seconds SmartPaste waits for your Cmd+V before giving up.
SmartPasteTimeoutSec=120

# Default for the "Put note inside the image" switch (the app remembers your last choice here).
NoteInImage=false

# Default colour for highlights and arrows (ink). The editor also offers blue and red.
AnnotationColor=#0B0B0C

# auto, light or dark
Theme=auto

# Where the Save button puts your notes (a PNG plus a .txt). Empty = Pictures/SnapNote.
SaveFolder=

# true = also save a copy there every time you copy for chat.
AlsoSaveOnCopy=false

# Little line-drawing animations (paper plane, tray, drawn-in marks, waiting bubble). false = off.
# They are also off when "Reduce motion" is on in System Settings > Accessibility > Display.
Animations=true

"""

    init(url: URL) {
        self.url = url
        load()
    }

    func load() {
        if !FileManager.default.fileExists(atPath: url.path) {
            try? Settings.defaultText.write(to: url, atomically: true, encoding: .utf8)
        }
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return }
        for raw in text.components(separatedBy: .newlines) {
            let t = raw.trimmingCharacters(in: .whitespaces)
            if t.isEmpty || t.hasPrefix("#") { continue }
            guard let eq = t.firstIndex(of: "=") else { continue }
            let k = t[..<eq].trimmingCharacters(in: .whitespaces).lowercased()
            let v = t[t.index(after: eq)...].trimmingCharacters(in: .whitespaces)
            switch k {
            case "hotkey": hotkey = v
            case "smartpaste": smartPaste = Settings.bool(v, true)
            case "smartpastedelayms": smartPasteDelayMs = Int(v) ?? smartPasteDelayMs
            case "smartpastetimeoutsec": smartPasteTimeoutSec = Int(v) ?? smartPasteTimeoutSec
            case "noteinimage": noteInImage = Settings.bool(v, false)
            case "annotationcolor": annotationColor = rgb(v)
            case "theme": theme = v
            case "savefolder": saveFolder = v
            case "alsosaveoncopy": alsoSaveOnCopy = Settings.bool(v, false)
            case "animations": animations = Settings.bool(v, true)
            default: break
            }
        }
    }

    static func bool(_ v: String, _ def: Bool) -> Bool {
        let s = v.lowercased()
        if ["true", "1", "yes", "on"].contains(s) { return true }
        if ["false", "0", "no", "off"].contains(s) { return false }
        return def
    }

    func set(_ key: String, _ value: String) {
        var lines = (try? String(contentsOf: url, encoding: .utf8))?.components(separatedBy: "\n") ?? []
        var found = false
        for i in 0..<lines.count {
            let t = lines[i].trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("#") { continue }
            if let eq = t.firstIndex(of: "="), t[..<eq].trimmingCharacters(in: .whitespaces).lowercased() == key.lowercased() {
                lines[i] = key + "=" + value
                found = true
            }
        }
        if !found { lines.append(key + "=" + value) }
        try? lines.joined(separator: "\n").write(to: url, atomically: true, encoding: .utf8)
    }

    var saveDir: URL {
        if saveFolder.isEmpty {
            let pics = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
                ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Pictures")
            return pics.appendingPathComponent("SnapNote")
        }
        let expanded = (saveFolder as NSString).expandingTildeInPath
        if expanded.hasPrefix("/") { return URL(fileURLWithPath: expanded) }
        return url.deletingLastPathComponent().appendingPathComponent(expanded)
    }
}

// MARK: - Global hotkey (Carbon; needs no permission)

let keyCodes: [String: UInt32] = [
    "a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7, "c": 8, "v": 9, "b": 11, "q": 12,
    "w": 13, "e": 14, "r": 15, "y": 16, "t": 17, "1": 18, "2": 19, "3": 20, "4": 21, "6": 22, "5": 23,
    "9": 25, "7": 26, "8": 28, "0": 29, "o": 31, "u": 32, "i": 34, "p": 35, "l": 37, "j": 38, "k": 40,
    "n": 45, "m": 46, "space": 49
]

func parseHotkey(_ s: String) -> (code: UInt32, mods: UInt32)? {
    var mods: UInt32 = 0
    var code: UInt32? = nil
    for raw in s.split(separator: "+") {
        let p = raw.trimmingCharacters(in: .whitespaces).lowercased()
        switch p {
        case "": continue
        case "ctrl", "control": mods |= UInt32(controlKey)
        case "alt", "option", "opt": mods |= UInt32(optionKey)
        case "shift": mods |= UInt32(shiftKey)
        case "cmd", "command": mods |= UInt32(cmdKey)
        default:
            guard let c = keyCodes[p] else { return nil }
            code = c
        }
    }
    guard let c = code else { return nil }
    return (c, mods)
}

func prettyHotkey(_ s: String) -> String {
    var out = ""
    var key = ""
    for raw in s.split(separator: "+") {
        let p = raw.trimmingCharacters(in: .whitespaces).lowercased()
        switch p {
        case "ctrl", "control": out += "\u{2303}"
        case "alt", "option", "opt": out += "\u{2325}"
        case "shift": out += "\u{21E7}"
        case "cmd", "command": out += "\u{2318}"
        default: key = p == "space" ? "Space" : p.uppercased()
        }
    }
    return out + key
}

enum HotKey {
    static var ref: EventHotKeyRef? = nil
    static var installed = false
    static var onPress: (() -> Void)? = nil

    static func register(_ text: String) -> Bool {
        guard let hk = parseHotkey(text) else { return false }
        if !installed {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
                DispatchQueue.main.async { HotKey.onPress?() }
                return noErr
            }, 1, &spec, nil, nil)
            installed = true
        }
        if let r = ref { UnregisterEventHotKey(r); ref = nil }
        let id = EventHotKeyID(signature: OSType(0x534E4F54), id: 1)
        var r: EventHotKeyRef? = nil
        let st = RegisterEventHotKey(hk.code, hk.mods, id, GetApplicationEventTarget(), 0, &r)
        if st == noErr { ref = r; return true }
        return false
    }
}

// MARK: - Clipboard and SmartPaste

enum Clip {
    static func png(_ img: CGImage) -> Data? {
        return NSBitmapImageRep(cgImage: img).representation(using: .png, properties: [:])
    }

    @discardableResult
    static func put(_ img: CGImage, text: String?) -> Bool {
        guard let data = png(img) else { return false }
        let pb = NSPasteboard.general
        pb.clearContents()
        let item = NSPasteboardItem()
        item.setData(data, forType: .png)
        if let tiff = NSImage(cgImage: img, size: NSSize(width: img.width, height: img.height)).tiffRepresentation {
            item.setData(tiff, forType: .tiff)
        }
        if let t = text, !t.isEmpty { item.setString(t, forType: .string) }
        return pb.writeObjects([item])
    }

    static func putText(_ t: String) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(t, forType: .string)
    }
}

// After "Copy for chat" the clipboard holds only the image. When the user next presses Cmd+V in another app,
// SnapNote waits a moment, puts the text on the clipboard and presses Cmd+V once more. Needs Accessibility.
enum SmartPaste {
    static var monitor: Any? = nil
    static var pending: String? = nil
    static var timeout: DispatchWorkItem? = nil

    // 0 = idle, 1 = waiting for Cmd+V, 2 = just pasted. The menu bar icon follows this.
    static var onState: ((Int) -> Void)? = nil
    static var state = 0
    static func setState(_ v: Int) { if state == v { return }; state = v; onState?(v) }

    static func trusted(prompt: Bool) -> Bool {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        return AXIsProcessTrustedWithOptions([key: prompt] as CFDictionary)
    }

    static func arm(_ text: String, delayMs: Int, timeoutSec: Int) -> Bool {
        disarm()
        guard trusted(prompt: true) else { return false }
        pending = text
        monitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { e in
            guard e.keyCode == 9, e.modifierFlags.contains(.command), let t = SmartPaste.pending else { return }
            SmartPaste.disarm()
            DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(delayMs)) {
                Clip.putText(t)
                DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(80)) { SmartPaste.pressCmdV(); SmartPaste.setState(2) }
            }
        }
        let w = DispatchWorkItem { SmartPaste.disarm() }
        timeout = w
        DispatchQueue.main.asyncAfter(deadline: .now() + .seconds(timeoutSec), execute: w)
        if monitor != nil { setState(1) }
        return monitor != nil
    }

    static func disarm() {
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
        pending = nil
        timeout?.cancel()
        timeout = nil
        if state == 1 { setState(0) }
    }

    static func pressCmdV() {
        let src = CGEventSource(stateID: .combinedSessionState)
        let down = CGEvent(keyboardEventSource: src, virtualKey: 9, keyDown: true)
        down?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        let up = CGEvent(keyboardEventSource: src, virtualKey: 9, keyDown: false)
        up?.flags = .maskCommand
        up?.post(tap: .cghidEventTap)
    }
}

// MARK: - Toast (small card, top right)

final class ToastView: NSView {
    var title = "", body = "", isError = false
    override var isFlipped: Bool { return true }
    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 0.5, dy: 0.5)
        let p = NSBezierPath(roundedRect: r, xRadius: 10, yRadius: 10)
        Theme.surface.setFill(); p.fill()
        Theme.line.setStroke(); p.lineWidth = 1; p.stroke()
        let x: CGFloat = 38
        if isError {
            Theme.danger.setFill()
            NSBezierPath(ovalIn: CGRect(x: 18, y: 20, width: 8, height: 8)).fill()
        } else {
            drawLogo(CGRect(x: 12, y: 14, width: 20, height: 20), ring: Theme.dark)
        }
        drawText(title, at: CGPoint(x: x, y: 14), font: Fonts.sans(13, medium: true), color: Theme.ink)
        let attrs: [NSAttributedString.Key: Any] = [.font: Fonts.sans(12), .foregroundColor: Theme.inkMuted]
        (body as NSString).draw(with: CGRect(x: x, y: 36, width: bounds.width - x - 16, height: bounds.height - 40),
                                options: [.usesLineFragmentOrigin], attributes: attrs, context: nil)
    }
}

enum Toast {
    static var panel: NSPanel? = nil
    static var timer: Timer? = nil

    static func show(_ title: String, _ body: String, ms: Int = 4500, error: Bool = false) {
        Toast.panel?.orderOut(nil)
        let w: CGFloat = 340
        let bodyH = (body as NSString).boundingRect(with: NSSize(width: w - 54, height: 1000), options: [.usesLineFragmentOrigin],
                                                    attributes: [.font: Fonts.sans(12)], context: nil).height
        let h = 36 + ceil(bodyH) + 16
        guard let scr = NSScreen.main else { return }
        let vf = scr.visibleFrame
        let p = NSPanel(contentRect: NSRect(x: vf.maxX - w - 16, y: vf.maxY - h - 16, width: w, height: h),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.level = .statusBar
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let v = ToastView(frame: NSRect(x: 0, y: 0, width: w, height: h))
        v.title = title; v.body = body; v.isError = error
        p.contentView = v
        p.orderFrontRegardless()
        Toast.panel = p
        Toast.timer?.invalidate()
        Toast.timer = Timer.scheduledTimer(withTimeInterval: Double(ms) / 1000, repeats: false) { _ in
            Toast.panel?.orderOut(nil)
            Toast.panel = nil
        }
    }

    static func hide() { Toast.panel?.orderOut(nil); Toast.panel = nil }
}

// MARK: - Motion

enum Motion {
    static var setting = true
    static var on: Bool { return setting && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    static let start = Date()
    static func now() -> Double { return Date().timeIntervalSince(start) * 1000 }
    static func seg(_ t: Double, _ a: Double, _ b: Double) -> CGFloat { return CGFloat(max(0, min(1, (t - a) / (b - a)))) }
    static func easeInOut(_ t: CGFloat) -> CGFloat { return t < 0.5 ? 4 * t * t * t : 1 - pow(-2 * t + 2, 3) / 2 }
    static func easeOut(_ t: CGFloat) -> CGFloat { return 1 - pow(1 - t, 3) }
    static func easeIn(_ t: CGFloat) -> CGFloat { return t * t * t }
    static func back(_ t: CGFloat) -> CGFloat { let c: CGFloat = 1.7; let c3 = c + 1; return 1 + c3 * pow(t - 1, 3) + c * pow(t - 1, 2) }
    static func spring(_ t: CGFloat) -> CGFloat { return t <= 0 ? 0 : CGFloat(exp(-6 * Double(t)) * cos(14 * Double(t))) }
}

func drawCG(_ img: CGImage, in r: CGRect, alpha: CGFloat) {
    let ns = NSImage(cgImage: img, size: NSSize(width: img.width, height: img.height))
    ns.draw(in: r, from: .zero, operation: .sourceOver, fraction: alpha, respectFlipped: true, hints: nil)
}

final class FxView: NSView {
    var paint: ((Double) -> Void)? = nil
    let t0 = Date()
    override var isFlipped: Bool { return true }
    var ms: Double { return Date().timeIntervalSince(t0) * 1000 }
    override func draw(_ dirtyRect: NSRect) { paint?(ms) }
}

// Short hairline animations over everything after Copy (paper plane) and Save (tray).
// A click-through transparent panel; it removes itself after about a second.
enum Fx {
    enum Kind { case plane, tray }
    static var live: [NSPanel] = []
    static let ink = rgb("#0b0b0c")

    static func play(_ kind: Kind, _ img: CGImage, from: NSRect, landing: @escaping () -> Void) {
        guard Motion.on, let scr = NSScreen.main else { landing(); return }
        let vf = scr.visibleFrame
        let startS = CGPoint(x: from.midX, y: from.midY)
        var targetS = CGPoint(x: vf.maxX - 16 - 170, y: vf.maxY - 16 - 40)   // where the toast appears
        if kind == .tray { targetS.y -= 80 }
        let area = NSRect(x: min(startS.x, targetS.x), y: min(startS.y, targetS.y),
                          width: abs(startS.x - targetS.x), height: abs(startS.y - targetS.y)).insetBy(dx: -170, dy: -170)
        let start = CGPoint(x: startS.x - area.minX, y: area.maxY - startS.y)
        let target = CGPoint(x: targetS.x - area.minX, y: area.maxY - targetS.y)
        let z = min(1, min(180 / CGFloat(img.width), 120 / CGFloat(img.height)) * 2)
        let card = CGSize(width: max(8, CGFloat(img.width) * z / 2), height: max(8, CGFloat(img.height) * z / 2))

        let p = NSPanel(contentRect: area, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.ignoresMouseEvents = true
        p.level = .statusBar
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let v = FxView(frame: NSRect(origin: .zero, size: area.size))
        v.paint = { ms in
            if kind == .plane { drawPlane(ms, start, target, img, card) } else { drawTray(ms, start, target, img, card) }
        }
        p.contentView = v
        p.orderFrontRegardless()
        live.append(p)
        var landed = false
        let timer = Timer(timeInterval: 1.0 / 60, repeats: true) { t in
            let ms = v.ms
            if !landed && ms >= (kind == .plane ? 700 : 760) { landed = true; landing() }
            if ms >= 1000 {
                t.invalidate()
                p.orderOut(nil)
                Fx.live.removeAll { $0 === p }
                return
            }
            v.needsDisplay = true
        }
        RunLoop.main.add(timer, forMode: .common)
    }

    static func drawCard(_ img: CGImage, _ c: CGPoint, _ size: CGSize, _ sx: CGFloat, _ sy: CGFloat, _ alpha: CGFloat) {
        if alpha <= 0.01 || sx <= 0.01 || sy <= 0.01 { return }
        let r = CGRect(x: c.x - size.width * sx / 2, y: c.y - size.height * sy / 2, width: size.width * sx, height: size.height * sy)
        NSColor(white: 0, alpha: 0.08 * alpha).setFill()
        NSBezierPath(rect: r.insetBy(dx: -2, dy: -2).offsetBy(dx: 0, dy: 2)).fill()
        drawCG(img, in: r, alpha: alpha)
        ink.withAlphaComponent(alpha).setStroke()
        let b = NSBezierPath(rect: r); b.lineWidth = 1; b.stroke()
    }

    static func poly(_ pts: [CGPoint]) -> NSBezierPath {
        let b = NSBezierPath()
        b.move(to: pts[0])
        for q in pts.dropFirst() { b.line(to: q) }
        b.close()
        b.lineJoinStyle = .round
        return b
    }

    static func drawPlaneShape(_ c: CGPoint, _ angle: CGFloat, _ scale: CGFloat, _ alpha: CGFloat) {
        if alpha <= 0.01 { return }
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.saveGState()
        ctx.translateBy(x: c.x, y: c.y)
        ctx.rotate(by: angle)
        ctx.scaleBy(x: scale, y: scale)
        let lower = poly([CGPoint(x: -14, y: -1), CGPoint(x: 14, y: 0), CGPoint(x: -7, y: 8)])
        let upper = poly([CGPoint(x: -14, y: -1), CGPoint(x: 14, y: 0), CGPoint(x: -5, y: -9)])
        NSColor(white: 1, alpha: alpha).setStroke()
        lower.lineWidth = 3.2; lower.stroke(); upper.lineWidth = 3.2; upper.stroke()
        rgb("#f1f1f3", alpha).setFill(); lower.fill()
        NSColor(white: 1, alpha: alpha).setFill(); upper.fill()
        ink.withAlphaComponent(alpha).setStroke()
        lower.lineWidth = 1.2; lower.stroke(); upper.lineWidth = 1.2; upper.stroke()
        let fold = NSBezierPath(); fold.move(to: CGPoint(x: 14, y: 0)); fold.line(to: CGPoint(x: -6, y: 3)); fold.lineWidth = 1.2; fold.stroke()
        ctx.restoreGState()
    }

    static func drawPlane(_ ms: Double, _ start: CGPoint, _ target: CGPoint, _ img: CGImage, _ card: CGSize) {
        // fold the card (0-240 ms), fly (200-820 ms), fade (800-1000 ms)
        let fold = Motion.easeIn(Motion.seg(ms, 0, 240))
        drawCard(img, start, card, 1 - 0.85 * fold, 1 - 0.95 * fold, 1 - Motion.seg(ms, 180, 250))
        let fly = Motion.easeInOut(Motion.seg(ms, 200, 820))
        let dist = hypot(target.x - start.x, target.y - start.y)
        let ctrl = CGPoint(x: (start.x + target.x) / 2, y: max(10, min(start.y, target.y) - max(60, dist * 0.3)))
        func at(_ u: CGFloat) -> CGPoint {
            let v = 1 - u
            return CGPoint(x: v * v * start.x + 2 * u * v * ctrl.x + u * u * target.x, y: v * v * start.y + 2 * u * v * ctrl.y + u * u * target.y)
        }
        let p0 = at(fly), p1 = at(min(1, fly + 0.01))
        var ang = atan2(p1.y - p0.y, p1.x - p0.x)
        if fly >= 1 { ang = atan2(target.y - ctrl.y, target.x - ctrl.x) }
        let appear = Motion.easeOut(Motion.seg(ms, 170, 300))
        let fade = 1 - Motion.seg(ms, 800, 980)
        if ms > 220 {
            var pts: [CGPoint] = []
            for i in 0...40 { pts.append(at(fly * CGFloat(i) / 40)) }
            let trail = NSBezierPath()
            trail.move(to: pts[0])
            for q in pts.dropFirst() { trail.line(to: q) }
            trail.lineWidth = 2.6
            NSColor(white: 1, alpha: 0.6 * fade).setStroke()
            trail.setLineDash([1, 1], count: 2, phase: 0)
            trail.stroke()
            trail.lineWidth = 0.9
            trail.setLineDash([2.5, 2.5], count: 2, phase: 0)
            ink.withAlphaComponent(0.6 * fade).setStroke()
            trail.stroke()
        }
        drawPlaneShape(p0, ang, (0.4 + 0.75 * appear) * (1 - 0.35 * fly), min(appear, fade))
    }

    static func trayPts(_ c: CGPoint, _ pts: [(CGFloat, CGFloat)]) -> [CGPoint] { return pts.map { CGPoint(x: c.x + $0.0, y: c.y + $0.1) } }

    static func drawTrayFront(_ c: CGPoint, _ alpha: CGFloat) {
        let front = poly(trayPts(c, [(-46, 6), (0, 21), (46, 6), (46, 13), (0, 28), (-46, 13)]))
        rgb("#f1f1f3", alpha).setFill(); front.fill()
        ink.withAlphaComponent(alpha).setStroke(); front.lineWidth = 1.1; front.stroke()
    }

    static func drawTrayShape(_ c: CGPoint, _ alpha: CGFloat) {
        if alpha <= 0.01 { return }
        let rim = poly(trayPts(c, [(-46, 6), (0, -9), (46, 6), (0, 21)]))
        let inner = poly(trayPts(c, [(-36, 6), (0, -5), (36, 6), (0, 17)]))
        let front = poly(trayPts(c, [(-46, 6), (0, 21), (46, 6), (46, 13), (0, 28), (-46, 13)]))
        NSColor(white: 1, alpha: alpha).setStroke()
        front.lineWidth = 3.5; front.stroke(); rim.lineWidth = 3.5; rim.stroke()
        NSColor(white: 1, alpha: alpha).setFill(); rim.fill()
        drawTrayFront(c, alpha)
        ink.withAlphaComponent(alpha).setStroke(); rim.lineWidth = 1.1; rim.stroke()
        rgb("#6b6b72", alpha).setStroke(); inner.lineWidth = 0.9; inner.stroke()
    }

    static func drawTray(_ ms: Double, _ start: CGPoint, _ target: CGPoint, _ img: CGImage, _ card: CGSize) {
        // tray appears (0-220), card shrinks (0-260) and drops in (260-640), tray dips and settles (640-1000)
        let trayIn = Motion.easeOut(Motion.seg(ms, 0, 220))
        let trayOut = 1 - Motion.seg(ms, 860, 1000)
        let dip = ms > 640 ? Motion.spring(Motion.seg(ms, 640, 1000)) * 6 : 0
        let tc = CGPoint(x: target.x, y: target.y + (1 - trayIn) * 10 + dip)
        let shrink = Motion.easeOut(Motion.seg(ms, 0, 260))
        let drop = Motion.easeIn(Motion.seg(ms, 260, 640))
        let sc = 1 - 0.6 * shrink
        let c = CGPoint(x: start.x + (tc.x - start.x) * drop, y: start.y + (tc.y - 2 - start.y) * drop - sin(drop * .pi) * 40)
        drawTrayShape(tc, trayIn * trayOut)
        drawCard(img, c, card, sc * (1 - 0.1 * drop), sc * (1 - 0.45 * drop), 1 - Motion.seg(ms, 610, 660))
        if drop > 0.85 { drawTrayFront(tc, trayIn * trayOut) }
    }
}

// MARK: - Marks: highlights and arrows, in image pixels

final class Mark {
    enum Kind { case rect, arrow }
    var kind: Kind
    var color: NSColor
    var a = CGPoint.zero, b = CGPoint.zero
    var w: CGFloat = 2
    var comment = ""
    var bubblePos = CGPoint.zero
    var placed = false, outside = false
    var born: Double = 0   // when it was drawn (Motion.now ms), for the drawn-in animation; 0 = none
    // Arrows have three nodes the user can pull. Until a node is pulled it follows the others: the middle node
    // sits halfway, and nodes 1 and 3 sit on the single curve through A, middle, B. Pulling the middle bends the
    // whole arrow one way; pulling node 1 or 3 adds a second bend.
    var n = [CGPoint](repeating: .zero, count: 3)
    var pinned = [false, false, false]

    init(kind: Kind, color: NSColor, w: CGFloat) {
        self.kind = kind; self.color = color; self.w = w
    }

    var box: CGRect {
        return CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }

    func node(_ i: Int) -> CGPoint {
        let mid = pinned[1] ? n[1] : CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2)
        if i == 1 { return mid }
        if pinned[i] { return n[i] }
        let t: CGFloat = i == 0 ? 0.25 : 0.75
        let u: CGFloat = 1 - t
        let cx = 2 * mid.x - (a.x + b.x) / 2
        let cy = 2 * mid.y - (a.y + b.y) / 2
        let k0 = u * u, k1 = 2 * t * u, k2 = t * t
        return CGPoint(x: k0 * a.x + k1 * cx + k2 * b.x, y: k0 * a.y + k1 * cy + k2 * b.y)
    }

    var points: [CGPoint] { return [a, node(0), node(1), node(2), b] }

    // What the comment bubble points at: the highlight, or the tail of the arrow.
    var anchor: CGRect { return kind == .rect ? box : CGRect(x: a.x - 2, y: a.y - 2, width: 4, height: 4) }

    var bounds: CGRect {
        if kind == .rect { return box }
        var l = a.x, t = a.y, r = a.x, bt = a.y
        for p in points { l = min(l, p.x); t = min(t, p.y); r = max(r, p.x); bt = max(bt, p.y) }
        return CGRect(x: l, y: t, width: r - l, height: bt - t)
    }
}

// MARK: - Drawing of marks and frosted bubbles (shared by the editor and the copied image)

enum Ann {
    static var K: CGFloat = 1          // image pixels per design point

    // Drawn-in animation: outline draws itself (0-368 ms), badge drops in (253-552), bubble unfolds (437-736).
    static let animMs: Double = 810
    static var animate = false
    static var nowMs: Double = 0
    static func prog(_ m: Mark, _ a: Double, _ b: Double) -> CGFloat {
        if !animate || m.born == 0 { return 1 }
        return Motion.seg(nowMs - m.born, a, b)
    }

    static func partial(_ f: [CGPoint], _ frac: CGFloat) -> [CGPoint] {
        var total: CGFloat = 0
        for i in 1..<f.count { total += hypot(f[i].x - f[i - 1].x, f[i].y - f[i - 1].y) }
        let want = total * frac
        var acc: CGFloat = 0
        var o: [CGPoint] = [f[0]]
        for i in 1..<f.count {
            let l = hypot(f[i].x - f[i - 1].x, f[i].y - f[i - 1].y)
            if acc + l >= want {
                let t = l == 0 ? 0 : (want - acc) / l
                o.append(CGPoint(x: f[i - 1].x + (f[i].x - f[i - 1].x) * t, y: f[i - 1].y + (f[i].y - f[i - 1].y) * t))
                break
            }
            o.append(f[i])
            acc += l
        }
        if o.count < 2 { o.append(f[0]) }
        return o
    }

    static func drawPartial(_ m: Mark, _ pts: [CGPoint]) {
        let line = polyline(pts)
        line.lineCapStyle = .round
        line.lineJoinStyle = .round
        NSColor(white: 1, alpha: 0.92).setStroke(); line.lineWidth = m.w + 3 * K; line.stroke()
        m.color.setStroke(); line.lineWidth = m.w; line.stroke()
    }
    static var scene: [Mark] = []      // marks of the open editor, so new bubbles can avoid the others
    static let placeholder = "Type a comment"
    static let emptyHint = "Click to comment"
    static let ink = rgb("#0b0b0c")
    static let inkMuted = rgb("#6b6b72")

    static var cachedK: CGFloat = -1
    static var fontCache = NSFont.systemFont(ofSize: 13)
    static var monoCache = NSFont.systemFont(ofSize: 11)

    static var font: NSFont { refresh(); return fontCache }
    static var mono: NSFont { refresh(); return monoCache }
    static func refresh() {
        if cachedK == K { return }
        fontCache = Fonts.sans(13 * K)
        monoCache = Fonts.mono(11 * K, bold: true)
        cachedK = K
    }

    static func wd(_ s: String) -> CGFloat { return s.isEmpty ? 0 : textSize(s, font).width }
    static var lineH: CGFloat { return ceil((font.ascender - font.descender + font.leading) * 1.08) }

    static var pad: CGFloat { return 10 * K }
    static var chip: CGFloat { return 18 * K }
    static var gap: CGFloat { return 8 * K }
    static var maxW: CGFloat { return 264 * K }
    static var minW: CGFloat { return 148 * K }
    static var radius: CGFloat { return 10 * K }
    static var textMax: CGFloat { return maxW - pad * 2 - chip - gap }
    static var nodeR: CGFloat { return 6 * K }

    // Word wrap; returns one UTF-16 range per line.
    static func wrap(_ t: NSString, _ maxWidth: CGFloat) -> [NSRange] {
        var lines: [NSRange] = []
        let n = t.length
        var ps = 0
        while true {
            let found = t.range(of: "\n", options: [], range: NSRange(location: ps, length: n - ps))
            let nl = found.location == NSNotFound ? -1 : found.location
            let pe = nl < 0 ? n : nl
            var ls = ps
            if ls == pe { lines.append(NSRange(location: ls, length: 0)) }
            while ls < pe {
                var fit = ls
                var j = ls
                while j < pe {
                    var k = j
                    while k < pe && t.character(at: k) != 32 { k += 1 }
                    while k < pe && t.character(at: k) == 32 { k += 1 }
                    if wd(rtrim(t.substring(with: NSRange(location: ls, length: k - ls)))) <= maxWidth { fit = k; j = k } else { break }
                }
                if fit == ls {
                    var c = ls + 1
                    while c < pe && wd(t.substring(with: NSRange(location: ls, length: c + 1 - ls))) <= maxWidth { c += 1 }
                    fit = c
                }
                lines.append(NSRange(location: ls, length: fit - ls))
                ls = fit
            }
            if nl < 0 { break }
            ps = nl + 1
        }
        return lines
    }

    static func bubbleSize(_ shown: String) -> (CGSize, [NSRange]) {
        let ns = shown as NSString
        let lines = wrap(ns, textMax)
        var widest: CGFloat = 0
        for l in lines { widest = max(widest, wd(rtrim(ns.substring(with: l)))) }
        let w = max(minW, min(maxW, pad * 2 + chip + gap + widest + 3 * K))
        let h = pad * 2 + max(chip, CGFloat(lines.count) * lineH)
        return (CGSize(width: ceil(w), height: ceil(h)), lines)
    }

    static func shown(_ m: Mark, editing: Bool, isFinal: Bool) -> String {
        if !m.comment.isEmpty { return m.comment }
        return isFinal ? "" : (editing ? placeholder : emptyHint)
    }

    // Bubble rectangle; places it on first use and keeps it inside the image whenever it fits.
    static func bubbleRect(_ m: Mark, _ img: CGSize, editing: Bool, isFinal: Bool) -> CGRect {
        let s = shown(m, editing: editing, isFinal: isFinal)
        let sz = bubbleSize(s.isEmpty ? placeholder : s).0
        if !m.placed { place(m, sz, img); m.placed = true }
        var p = m.bubblePos
        if !m.outside && sz.width <= img.width && sz.height <= img.height {
            p.x = max(0, min(img.width - sz.width, p.x))
            p.y = max(0, min(img.height - sz.height, p.y))
        }
        return CGRect(origin: p, size: sz)
    }

    static func place(_ m: Mark, _ sz: CGSize, _ img: CGSize) {
        let box = m.anchor
        let g = 14 * K, w = sz.width, h = sz.height
        var taken: [CGRect] = []
        if m.kind == .arrow { taken.append(m.bounds) }
        var outsideBottom = -CGFloat.greatestFiniteMagnitude
        for o in scene where o !== m {
            taken.append(o.bounds)
            if !o.placed { continue }
            let ob = bubbleRect(o, img, editing: false, isFinal: false)
            taken.append(ob)
            if o.outside { outsideBottom = max(outsideBottom, ob.maxY) }
        }
        let roomy = img.width >= w * 1.8 && img.height >= h * 2.5
        if roomy {
            var yAt = max(0, min(img.height - h, box.minY))
            var xAt = max(0, min(img.width - w, box.minX))
            if m.kind == .arrow {
                yAt = max(0, min(img.height - h, box.minY - h / 2))
                xAt = max(0, min(img.width - w, box.minX - w / 2))
            }
            var cands = [CGPoint(x: box.maxX + g, y: yAt), CGPoint(x: box.minX - g - w, y: yAt),
                         CGPoint(x: xAt, y: box.maxY + g), CGPoint(x: xAt, y: box.minY - g - h)]
            if m.kind == .arrow {
                // Prefer the side of the tail facing away from where the arrow goes
                let n0 = m.node(0)
                let dx = n0.x - m.a.x, dy = n0.y - m.a.y
                cands.sort { p1, p2 in
                    let d1 = (p1.x + w / 2 - m.a.x) * dx + (p1.y + h / 2 - m.a.y) * dy
                    let d2 = (p2.x + w / 2 - m.a.x) * dx + (p2.y + h / 2 - m.a.y) * dy
                    return d1 < d2
                }
            }
            for strict in [true, false] {
                for c in cands {
                    let r = CGRect(origin: c, size: sz)
                    if r.minX < 0 || r.minY < 0 || r.maxX > img.width || r.maxY > img.height { continue }
                    var hit = false
                    if strict { for t in taken where t.intersects(r) { hit = true; break } }
                    if !hit { m.bubblePos = c; m.outside = false; return }
                }
            }
            m.bubblePos = CGPoint(x: img.width - w, y: img.height - h)
            m.outside = false
            return
        }
        // Small capture: stack bubbles in a column to the right of the image
        var y = max(0, box.minY)
        if outsideBottom > -CGFloat.greatestFiniteMagnitude { y = max(y, outsideBottom + 8 * K) }
        m.bubblePos = CGPoint(x: img.width + g, y: y)
        m.outside = true
    }

    static func closeRect(_ bubble: CGRect) -> CGRect {
        let d = 18 * K
        return CGRect(x: bubble.maxX - d * 0.6, y: bubble.minY - d * 0.4, width: d, height: d)
    }

    static func badgeRect(_ m: Mark, _ img: CGSize) -> CGRect {
        let d = 20 * K
        let corner = m.kind == .rect ? CGPoint(x: m.box.minX, y: m.box.minY) : m.a
        var x = corner.x - d / 2, y = corner.y - d / 2
        if img.width >= d && img.height >= d {
            x = max(0, min(img.width - d, x))
            y = max(0, min(img.height - d, y))
        }
        return CGRect(x: x, y: y, width: d, height: d)
    }

    // Everything that should be visible: the image plus bubbles that stick out of it.
    static func content(_ marks: [Mark], _ img: CGSize, isFinal: Bool) -> CGRect {
        var r = CGRect(origin: .zero, size: img)
        for m in marks {
            if isFinal && m.comment.isEmpty { continue }
            r = r.union(bubbleRect(m, img, editing: false, isFinal: isFinal))
        }
        return r
    }

    // MARK: arrows

    // Smooth curve through the five points (cardinal spline), flattened into short straight pieces.
    static func flat(_ m: Mark) -> [CGPoint] {
        let p = m.points
        var out: [CGPoint] = [p[0]]
        let ten: CGFloat = 0.5
        for i in 0..<(p.count - 1) {
            let p0 = p[max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[min(i + 2, p.count - 1)]
            let c1 = CGPoint(x: p1.x + ten * (p2.x - p0.x) / 3, y: p1.y + ten * (p2.y - p0.y) / 3)
            let c2 = CGPoint(x: p2.x - ten * (p3.x - p1.x) / 3, y: p2.y - ten * (p3.y - p1.y) / 3)
            for s in 1...16 {
                let u = CGFloat(s) / 16
                let v: CGFloat = 1 - u
                let k0: CGFloat = v * v * v
                let k1: CGFloat = 3 * v * v * u
                let k2: CGFloat = 3 * v * u * u
                let k3: CGFloat = u * u * u
                let x: CGFloat = k0 * p1.x + k1 * c1.x + k2 * c2.x + k3 * p2.x
                let y: CGFloat = k0 * p1.y + k1 * c1.y + k2 * c2.y + k3 * p2.y
                out.append(CGPoint(x: x, y: y))
            }
        }
        return out
    }

    static func distToArrow(_ m: Mark, _ p: CGPoint) -> CGFloat {
        let f = flat(m)
        var best = CGFloat.greatestFiniteMagnitude
        for i in 1..<f.count {
            let a = f[i - 1], b = f[i]
            let vx = b.x - a.x, vy = b.y - a.y
            let l2 = vx * vx + vy * vy
            let t = l2 == 0 ? 0 : max(0, min(1, ((p.x - a.x) * vx + (p.y - a.y) * vy) / l2))
            let dx = a.x + t * vx - p.x, dy = a.y + t * vy - p.y
            best = min(best, sqrt(dx * dx + dy * dy))
        }
        return best
    }

    // Point at a distance back from the end of a polyline, plus the polyline up to it.
    static func back(_ f: [CGPoint], _ dist: CGFloat) -> (CGPoint, [CGPoint]) {
        var acc: CGFloat = 0
        var i = f.count - 1
        while i > 0 {
            let vx = f[i].x - f[i - 1].x, vy = f[i].y - f[i - 1].y
            let l = sqrt(vx * vx + vy * vy)
            if acc + l >= dist {
                let t = l == 0 ? 0 : (dist - acc) / l
                let q = CGPoint(x: f[i].x - vx * t, y: f[i].y - vy * t)
                var before = Array(f[0..<i])
                before.append(q)
                return (q, before)
            }
            acc += l
            i -= 1
        }
        return (f[0], [f[0]])
    }

    static func polyline(_ pts: [CGPoint]) -> NSBezierPath {
        let p = NSBezierPath()
        guard let first = pts.first else { return p }
        p.move(to: first)
        for q in pts.dropFirst() { p.line(to: q) }
        return p
    }

    static func drawArrow(_ m: Mark) {
        let f = flat(m)
        var len: CGFloat = 0
        for i in 1..<f.count { len += hypot(f[i].x - f[i - 1].x, f[i].y - f[i - 1].y) }
        if len < 1 { return }
        let dp = Motion.easeInOut(prog(m, 0, 368))
        if dp < 1 { if dp > 0 { drawPartial(m, partial(f, dp)) }; return }
        let head = min(len * 0.6, m.w * 4.5 + 6 * K)
        let half = head * 0.55
        let bp = back(f, head).0
        let shaft = back(f, head * 0.7).1
        let dx = m.b.x - bp.x, dy = m.b.y - bp.y
        let hl = hypot(dx, dy)
        if hl < 0.01 { return }
        let ux = dx / hl, uy = dy / hl
        let tri = NSBezierPath()
        tri.move(to: m.b)
        tri.line(to: CGPoint(x: bp.x - uy * half, y: bp.y + ux * half))
        tri.line(to: CGPoint(x: bp.x + uy * half, y: bp.y - ux * half))
        tri.close()
        let line = polyline(shaft)
        line.lineCapStyle = .round
        line.lineJoinStyle = .round
        tri.lineJoinStyle = .round

        NSColor(white: 1, alpha: 0.92).setStroke()
        line.lineWidth = m.w + 3 * K; line.stroke()
        tri.lineWidth = m.w + 3 * K; tri.stroke()
        m.color.setStroke()
        line.lineWidth = m.w; line.stroke()
        m.color.setFill(); tri.fill()
        tri.lineWidth = m.w * 0.5; tri.stroke()
    }

    static func drawNodes(_ m: Mark) {
        let guide = polyline(m.points)
        guide.lineWidth = max(1, K)
        guide.setLineDash([3 * K, 3 * K], count: 2, phase: 0)
        NSColor(white: 1, alpha: 0.5).setStroke()
        guide.stroke()
        for i in 0..<3 {
            let c = m.node(i)
            let r = nodeR * (i == 1 ? 1.15 : 1)
            let o = NSBezierPath(ovalIn: CGRect(x: c.x - r, y: c.y - r, width: 2 * r, height: 2 * r))
            (m.pinned[i] ? m.color : NSColor.white).setFill(); o.fill()
            (m.pinned[i] ? NSColor.white : ink).setStroke(); o.lineWidth = max(1, 1.5 * K); o.stroke()
        }
    }

    // MARK: highlights, badges, bubbles

    static func drawHighlight(_ m: Mark) {
        let hp = Motion.easeInOut(prog(m, 0, 368))
        if hp < 1 {
            if hp > 0 {
                let r = m.box
                drawPartial(m, partial([CGPoint(x: r.minX, y: r.minY), CGPoint(x: r.maxX, y: r.minY), CGPoint(x: r.maxX, y: r.maxY),
                                        CGPoint(x: r.minX, y: r.maxY), CGPoint(x: r.minX, y: r.minY)], hp))
            }
            return
        }
        let p = NSBezierPath(roundedRect: m.box, xRadius: 2.5 * K, yRadius: 2.5 * K)
        p.lineJoinStyle = .round
        NSColor(white: 1, alpha: 0.92).setStroke(); p.lineWidth = m.w + 3 * K; p.stroke()
        m.color.setStroke(); p.lineWidth = m.w; p.stroke()
    }

    static func onColor(_ c: NSColor) -> NSColor {
        let s = c.usingColorSpace(.sRGB) ?? c
        let lum = 0.2126 * s.redComponent + 0.7152 * s.greenComponent + 0.0722 * s.blueComponent
        return lum > 0.63 ? ink : NSColor.white
    }

    static func drawCentered(_ t: String, _ r: CGRect, _ c: NSColor) {
        let sz = textSize(t, mono)
        drawText(t, at: CGPoint(x: r.midX - sz.width / 2, y: r.midY - sz.height / 2), font: mono, color: c)
    }

    static func drawBadge(_ r: CGRect, _ c: NSColor, _ n: Int) {
        let p = NSBezierPath(roundedRect: r, xRadius: 5 * K, yRadius: 5 * K)
        NSColor.white.setStroke(); p.lineWidth = 2 * K; p.stroke()
        c.setFill(); p.fill()
        drawCentered(String(n), r, onColor(c))
    }

    // Rounded rectangle with a speech-bubble pointer on one side, aimed at the anchor.
    static func bubblePath(_ r: CGRect, _ box: CGRect) -> NSBezierPath {
        let rad = radius, half = 7 * K, inset = 3 * K
        var side = 0
        var bpos: CGFloat = 0
        var tip = CGPoint.zero
        if r.minX >= box.maxX + 2 * K { side = 1 }
        else if r.maxX <= box.minX - 2 * K { side = 2 }
        else if r.minY >= box.maxY + 2 * K { side = 3 }
        else if r.maxY <= box.minY - 2 * K { side = 4 }

        if side == 1 || side == 2 {
            let lo = r.minY + rad + half, hi = r.maxY - rad - half
            if hi < lo { side = 0 } else {
                var cy = (max(r.minY, box.minY) + min(r.maxY, box.maxY)) / 2
                if max(r.minY, box.minY) > min(r.maxY, box.maxY) { cy = box.midY }
                bpos = max(lo, min(hi, cy))
                tip = CGPoint(x: side == 1 ? box.maxX + inset : box.minX - inset, y: max(box.minY + inset, min(box.maxY - inset, bpos)))
                if box.height < 2 * inset { tip.y = box.midY }
            }
        } else if side == 3 || side == 4 {
            let lo = r.minX + rad + half, hi = r.maxX - rad - half
            if hi < lo { side = 0 } else {
                var cx = (max(r.minX, box.minX) + min(r.maxX, box.maxX)) / 2
                if max(r.minX, box.minX) > min(r.maxX, box.maxX) { cx = box.midX }
                bpos = max(lo, min(hi, cx))
                tip = CGPoint(x: max(box.minX + inset, min(box.maxX - inset, bpos)), y: side == 3 ? box.maxY + inset : box.minY - inset)
                if box.width < 2 * inset { tip.x = box.midX }
            }
        }

        let p = NSBezierPath()
        p.move(to: CGPoint(x: r.minX + rad, y: r.minY))
        if side == 3 { p.line(to: CGPoint(x: bpos - half, y: r.minY)); p.line(to: tip); p.line(to: CGPoint(x: bpos + half, y: r.minY)) }
        p.appendArc(from: CGPoint(x: r.maxX, y: r.minY), to: CGPoint(x: r.maxX, y: r.maxY), radius: rad)
        if side == 2 { p.line(to: CGPoint(x: r.maxX, y: bpos - half)); p.line(to: tip); p.line(to: CGPoint(x: r.maxX, y: bpos + half)) }
        p.appendArc(from: CGPoint(x: r.maxX, y: r.maxY), to: CGPoint(x: r.minX, y: r.maxY), radius: rad)
        if side == 4 { p.line(to: CGPoint(x: bpos + half, y: r.maxY)); p.line(to: tip); p.line(to: CGPoint(x: bpos - half, y: r.maxY)) }
        p.appendArc(from: CGPoint(x: r.minX, y: r.maxY), to: CGPoint(x: r.minX, y: r.minY), radius: rad)
        if side == 1 { p.line(to: CGPoint(x: r.minX, y: bpos + half)); p.line(to: tip); p.line(to: CGPoint(x: r.minX, y: bpos - half)) }
        p.appendArc(from: CGPoint(x: r.minX, y: r.minY), to: CGPoint(x: r.maxX, y: r.minY), radius: rad)
        p.close()
        return p
    }

    static func drawBubble(_ m: Mark, _ blur: CGImage?, _ isz: CGSize, _ num: Int, isFinal: Bool, editing: Bool, caret: Int, showClose: Bool) {
        let s = shown(m, editing: editing, isFinal: isFinal)
        let ph = m.comment.isEmpty
        let r = bubbleRect(m, isz, editing: editing, isFinal: isFinal)
        let lines = bubbleSize(s).1
        let path = bubblePath(r, m.anchor)
        guard let ctx = NSGraphicsContext.current else { return }

        // Soft shadow, outside the bubble only
        ctx.saveGraphicsState()
        let outside = NSBezierPath(rect: r.insetBy(dx: -60 * K, dy: -60 * K))
        outside.append(path)
        outside.windingRule = .evenOdd
        outside.addClip()
        let shadowPath = path.copy() as! NSBezierPath
        shadowPath.transform(using: AffineTransform(translationByX: 0, byY: 2 * K))
        let widths: [CGFloat] = [10, 6, 3]
        let alphas: [CGFloat] = [0.03, 0.05, 0.07]
        for i in 0..<3 {
            NSColor(white: 0, alpha: alphas[i]).setStroke()
            shadowPath.lineWidth = widths[i] * K
            shadowPath.lineJoinStyle = .round
            shadowPath.stroke()
        }
        ctx.restoreGraphicsState()

        // Frosted glass: blurred image behind, white tint on top
        ctx.saveGraphicsState()
        path.addClip()
        if let bl = blur { drawCG(bl, in: CGRect(origin: .zero, size: isz)) }
        NSColor(white: 1, alpha: 0.77).setFill()
        path.fill()
        ctx.restoreGraphicsState()

        NSColor(srgbRed: 11 / 255, green: 11 / 255, blue: 12 / 255, alpha: editing ? 0.35 : 0.15).setStroke()
        path.lineWidth = max(1, K)
        path.stroke()

        // Number chip, matching the badge
        let chipR = CGRect(x: r.minX + pad, y: r.minY + pad, width: chip, height: chip)
        m.color.setFill()
        NSBezierPath(roundedRect: chipR, xRadius: 4.5 * K, yRadius: 4.5 * K).fill()
        drawCentered(String(num), chipR, onColor(m.color))

        // Text
        let tx = chipR.maxX + gap
        let ty = r.minY + pad + max(0, (chip - lineH) / 2)
        let ns = s as NSString
        for (i, l) in lines.enumerated() {
            drawText(ns.substring(with: l), at: CGPoint(x: tx, y: ty + CGFloat(i) * lineH), font: font, color: ph ? inkMuted : ink)
        }

        // Caret
        if caret >= 0 {
            var cx = tx, cy = ty
            if !ph {
                var li = 0
                for (i, l) in lines.enumerated() where l.location <= caret { li = i }
                let from = lines[li].location
                let cnt = max(0, min(caret, from + lines[li].length) - from)
                cx = tx + wd(ns.substring(with: NSRange(location: from, length: cnt)))
                cy = ty + CGFloat(li) * lineH
            }
            let cp = NSBezierPath()
            cp.move(to: CGPoint(x: cx, y: cy + 1.5 * K))
            cp.line(to: CGPoint(x: cx, y: cy + lineH - 1.5 * K))
            cp.lineWidth = max(1, 1.25 * K)
            ink.setStroke(); cp.stroke()
        }

        // Remove button (editor only)
        if showClose {
            let x = closeRect(r)
            let o = NSBezierPath(ovalIn: x)
            NSColor.white.setFill(); o.fill()
            NSColor(white: 0, alpha: 0.25).setStroke(); o.lineWidth = max(1, K); o.stroke()
            let mm = x.width * 0.34
            let xp = NSBezierPath()
            xp.move(to: CGPoint(x: x.minX + mm, y: x.minY + mm)); xp.line(to: CGPoint(x: x.maxX - mm, y: x.maxY - mm))
            xp.move(to: CGPoint(x: x.maxX - mm, y: x.minY + mm)); xp.line(to: CGPoint(x: x.minX + mm, y: x.maxY - mm))
            xp.lineWidth = max(1, 1.25 * K); xp.lineCapStyle = .round
            ink.setStroke(); xp.stroke()
        }
    }

    static func drawAll(_ img: CGImage, _ blur: CGImage?, _ marks: [Mark], live: Mark?, isFinal: Bool,
                        editing: Mark?, caret: Int, caretOn: Bool, hover: Mark?, selected: Mark?) {
        let isz = CGSize(width: img.width, height: img.height)
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        animate = !isFinal && Motion.on
        nowMs = Motion.now()
        var all = marks
        if let l = live { all.append(l) }

        // Spotlight: gently dim everything outside the highlights
        let rectMarks = all.filter { $0.kind == .rect }
        let rects = rectMarks.map { $0.box }
        if !rects.isEmpty {
            let fade = rectMarks.count == 1 ? Motion.easeOut(prog(rectMarks[0], 0, 368)) : 1
            ctx.saveGState()
            ctx.beginTransparencyLayer(auxiliaryInfo: nil)
            ctx.setFillColor(NSColor(white: 0.04, alpha: 0.18 * fade).cgColor)
            ctx.fill(CGRect(origin: .zero, size: isz))
            ctx.setBlendMode(.clear)
            for r in rects { ctx.fill(r) }
            ctx.endTransparencyLayer()
            ctx.restoreGState()
        }

        for m in all { if m.kind == .rect { drawHighlight(m) } else { drawArrow(m) } }
        if let s = selected, s.kind == .arrow, !isFinal { drawNodes(s) }

        var num = 0
        for m in marks {
            num += 1
            let d = prog(m, 253, 552)
            if d <= 0 { continue }
            let br = badgeRect(m, isz)
            if d >= 1 { drawBadge(br, m.color, num); continue }
            // drops in from just above its corner and settles
            let sc = 0.5 + 0.5 * Motion.back(d)
            ctx.saveGState()
            ctx.translateBy(x: br.midX, y: br.midY - (1 - Motion.easeOut(d)) * 12 * K)
            ctx.scaleBy(x: sc, y: sc)
            ctx.translateBy(x: -br.midX, y: -br.midY)
            drawBadge(br, m.color, num)
            ctx.restoreGState()
        }
        if let l = live { drawBadge(badgeRect(l, isz), l.color, num + 1) }

        num = 0
        for m in marks {
            num += 1
            if isFinal && m.comment.isEmpty { continue }
            let ed = m === editing
            let u = prog(m, 437, 736)
            if u <= 0 { continue }
            if u >= 1 {
                drawBubble(m, blur, isz, num, isFinal: isFinal, editing: ed, caret: ed && caretOn ? caret : -1,
                           showClose: !isFinal && (ed || m === hover))
                continue
            }
            // unfolds out of its pointer
            let bub = bubbleRect(m, isz, editing: ed, isFinal: isFinal)
            let an = m.anchor
            let px = max(bub.minX, min(bub.maxX, an.midX)), py = max(bub.minY, min(bub.maxY, an.midY))
            let bsc = max(0.05, Motion.back(u))
            ctx.saveGState()
            ctx.translateBy(x: px, y: py)
            ctx.scaleBy(x: bsc, y: bsc)
            ctx.translateBy(x: -px, y: -py)
            drawBubble(m, blur, isz, num, isFinal: isFinal, editing: ed, caret: -1, showClose: false)
            ctx.restoreGState()
        }
    }

    static func makeBlur(_ img: CGImage) -> CGImage? {
        let ci = CIImage(cgImage: img)
        guard let f = CIFilter(name: "CIGaussianBlur") else { return nil }
        f.setValue(ci.clampedToExtent(), forKey: kCIInputImageKey)
        f.setValue(NSNumber(value: Double(12 * K)), forKey: kCIInputRadiusKey)
        guard let out = f.outputImage?.cropped(to: ci.extent) else { return nil }
        return CIContext(options: nil).createCGImage(out, from: ci.extent)
    }
}

// MARK: - Small Linework controls

final class LWButton: NSView {
    enum Kind { case primary, secondary, ghost, chip }
    var kind: Kind = .secondary
    var title = ""
    var hint: String? = nil
    var icon: ((CGRect, NSColor) -> Void)? = nil
    var action: (() -> Void)? = nil
    var pressed = false { didSet { needsDisplay = true } }
    var enabled = true { didSet { needsDisplay = true } }
    private var hovering = false, down = false

    override var isFlipped: Bool { return true }

    init(_ title: String, kind: Kind, action: (() -> Void)? = nil) {
        super.init(frame: .zero)
        self.title = title; self.kind = kind; self.action = action
    }
    required init?(coder: NSCoder) { fatalError() }

    var preferredWidth: CGFloat {
        var w = textSize(title, Fonts.sans(13, medium: true)).width + (kind == .chip ? 20 : 28)
        if icon != nil { w += 20 }
        if let h = hint { w += textSize(h, Fonts.mono(11)).width + 10 }
        return ceil(w)
    }

    override func updateTrackingAreas() {
        for t in trackingAreas { removeTrackingArea(t) }
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self, userInfo: nil))
    }
    override func mouseEntered(with event: NSEvent) { hovering = true; needsDisplay = true }
    override func mouseExited(with event: NSEvent) { hovering = false; needsDisplay = true }
    override func mouseDown(with event: NSEvent) { if enabled { down = true; needsDisplay = true } }
    override func mouseUp(with event: NSEvent) {
        let inside = bounds.contains(convert(event.locationInWindow, from: nil))
        down = false; needsDisplay = true
        if enabled && inside { action?() }
    }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 0.5, dy: 0.5)
        let p = NSBezierPath(roundedRect: r, xRadius: 6, yRadius: 6)
        var fg = Theme.ink
        switch kind {
        case .primary:
            (down ? Theme.ink.withAlphaComponent(0.85) : Theme.ink).setFill(); p.fill()
            fg = Theme.onPrimary
        case .secondary:
            (hovering ? Theme.hover : Theme.surface).setFill(); p.fill()
            Theme.lineControl.withAlphaComponent(0.6).setStroke(); p.lineWidth = 1; p.stroke()
        case .ghost, .chip:
            if pressed || hovering || down { (pressed ? Theme.hover : Theme.sunken).setFill(); p.fill() }
            fg = pressed || kind == .ghost ? Theme.ink : Theme.inkMuted
        }
        if !enabled { fg = Theme.inkFaint }
        let f = Fonts.sans(13, medium: true)
        let ts = textSize(title, f)
        var x = (bounds.width - preferredWidth) / 2 + (kind == .chip ? 10 : 14)
        if let ic = icon {
            ic(CGRect(x: x, y: (bounds.height - 16) / 2, width: 16, height: 16), fg)
            x += 20
        }
        drawText(title, at: CGPoint(x: x, y: (bounds.height - ts.height) / 2), font: f, color: fg)
        if let h = hint {
            let hf = Fonts.mono(11)
            let hs = textSize(h, hf)
            drawText(h, at: CGPoint(x: x + ts.width + 10, y: (bounds.height - hs.height) / 2), font: hf, color: fg.withAlphaComponent(0.6))
        }
    }
}

final class ColorDot: NSView {
    var swatch = NSColor.black
    var selected = false { didSet { needsDisplay = true } }
    var action: (() -> Void)? = nil
    override var isFlipped: Bool { return true }
    override func mouseUp(with event: NSEvent) { action?() }
    override func draw(_ dirtyRect: NSRect) {
        let c = CGPoint(x: bounds.midX, y: bounds.midY)
        if selected {
            let ring = NSBezierPath(ovalIn: CGRect(x: c.x - 10, y: c.y - 10, width: 20, height: 20))
            Theme.ink.setStroke(); ring.lineWidth = 1.5; ring.stroke()
        }
        let dot = NSBezierPath(ovalIn: CGRect(x: c.x - 6.5, y: c.y - 6.5, width: 13, height: 13))
        swatch.setFill(); dot.fill()
        Theme.lineControl.withAlphaComponent(0.5).setStroke(); dot.lineWidth = 1; dot.stroke()
    }
}

final class NoteTextView: NSTextView {
    var placeholder = ""
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        if string.isEmpty {
            let o = textContainerOrigin
            let pad = textContainer?.lineFragmentPadding ?? 5
            drawText(placeholder, at: CGPoint(x: o.x + pad, y: o.y), font: font ?? Fonts.sans(13), color: Theme.inkFaint)
        }
    }
    override func didChangeText() { super.didChangeText(); needsDisplay = true }
}

func iconRect(_ r: CGRect, _ c: NSColor) {
    let p = NSBezierPath(roundedRect: CGRect(x: r.minX + 2.5, y: r.minY + 3.5, width: 11, height: 9), xRadius: 1.5, yRadius: 1.5)
    p.lineWidth = 1.25; c.setStroke(); p.stroke()
}

func iconArrow(_ r: CGRect, _ c: NSColor) {
    let p = NSBezierPath()
    p.move(to: CGPoint(x: r.minX + 3.5, y: r.minY + 12.5))
    p.curve(to: CGPoint(x: r.minX + 12.5, y: r.minY + 3.5), controlPoint1: CGPoint(x: r.minX + 4, y: r.minY + 6), controlPoint2: CGPoint(x: r.minX + 9, y: r.minY + 4))
    p.move(to: CGPoint(x: r.minX + 7.5, y: r.minY + 2.5))
    p.line(to: CGPoint(x: r.minX + 12.5, y: r.minY + 3.5))
    p.line(to: CGPoint(x: r.minX + 11.5, y: r.minY + 8.5))
    p.lineWidth = 1.25; p.lineCapStyle = .round; p.lineJoinStyle = .round
    c.setStroke(); p.stroke()
}

func iconUndo(_ r: CGRect, _ c: NSColor) {
    let p = NSBezierPath()
    p.move(to: CGPoint(x: r.minX + 6, y: r.minY + 4))
    p.line(to: CGPoint(x: r.minX + 3.5, y: r.minY + 6.5))
    p.line(to: CGPoint(x: r.minX + 6, y: r.minY + 9))
    p.move(to: CGPoint(x: r.minX + 3.5, y: r.minY + 6.5))
    p.line(to: CGPoint(x: r.minX + 9.5, y: r.minY + 6.5))
    p.curve(to: CGPoint(x: r.minX + 9.5, y: r.minY + 12.5), controlPoint1: CGPoint(x: r.minX + 13.5, y: r.minY + 6.5), controlPoint2: CGPoint(x: r.minX + 13.5, y: r.minY + 12.5))
    p.line(to: CGPoint(x: r.minX + 6.5, y: r.minY + 12.5))
    p.lineWidth = 1.25; p.lineCapStyle = .round; p.lineJoinStyle = .round
    c.setStroke(); p.stroke()
}

// MARK: - Canvas: shows the capture and lets you draw highlights, arrows and comments on it

final class CanvasView: NSView {
    var img: CGImage? = nil { didSet { blur = img.flatMap { Ann.makeBlur($0) } } }
    var blur: CGImage? = nil
    var marks: [Mark] = [] { didSet { Ann.scene = marks } }
    var tool: Mark.Kind = .rect
    var penColor = rgb("#0b0b0c")
    var strokeW: CGFloat = 2
    var changed: (() -> Void)? = nil

    private var live: Mark? = nil, editing: Mark? = nil, hover: Mark? = nil, dragBubble: Mark? = nil
    private var selected: Mark? = nil, dragNodeOf: Mark? = nil
    private var dragNode = -1
    private var caret = 0
    private var caretOn = true, moved = false
    private var dragOff = CGPoint.zero, downAt = CGPoint.zero
    private var zoom: CGFloat = 1
    private var off = CGPoint.zero
    private var blink: Timer? = nil
    private var animTimer: Timer? = nil

    func startAnim() {
        if animTimer != nil { return }
        let t = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] tm in
            guard let s = self else { tm.invalidate(); return }
            let now = Motion.now()
            if !s.marks.contains(where: { $0.born != 0 && now - $0.born < Ann.animMs }) { tm.invalidate(); s.animTimer = nil }
            s.needsDisplay = true
        }
        RunLoop.main.add(t, forMode: .common)
        animTimer = t
    }

    override var isFlipped: Bool { return true }
    override var acceptsFirstResponder: Bool { return true }
    var isEditing: Bool { return editing != nil }
    var isz: CGSize { return img.map { CGSize(width: $0.width, height: $0.height) } ?? .zero }

    override func updateTrackingAreas() {
        for t in trackingAreas { removeTrackingArea(t) }
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self, userInfo: nil))
    }

    func fit() {
        guard img != nil, dragBubble == nil else { return }
        let cb = Ann.content(marks, isz, isFinal: false)
        let pad: CGFloat = 16
        let aw = max(1, bounds.width - 2 * pad), ah = max(1, bounds.height - 2 * pad)
        zoom = min(1 / Ann.K, min(aw / cb.width, ah / cb.height))
        off = CGPoint(x: round((bounds.width - cb.width * zoom) / 2 - cb.minX * zoom), y: round((bounds.height - cb.height * zoom) / 2 - cb.minY * zoom))
    }

    func toImg(_ e: NSEvent) -> CGPoint {
        let p = convert(e.locationInWindow, from: nil)
        return CGPoint(x: (p.x - off.x) / zoom, y: (p.y - off.y) / zoom)
    }

    func clamp(_ p: CGPoint) -> CGPoint {
        return CGPoint(x: max(0, min(isz.width, p.x)), y: max(0, min(isz.height, p.y)))
    }

    func bubbleAt(_ p: CGPoint) -> Mark? {
        for m in marks.reversed() where Ann.bubbleRect(m, isz, editing: m === editing, isFinal: false).contains(p) { return m }
        return nil
    }

    func closeAt(_ p: CGPoint) -> Mark? {
        for m in marks.reversed() where m === editing || m === hover {
            let x = Ann.closeRect(Ann.bubbleRect(m, isz, editing: m === editing, isFinal: false)).insetBy(dx: -2 * Ann.K, dy: -2 * Ann.K)
            if x.contains(p) { return m }
        }
        return nil
    }

    func boxAt(_ p: CGPoint) -> Mark? {
        for m in marks.reversed() where m.kind == .rect && m.box.contains(p) { return m }
        return nil
    }

    func arrowAt(_ p: CGPoint) -> Mark? {
        for m in marks.reversed() where m.kind == .arrow && Ann.distToArrow(m, p) <= max(m.w + 4 * Ann.K, 6 / zoom) { return m }
        return nil
    }

    func nodeAt(_ p: CGPoint) -> Int {
        guard let s = selected, s.kind == .arrow else { return -1 }
        let r = max(Ann.nodeR * 1.6, 8 / zoom)
        for i in [1, 0, 2] {
            let c = s.node(i)
            if (c.x - p.x) * (c.x - p.x) + (c.y - p.y) * (c.y - p.y) <= r * r { return i }
        }
        return -1
    }

    func beginEdit(_ m: Mark) {
        selected = m
        editing = m
        caret = (m.comment as NSString).length
        caretOn = true
        blink?.invalidate()
        blink = Timer.scheduledTimer(withTimeInterval: 0.53, repeats: true) { [weak self] _ in
            guard let s = self else { return }
            s.caretOn.toggle()
            s.needsDisplay = true
        }
        window?.makeFirstResponder(self)
        needsDisplay = true
    }

    func endEdit() {
        guard let m = editing else { return }
        m.comment = m.comment.trimmingCharacters(in: .whitespacesAndNewlines)
        editing = nil
        blink?.invalidate(); blink = nil
        needsDisplay = true
    }

    func remove(_ m: Mark) {
        if m === editing { editing = nil; blink?.invalidate(); blink = nil }
        if m === hover { hover = nil }
        if m === selected { selected = nil }
        marks.removeAll { $0 === m }
        fit()
        needsDisplay = true
        changed?()
    }

    func undo() { if let last = marks.last { remove(last) } }

    override func mouseDown(with e: NSEvent) {
        guard img != nil else { return }
        let p = toImg(e)
        downAt = convert(e.locationInWindow, from: nil)
        moved = false
        if let x = closeAt(p) { remove(x); return }
        let ni = nodeAt(p)
        if ni >= 0 { dragNode = ni; dragNodeOf = selected; return }
        if let b = bubbleAt(p) {
            dragBubble = b
            let br = Ann.bubbleRect(b, isz, editing: b === editing, isFinal: false)
            dragOff = CGPoint(x: p.x - br.minX, y: p.y - br.minY)
            b.bubblePos = br.origin
            return
        }
        if let arr = arrowAt(p) { beginEdit(arr); return }
        endEdit()
        selected = nil
        window?.makeFirstResponder(self)
        let m = Mark(kind: tool, color: penColor, w: strokeW)
        m.a = clamp(p); m.b = m.a
        live = m
    }

    override func mouseDragged(with e: NSEvent) {
        guard img != nil else { return }
        let p = toImg(e)
        let v = convert(e.locationInWindow, from: nil)
        if abs(v.x - downAt.x) > 3 || abs(v.y - downAt.y) > 3 { moved = true }
        if dragNode >= 0, let a = dragNodeOf {
            if moved { a.n[dragNode] = clamp(p); a.pinned[dragNode] = true; needsDisplay = true }
            return
        }
        if let b = dragBubble {
            if moved { b.bubblePos = CGPoint(x: p.x - dragOff.x, y: p.y - dragOff.y); needsDisplay = true }
            return
        }
        if let l = live { l.b = clamp(p); needsDisplay = true }
    }

    override func mouseMoved(with e: NSEvent) {
        guard img != nil else { return }
        let p = toImg(e)
        var h = bubbleAt(p)
        if h == nil { h = closeAt(p) }
        if h !== hover { hover = h; needsDisplay = true }
        if nodeAt(p) >= 0 || closeAt(p) != nil { NSCursor.pointingHand.set() }
        else if h != nil { NSCursor.openHand.set() }
        else if arrowAt(p) != nil { NSCursor.pointingHand.set() }
        else { NSCursor.crosshair.set() }
    }

    override func mouseExited(with event: NSEvent) {
        if hover != nil && dragBubble == nil { hover = nil; needsDisplay = true }
        NSCursor.arrow.set()
    }

    override func mouseUp(with e: NSEvent) {
        if dragNode >= 0, let a = dragNodeOf {
            let i = dragNode
            dragNode = -1; dragNodeOf = nil
            if !moved { a.pinned[i] = false }   // clicking a pulled node lets it follow the curve again
            fit()
            needsDisplay = true
            if editing == nil { beginEdit(a) }
            return
        }
        if let b = dragBubble {
            dragBubble = nil
            b.bubblePos = Ann.bubbleRect(b, isz, editing: b === editing, isFinal: false).origin
            fit()
            if !moved { beginEdit(b) } else { needsDisplay = true }
            return
        }
        guard let s = live else { return }
        live = nil
        let dx = (s.b.x - s.a.x) * zoom, dy = (s.b.y - s.a.y) * zoom
        if hypot(dx, dy) >= 6 && (s.kind == .arrow || (abs(dx) >= 4 && abs(dy) >= 4)) {
            if Motion.on { s.born = Motion.now() + 1; startAnim() }
            marks.append(s)
            changed?()
            beginEdit(s)
            fit()
        } else if let box = boxAt(s.a) {
            beginEdit(box)   // a click inside a highlight opens its comment
        }
        needsDisplay = true
    }

    // Typing into the bubble being edited
    func insert(_ t: String) {
        guard let m = editing else { return }
        let clean = t.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n").replacingOccurrences(of: "\t", with: " ")
        let ns = m.comment as NSString
        m.comment = ns.replacingCharacters(in: NSRange(location: caret, length: 0), with: clean)
        caret += (clean as NSString).length
        typed()
    }

    func typed() {
        caretOn = true
        fit()
        needsDisplay = true
    }

    @objc func paste(_ sender: Any?) {
        if editing != nil, let t = NSPasteboard.general.string(forType: .string) { insert(t) }
    }

    override func keyDown(with e: NSEvent) {
        guard let m = editing else { super.keyDown(with: e); return }
        let ns = m.comment as NSString
        let fl = e.modifierFlags
        switch e.keyCode {
        case 36, 76:
            if fl.contains(.shift) { insert("\n") } else { endEdit() }
        case 51:
            if caret > 0 {
                var from = ns.rangeOfComposedCharacterSequence(at: caret - 1).location
                if fl.contains(.option) {
                    from = caret
                    while from > 0 && ns.character(at: from - 1) == 32 { from -= 1 }
                    while from > 0 && ns.character(at: from - 1) != 32 && ns.character(at: from - 1) != 10 { from -= 1 }
                }
                m.comment = ns.replacingCharacters(in: NSRange(location: from, length: caret - from), with: "")
                caret = from
                typed()
            }
        case 117:
            if caret < ns.length {
                let r = ns.rangeOfComposedCharacterSequence(at: caret)
                m.comment = ns.replacingCharacters(in: r, with: "")
                typed()
            }
        case 123:
            if caret > 0 { caret = ns.rangeOfComposedCharacterSequence(at: caret - 1).location }
            typed()
        case 124:
            if caret < ns.length { caret = NSMaxRange(ns.rangeOfComposedCharacterSequence(at: caret)) }
            typed()
        case 115, 126: caret = 0; typed()
        case 119, 125: caret = ns.length; typed()
        default:
            if fl.contains(.command) || fl.contains(.control) { super.keyDown(with: e); return }
            if let ch = e.characters {
                var s = ""
                for u in ch.unicodeScalars where u.value >= 32 && u.value != 127 && !(u.value >= 0xF700 && u.value <= 0xF8FF) {
                    s.unicodeScalars.append(u)
                }
                if !s.isEmpty { insert(s) }
            }
        }
    }

    override func resignFirstResponder() -> Bool {
        endEdit()
        return true
    }

    override func draw(_ dirtyRect: NSRect) {
        Theme.sunken.setFill()
        bounds.fill()
        guard let im = img, let ctx = NSGraphicsContext.current?.cgContext else { return }
        fit()
        let dest = CGRect(x: off.x, y: off.y, width: isz.width * zoom, height: isz.height * zoom)
        drawCG(im, in: dest)
        (Theme.dark ? NSColor(white: 1, alpha: 0.16) : NSColor(white: 0, alpha: 0.12)).setStroke()
        let border = NSBezierPath(rect: dest.insetBy(dx: -0.5, dy: -0.5))
        border.lineWidth = 1
        border.stroke()

        ctx.saveGState()
        ctx.translateBy(x: off.x, y: off.y)
        ctx.scaleBy(x: zoom, y: zoom)
        Ann.drawAll(im, blur, marks, live: live, isFinal: false, editing: editing, caret: caret, caretOn: caretOn, hover: hover, selected: selected)
        ctx.restoreGState()
    }
}

// MARK: - Editor window

final class EditorWindow: NSWindow {
    override var canBecomeKey: Bool { return true }
}

final class TopBar: NSView {
    override var isFlipped: Bool { return true }
    var sepX: CGFloat = 0
    var tagLeft: CGFloat = 0, tagRight: CGFloat = 0
    override func draw(_ dirtyRect: NSRect) {
        Theme.surface.setFill(); bounds.fill()
        let bf = Fonts.sans(15, medium: true)
        let bs = textSize("SnapNote", bf)
        let y = (bounds.height - bs.height) / 2
        drawLogo(CGRect(x: 16, y: (bounds.height - 24) / 2, width: 24, height: 24), ring: Theme.dark)
        let bx: CGFloat = 16 + 24 + 8
        drawText("SnapNote", at: CGPoint(x: bx, y: y), font: bf, color: Theme.ink)
        let vf = Fonts.mono(11)
        let vs = textSize("v" + appVersion, vf)
        drawText("v" + appVersion, at: CGPoint(x: bx + bs.width + 8, y: y + bs.height - vs.height - 1), font: vf, color: Theme.inkMuted)
        Theme.line.setFill()
        CGRect(x: 0, y: bounds.height - 1, width: bounds.width, height: 1).fill()
        CGRect(x: sepX, y: 16, width: 1, height: bounds.height - 32).fill()
        let tf = Fonts.sans(12)
        let ts = textSize(tagline, tf)
        if tagRight - ts.width > tagLeft {
            drawText(tagline, at: CGPoint(x: tagRight - ts.width, y: (bounds.height - ts.height) / 2), font: tf, color: Theme.inkMuted)
        }
    }
}

final class BottomBar: NSView {
    override var isFlipped: Bool { return true }
    var hintX: CGFloat = -1, hintY: CGFloat = 0
    override func draw(_ dirtyRect: NSRect) {
        Theme.surface.setFill(); bounds.fill()
        Theme.line.setFill(); CGRect(x: 0, y: 0, width: bounds.width, height: 1).fill()
        if hintX >= 0 {
            drawText("Click an arrow to bend it  \u{00B7}  Esc to discard", at: CGPoint(x: hintX, y: hintY), font: Fonts.mono(11), color: Theme.inkFaint)
        }
    }
}

final class EditorRoot: NSView {
    override var isFlipped: Bool { return true }
    var onLayout: (() -> Void)? = nil
    override func layout() { super.layout(); onLayout?() }
}

final class Editor: NSObject, NSWindowDelegate {
    let window: EditorWindow
    let img: CGImage
    let K: CGFloat
    let settings: Settings
    let canvas = CanvasView()
    let top = TopBar()
    let bottom = BottomBar()
    let root = EditorRoot()
    let noteScroll = NSScrollView()
    let note = NoteTextView()
    let burn = NSSwitch()
    let burnLabel = NSTextField(labelWithString: "Put note inside the image")
    var chipRect: LWButton! = nil, chipArrow: LWButton! = nil, btnUndo: LWButton! = nil, btnClose: LWButton! = nil
    var btnSave: LWButton! = nil, btnImg: LWButton! = nil, btnChat: LWButton! = nil
    var dots: [ColorDot] = []
    var monitor: Any? = nil
    var onClose: (() -> Void)? = nil

    init(image: CGImage, scale: CGFloat, screen: NSScreen, settings: Settings) {
        img = image
        K = scale
        self.settings = settings
        Ann.K = scale
        window = EditorWindow(contentRect: NSRect(x: 0, y: 0, width: 800, height: 600),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        super.init()
        window.title = "SnapNote"
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.backgroundColor = Theme.surface
        window.appearance = NSAppearance(named: Theme.dark ? .darkAqua : .aqua)
        window.minSize = NSSize(width: 760, height: 420)

        canvas.img = image
        canvas.penColor = settings.annotationColor
        canvas.strokeW = max(1.5, 2 * scale)
        canvas.changed = { [weak self] in
            guard let s = self else { return }
            s.btnUndo.enabled = !s.canvas.marks.isEmpty
        }

        chipRect = LWButton("Highlight", kind: .chip) { [weak self] in self?.setTool(.rect) }
        chipRect.icon = iconRect
        chipRect.pressed = true
        chipArrow = LWButton("Arrow", kind: .chip) { [weak self] in self?.setTool(.arrow) }
        chipArrow.icon = iconArrow
        btnUndo = LWButton("Undo", kind: .chip) { [weak self] in self?.canvas.undo() }
        btnUndo.icon = iconUndo
        btnUndo.enabled = false
        btnClose = LWButton("Close", kind: .ghost) { [weak self] in self?.window.performClose(nil) }
        for b in [chipRect!, chipArrow!, btnUndo!, btnClose!] { top.addSubview(b) }

        var swatches: [NSColor] = [settings.annotationColor]
        for hx in ["#0b0b0c", "#2b59c3", "#e5484d"] {
            let c = rgb(hx)
            if swatches.count < 3 && !Editor.same(c, settings.annotationColor) { swatches.append(c) }
        }
        for c in swatches {
            let d = ColorDot()
            d.swatch = c
            d.selected = dots.isEmpty
            d.action = { [weak self, weak d] in
                guard let s = self, let dd = d else { return }
                for o in s.dots { o.selected = o === dd }
                s.canvas.penColor = dd.swatch
            }
            dots.append(d)
            top.addSubview(d)
        }

        note.placeholder = "Your overall question (optional). For specific spots, draw a highlight or arrow and type in its bubble."
        note.font = Fonts.sans(13)
        note.textColor = Theme.ink
        note.backgroundColor = Theme.surface
        note.insertionPointColor = Theme.ink
        note.isRichText = false
        note.allowsUndo = true
        note.textContainerInset = NSSize(width: 6, height: 8)
        note.isVerticallyResizable = true
        note.minSize = NSSize(width: 0, height: 0)
        note.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        note.autoresizingMask = [.width]
        note.textContainer?.widthTracksTextView = true
        noteScroll.documentView = note
        noteScroll.hasVerticalScroller = true
        noteScroll.borderType = .noBorder
        noteScroll.drawsBackground = false
        noteScroll.wantsLayer = true
        noteScroll.layer?.cornerRadius = 8
        noteScroll.layer?.borderWidth = 1
        noteScroll.layer?.borderColor = Theme.lineControl.withAlphaComponent(0.6).cgColor

        burn.state = settings.noteInImage ? .on : .off
        burn.controlSize = .small
        burnLabel.font = Fonts.sans(13)
        burnLabel.textColor = Theme.ink
        btnSave = LWButton("Save", kind: .secondary) { [weak self] in self?.doSave() }
        btnImg = LWButton("Copy image only", kind: .secondary) { [weak self] in self?.doCopy(forChat: false) }
        btnChat = LWButton("Copy for chat", kind: .primary) { [weak self] in self?.doCopy(forChat: true) }
        btnChat.hint = "\u{2318}\u{21A9}"
        for v in [noteScroll, burn, burnLabel, btnSave!, btnImg!, btnChat!] as [NSView] { bottom.addSubview(v) }

        root.addSubview(canvas)
        root.addSubview(top)
        root.addSubview(bottom)
        root.onLayout = { [weak self] in self?.layoutAll() }
        window.contentView = root

        // Size the window around the image (actual size when it fits)
        let vf = screen.visibleFrame
        let iw = CGFloat(image.width) / scale, ih = CGFloat(image.height) / scale
        let chromeH: CGFloat = 48 + 152 + 28
        let z = min(1, min((vf.width * 0.92 - 32) / iw, (vf.height * 0.92 - chromeH - 32) / ih))
        let w = min(vf.width, max(760, iw * z + 34))
        let h = min(vf.height, max(chromeH + 180, ih * z + 32 + chromeH))
        window.setFrame(NSRect(x: vf.minX + (vf.width - w) / 2, y: vf.minY + (vf.height - h) / 2, width: w, height: h), display: false)

        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let s = self, e.window === s.window else { return e }
            return s.handleKey(e) ? nil : e
        }
    }

    static func same(_ a: NSColor, _ b: NSColor) -> Bool {
        guard let x = a.usingColorSpace(.sRGB), let y = b.usingColorSpace(.sRGB) else { return false }
        return abs(x.redComponent - y.redComponent) < 0.01 && abs(x.greenComponent - y.greenComponent) < 0.01 && abs(x.blueComponent - y.blueComponent) < 0.01
    }

    func show() {
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(note)
    }

    func layoutAll() {
        let W = root.bounds.width, H = root.bounds.height
        top.frame = CGRect(x: 0, y: 0, width: W, height: 48)
        bottom.frame = CGRect(x: 0, y: H - 152, width: W, height: 152)
        canvas.frame = CGRect(x: 0, y: 48, width: W, height: max(10, H - 200))

        let cy: CGFloat = 10
        var x = 16 + 24 + 8 + textSize("SnapNote", Fonts.sans(15, medium: true)).width + 8 + textSize("v" + appVersion, Fonts.mono(11)).width + 24
        chipRect.frame = CGRect(x: x, y: cy, width: chipRect.preferredWidth, height: 28); x += chipRect.frame.width + 4
        chipArrow.frame = CGRect(x: x, y: cy, width: chipArrow.preferredWidth, height: 28); x += chipArrow.frame.width + 14
        top.sepX = x - 7
        for d in dots { d.frame = CGRect(x: x, y: cy, width: 26, height: 28); x += 26 }
        x += 10
        btnUndo.frame = CGRect(x: x, y: cy, width: btnUndo.preferredWidth, height: 28)
        btnClose.frame = CGRect(x: W - 12 - btnClose.preferredWidth, y: cy, width: btnClose.preferredWidth, height: 28)
        top.tagRight = btnClose.frame.minX - 12
        top.tagLeft = btnUndo.frame.maxX + 24
        top.needsDisplay = true

        let pad: CGFloat = 16
        noteScroll.frame = CGRect(x: pad, y: pad, width: W - 2 * pad, height: 76)
        let cs = noteScroll.contentSize
        note.frame = CGRect(x: 0, y: 0, width: cs.width, height: max(note.frame.height, cs.height))
        let rowY = noteScroll.frame.maxY + 12, rowH: CGFloat = 34
        burn.sizeToFit()
        burn.frame = CGRect(x: pad, y: rowY + (rowH - burn.frame.height) / 2, width: burn.frame.width, height: burn.frame.height)
        burnLabel.sizeToFit()
        burnLabel.frame = CGRect(x: burn.frame.maxX + 8, y: rowY + (rowH - burnLabel.frame.height) / 2, width: burnLabel.frame.width, height: burnLabel.frame.height)
        btnChat.frame = CGRect(x: W - pad - btnChat.preferredWidth, y: rowY, width: btnChat.preferredWidth, height: rowH)
        btnImg.frame = CGRect(x: btnChat.frame.minX - 8 - btnImg.preferredWidth, y: rowY, width: btnImg.preferredWidth, height: rowH)
        btnSave.frame = CGRect(x: btnImg.frame.minX - 8 - btnSave.preferredWidth, y: rowY, width: btnSave.preferredWidth, height: rowH)
        let hs = textSize("Click an arrow to bend it  \u{00B7}  Esc to discard", Fonts.mono(11))
        let hx = btnSave.frame.minX - 16 - hs.width
        bottom.hintX = hx > burnLabel.frame.maxX + 16 ? hx : -1
        bottom.hintY = rowY + (rowH - hs.height) / 2
        bottom.needsDisplay = true
        canvas.needsDisplay = true
    }

    func setTool(_ t: Mark.Kind) {
        canvas.tool = t
        chipRect.pressed = t == .rect
        chipArrow.pressed = t == .arrow
    }

    // Returns true when the key was handled.
    func handleKey(_ e: NSEvent) -> Bool {
        let fl = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let key = e.charactersIgnoringModifiers?.lowercased() ?? ""
        if e.keyCode == 53 {   // Esc
            if canvas.isEditing { canvas.endEdit() } else { window.performClose(nil) }
            return true
        }
        if fl.contains(.command) {
            if e.keyCode == 36 || e.keyCode == 76 { doCopy(forChat: true); return true }
            if key == "s" && !fl.contains(.shift) { doSave(); return true }
            if key == "c" && fl.contains(.shift) { doCopy(forChat: false); return true }
            if key == "w" { window.performClose(nil); return true }
            if key == "z" && !fl.contains(.shift) {
                if window.firstResponder === note { return false }
                if !canvas.isEditing { canvas.undo() }
                return true
            }
        }
        return false
    }

    // Image with highlights, arrows and bubbles, optionally with the note as a caption strip under it.
    // If a bubble sticks out past the edge of a small capture, the image grows to make room for it.
    func render(caption: String?) -> CGImage? {
        canvas.endEdit()
        let isz = CGSize(width: img.width, height: img.height)
        var cb = Ann.content(canvas.marks, isz, isFinal: true)
        var area = CGRect(origin: .zero, size: isz)
        if cb.minX < 0 || cb.minY < 0 || cb.maxX > isz.width || cb.maxY > isz.height {
            cb = cb.insetBy(dx: -12 * K, dy: -12 * K)
            area = CGRect(x: floor(min(0, cb.minX)), y: floor(min(0, cb.minY)), width: 0, height: 0)
            area.size = CGSize(width: ceil(max(isz.width, cb.maxX)) - area.minX, height: ceil(max(isz.height, cb.maxY)) - area.minY)
        }
        var capH: CGFloat = 0
        var W = area.width
        let capFont = Fonts.sans(15 * K)
        let pad = 16 * K
        var capRect = CGRect.zero
        if let c = caption, !c.isEmpty {
            W = max(W, 480 * K)
            let ts = (c as NSString).boundingRect(with: NSSize(width: W - 2 * pad, height: 100000), options: [.usesLineFragmentOrigin], attributes: [.font: capFont], context: nil)
            capH = pad * 2 + ceil(ts.height)
            capRect = CGRect(x: pad, y: area.height + pad, width: W - 2 * pad, height: ceil(ts.height) + 4)
        }
        let pw = Int(ceil(W)), ph = Int(ceil(area.height + capH))
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pw, pixelsHigh: ph, bitsPerSample: 8,
                                         samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                         bytesPerRow: 0, bitsPerPixel: 0),
              let base = NSGraphicsContext(bitmapImageRep: rep) else { return nil }
        rep.size = NSSize(width: pw, height: ph)
        let cg = base.cgContext
        let flipped = NSGraphicsContext(cgContext: cg, flipped: true)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = flipped
        cg.translateBy(x: 0, y: CGFloat(ph))
        cg.scaleBy(x: 1, y: -1)
        cg.interpolationQuality = .high

        rgb("#ffffff").setFill(); CGRect(x: 0, y: 0, width: pw, height: ph).fill()
        rgb("#f5f5f6").setFill(); CGRect(x: 0, y: 0, width: W, height: area.height).fill()
        cg.saveGState()
        cg.translateBy(x: -area.minX, y: -area.minY)
        drawCG(img, in: CGRect(origin: .zero, size: isz))
        Ann.drawAll(img, canvas.blur, canvas.marks, live: nil, isFinal: true, editing: nil, caret: -1, caretOn: false, hover: nil, selected: nil)
        cg.restoreGState()
        if let c = caption, !c.isEmpty {
            rgb("#e8e8eb").setFill(); CGRect(x: 0, y: area.height, width: W, height: max(1, K)).fill()
            (c as NSString).draw(with: capRect, options: [.usesLineFragmentOrigin], attributes: [.font: capFont, .foregroundColor: rgb("#0b0b0c")], context: nil)
        }
        NSGraphicsContext.restoreGraphicsState()
        return rep.cgImage
    }

    // The text for the chat box: the overall note, then each bubble's comment by its number.
    func chatText(_ noteText: String) -> String {
        canvas.endEdit()
        var items: [String] = []
        for (i, m) in canvas.marks.enumerated() {
            let c = m.comment.trimmingCharacters(in: .whitespacesAndNewlines)
            if !c.isEmpty { items.append("\(i + 1). " + c.replacingOccurrences(of: "\n", with: "\n   ")) }
        }
        if items.isEmpty { return noteText }
        let head = items.count == 1 ? "About the marked spot in the screenshot:" : "About the numbered areas in the screenshot:"
        let list = head + "\n" + items.joined(separator: "\n")
        return noteText.isEmpty ? list : noteText + "\n\n" + list
    }

    func rememberSwitch() {
        let on = burn.state == .on
        if settings.noteInImage != on {
            settings.noteInImage = on
            settings.set("NoteInImage", on ? "true" : "false")
        }
    }

    func doCopy(forChat: Bool) {
        let noteOnly = note.string.trimmingCharacters(in: .whitespacesAndNewlines)
        let inImage = forChat && burn.state == .on && !noteOnly.isEmpty
        let text = inImage ? noteOnly : chatText(noteOnly)
        guard let outImg = render(caption: inImage ? noteOnly : nil) else {
            Toast.show("Could not copy", "The image could not be made. Try again.", error: true)
            return
        }
        var title = "", body = ""
        var ok = true
        if !forChat || text.isEmpty {
            ok = Clip.put(outImg, text: nil)
            title = "Screenshot copied"; body = "Paste it with \u{2318}V."
        } else if inImage {
            ok = Clip.put(outImg, text: nil)
            title = "Screenshot and note copied"; body = "Your note is inside the image. Paste it with \u{2318}V."
        } else if settings.smartPaste && Clip.put(outImg, text: nil) &&
                    SmartPaste.arm(text, delayMs: settings.smartPasteDelayMs, timeoutSec: settings.smartPasteTimeoutSec) {
            title = "Ready to paste"
            body = "Click your chat box and press \u{2318}V once. The screenshot goes in first, then your text."
        } else {
            ok = Clip.put(outImg, text: text)
            title = "Copied"
            body = settings.smartPaste
                ? "Image and text are both on the clipboard. To paste them in one go, allow SnapNote under Privacy & Security > Accessibility."
                : "Image and text are both on the clipboard. Some chat apps paste only one of them."
        }
        if !ok { Toast.show("Could not copy", "Try again.", error: true); return }
        if settings.alsoSaveOnCopy { _ = writeFiles(outImg, text) }
        rememberSwitch()
        let from = window.frame
        window.performClose(nil)
        // The screenshot folds into a paper plane and flies to the corner, where the toast appears.
        Fx.play(.plane, outImg, from: from) { Toast.show(title, body) }
    }

    func writeFiles(_ outImg: CGImage, _ text: String) -> URL? {
        let dir = settings.saveDir
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let f = DateFormatter()
            f.dateFormat = "yyyy-MM-dd HHmmss"
            let stem = dir.appendingPathComponent("SnapNote " + f.string(from: Date()))
            guard let data = Clip.png(outImg) else { return nil }
            let png = stem.appendingPathExtension("png")
            try data.write(to: png)
            if !text.isEmpty { try text.write(to: stem.appendingPathExtension("txt"), atomically: true, encoding: .utf8) }
            return png
        } catch { return nil }
    }

    // Save: the marked-up image as a PNG, plus a .txt with the note and comments.
    func doSave() {
        let noteOnly = note.string.trimmingCharacters(in: .whitespacesAndNewlines)
        let inImage = burn.state == .on && !noteOnly.isEmpty
        let text = chatText(noteOnly)
        guard let outImg = render(caption: inImage ? noteOnly : nil), let url = writeFiles(outImg, text) else {
            Toast.show("Could not save", "Check the SaveFolder line in settings.ini.", error: true)
            return
        }
        rememberSwitch()
        let from = window.frame
        window.performClose(nil)
        // The screenshot drops into a little tray, then the toast says where it went.
        let msg = url.lastPathComponent + (text.isEmpty ? "" : " and its notes") + " in " + settings.saveDir.path
        Fx.play(.tray, outImg, from: from) { Toast.show("Saved", msg) }
    }

    func windowWillClose(_ notification: Notification) {
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
        canvas.endEdit()
        onClose?()
    }
}

// MARK: - Capture overlay: frozen screen, drag a region, click a window, Enter for the whole screen

final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { return true }
    override var canBecomeMain: Bool { return true }
}

final class OverlayView: NSView {
    let shot: CGImage
    let wins: [CGRect]
    var down = CGPoint.zero
    var mouseIsDown = false, dragging = false
    var sel = CGRect.zero, hover = CGRect.zero
    var done: ((CGRect) -> Void)? = nil   // in view points; empty = cancelled
    static let hint = "Drag to select   \u{00B7}   Click a window   \u{00B7}   Return for full screen   \u{00B7}   Esc to cancel"

    init(frame: CGRect, shot: CGImage, wins: [CGRect]) {
        self.shot = shot
        self.wins = wins
        super.init(frame: frame)
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { return true }
    override var acceptsFirstResponder: Bool { return true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { return true }

    override func updateTrackingAreas() {
        for t in trackingAreas { removeTrackingArea(t) }
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeAlways, .inVisibleRect, .cursorUpdate], owner: self, userInfo: nil))
    }
    override func cursorUpdate(with event: NSEvent) { NSCursor.crosshair.set() }

    func pick(_ p: CGPoint) -> CGRect {
        for r in wins where r.contains(p) { return r }
        return bounds
    }

    var current: CGRect { return dragging ? sel : hover }

    override func mouseDown(with e: NSEvent) {
        mouseIsDown = true
        down = convert(e.locationInWindow, from: nil)
    }

    override func mouseDragged(with e: NSEvent) {
        let p = convert(e.locationInWindow, from: nil)
        if !dragging && (abs(p.x - down.x) > 3 || abs(p.y - down.y) > 3) { dragging = true }
        if dragging {
            sel = CGRect(x: min(p.x, down.x), y: min(p.y, down.y), width: abs(p.x - down.x), height: abs(p.y - down.y)).intersection(bounds)
        }
        needsDisplay = true
    }

    override func mouseMoved(with e: NSEvent) {
        hover = pick(convert(e.locationInWindow, from: nil))
        NSCursor.crosshair.set()
        needsDisplay = true
    }

    override func mouseUp(with e: NSEvent) {
        guard mouseIsDown else { return }
        mouseIsDown = false
        let r = dragging ? sel : pick(convert(e.locationInWindow, from: nil))
        done?(r)
    }

    override func rightMouseDown(with event: NSEvent) {
        if dragging { dragging = false; mouseIsDown = false; needsDisplay = true } else { done?(.zero) }
    }

    override func keyDown(with e: NSEvent) {
        if e.keyCode == 53 { done?(.zero) }
        else if (e.keyCode == 36 || e.keyCode == 76) && !dragging { done?(bounds) }
    }

    override func draw(_ dirtyRect: NSRect) {
        drawCG(shot, in: bounds)
        let cur = current
        let dim = NSBezierPath(rect: bounds)
        if !cur.isEmpty { dim.append(NSBezierPath(rect: cur)); dim.windingRule = .evenOdd }
        NSColor(srgbRed: 11 / 255, green: 11 / 255, blue: 12 / 255, alpha: 0.45).setFill()
        dim.fill()

        if !cur.isEmpty {
            // Linework selection: ink outline with a light inner line, square handles while dragging
            let outer = NSBezierPath(rect: cur.insetBy(dx: 0.5, dy: 0.5))
            NSColor(srgbRed: 11 / 255, green: 11 / 255, blue: 12 / 255, alpha: 0.8).setStroke()
            outer.lineWidth = 3; outer.stroke()
            let inner = NSBezierPath(rect: cur.insetBy(dx: 0.5, dy: 0.5))
            if !dragging { inner.setLineDash([4, 3], count: 2, phase: 0) }
            rgb("#f2f2f3").setStroke(); inner.lineWidth = 1; inner.stroke()
            if dragging {
                let hs: CGFloat = 7
                for hx in [cur.minX, cur.midX, cur.maxX] {
                    for hy in [cur.minY, cur.midY, cur.maxY] where !(hx == cur.midX && hy == cur.midY) {
                        let hr = CGRect(x: hx - hs / 2, y: hy - hs / 2, width: hs, height: hs)
                        rgb("#f2f2f3").setFill(); hr.fill()
                        rgb("#0b0b0c").setStroke(); NSBezierPath(rect: hr).stroke()
                    }
                }
            }
            // Size readout in pixels
            let sc = CGFloat(shot.width) / max(1, bounds.width)
            let label = "\(Int(round(cur.width * sc))) \u{00D7} \(Int(round(cur.height * sc)))"
            let f = Fonts.mono(11)
            let sz = textSize(label, f)
            let lw = sz.width + 12, lh: CGFloat = 22
            var ly = cur.minY - lh - 6
            if ly < 4 { ly = cur.minY + 6 }
            let lr = CGRect(x: cur.minX, y: ly, width: lw, height: lh)
            rgb("#0b0b0c").setFill()
            NSBezierPath(roundedRect: lr, xRadius: 4, yRadius: 4).fill()
            drawText(label, at: CGPoint(x: lr.minX + 6, y: lr.minY + (lh - sz.height) / 2), font: f, color: rgb("#f2f2f3"))
        }

        if !dragging {
            let f = Fonts.sans(13)
            let sz = textSize(OverlayView.hint, f)
            let w = sz.width + 32, h: CGFloat = 36
            let pill = CGRect(x: (bounds.width - w) / 2, y: 24, width: w, height: h)
            let pp = NSBezierPath(roundedRect: pill, xRadius: h / 2, yRadius: h / 2)
            Theme.surface.setFill(); pp.fill()
            Theme.line.setStroke(); pp.lineWidth = 1; pp.stroke()
            drawText(OverlayView.hint, at: CGPoint(x: pill.minX + 16, y: pill.minY + (h - sz.height) / 2), font: f, color: Theme.ink)
        }
    }
}

// MARK: - Log (snapnote.log next to SnapNote.app), for diagnosing problems on a Mac

enum Log {
    static var url: URL? = nil
    static func write(_ line: String) {
        guard let u = url else { return }
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm:ss"
        let text = f.string(from: Date()) + "  " + line + "\n"
        if let h = try? FileHandle(forWritingTo: u) {
            h.seekToEndOfFile()
            h.write(text.data(using: .utf8)!)
            h.closeFile()
        } else {
            try? text.write(to: u, atomically: true, encoding: .utf8)
        }
    }
}

// MARK: - Screen capture

enum Capture {
    // ScreenCaptureKit on macOS 14 and newer (Apple's current way); the screencapture tool before that.
    static func grab(_ screen: NSScreen, _ done: @escaping (CGImage?, String) -> Void) {
        let did = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? CGMainDisplayID()
        if #available(macOS 14.0, *) {
            SCShareableContent.getExcludingDesktopWindows(false, onScreenWindowsOnly: true) { content, error in
                guard let content = content, let display = content.displays.first(where: { $0.displayID == did }) ?? content.displays.first else {
                    let why = error?.localizedDescription ?? "no display"
                    DispatchQueue.main.async { Log.write("ScreenCaptureKit: " + why); Capture.grabTool(screen, done) }
                    return
                }
                let me = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
                let filter = SCContentFilter(display: display, excludingApplications: me, exceptingWindows: [])
                let cfg = SCStreamConfiguration()
                let scale = screen.backingScaleFactor
                cfg.width = Int(CGFloat(display.width) * scale)
                cfg.height = Int(CGFloat(display.height) * scale)
                cfg.showsCursor = false
                SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg) { img, err in
                    DispatchQueue.main.async {
                        if let im = img { done(im, "ScreenCaptureKit") }
                        else { Log.write("ScreenCaptureKit capture: " + (err?.localizedDescription ?? "no image")); Capture.grabTool(screen, done) }
                    }
                }
            }
        } else {
            grabTool(screen, done)
        }
    }

    static func grabTool(_ screen: NSScreen, _ done: @escaping (CGImage?, String) -> Void) {
        let idx = (NSScreen.screens.firstIndex(of: screen) ?? 0) + 1
        let path = NSTemporaryDirectory() + "snapnote-\(UUID().uuidString).png"
        DispatchQueue.global(qos: .userInitiated).async {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
            p.arguments = ["-x", "-D\(idx)", path]
            do { try p.run(); p.waitUntilExit() } catch {}
            var img: CGImage? = nil
            if let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil) {
                img = CGImageSourceCreateImageAtIndex(src, 0, nil)
            }
            try? FileManager.default.removeItem(atPath: path)
            DispatchQueue.main.async { done(img, "screencapture") }
        }
    }

    // Average and brightest value (0-255) of a small copy of the image.
    static func brightness(_ img: CGImage) -> (avg: Double, max: Double) {
        let w = 32, h = 18
        var px = [UInt8](repeating: 0, count: w * h * 4)
        guard let ctx = CGContext(data: &px, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return (255, 255) }
        ctx.interpolationQuality = .medium
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        var sum = 0.0, top = 0.0
        for i in 0..<(w * h) {
            let v = (Double(px[i * 4]) + Double(px[i * 4 + 1]) + Double(px[i * 4 + 2])) / 3
            sum += v
            top = Swift.max(top, v)
        }
        return (sum / Double(w * h), top)
    }
}

// MARK: - App: menu bar icon, hotkey, capture

final class AppController: NSObject, NSApplicationDelegate {
    var settings: Settings! = nil
    var status: NSStatusItem! = nil
    var hotkeyText: String? = nil
    var overlay: OverlayWindow? = nil
    var editor: Editor? = nil
    var capturing = false

    var folder: URL {
        // settings.ini lives next to SnapNote.app; fall back to Application Support if that folder is read-only.
        let dir = Bundle.main.bundleURL.deletingLastPathComponent()
        if FileManager.default.isWritableFile(atPath: dir.path) { return dir }
        let sup = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!.appendingPathComponent("SnapNote")
        try? FileManager.default.createDirectory(at: sup, withIntermediateDirectories: true)
        return sup
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        settings = Settings(url: folder.appendingPathComponent("settings.ini"))
        Log.url = folder.appendingPathComponent("snapnote.log")
        Log.write("SnapNote \(appVersion) started on macOS \(ProcessInfo.processInfo.operatingSystemVersionString); screen recording allowed: \(CGPreflightScreenCaptureAccess())")
        Theme.load(settings.theme)
        buildMainMenu()
        NSApp.setActivationPolicy(.accessory)

        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        status.button?.image = AppController.menuIcon()
        Motion.setting = settings.animations

        // Waiting bubble: while SnapNote waits for your Cmd+V the bubble on the menu bar icon breathes;
        // after the paste it shows a tick for a moment, then goes back to rest.
        for i in 0..<14 {
            let s = 0.9 + 0.26 * (0.5 - 0.5 * cos(2 * Double.pi * Double(i) / 14))
            pulseFrames.append(AppController.menuIcon(bubble: CGFloat(s), tick: false))
        }
        SmartPaste.onState = { [weak self] st in self?.pasteState(st) }
        status.button?.toolTip = "SnapNote"

        HotKey.onPress = { [weak self] in self?.startCapture() }
        var tries = [settings.hotkey]
        for fb in ["Ctrl+Shift+S", "Ctrl+Option+S", "Ctrl+Shift+X", "Ctrl+Option+A"] where fb.lowercased() != settings.hotkey.lowercased() { tries.append(fb) }
        for t in tries where HotKey.register(t) { hotkeyText = t; break }
        buildStatusMenu()

        if hotkeyText == nil {
            Toast.show("No hotkey available", settings.hotkey + " and the backups are taken. Pick another in settings.ini. You can still capture from the menu bar icon.", ms: 9000, error: true)
        } else if hotkeyText!.lowercased() != settings.hotkey.lowercased() {
            Toast.show("Using " + prettyHotkey(hotkeyText!) + " to capture", settings.hotkey + " is taken by another app, so SnapNote is using " + hotkeyText! + ". Change it in settings.ini.", ms: 8000)
        } else {
            Toast.show("SnapNote is running", "Press " + prettyHotkey(hotkeyText!) + " to capture. " + tagline, ms: 5000)
        }
        if !CGPreflightScreenCaptureAccess() { CGRequestScreenCaptureAccess() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        startCapture()   // opening SnapNote.app again starts a capture
        return false
    }

    func buildMainMenu() {
        // Needed so Cmd+C / Cmd+V / Cmd+A / Cmd+Z work in the note box.
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Close Window", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        appMenu.addItem(withTitle: "Quit SnapNote", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        let editItem = NSMenuItem()
        main.addItem(editItem)
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(NSMenuItem.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        NSApp.mainMenu = main
    }

    func buildStatusMenu() {
        let m = NSMenu()
        let cap = NSMenuItem(title: "Capture" + (hotkeyText.map { "   " + prettyHotkey($0) } ?? ""), action: #selector(captureMenu), keyEquivalent: "")
        cap.target = self
        m.addItem(cap)
        m.addItem(NSMenuItem.separator())
        for (title, sel) in [("Open saved notes", #selector(openSaved)), ("Edit settings", #selector(editSettings)), ("Open SnapNote folder", #selector(openFolder))] {
            let it = NSMenuItem(title: title, action: sel, keyEquivalent: "")
            it.target = self
            m.addItem(it)
        }
        m.addItem(NSMenuItem.separator())
        let q = NSMenuItem(title: "Quit SnapNote", action: #selector(quit), keyEquivalent: "")
        q.target = self
        m.addItem(q)
        status.menu = m
    }

    var pulseFrames: [NSImage] = []
    var pulseTimer: Timer? = nil
    var frameIx = 0

    func pasteState(_ st: Int) {
        pulseTimer?.invalidate(); pulseTimer = nil
        if st == 1 {
            status.button?.toolTip = "SnapNote: waiting for your \u{2318}V"
            if Motion.on {
                let t = Timer(timeInterval: 0.07, repeats: true) { [weak self] _ in
                    guard let s = self else { return }
                    s.frameIx = (s.frameIx + 1) % s.pulseFrames.count
                    s.status.button?.image = s.pulseFrames[s.frameIx]
                }
                RunLoop.main.add(t, forMode: .common)
                pulseTimer = t
            } else {
                status.button?.image = pulseFrames[pulseFrames.count / 2]
            }
        } else if st == 2 {
            status.button?.image = AppController.menuIcon(bubble: 1.08, tick: true)
            status.button?.toolTip = "SnapNote: pasted"
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak self] in
                guard let s = self, SmartPaste.state != 1 else { return }
                s.status.button?.image = AppController.menuIcon()
                s.status.button?.toolTip = "SnapNote"
            }
        } else {
            status.button?.image = AppController.menuIcon()
            status.button?.toolTip = "SnapNote"
        }
    }

    @objc func captureMenu() {
        // Let the menu close before the screen is captured
        DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(250)) { [weak self] in self?.startCapture() }
    }
    @objc func openSaved() {
        try? FileManager.default.createDirectory(at: settings.saveDir, withIntermediateDirectories: true)
        NSWorkspace.shared.open(settings.saveDir)
    }
    @objc func editSettings() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        p.arguments = ["-t", settings.url.path]
        try? p.run()
    }
    @objc func openFolder() { NSWorkspace.shared.open(folder) }
    @objc func quit() { SmartPaste.disarm(); NSApp.terminate(nil) }

    static func menuIcon() -> NSImage { return menuIcon(bubble: 1, tick: false) }

    // Menu bar icon with the bubble at a given size (the waiting pulse) or with a tick cut out of it (just pasted).
    static func menuIcon(bubble: CGFloat, tick: Bool) -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: true) { _ in
            NSColor.black.setStroke()
            let p = NSBezierPath()
            let a: CGFloat = 2.5, z: CGFloat = 15.5, L: CGFloat = 4.5
            p.move(to: CGPoint(x: a, y: a + L)); p.line(to: CGPoint(x: a, y: a)); p.line(to: CGPoint(x: a + L, y: a))
            p.move(to: CGPoint(x: z - L, y: a)); p.line(to: CGPoint(x: z, y: a)); p.line(to: CGPoint(x: z, y: a + L))
            p.move(to: CGPoint(x: a, y: z - L)); p.line(to: CGPoint(x: a, y: z)); p.line(to: CGPoint(x: a + L, y: z))
            p.lineWidth = 1.5; p.lineCapStyle = .round; p.lineJoinStyle = .round
            p.stroke()
            let bub = NSBezierPath(roundedRect: CGRect(x: 8.5, y: 8.5, width: 8, height: 6), xRadius: 1.6, yRadius: 1.6)
            bub.move(to: CGPoint(x: 10, y: 14.5)); bub.line(to: CGPoint(x: 10, y: 17)); bub.line(to: CGPoint(x: 12.5, y: 14.5))
            // scale the bubble about its own centre (12.5, 11.5)
            bub.transform(using: AffineTransform(m11: bubble, m12: 0, m21: 0, m22: bubble, tX: 12.5 - bubble * 12.5, tY: 11.5 - bubble * 11.5))
            NSColor.black.setFill(); bub.fill()
            if tick {
                NSGraphicsContext.current?.compositingOperation = .clear
                let t = NSBezierPath()
                t.move(to: CGPoint(x: 10.6, y: 11.6)); t.line(to: CGPoint(x: 12, y: 13)); t.line(to: CGPoint(x: 14.6, y: 10.2))
                t.lineWidth = 1.3; t.lineCapStyle = .round; t.lineJoinStyle = .round
                t.stroke()
                NSGraphicsContext.current?.compositingOperation = .sourceOver
            }
            return true
        }
        img.isTemplate = true
        return img
    }

    static func windowRects(_ screen: NSScreen) -> [CGRect] {
        guard let info = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return [] }
        let mainH = NSScreen.screens.first?.frame.height ?? screen.frame.height
        let sf = screen.frame
        let sx = sf.minX, sy = mainH - sf.maxY
        let me = ProcessInfo.processInfo.processIdentifier
        var out: [CGRect] = []
        for w in info {
            guard (w[kCGWindowLayer as String] as? NSNumber)?.intValue == 0 else { continue }
            if (w[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == me { continue }
            guard let bd = w[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: bd as CFDictionary) else { continue }
            let v = r.offsetBy(dx: -sx, dy: -sy).intersection(CGRect(origin: .zero, size: sf.size))
            if !v.isNull && v.width >= 24 && v.height >= 24 { out.append(v) }
        }
        return out
    }

    func startCapture() {
        if let e = editor {
            NSApp.activate(ignoringOtherApps: true)
            e.window.makeKeyAndOrderFront(nil)
            return
        }
        if overlay != nil || capturing { return }
        if !CGPreflightScreenCaptureAccess() {
            CGRequestScreenCaptureAccess()
            Toast.show("Allow screen recording",
                       "Turn on SnapNote under System Settings > Privacy & Security > Screen & System Audio Recording, then quit SnapNote and open it again.",
                       ms: 12000, error: true)
            return
        }
        SmartPaste.disarm()
        Toast.hide()
        capturing = true
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens[0]
        let wins = AppController.windowRects(screen)
        Capture.grab(screen) { [weak self] img, how in
            guard let s = self else { return }
            s.capturing = false
            guard let shot = img else {
                Log.write("capture failed (" + how + ")")
                Toast.show("Could not capture the screen", "Check Screen Recording permission for SnapNote in System Settings > Privacy & Security, then quit and reopen SnapNote.", ms: 12000, error: true)
                return
            }
            let lum = Capture.brightness(shot)
            Log.write("capture via \(how): \(shot.width)x\(shot.height), \(shot.bitsPerComponent) bits, brightness \(String(format: "%.1f", lum.avg))/\(String(format: "%.1f", lum.max))")
            if lum.max < 3 {
                // macOS hands back an all-black picture when Screen Recording isn't really allowed
                Toast.show("The screen came back black",
                           "macOS isn't letting SnapNote see the screen yet. In System Settings > Privacy & Security > Screen & System Audio Recording, remove SnapNote with the minus button, add SnapNote.app again with plus, then quit and reopen SnapNote.",
                           ms: 16000, error: true)
                return
            }
            s.showOverlay(shot, screen, wins)
        }
    }

    func showOverlay(_ shot: CGImage, _ screen: NSScreen, _ wins: [CGRect]) {
        let w = OverlayWindow(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        w.setFrame(screen.frame, display: false)
        w.level = .screenSaver
        w.isOpaque = true
        w.hasShadow = false
        w.isReleasedWhenClosed = false
        w.acceptsMouseMovedEvents = true
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let v = OverlayView(frame: CGRect(origin: .zero, size: screen.frame.size), shot: shot, wins: wins)
        let mouse = NSEvent.mouseLocation
        v.hover = v.pick(CGPoint(x: mouse.x - screen.frame.minX, y: screen.frame.maxY - mouse.y))
        v.done = { [weak self, weak w] r in
            w?.orderOut(nil)
            self?.overlay = nil
            if r.width >= 2 && r.height >= 2 { self?.openEditor(shot, screen, r) }
        }
        w.contentView = v
        overlay = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        w.makeFirstResponder(v)
    }

    func openEditor(_ shot: CGImage, _ screen: NSScreen, _ sel: CGRect) {
        let sc = CGFloat(shot.width) / screen.frame.width
        let px = CGRect(x: round(sel.minX * sc), y: round(sel.minY * sc), width: round(sel.width * sc), height: round(sel.height * sc))
            .intersection(CGRect(x: 0, y: 0, width: shot.width, height: shot.height))
        guard !px.isNull, let crop = shot.cropping(to: px) else { return }
        let lum = Capture.brightness(crop)
        Log.write("editor opened: \(crop.width)x\(crop.height) at scale \(sc), brightness \(String(format: "%.1f", lum.avg))")
        let ed = Editor(image: crop, scale: sc, screen: screen, settings: settings)
        ed.onClose = { [weak self] in
            self?.editor = nil
            NSApp.setActivationPolicy(.accessory)   // back to living in the menu bar only
        }
        editor = ed
        NSApp.setActivationPolicy(.regular)          // show in the Dock while the editor is open, like Screenshot
        NSApp.activate(ignoringOtherApps: true)
        ed.show()
    }
}

let application = NSApplication.shared
let controller = AppController()
application.delegate = controller
application.run()
