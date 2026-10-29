// HS SCS Monitor - backend nativo para macOS (puerto de backend.swift).
// Telemetria ATS/ETS2 -> StreamDock Ajazz. Sin dependencias externas.
// Shared memory POSIX "/SCSTelemetry" + WebSocket RFC6455 manual +
// render 72x72 con CoreGraphics/CoreText + teclas via CGEvent.
// Offsets: scs-telemetry-common.hpp REV12 (identicos a backend.cs).

import Foundation
import CoreGraphics
import CoreText
import ImageIO
import AppKit
import ApplicationServices
import Darwin

// MARK: - Offsets MMF (REV12)
let O_SDK = 0, O_PAUSED = 4
let O_GAME = 52
let O_SPEED = 948, O_RPM = 952, O_CRUISE = 988, O_FUEL = 1000, O_RANGE = 1008
let O_LIMIT = 1068, O_FCAP = 704, O_GEAR = 504, O_CONS = 1004, O_RPMMAX = 740
let O_PARK = 1566, O_MOTOR = 1567, O_BL = 1580, O_BR = 1581
let O_LO = 1583, O_HI = 1584, O_BRK = 1586, O_ENG = 1576
let O_CRZON = 1589, O_PARKING = 1582, O_HAZ = 1588, O_FWARN = 1570, O_REV = 1587, O_BEACON = 1585, O_WIPERS = 1577, O_DIFF = 1608
let O_WENG = 1036, O_WTRA = 1040, O_WCAB = 1044, O_WCHA = 1048, O_WWHE = 1052
let O_TCD = 6152, O_TCHA = 6156, O_TWHE = 6160, O_TBOD = 6164, O_JCD = 1468, O_TATT = 6080
let O_ONJOB = 4300
let O_PLANNED = 100
let O_ROUTED = 1060, O_ROUTET = 1064
let O_AIR = 992, O_BRAKETEMP = 996, O_OILTEMP = 1020, O_WATERTEMP = 1024
let O_AIRWARN = 720, O_AIREMERG = 724, O_OILWARN = 728, O_WATERWARN = 732

// MARK: - Teclado (keycodes ANSI de macOS; el juego recibe por posicion fisica)
struct KeyDef { let kc: CGKeyCode; let uni: String }
let KEYS: [String: KeyDef] = [
    "hazbtn":        KeyDef(kc: 3,  uni: "F"),  // kVK_ANSI_F
    "blinkLbtn":     KeyDef(kc: 33, uni: "["),  // kVK_ANSI_LeftBracket
    "blinkRbtn":     KeyDef(kc: 30, uni: "]"),  // kVK_ANSI_RightBracket
    "lightbtn":      KeyDef(kc: 37, uni: "L"),  // kVK_ANSI_L
    "highbtn":       KeyDef(kc: 40, uni: "K"),  // kVK_ANSI_K
    "beaconbtn":     KeyDef(kc: 31, uni: "O"),  // kVK_ANSI_O
    "wiperbtn":      KeyDef(kc: 35, uni: "P"),  // kVK_ANSI_P
    "difflock":      KeyDef(kc: 9,  uni: "V"),  // kVK_ANSI_V
    "emergencybrake":KeyDef(kc: 49, uni: " "),  // kVK_Space
    "cruise":        KeyDef(kc: 8,  uni: "C"),  // kVK_ANSI_C
]

var plugDir = ""
var logFile = ""
var wiperMode = 0
let tickLock = NSLock()
var lastHighTick: TimeInterval = 0

func Log(_ m: String) {
    let fm = DateFormatter(); fm.dateFormat = "HH:mm:ss.SSS"
    let line = "\(fm.string(from: Date())) [\(getpid())] \(m)\n"
    let url = URL(fileURLWithPath: logFile)
    if let attrs = try? FileManager.default.attributesOfItem(atPath: logFile),
       let sz = attrs[.size] as? NSNumber, sz.intValue > 200 * 1024 {
        try? FileManager.default.removeItem(at: url)
    }
    if let data = line.data(using: .utf8) {
        if let fh = FileHandle(forWritingAtPath: logFile) {
            defer { try? fh.close() }
            _ = try? fh.seekToEnd()
            try? fh.write(contentsOf: data)
        } else {
            try? data.write(to: url)
        }
    }
}

// MARK: - Snapshot
struct Snap {
    var active = false, paused = false, ats = false
    var speed: Float?, rpm: Float?, fuel: Float?, fuelCap: Float?, range: Float?
    var cons: Float?, cruise: Float?, limit: Float?, rpmMax: Float?
    var routeD: Float?, routeT: Float?
    var planned: UInt32?
    var gear: Int32?
    var park = false, motor = false, blinkL = false, blinkR = false
    var lowB = false, highB = false, brake = false, engine = false
    var cruiseOn = false, parking = false, hazard = false, fuelWarn = false
    var onJob = false, beacon = false, diffLock = false
    var wipers = 0
    var truckDmg: Float?, trailerDmg: Float?, cargoDmg: Float?
    var airPressure: Float?, oilTemp: Float?, waterTemp: Float?
    var airWarn: Float?, airEmergency: Float?, waterWarn: Float?
}

// MARK: - Shared memory POSIX
final class Shm {
    var base: UnsafeMutableRawPointer? = nil
    var fd: Int32 = -1

    func closeShm() {
        if let p = base { munmap(p, 32768) }
        if fd >= 0 { Darwin.close(fd) }
        base = nil; fd = -1
    }

    func tryOpen(checkSDK: Bool) -> Bool {
        closeShm()
        // shm_open es variadic en C y Swift la marca inaccesible; resolver via dlsym.
        typealias ShmOpenFn = @convention(c) (UnsafePointer<CChar>, Int32, Int32) -> Int32
        guard let sym = dlsym(dlopen(nil, RTLD_NOW), "shm_open") else { return false }
        let shmOpen = unsafeBitCast(sym, to: ShmOpenFn.self)
        let f = "/SCSTelemetry".withCString { shmOpen($0, O_RDONLY, 0) }
        guard f >= 0 else { return false }
        guard let p = mmap(nil, 32768, PROT_READ, MAP_SHARED, f, 0), p != MAP_FAILED else {
            Darwin.close(f); return false
        }
        if checkSDK && p.loadUnaligned(fromByteOffset: O_SDK, as: UInt8.self) == 0 {
            munmap(p, 32768); Darwin.close(f); return false
        }
        base = p; fd = f
        return true
    }

    func rb(_ o: Int) -> Int { Int(base!.loadUnaligned(fromByteOffset: o, as: UInt8.self)) }
    func ru(_ o: Int) -> UInt32 { base!.loadUnaligned(fromByteOffset: o, as: UInt32.self) }
    func ri(_ o: Int) -> Int32 { base!.loadUnaligned(fromByteOffset: o, as: Int32.self) }
    func rf(_ o: Int) -> Float { base!.loadUnaligned(fromByteOffset: o, as: Float.self) }
}

func getSnap(_ shm: Shm) -> Snap {
    var s = Snap()
    guard shm.base != nil else { return s }
    if shm.rb(O_SDK) == 0 { return s }
    s.active = true
    s.paused = shm.rb(O_PAUSED) != 0
    s.ats = shm.ru(Int(O_GAME)) == 2
    s.speed = shm.rf(O_SPEED); s.rpm = shm.rf(O_RPM); s.gear = shm.ri(O_GEAR)
    s.fuel = shm.rf(O_FUEL); s.fuelCap = shm.rf(O_FCAP); s.range = shm.rf(O_RANGE)
    s.cons = shm.rf(O_CONS); s.rpmMax = shm.rf(O_RPMMAX)
    s.cruise = shm.rf(O_CRUISE); s.cruiseOn = shm.rb(O_CRZON) != 0; s.limit = shm.rf(O_LIMIT)
    s.lowB = shm.rb(O_LO) != 0; s.highB = shm.rb(O_HI) != 0; s.blinkL = shm.rb(O_BL) != 0
    s.airPressure = shm.rf(O_AIR); s.oilTemp = shm.rf(O_OILTEMP); s.waterTemp = shm.rf(O_WATERTEMP)
    s.airWarn = shm.rf(O_AIRWARN); s.airEmergency = shm.rf(O_AIREMERG)
    s.waterWarn = shm.rf(O_WATERWARN)
    s.blinkR = shm.rb(O_BR) != 0; s.brake = shm.rb(O_BRK) != 0; s.park = shm.rb(O_PARK) != 0
    s.motor = shm.rb(O_MOTOR) != 0; s.engine = shm.rb(O_ENG) != 0; s.parking = shm.rb(O_PARKING) != 0
    s.hazard = shm.rb(O_HAZ) != 0; s.fuelWarn = shm.rb(O_FWARN) != 0
    s.beacon = shm.rb(O_BEACON) != 0; s.diffLock = shm.rb(O_DIFF) != 0
    s.wipers = shm.rb(O_WIPERS)
    s.onJob = shm.rb(O_ONJOB) != 0; s.planned = shm.ru(O_PLANNED)
    s.routeD = shm.rf(O_ROUTED); s.routeT = shm.rf(O_ROUTET)
    var mx: Float? = nil
    for v in [shm.rf(O_WENG), shm.rf(O_WTRA), shm.rf(O_WCAB), shm.rf(O_WCHA), shm.rf(O_WWHE)] {
        if mx == nil || v > mx! { mx = v }
    }
    if let m = mx { s.truckDmg = m * 100 }
    let att = shm.rb(O_TATT) != 0
    var mr: Float? = nil
    for v in [shm.rf(O_TCHA), shm.rf(O_TWHE), shm.rf(O_TBOD)] {
        if mr == nil || v > mr! { mr = v }
    }
    if att, let m = mr { s.trailerDmg = m * 100 }
    let c1 = shm.rf(O_TCD), c2 = shm.rf(O_JCD)
    if att { s.cargoDmg = max(c1, c2) * 100 }
    return s
}

// MARK: - Formato
func fmtGear(_ g: Int32?) -> String {
    guard let g = g else { return "--" }
    if g > 0 { return String(g) }
    if g == 0 { return "N" }
    return "R"
}
func dmgCol(_ p: Float?) -> CGColor? {
    guard let p = p else { return nil }
    if p <= 20 { return C("Cyan") }
    if p <= 40 { return C("Lime") }
    if p <= 60 { return C("Yellow") }
    if p <= 80 { return C("Orange") }
    return C("Red")
}
func fmtETA(_ rt: Float?) -> String {
    guard let rt = rt, rt > 0 else { return "--" }
    let v = Double(rt)
    if v >= 3600 { return "\(Int(v/3600)):\(String(format: "%02d", Int((v.truncatingRemainder(dividingBy: 3600))/60)))" }
    if v >= 90 { return "\(Int(v/60)) min" }
    return "\(Int(v)) s"
}

// MARK: - Paleta (nombres GDI+)
func rgb(_ r: Double, _ g: Double, _ b: Double, _ a: Double = 1) -> CGColor {
    CGColor(srgbRed: CGFloat(r)/255, green: CGFloat(g)/255, blue: CGFloat(b)/255, alpha: CGFloat(a))
}
let PALETTE: [String: CGColor] = [
    "Lime": rgb(0,255,0), "Cyan": rgb(0,255,255), "Orange": rgb(255,165,0),
    "Red": rgb(255,0,0), "White": rgb(255,255,255), "Yellow": rgb(255,255,0),
    "DodgerBlue": rgb(30,144,255), "Purple": rgb(128,0,128), "DeepPink": rgb(255,20,147),
    "Gray": rgb(128,128,128), "Black": rgb(0,0,0),
]
func C(_ name: String) -> CGColor { PALETTE[name] ?? rgb(255,255,255) }
let THEMES: [String: CGColor] = [
    "green": C("Lime"), "cyan": C("Cyan"), "amber": C("Orange"), "red": C("Red"),
    "white": C("White"), "yellow": C("Yellow"), "blue": C("DodgerBlue"),
    "purple": C("Purple"), "pink": C("DeepPink"),
]

// MARK: - Lienzo 72x72 (coordenadas GDI+: origen arriba-izquierda)
final class Canvas {
    let ctx: CGContext
    init?() {
        let cs = CGColorSpaceCreateDeviceRGB()
        guard let c = CGContext(data: nil, width: 72, height: 72, bitsPerComponent: 8,
                                bytesPerRow: 72 * 4, space: cs,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        c.setAllowsAntialiasing(true); c.setShouldAntialias(true)
        c.setFillColor(rgb(0,0,0)); c.fill(CGRect(x: 0, y: 0, width: 72, height: 72))
        ctx = c
    }
    var url: CGRect { CGRect(x: 0, y: 0, width: 72, height: 72) }
    func cg(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> CGRect {
        CGRect(x: x, y: 72 - y - h, width: w, height: h)
    }
    func fillRect(_ col: CGColor, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, alpha: CGFloat = 1) {
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.setFillColor(col); ctx.fill(cg(x, y, w, h)); ctx.restoreGState()
    }
    func strokeRect(_ col: CGColor, _ lw: CGFloat, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) {
        ctx.saveGState(); ctx.setStrokeColor(col); ctx.setLineWidth(lw); ctx.stroke(cg(x, y, w, h)); ctx.restoreGState()
    }
    func fillEllipse(_ col: CGColor, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, alpha: CGFloat = 1) {
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.setFillColor(col); ctx.fillEllipse(in: cg(x, y, w, h)); ctx.restoreGState()
    }
    func strokeEllipse(_ col: CGColor, _ lw: CGFloat, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) {
        ctx.saveGState(); ctx.setStrokeColor(col); ctx.setLineWidth(lw); ctx.strokeEllipse(in: cg(x, y, w, h)); ctx.restoreGState()
    }
    func line(_ col: CGColor, _ lw: CGFloat, _ x1: CGFloat, _ y1: CGFloat, _ x2: CGFloat, _ y2: CGFloat) {
        ctx.saveGState(); ctx.setStrokeColor(col); ctx.setLineWidth(lw)
        ctx.move(to: CGPoint(x: x1, y: 72 - y1)); ctx.addLine(to: CGPoint(x: x2, y: 72 - y2))
        ctx.strokePath(); ctx.restoreGState()
    }
    // Arco estilo GDI+ (grados, 0 = 3 en punto, horario en y-abajo) como polilinea fina.
    func arc(_ col: CGColor, _ lw: CGFloat, _ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ a0: Double, _ a1: Double) {
        ctx.saveGState(); ctx.setStrokeColor(col); ctx.setLineWidth(lw); ctx.setLineCap(.butt)
        let p = CGMutablePath()
        var first = true
        var a = a0
        while a <= a1 {
            let rad = a * Double.pi / 180
            let pt = CGPoint(x: cx + CGFloat(cos(rad)) * r, y: 72 - (cy + CGFloat(sin(rad)) * r))
            if first { p.move(to: pt); first = false } else { p.addLine(to: pt) }
            a += 1.0
        }
        ctx.addPath(p); ctx.strokePath(); ctx.restoreGState()
    }
    func text(_ s: String, size: CGFloat, bold: Bool, col: CGColor, x: CGFloat, yTop: CGFloat, center: Bool = true) {
        // Arial Bold usa el nombre PostScript "Arial-BoldMT"; evita CTFontCreateCopyWithSymbolicTraits.
        let name = bold ? "Arial-BoldMT" : "Arial"
        let font = CTFontCreateWithName(name as CFString, size, nil)
        let attrs: [CFString: Any] = [kCTFontAttributeName: font, kCTForegroundColorAttributeName: col]
        let line = CTLineCreateWithAttributedString(
            CFAttributedStringCreate(kCFAllocatorDefault, s as CFString, attrs as CFDictionary))
        var pos = CGPoint(x: x, y: 72 - yTop - CGFloat(CTFontGetAscent(font)))
        if center { pos.x = x - CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil)) / 2 }
        ctx.textPosition = pos
        CTLineDraw(line, ctx)
    }
    func drawImage(_ img: CGImage, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, alpha: CGFloat = 1) {
        ctx.saveGState(); ctx.setAlpha(alpha)
        ctx.translateBy(x: 0, y: 72); ctx.scaleBy(x: 1, y: -1)
        ctx.draw(img, in: CGRect(x: x, y: y, width: w, height: h))
        ctx.restoreGState()
    }
    // Copia del icono teñida de rojo con alpha=pulse (equivalente al ColorMatrix de GDI+).
    func drawTintedRed(_ img: CGImage, pulse: CGFloat) {
        drawImage(img, 0, 0, 72, 72, alpha: 1)
        if pulse > 0 {
            let cs = CGColorSpaceCreateDeviceRGB()
            guard let t = CGContext(data: nil, width: 72, height: 72, bitsPerComponent: 8,
                                    bytesPerRow: 72 * 4, space: cs,
                                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
            else { return }
            t.draw(img, in: CGRect(x: 0, y: 0, width: 72, height: 72))
            t.setBlendMode(.sourceAtop)
            t.setFillColor(rgb(255, 0, 0, Double(pulse)))
            t.fill(CGRect(x: 0, y: 0, width: 72, height: 72))
            if let tinted = t.makeImage() { drawImage(tinted, 0, 0, 72, 72, alpha: 1) }
        }
    }
    func png() -> String? {
        guard let img = ctx.makeImage() else { return nil }
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data as CFMutableData, "public.png" as CFString, 1, nil)
        else { return nil }
        CGImageDestinationAddImage(dest, img, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return "data:image/png;base64," + (data as Data).base64EncodedString()
    }
}

func pulsePhase() -> CGFloat {
    let ms = Date().timeIntervalSince1970.truncatingRemainder(dividingBy: 1) * 1000
    return CGFloat(0.25 + 0.75 * (0.5 - 0.5 * cos(2 * Double.pi * ms / 1000)))
}

// MARK: - Iconos
var imgs: [String: CGImage] = [:]
func preload() {
    let names = ["left","right","hazard","low","high","brake","park","beacon","cruise","wiper","diff","emergency-brake"]
    for n in names {
        for suf in ["", "-c"] {
            let p = plugDir + "/pk-" + n + suf + ".png"
            if FileManager.default.fileExists(atPath: p),
               let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: p) as CFURL, nil),
               let img = CGImageSourceCreateImageAtIndex(src, 0, nil) {
                imgs[n + suf] = img
            }
        }
    }
}

// MARK: - Render
func renderPack(_ name: String, _ on: Bool) -> String {
    guard let c = Canvas() else { return "" }
    if let im = imgs[on ? name + "-c" : name] { c.drawImage(im, 0, 0, 72, 72) }
    return c.png() ?? ""
}
func renderBig(_ top: String, _ label: String, _ colorName: String, _ sub: String?) -> String {
    guard let c = Canvas() else { return "" }
    c.text(top, size: 20, bold: true, col: C(colorName), x: 36, yTop: 6)
    c.text(label, size: 10, bold: true, col: C("White"), x: 36, yTop: 34)
    if let s = sub { c.text(s, size: 9, bold: false, col: C("Gray"), x: 36, yTop: 52) }
    return c.png() ?? ""
}
func renderSpeed(_ s: Snap, _ colorName: String) -> String {
    guard let c = Canvas() else { return "" }
    c.arc(C("White"), 3, 36, 38, 30, 135, 405)
    var a = 135.0
    while a <= 405 {
        let rad = a * Double.pi / 180
        c.line(C("White"), 3,
               36 + CGFloat(cos(rad)) * 30, 38 + CGFloat(sin(rad)) * 30,
               36 + CGFloat(cos(rad)) * 25, 38 + CGFloat(sin(rad)) * 25)
        a += 27
    }
    var t = "--"
    if let sp = s.speed { t = String(Int(abs((Double(sp) * 3.6).rounded()))) }
    c.text(t, size: 24, bold: true, col: C(colorName), x: 36, yTop: 19)
    c.text("km/h", size: 9, bold: true, col: C("White"), x: 36, yTop: 54)
    return c.png() ?? ""
}
func renderSpeedLimit(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    let limitKmh = s.limit == nil ? 0 : Double(s.limit!) * 3.6
    let speedKmh = s.speed == nil ? 0 : abs(Double(s.speed!) * 3.6)
    let valid = s.limit != nil && s.limit! > 0.5
    let exceeded = valid && speedKmh > limitKmh + 1.0
    let blinkOn = Int(Date().timeIntervalSince1970 / 0.25) % 2 == 0
    let ringColor: CGColor = (exceeded && blinkOn) ? C("Orange") : C("Red")
    c.text("LIMITE", size: 8, bold: true, col: C("White"), x: 36, yTop: 3)
    c.strokeEllipse(ringColor, 3, 8, 15, 56, 54)
    let value = valid ? String(Int(limitKmh.rounded())) : "--"
    c.text(value, size: 24, bold: true, col: exceeded ? C("Red") : C("White"), x: 36, yTop: 24)
    return c.png() ?? ""
}
func renderGauge(_ label: String, _ value: Float?, _ unit: String, _ warning: Bool) -> String {
    guard let c = Canvas() else { return "" }
    let color: CGColor = warning ? C("Red") : C("Lime")
    c.strokeEllipse(color, 3, 5, 5, 62, 62)
    c.text(label, size: 6, bold: true, col: C("White"), x: 36, yTop: 12)
    let v = value == nil ? "--" : String(Int(value!.rounded()))
    c.text(v, size: 22, bold: true, col: color, x: 36, yTop: 22)
    c.text(unit, size: 8, bold: true, col: C("Gray"), x: 36, yTop: 51)
    return c.png() ?? ""
}
func renderRpm(_ s: Snap) -> String {
    let t = s.rpm == nil ? "--" : String(Int(s.rpm!.rounded()))
    var zc = "Lime"
    var gearTxt = fmtGear(s.gear)
    let isR = gearTxt == "R"
    var gearCol = "White"
    if let rpm = s.rpm, let mx = s.rpmMax, mx > 0 {
        let f = Double(rpm) / Double(mx)
        if f >= 0.9 { zc = "Red"; gearTxt = "CAMBIA"; gearCol = "Red" }
        else if f >= 0.75 { zc = "Orange" }
        else if f >= 0.6 { zc = "Yellow" }
        else { zc = "Lime" }
    }
    if isR { gearCol = "Red" }
    guard let c = Canvas() else { return "" }
    c.text(t, size: 18, bold: true, col: C(zc), x: 36, yTop: 2)
    c.text("RPM", size: 9, bold: true, col: C("White"), x: 36, yTop: 28)
    c.text(gearTxt, size: gearTxt == "CAMBIA" ? 13 : 15, bold: true, col: C(gearCol), x: 36, yTop: 42)
    return c.png() ?? ""
}
func renderFuel(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    c.fillRect(C("White"), 6, 11, 18, 26)
    c.fillRect(C("White"), 9, 6, 12, 5)
    c.fillRect(C("White"), 4, 37, 22, 4)
    c.line(C("White"), 2, 24, 20, 31, 20)
    c.line(C("White"), 2, 31, 20, 31, 34)
    c.fillRect(C("White"), 28, 32, 6, 8)
    c.fillRect(C("Black"), 9, 16, 12, 9)
    let rk = s.range == nil ? "--" : String(Int(s.range!.rounded()))
    c.text(rk, size: 11, bold: true, col: C("White"), x: 16, yTop: 44)
    c.text("km", size: 8, bold: false, col: C("Gray"), x: 16, yTop: 58)
    var frac = 0.0
    if let f = s.fuel, let cap = s.fuelCap, cap > 0 { frac = Double(f) / Double(cap) }
    frac = min(max(frac, 0), 1)
    let warn = s.fuelWarn || (s.active && frac < 0.12)
    let bx: CGFloat = 50, bw: CGFloat = 10, by: CGFloat = 6, bh: CGFloat = 60
    c.fillRect(rgb(35, 35, 35), bx, by, bw, bh)
    let fh = CGFloat(bh) * CGFloat(frac)
    if fh > 0 { c.fillRect(warn ? C("Red") : C("Lime"), bx, by + bh - fh, bw, fh) }
    c.strokeRect(C("Gray"), 1, bx, by, bw, bh)
    for q in [0.25, 0.5, 0.75] {
        let dy = by + bh * CGFloat(1 - q)
        c.line(C("White"), 1, bx, dy, bx + bw, dy)
    }
    return c.png() ?? ""
}
func drawDmgIcon(_ c: Canvas, _ kind: String, _ x: CGFloat, _ y: CGFloat, _ col: CGColor) {
    if kind == "truck" {
        c.fillRect(col, x, y + 6, 9, 7)
        c.fillRect(col, x + 9, y + 9, 5, 4)
        c.fillEllipse(col, x + 2, y + 14, 4, 4)
        c.fillEllipse(col, x + 10, y + 14, 4, 4)
    } else if kind == "trailer" {
        c.fillRect(col, x, y + 3, 16, 9)
        c.fillEllipse(col, x + 3, y + 13, 4, 4)
        c.fillEllipse(col, x + 10, y + 13, 4, 4)
        c.line(col, 2, x + 16, y + 8, x + 20, y + 8)
    } else {
        c.strokeRect(col, 2, x + 2, y + 4, 14, 14)
        c.line(col, 2, x + 2, y + 4, x + 16, y + 18)
        c.line(col, 2, x + 16, y + 4, x + 2, y + 18)
    }
}
func renderDamage(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    let kinds = ["truck", "trailer", "cargo"]
    let vals: [Float?] = [s.truckDmg, s.trailerDmg, s.cargoDmg]
    var y: CGFloat = 2
    for i in 0..<3 {
        drawDmgIcon(c, kinds[i], 3, y, C("White"))
        if let v = vals[i] {
            c.text("\(Int(v.rounded()))%", size: 15, bold: true, col: dmgCol(v) ?? C("White"), x: 48, yTop: y + 1)
        } else {
            c.text("--", size: 15, bold: true, col: rgb(90, 90, 90), x: 48, yTop: y + 1)
        }
        y += 23
    }
    return c.png() ?? ""
}
func renderLights(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    let rows: [(String, Bool)] = [
        ("LO", s.lowB), ("HI", s.highB), ("PK", s.parking),
        ("L", s.blinkL), ("R", s.blinkR), ("BRK", s.brake),
        ("MT", s.motor), ("ENG", s.engine), ("CRZ", s.cruiseOn),
    ]
    var y: CGFloat = 4
    for r in 0..<3 {
        var x: CGFloat = 4
        for col in 0..<3 {
            let (label, on) = rows[r * 3 + col]
            c.text(label, size: 10, bold: true, col: on ? C("Lime") : rgb(70, 70, 70), x: x + 11, yTop: y)
            x += 23
        }
        y += 22
    }
    return c.png() ?? ""
}
func renderCruise(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    if let icon = imgs["cruise"] { c.drawImage(icon, 0, 0, 72, 72, alpha: 0.1) }
    let setKmh = s.cruise == nil ? 0 : Double(s.cruise!) * 3.6
    let actualKmh = s.speed == nil ? 0 : Double(s.speed!) * 3.6
    let hasSetSpeed = s.cruise != nil && s.cruise! > 0.5
    let active = s.cruiseOn && hasSetSpeed
    let inTolerance = active && s.speed != nil && abs(actualKmh - setKmh) <= 5.0
    let speedText = hasSetSpeed ? String(Int(setKmh.rounded())) : "--"
    c.fillRect(rgb(0, 0, 0, 145.0/255.0), 4, 5, 64, 62)
    c.text(speedText, size: 21, bold: true, col: C("Lime"), x: 36, yTop: 8)
    c.text("CRUCERO km/h", size: 8, bold: true, col: C("White"), x: 36, yTop: 34)
    let statusText = !active ? "OFF" : (s.speed == nil ? "--" : (inTolerance ? "OK ±5" : "FUERA ±5"))
    c.text(statusText, size: 13, bold: true, col: (!active || inTolerance) ? (active ? C("Lime") : C("Red")) : C("Red"), x: 36, yTop: 48)
    return c.png() ?? ""
}
func renderCruiseButton(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    if let icon = imgs["cruise"] { c.drawImage(icon, 0, 0, 72, 72, alpha: 0.20) }
    let value = (s.cruise != nil && s.cruise! > 0.5) ? String(Int((Double(s.cruise!) * 3.6).rounded())) : "--"
    c.fillRect(rgb(0, 0, 0, 90.0/255.0), 4, 7, 64, 58)
    c.text(value, size: 27, bold: true, col: C("Lime"), x: 36, yTop: 10)
    c.text("CRUCERO", size: 8, bold: true, col: C("White"), x: 36, yTop: 47)
    if s.cruiseOn {
        c.fillEllipse(rgb(0, 255, 0, Double(pulsePhase() * 220) / 255.0), 56, 3, 12, 12)
    }
    return c.png() ?? ""
}
func renderRoute(_ s: Snap) -> String {
    guard s.onJob, let planned = s.planned, let routeD = s.routeD, planned > 0,
          let c = Canvas()
    else { return renderBig("--", "SIN VIAJE", "Gray", nil) }
    let remainingKm = Double(routeD) / 1000.0
    let travelledKm = max(0, Double(planned) - remainingKm)
    let eta = fmtETA(s.routeT)
    c.text("RECORRIDA", size: 8, bold: false, col: C("Gray"), x: 36, yTop: 0)
    c.text("\(Int(travelledKm.rounded())) km", size: 14, bold: true, col: C("Lime"), x: 36, yTop: 11)
    c.text("FALTAN \(eta)", size: 8, bold: false, col: C("Gray"), x: 36, yTop: 33)
    c.text("\(Int(remainingKm.rounded())) km", size: 14, bold: true, col: C("Cyan"), x: 36, yTop: 45)
    return c.png() ?? ""
}
func renderHazBtn(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    let lit = s.hazard && (s.blinkL || s.blinkR)
    if let l = imgs[lit ? "left-c" : "left"], let r = imgs[lit ? "right-c" : "right"] {
        c.drawImage(l, 2, 12, 32, 48)
        c.drawImage(r, 38, 12, 32, 48)
    }
    return c.png() ?? ""
}
func renderBeacon(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    if let b = imgs["beacon"] { c.drawImage(b, 0, 0, 72, 72) }
    if s.beacon, let cc = imgs["beacon-c"] { c.drawImage(cc, 0, 0, 72, 72, alpha: pulsePhase()) }
    return c.png() ?? ""
}
func renderWipers(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    var mode = wiperMode
    if s.wipers == 0 { mode = 0 }
    else if mode == 0 { mode = 1 }
    let iconName = mode > 0 ? "wiper-c" : "wiper"
    if let icon = imgs[iconName] { c.drawImage(icon, 0, 0, 44, 44) }
    if mode > 0 {
        let state = mode == 1 ? "A" : String(mode)
        c.text(state, size: 24, bold: true, col: C("White"), x: 45, yTop: 20, center: false)
    }
    return c.png() ?? ""
}
func renderDiffLock(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    let key = s.diffLock ? "diff-c" : "diff"
    guard let icon = imgs[key] ?? imgs["diff"] else { return c.png() ?? "" }
    if s.diffLock, imgs[key] != nil {
        let phase = Date().timeIntervalSince1970.truncatingRemainder(dividingBy: 1) * 1000
        let alpha = CGFloat(0.2 + 0.8 * (0.5 - 0.5 * cos(2 * Double.pi * phase / 1000)))
        c.drawImage(icon, 0, 0, 72, 72, alpha: alpha)
        c.fillEllipse(rgb(255, 150, 0, Double(alpha * 210) / 255.0), 56, 4, 12, 12)
    } else {
        c.drawImage(icon, 0, 0, 72, 72)
    }
    return c.png() ?? ""
}
func renderEmergencyBrake(_ s: Snap) -> String {
    guard let c = Canvas() else { return "" }
    if let icon = imgs["emergency-brake"] {
        let pulse: CGFloat = s.park
            ? CGFloat(0.5 - 0.5 * cos(2 * Double.pi * Date().timeIntervalSince1970.truncatingRemainder(dividingBy: 1)))
            : 0
        c.drawTintedRed(icon, pulse: pulse)
    }
    return c.png() ?? ""
}

func renderKey(_ metric: String, _ s: Snap, _ colorName: String) -> String {
    if !s.active { return renderBig("--", "SCS", colorName, "sin juego") }
    switch metric {
    case "speed": return renderSpeed(s, colorName)
    case "gear": return renderBig(fmtGear(s.gear), "MARCHA", colorName, nil)
    case "rpm": return renderRpm(s)
    case "fuel": return renderFuel(s)
    case "range":
        let t = s.range == nil ? "--" : String(Int(s.range!.rounded()))
        return renderBig(t, "AUTONOMIA", colorName, "km")
    case "cons":
        let t = s.cons == nil ? "--" : String(format: "%.1f", Double(s.cons!) * 100)
        return renderBig(t, "L/100KM", colorName, "consumo")
    case "cruise": return renderCruiseButton(s)
    case "cruiseSpeed": return renderCruise(s)
    case "limit": return renderSpeedLimit(s)
    case "watertemp":
        let warn = s.waterWarn != nil && s.waterTemp != nil && s.waterTemp! >= s.waterWarn!
        return renderGauge("MOTOR", s.waterTemp, "°C", warn)
    case "oiltemp":
        return renderGauge("ACEITE", s.oilTemp, "°C", s.oilTemp != nil && s.oilTemp! >= 130.0)
    case "airpressure":
        var warn = false
        if let ap = s.airPressure {
            if let ae = s.airEmergency, ap <= ae { warn = true }
            if let aw = s.airWarn, ap <= aw { warn = true }
        }
        return renderGauge("AIRE", s.airPressure, "bar", warn)
    case "lowB": return renderPack("low", s.lowB)
    case "highB": return renderPack("high", highShown(s))
    case "blinkL", "blinkLbtn": return renderPack("left", s.blinkL)
    case "blinkRbtn": return renderPack("right", s.blinkR)
    case "lightbtn":
        if s.lowB { return renderPack("low", true) }
        if s.parking { return renderPack("park", true) }
        return renderPack("low", false)
    case "highbtn": return renderPack("high", highShown(s))
    case "beaconbtn": return renderBeacon(s)
    case "wiperbtn": return renderWipers(s)
    case "difflock": return renderDiffLock(s)
    case "emergencybrake": return renderEmergencyBrake(s)
    case "blinkR": return renderPack("right", s.blinkR)
    case "hazard": return renderPack("hazard", s.hazard)
    case "hazbtn": return renderHazBtn(s)
    case "brake": return renderPack("brake", s.brake)
    case "park": return renderPack("park", s.parking)
    case "turn":
        if s.hazard { return renderPack("hazard", true) }
        if s.blinkL { return renderPack("left", true) }
        if s.blinkR { return renderPack("right", true) }
        return renderPack("hazard", false)
    case "beams":
        if highShown(s) { return renderPack("high", true) }
        if s.lowB { return renderPack("low", true) }
        if s.parking { return renderPack("park", true) }
        return renderPack("low", false)
    case "auto":
        if s.hazard { return renderPack("hazard", true) }
        if s.blinkL { return renderPack("left", true) }
        if s.blinkR { return renderPack("right", true) }
        if s.brake { return renderPack("brake", true) }
        if highShown(s) { return renderPack("high", true) }
        if s.lowB { return renderPack("low", true) }
        if s.parking || s.park { return renderPack("park", true) }
        return renderPack("low", false)
    case "lights": return renderLights(s)
    case "damage": return renderDamage(s)
    case "route": return renderRoute(s)
    default: return renderBig("--", "SCS", colorName, nil)
    }
}

// MARK: - Teclas (CGEvent)
func setUnicode(_ ev: CGEvent, _ s: String) {
    var uni = Array(s.utf16)
    ev.keyboardSetUnicodeString(stringLength: uni.count, unicodeString: &uni)
}
func postKey(_ kc: CGKeyCode, _ uni: String) {
    guard let d = CGEvent(keyboardEventSource: nil, virtualKey: kc, keyDown: true),
          let u = CGEvent(keyboardEventSource: nil, virtualKey: kc, keyDown: false)
    else { Log("tecla: no se pudo crear CGEvent"); return }
    setUnicode(d, uni)
    setUnicode(u, uni)
    let fg = NSWorkspace.shared.frontmostApplication?.localizedName ?? "?"
    d.post(tap: .cghidEventTap)
    usleep(6000)
    u.post(tap: .cghidEventTap)
    Log("tecla \(uni) (keyCode \(kc)) -> foco:\(fg)")
}

// MARK: - WebSocket RFC6455
final class Ws {
    var fd: Int32 = -1

    init(host: String, port: Int) throws {
        fd = socket(AF_INET, SOCK_STREAM, 0)
        guard fd >= 0 else { throw "socket" }
        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = UInt16(port).bigEndian
        addr.sin_addr = in_addr(s_addr: inet_addr(host))
        let rc = withUnsafePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                connect(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard rc == 0 else { throw "connect" }
    }

    func readN(_ n: Int) throws -> [UInt8] {
        var out = [UInt8](repeating: 0, count: n)
        var got = 0
        while got < n {
            let r = out.withUnsafeMutableBytes { raw -> Int in
                read(fd, raw.baseAddress! + got, n - got)
            }
            if r <= 0 { throw "conexion cerrada" }
            got += r
        }
        return out
    }
    func writeAll(_ b: [UInt8]) {
        b.withUnsafeBytes { raw in
            var i = 0
            while i < raw.count {
                let r = write(fd, raw.baseAddress! + i, raw.count - i)
                if r <= 0 { return }
                i += r
            }
        }
    }
    func handshake(_ path: String) throws {
        var rk = [UInt8](repeating: 0, count: 16)
        _ = SecRandomCopyBytes(kSecRandomDefault, 16, &rk)
        let key = Data(rk).base64EncodedString()
        let req = "GET \(path) HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: \(key)\r\nSec-WebSocket-Version: 13\r\n\r\n"
        writeAll([UInt8](req.utf8))
        var resp = ""
        while !resp.contains("\r\n\r\n") {
            var b: UInt8 = 0
            let r = read(fd, &b, 1)
            if r <= 0 { throw "handshake roto" }
            resp.append(Character(UnicodeScalar(b)))
        }
        if !resp.contains(" 101 ") { throw "handshake sin 101" }
    }
    func pollReadable(_ ms: Int) -> Bool {
        var pfd = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
        return poll(&pfd, 1, Int32(ms)) > 0
    }
    func sendRaw(_ opcode: Int, _ payload: [UInt8]) {
        var m: [UInt8] = [UInt8](arrayLiteral: UInt8(0x80 | opcode))
        var mask = [UInt8](repeating: 0, count: 4)
        _ = SecRandomCopyBytes(kSecRandomDefault, 4, &mask)
        let len = payload.count
        if len < 126 { m.append(UInt8(0x80 | len)) }
        else if len < 65536 {
            m.append(0x80 | 126); m.append(UInt8(len >> 8)); m.append(UInt8(len & 0xFF))
        } else {
            m.append(UInt8(0x80 | 127))
            for i in (0..<8).reversed() { m.append(UInt8((len >> (8 * i)) & 0xFF)) }
        }
        m.append(contentsOf: mask)
        for (i, b) in payload.enumerated() { m.append(b ^ mask[i % 4]) }
        writeAll(m)
    }
    func send(_ text: String) { sendRaw(0x1, [UInt8](text.utf8)) }

    // Devuelve (mensaje de texto, opcode). Timeout -> (nil, -1).
    func recv(_ timeoutMs: Int) throws -> (String?, Int) {
        if !pollReadable(timeoutMs) { return (nil, -1) }
        var opcode = -1
        let h = try readN(2)
        let fin = (h[0] & 0x80) != 0
        opcode = Int(h[0] & 0x0F)
        let masked = (h[1] & 0x80) != 0
        var len = Int64(h[1] & 0x7F)
        if len == 126 { let e = try readN(2); len = Int64(e[0]) << 8 | Int64(e[1]) }
        else if len == 127 {
            let e = try readN(8); len = 0
            for i in 0..<8 { len = (len << 8) | Int64(e[i]) }
        }
        var mask = [UInt8](repeating: 0, count: 4)
        if masked { mask = try readN(4) }
        var pay = len > 0 ? try readN(Int(len)) : []
        if masked { for i in 0..<pay.count { pay[i] ^= mask[i % 4] } }
        if opcode == 0x8 { throw "close del host" }
        if opcode == 0x9 { sendRaw(0xA, pay); return try recv(0) }
        if opcode == 0xA { return try recv(0) }
        if !fin {
            var acc = pay
            while true {
                let h2 = try readN(2)
                let fin2 = (h2[0] & 0x80) != 0
                let op2 = Int(h2[0] & 0x0F)
                let m2 = (h2[1] & 0x80) != 0
                var l2 = Int(h2[1] & 0x7F)
                if l2 == 126 { let e = try readN(2); l2 = Int(e[0]) << 8 | Int(e[1]) }
                else if l2 == 127 {
                    let e = try readN(8); var v: Int64 = 0
                    for i in 0..<8 { v = (v << 8) | Int64(e[i]) }
                    l2 = Int(v)
                }
                var mk = [UInt8](repeating: 0, count: 4)
                if m2 { mk = try readN(4) }
                var p2 = l2 > 0 ? try readN(l2) : []
                if m2 { for i in 0..<p2.count { p2[i] ^= mk[i % 4] } }
                if op2 == 0x8 { throw "close del host" }
                if op2 == 0x0 {
                    acc.append(contentsOf: p2)
                    if fin2 { break }
                }
            }
            return (String(bytes: acc, encoding: .utf8) ?? "", opcode)
        }
        return (String(bytes: pay, encoding: .utf8) ?? "", opcode)
    }
    func closeWs() { if fd >= 0 { Darwin.close(fd); fd = -1 } }
}
extension String: @retroactive Error {}

// MARK: - JSON aux (regex como el original)
func rxFirst(_ text: String, _ pattern: String) -> String? {
    guard let re = try? NSRegularExpression(pattern: pattern) else { return nil }
    let r = NSRange(text.startIndex..., in: text)
    guard let m = re.firstMatch(in: text, range: r), let rr = Range(m.range(at: 1), in: text) else { return nil }
    return String(text[rr])
}
func getS(_ json: String, _ key: String, _ def: String?) -> String? {
    rxFirst(json, "\"\(key)\"\\s*:\\s*\"([^\"]*)\"") ?? def
}
func getN(_ json: String, _ key: String, _ def: String?) -> String? {
    rxFirst(json, "\"\(key)\"\\s*:\\s*([0-9.\\-]+)") ?? def
}

// MARK: - FlashWatch (destello de largas)
func highShown(_ s: Snap) -> Bool {
    if s.highB { return true }
    tickLock.lock(); let t = lastHighTick; tickLock.unlock()
    return (Date().timeIntervalSince1970 - t) < 1.5
}
func flashWatchLoop() {
    let shm = Shm()
    while true {
        if shm.tryOpen(checkSDK: false) {
            var bad = 0
            while true {
                if shm.base == nil { break }
                let sdk = shm.rb(O_SDK)
                if sdk == 0 { usleep(100_000); continue }
                let v = shm.rb(O_HI)
                if v == 1 {
                    tickLock.lock(); lastHighTick = Date().timeIntervalSince1970; tickLock.unlock()
                }
                if bad > 40 { break }
                bad = 0
                usleep(15_000)
            }
            shm.closeShm()
        }
        usleep(1_000_000)
    }
}

// MARK: - Diagnostico
func runRenderTest() -> Int32 {
    preload()
    var s = Snap()
    s.active = true; s.paused = false; s.ats = true
    s.speed = 22.2; s.rpm = 1450; s.gear = 10
    s.fuel = 320; s.fuelCap = 600; s.range = 740
    s.cons = 0.00028; s.cruise = 20.8; s.limit = 27.7
    s.rpmMax = 2100; s.cruiseOn = true
    s.lowB = true; s.highB = false; s.blinkL = true; s.blinkR = false
    s.brake = false; s.parking = false; s.park = false; s.motor = false
    s.engine = true; s.hazard = false; s.fuelWarn = false; s.beacon = true
    s.diffLock = true; s.wipers = 1; s.onJob = true; s.planned = 1250
    s.routeD = 430000; s.routeT = 3600
    s.truckDmg = 12; s.trailerDmg = 45; s.cargoDmg = 80
    s.airPressure = 7.2; s.oilTemp = 88; s.waterTemp = 92
    s.airWarn = 6.5; s.airEmergency = 5.5; s.waterWarn = 105
    let dir = NSTemporaryDirectory() + "scsmon-test"
    try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    let metrics = ["speed","gear","rpm","fuel","range","cons","cruise","cruiseSpeed","limit",
                   "watertemp","oiltemp","airpressure","blinkL","blinkR","hazard","lowB","highB",
                   "brake","park","lights","auto","turn","beams","damage","route","hazbtn",
                   "blinkLbtn","blinkRbtn","lightbtn","highbtn","beaconbtn","wiperbtn","difflock","emergencybrake"]
    for m in metrics {
        let img = renderKey(m, s, "Lime")
        if let b64 = img.split(separator: ",").last,
           let data = Data(base64Encoded: String(b64)) {
            try? data.write(to: URL(fileURLWithPath: dir + "/" + m + ".png"))
        } else {
            print("FALLO render \(m)")
        }
    }
    print("PNGs ficticios en \(dir)")
    return 0
}
func runWatchHigh() -> Int32 {
    let shm = Shm()
    if !shm.tryOpen(checkSDK: false) {
        print("SIN JUEGO (sin shm /SCSTelemetry)")
        return 0
    }
    print("Muestreando 60 s: haz rafagas (palanca largas) y observa HI 0->1.")
    print("t(s) sdk paused LO HI | HighShown(latch 1.5s)")
    let t0 = Date()
    var lastHi = -1
    var latch: TimeInterval = 0
    while Date().timeIntervalSince(t0) < 60 {
        let sdk = shm.rb(O_SDK)
        let pau = shm.rb(O_PAUSED)
        let lo = shm.rb(O_LO)
        let hi = shm.rb(O_HI)
        if hi == 1 && sdk == 1 { latch = Date().timeIntervalSince1970 }
        let shown = hi == 1 || (Date().timeIntervalSince1970 - latch) < 1.5
        if hi != lastHi {
            print(String(format: "%5.1f sdk=%d pau=%d LO=%d HI=%d shown=%d <-- CAMBIO",
                         Date().timeIntervalSince(t0), sdk, pau, lo, hi, shown ? 1 : 0))
            lastHi = hi
        }
        usleep(15_000)
    }
    print("Fin. Si HI nunca paso a 1, el juego no publica la rafaga en 1584 (revisar tecla/binding).")
    return 0
}
func runTest() -> Int32 {
    preload()
    let shm = Shm()
    if !shm.tryOpen(checkSDK: true) {
        print("SIN JUEGO")
        return 0
    }
    let s = getSnap(shm)
    if !s.active { print("SIN JUEGO"); return 0 }
    print("Juego: \(s.ats ? "ATS" : "ETS2") | Pausado: \(s.paused)")
    let kmh = s.speed == nil ? 0 : Double(s.speed!) * 3.6
    print("Velocidad: \(Int(kmh.rounded())) km/h | Marcha: \(fmtGear(s.gear)) | RPM: \(s.rpm == nil ? "--" : String(Int(s.rpm!.rounded())))")
    print("Fuel: \(s.fuel ?? -1) L | Rango: \(s.range ?? -1) km | Daños: \(s.truckDmg ?? -1)/\(s.trailerDmg ?? -1)/\(s.cargoDmg ?? -1)")
    let dir = NSTemporaryDirectory() + "scsmon-test"
    try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    let metrics = ["speed","gear","rpm","fuel","range","cons","cruise","cruiseSpeed","limit",
                   "watertemp","oiltemp","airpressure","blinkL","blinkR","hazard","lowB","highB",
                   "brake","park","lights","auto","turn","beams","damage","route","hazbtn",
                   "blinkLbtn","blinkRbtn","lightbtn","highbtn","beaconbtn","wiperbtn","difflock","emergencybrake"]
    for m in metrics {
        let img = renderKey(m, s, "Lime")
        if let b64 = img.split(separator: ",").last,
           let data = Data(base64Encoded: String(b64)) {
            try? data.write(to: URL(fileURLWithPath: dir + "/" + m + ".png"))
        }
    }
    print("PNGs en \(dir)")
    return 0
}

// MARK: - Contexto de tecla
final class Ctx {
    var metric = "speed", theme = "green"
    var interval = 0.15
    var visible = false, titled = false
    var last: String? = nil
}

// MARK: - Main
func scsMain() -> Int32 {
    var test = false, watchHigh = false, renderTest = false
    var port = 0
    var uuid: String? = nil
    var regEv = "registerPlugin"
    let args = CommandLine.arguments
    var i = 1
    while i < args.count {
        let a = args[i]
        if a == "-Test" || a == "--test" { test = true }
        else if a == "-WatchHigh" || a == "--watch-high" { watchHigh = true }
        else if a == "-RenderTest" || a == "--render-test" { renderTest = true }
        else if a == "-port", i + 1 < args.count { i += 1; port = Int(args[i]) ?? 0 }
        else if a == "-pluginUUID", i + 1 < args.count { i += 1; uuid = args[i] }
        else if a == "-registerEvent", i + 1 < args.count { i += 1; regEv = args[i] }
        i += 1
    }
    plugDir = URL(fileURLWithPath: args[0]).deletingLastPathComponent().path
    logFile = plugDir + "/backend.log"
    if watchHigh { return runWatchHigh() }
    if renderTest { return runRenderTest() }
    if test { return runTest() }
    guard port > 0, uuid != nil else { return 1 }
    Log("=== inicio (macOS) ===")
    if !AXIsProcessTrusted() {
        Log("AVISO: sin permiso de Accessibility en el proceso responsable; las teclas pueden no llegar al juego")
    }
    preload()
    Thread.detachNewThread { flashWatchLoop() }
    let ws: Ws
    do {
        ws = try Ws(host: "127.0.0.1", port: port)
        try ws.handshake("/")
    } catch {
        Log("MUERTE-conexion: \(error)")
        return 0
    }
    ws.send("{\"event\":\"\(regEv)\",\"uuid\":\"\(uuid!)\"}")
    Log("registrado")
    let shm = Shm()
    var nextMapRetry: TimeInterval = 0
    var ctxs: [String: Ctx] = [:]
    let t0 = Date()
    func ms() -> Double { Date().timeIntervalSince(t0) * 1000 }
    while true {
        let loopT0 = ms()
        // drenar mensajes (50 ms)
        let deadline = loopT0 + 50
        while ms() < deadline {
            let left = max(1, Int(deadline - ms()))
            var msg: String? = nil
            do { msg = try ws.recv(left).0 }
            catch { Log("MUERTE-socket: \(error)"); return 0 }
            guard let msg = msg else { break }
            let ev = getS(msg, "event", "") ?? ""
            let context = getS(msg, "context", "") ?? ""
            let action = getS(msg, "action", "") ?? ""
            Log("ev \(ev)")
            if ev == "willAppear" {
                let parts = action.split(separator: ".")
                var b = parts.isEmpty ? "monitor" : String(parts.last!)
                if b == "" { b = "monitor" }
                let c = Ctx()
                c.visible = true
                c.metric = b
                var msetBody: String? = nil
                if let r = rxFirst(msg, "\"settings\"\\s*:\\s*\\{([^\\}]*)\\}") { msetBody = r }
                let m2 = msetBody.flatMap { getS($0, "metric", nil) }
                let th2 = msetBody.flatMap { getS($0, "theme", nil) }
                let iv2 = msetBody.flatMap { getN($0, "interval", nil) }
                if b == "monitor", let m2 = m2 { c.metric = m2 }
                if let th2 = th2 { c.theme = th2 }
                if let iv2 = iv2, let d = Double(iv2) { c.interval = d }
                ctxs[context] = c
            } else if ev == "willDisappear" {
                ctxs[context]?.visible = false
            } else if ev == "keyDown" {
                if context != "", let c = ctxs[context] {
                    if let kd = KEYS[c.metric] {
                        if c.metric == "wiperbtn" { wiperMode = (wiperMode + 1) % 4; Log("limpiaparabrisas modo=\(wiperMode)") }
                        postKey(kd.kc, kd.uni)
                    }
                }
            } else if ev == "didReceiveSettings" || ev == "didReceiveGlobalSettings" {
                if context != "", let c = ctxs[context] {
                    if let msetBody = rxFirst(msg, "\"settings\"\\s*:\\s*\\{([^\\}]*)\\}") {
                        if let m2 = getS(msetBody, "metric", nil) { c.metric = m2 }
                        if let th2 = getS(msetBody, "theme", nil) { c.theme = th2 }
                        if let iv2 = getN(msetBody, "interval", nil), let d = Double(iv2) { c.interval = d }
                    }
                }
            }
        }
        // refresco
        var s = shm.base != nil ? getSnap(shm) : Snap()
        if !s.active {
            if shm.base != nil {
                shm.closeShm()
                Log("telemetria desconectada; aguardando juego")
            }
            if ms() >= nextMapRetry {
                if shm.tryOpen(checkSDK: true) { Log("mapa /SCSTelemetry conectado") }
                nextMapRetry = ms() + 1000
            }
            s = getSnap(shm)
        }
        for (context, c) in ctxs {
            if !c.visible { continue }
            let img: String
            do {
                let th = THEMES[c.theme] != nil ? c.theme : "green"
                img = renderKey(c.metric, s, th)
            }
            if img != c.last {
                ws.send("{\"event\":\"setImage\",\"context\":\"\(context)\",\"payload\":{\"image\":\"\(img)\",\"target\":0}}")
                c.last = img
            }
            if !c.titled {
                ws.send("{\"event\":\"setTitle\",\"context\":\"\(context)\",\"payload\":{\"title\":\"\",\"target\":0}}")
                c.titled = true
            }
        }
        var wait = 2.0
        var any = false
        for (_, c) in ctxs where c.visible {
            any = true
            if c.interval < wait { wait = c.interval }
        }
        if !any { wait = 2 }
        if wait < 0.1 { wait = 0.1 }
        let rest = Int(wait * 1000 - (ms() - loopT0))
        if rest > 0 { usleep(useconds_t(rest * 1000)) }
    }
}

@main struct SCSMon {
    static func main() {
        exit(scsMain())
    }
}
