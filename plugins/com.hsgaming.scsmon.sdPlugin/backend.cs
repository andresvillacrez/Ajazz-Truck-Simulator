// HS SCS Monitor - backend nativo (C#). Telemetria ATS/ETS2 -> Ajazz.
// Sin PowerShell, sin dependencias: TCP crudo + WebSocket RFC6455 manual,
// MemoryMappedFile, System.Drawing. Offsets: scs-telemetry-common.hpp REV12.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;

class SCSMon {
    // ---- offsets MMF ----
    const int O_SDK = 0, O_PAUSED = 4;
    static readonly uint O_GAME = 52;
    const int O_SPEED = 948, O_RPM = 952, O_CRUISE = 988, O_FUEL = 1000, O_RANGE = 1008;
    const int O_LIMIT = 1068, O_FCAP = 704, O_GEAR = 504, O_CONS = 1004, O_RPMMAX = 740;
    const int O_PARK = 1566, O_MOTOR = 1567, O_BL = 1580, O_BR = 1581;
    const int O_LO = 1583, O_HI = 1584, O_BRK = 1586, O_ENG = 1576;
    const int O_CRZON = 1589, O_PARKING = 1582, O_HAZ = 1588, O_FWARN = 1570, O_REV = 1587, O_BEACON = 1585, O_WIPERS = 1577, O_DIFF = 1608;
    const int O_WENG = 1036, O_WTRA = 1040, O_WCAB = 1044, O_WCHA = 1048, O_WWHE = 1052;
    const int O_TCD = 6152, O_TCHA = 6156, O_TWHE = 6160, O_TBOD = 6164, O_JCD = 1468, O_TATT = 6080;
    const int O_ONJOB = 4300;
    static readonly uint O_PLANNED = 100;
    const int O_ROUTED = 1060, O_ROUTET = 1064;
    const int O_AIR = 992, O_BRAKETEMP = 996, O_OILTEMP = 1020, O_WATERTEMP = 1024;
    const int O_AIRWARN = 720, O_AIREMERG = 724, O_OILWARN = 728, O_WATERWARN = 732;

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct KIn {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public UIntPtr extra;
    }
    [DllImport("user32.dll")]
    static extern uint SendInput(uint n, KIn[] p, int cb);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    static void PressScan(byte scan) {
        try {
            KIn[] d = new KIn[2];
            d[0].type = 1; d[0].wVk = 0; d[0].wScan = scan; d[0].dwFlags = 0x0008;
            d[1].type = 1; d[1].wVk = 0; d[1].wScan = scan; d[1].dwFlags = 0x0008 | 0x0002;
            string fg = "?";
            try {
                IntPtr h = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                fg = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            } catch { }
            uint nIns = SendInput(2, d, Marshal.SizeOf(typeof(KIn)));
            Log("tecla scan " + scan.ToString("X2") + " -> foco:" + fg + " insertados=" + nIns);
        } catch (Exception ex) { Log("tecla: " + ex.Message); }
    }
    static void PressKey(ushort vk) {
        try {
            uint sc = MapVirtualKey(vk, 0);
            KIn[] d = new KIn[2];
            d[0].type = 1; d[0].wVk = vk; d[0].wScan = (ushort)sc;
            d[1].type = 1; d[1].wVk = vk; d[1].wScan = (ushort)sc; d[1].dwFlags = 0x0002;
            string fg = "?";
            try {
                IntPtr h = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                fg = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            } catch { }
            Log("tecla " + vk.ToString("X2") + " -> foco:" + fg);
            uint nIns = SendInput(2, d, Marshal.SizeOf(typeof(KIn)));
            Log("tecla insertados=" + nIns);
        } catch (Exception ex) { Log("tecla: " + ex.Message); }
    }

    static string plugDir, logFile;
    static void Log(string m) {
        try {
            var f = new FileInfo(logFile);
            if (f.Exists && f.Length > 200 * 1024) f.Delete();
            File.AppendAllText(logFile, string.Format("{0:HH:mm:ss.fff} [{1}] {2}\r\n", DateTime.Now, System.Diagnostics.Process.GetCurrentProcess().Id, m));
        } catch { }
    }

    // ---- snapshot ----
    class Snap {
        public bool active, paused, ats;
        public float? speed, rpm, fuel, fuelCap, range, cons, cruise, limit, rpmMax;
        public float? routeD, routeT;
        public uint? planned;
        public int? gear;
        public bool park, motor, blinkL, blinkR, lowB, highB, brake, engine, cruiseOn, parking, hazard, fuelWarn, onJob, beacon, diffLock;
        public int wipers;
        public float? truckDmg, trailerDmg, cargoDmg;
        public float? airPressure, oilTemp, waterTemp;
        public float? airWarn, airEmergency, waterWarn;
    }

    static float? RF(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, int o) { try { return a.ReadSingle(o); } catch { return null; } }
    static int? RI(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, int o) { try { return a.ReadInt32(o); } catch { return null; } }
    static uint? RU(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, uint o) { try { return a.ReadUInt32((long)o); } catch { return null; } }
    static bool RB(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, int o) { try { return a.ReadByte(o) != 0; } catch { return false; } }

    static bool TryOpenTelemetry(out System.IO.MemoryMappedFiles.MemoryMappedFile mmf,
        out System.IO.MemoryMappedFiles.MemoryMappedViewAccessor acc) {
        mmf = null; acc = null;
        try {
            mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting("Local\\SCSTelemetry");
            acc = mmf.CreateViewAccessor(0, 32768, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
            if (acc.ReadByte(O_SDK) == 0) {
                acc.Dispose(); mmf.Dispose(); acc = null; mmf = null;
                return false;
            }
            return true;
        } catch {
            try { if (acc != null) acc.Dispose(); } catch { }
            try { if (mmf != null) mmf.Dispose(); } catch { }
            acc = null; mmf = null;
            return false;
        }
    }

    static Snap GetSnap(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a) {
        var s = new Snap();
        if (a == null) return s;
        try {
            if (a.ReadByte(O_SDK) == 0) return s;
            s.active = true;
            s.paused = a.ReadByte(O_PAUSED) != 0;
            s.ats = a.ReadUInt32((long)O_GAME) == 2;
            s.speed = RF(a, O_SPEED); s.rpm = RF(a, O_RPM); s.gear = RI(a, O_GEAR);
            s.fuel = RF(a, O_FUEL); s.fuelCap = RF(a, O_FCAP); s.range = RF(a, O_RANGE);
            s.cons = RF(a, O_CONS); s.rpmMax = RF(a, O_RPMMAX);
            s.cruise = RF(a, O_CRUISE); s.cruiseOn = RB(a, O_CRZON); s.limit = RF(a, O_LIMIT);
            s.lowB = RB(a, O_LO); s.highB = RB(a, O_HI); s.blinkL = RB(a, O_BL);
            s.airPressure = RF(a, O_AIR); s.oilTemp = RF(a, O_OILTEMP); s.waterTemp = RF(a, O_WATERTEMP);
            s.airWarn = RF(a, O_AIRWARN); s.airEmergency = RF(a, O_AIREMERG);
            s.waterWarn = RF(a, O_WATERWARN);
            s.blinkR = RB(a, O_BR); s.brake = RB(a, O_BRK); s.park = RB(a, O_PARK);
            s.motor = RB(a, O_MOTOR); s.engine = RB(a, O_ENG); s.parking = RB(a, O_PARKING);
            s.hazard = RB(a, O_HAZ); s.fuelWarn = RB(a, O_FWARN); s.beacon = RB(a, O_BEACON); s.diffLock = RB(a, O_DIFF);
            s.wipers = a.ReadByte(O_WIPERS);
            s.onJob = RB(a, O_ONJOB); s.planned = RU(a, O_PLANNED);
            s.routeD = RF(a, O_ROUTED); s.routeT = RF(a, O_ROUTET);
            float? mx = null;
            foreach (var v in new float?[] { RF(a, O_WENG), RF(a, O_WTRA), RF(a, O_WCAB), RF(a, O_WCHA), RF(a, O_WWHE) })
                if (v != null && (mx == null || v > mx)) mx = v;
            if (mx != null) s.truckDmg = mx * 100;
            bool att = RB(a, O_TATT);
            float? mr = null;
            foreach (var v in new float?[] { RF(a, O_TCHA), RF(a, O_TWHE), RF(a, O_TBOD) })
                if (v != null && (mr == null || v > mr)) mr = v;
            if (att && mr != null) s.trailerDmg = mr * 100;
            float? c1 = RF(a, O_TCD), c2 = RF(a, O_JCD);
            if (att && (c1 != null || c2 != null)) s.cargoDmg = Math.Max(c1 ?? 0, c2 ?? 0) * 100;
        } catch { s.active = false; }
        return s;
    }

    // ---- formato ----
    static string FmtGear(int? g) {
        if (g == null) return "--";
        if (g > 0) return g.ToString();
        if (g == 0) return "N";
        return "R";
    }
    static string DmgCol(float? p) {
        if (p == null) return null;
        if (p <= 20) return "Cyan";
        if (p <= 40) return "Lime";
        if (p <= 60) return "Yellow";
        if (p <= 80) return "Orange";
        return "Red";
    }
    static string FmtETA(float? rt) {
        if (rt == null || rt <= 0) return "--";
        double v = rt.Value;
        if (v >= 3600) return ((int)(v / 3600)).ToString() + ":" + ((int)((v % 3600) / 60)).ToString("00");
        if (v >= 90) return ((int)(v / 60)).ToString() + " min";
        return ((int)v).ToString() + " s";
    }

    // ---- dibujo ----
    static readonly Dictionary<string, string> themes = new Dictionary<string, string> {
        {"green","Lime"},{"cyan","Cyan"},{"amber","Orange"},{"red","Red"},
        {"white","White"},{"yellow","Yellow"},{"blue","DodgerBlue"},{"purple","Purple"},{"pink","DeepPink"}
    };
    static Bitmap NewBmp() { return new Bitmap(72, 72); }
    static string B64(Bitmap bmp) {
        using (var ms = new MemoryStream()) {
            bmp.Save(ms, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
    }
    static readonly Dictionary<string, Image> imgs = new Dictionary<string, Image>();
    static void Preload() {
        foreach (var n in new string[] { "left", "right", "hazard", "low", "high", "brake", "park", "beacon", "cruise", "wiper", "diff", "emergency-brake" })
            foreach (var suf in new string[] { "", "-c" }) {
                string p = Path.Combine(plugDir, "pk-" + n + suf + ".png");
                if (File.Exists(p)) { try { imgs[n + suf] = Image.FromFile(p); } catch { } }
            }
    }
    static string RenderPack(string name, bool on) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image im;
            if (imgs.TryGetValue(on ? name + "-c" : name, out im)) g.DrawImage(im, 0, 0, 72, 72);
            return B64(bmp);
        }
    }
    static string RenderBig(string top, string label, string colorName, string sub) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            Color col = Color.FromName(colorName);
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var f1 = new Font("Arial", 20, FontStyle.Bold))
            using (var f2 = new Font("Arial", 10, FontStyle.Bold))
            using (var f3 = new Font("Arial", 9))
            using (var br = new SolidBrush(col)) {
                g.DrawString(top, f1, br, 36, 6, sf);
                g.DrawString(label, f2, Brushes.White, 36, 34, sf);
                if (sub != null) g.DrawString(sub, f3, Brushes.Gray, 36, 52, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderSpeed(Snap s, string colorName) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            int cx = 36, cy = 38, r = 30;
            using (var penW = new Pen(Color.White, 3)) {
                g.DrawArc(penW, cx - r, cy - r, r * 2, r * 2, 135, 270);
                for (int a = 135; a <= 405; a += 27) {
                    double rad = a * Math.PI / 180;
                    g.DrawLine(penW,
                        (float)(cx + Math.Cos(rad) * r), (float)(cy + Math.Sin(rad) * r),
                        (float)(cx + Math.Cos(rad) * (r - 5)), (float)(cy + Math.Sin(rad) * (r - 5)));
                }
            }
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            string t = "--";
            if (s.speed != null) t = (Math.Abs(Math.Round(s.speed.Value * 3.6))).ToString();
            using (var fN = new Font("Arial", 24, FontStyle.Bold))
            using (var fU = new Font("Arial", 9, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromName(colorName))) {
                g.DrawString(t, fN, br, 36, 19, sf);
                g.DrawString("km/h", fU, Brushes.White, 36, 54, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderSpeedLimit(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            double limitKmh = s.limit == null ? 0 : s.limit.Value * 3.6;
            double speedKmh = s.speed == null ? 0 : Math.Abs(s.speed.Value * 3.6);
            bool valid = s.limit != null && s.limit > 0.5;
            bool exceeded = valid && speedKmh > limitKmh + 1.0;
            Color ringColor = exceeded && ((DateTime.UtcNow.Ticks / (250 * TimeSpan.TicksPerMillisecond)) % 2 == 0)
                ? Color.Orange : Color.Red;
            using (var fLabel = new Font("Arial", 8, FontStyle.Bold))
            using (var fNumber = new Font("Arial", 24, FontStyle.Bold))
            using (var pen = new Pen(ringColor, 3)) {
                g.DrawString("LIMITE", fLabel, Brushes.White, 36, 3, sf);
                g.DrawEllipse(pen, 8, 15, 56, 54);
                string value = valid ? Math.Round(limitKmh).ToString() : "--";
                g.DrawString(value, fNumber, exceeded ? Brushes.Red : Brushes.White, 36, 24, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderGauge(string label, float? value, string unit, bool warning) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            Color color = warning ? Color.Red : Color.Lime;
            using (var ring = new Pen(color, 3))
            using (var fLabel = new Font("Arial", 6, FontStyle.Bold))
            using (var fValue = new Font("Arial", 22, FontStyle.Bold))
            using (var fUnit = new Font("Arial", 8, FontStyle.Bold))
            using (var bValue = new SolidBrush(color)) {
                g.DrawEllipse(ring, 5, 5, 62, 62);
                g.DrawString(label, fLabel, Brushes.White, 36, 12, sf);
                g.DrawString(value == null ? "--" : Math.Round(value.Value).ToString(), fValue, bValue, 36, 22, sf);
                g.DrawString(unit, fUnit, Brushes.Gray, 36, 51, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderRpm(Snap s) {
        string t = s.rpm != null ? Math.Round(s.rpm.Value).ToString() : "--";
        string zc = "Lime", gearTxt = FmtGear(s.gear);
        bool isR = gearTxt == "R";
        string gearCol = "White";
        if (s.rpm != null && s.rpmMax != null && s.rpmMax > 0) {
            double f = s.rpm.Value / s.rpmMax.Value;
            if (f >= 0.9) { zc = "Red"; gearTxt = "CAMBIA"; gearCol = "Red"; }
            else if (f >= 0.75) zc = "Orange";
            else if (f >= 0.6) zc = "Yellow";
            else zc = "Lime";
        }
        if (isR && gearTxt == "R") gearCol = "Red";
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var fN = new Font("Arial", 18, FontStyle.Bold))
            using (var fL = new Font("Arial", 9, FontStyle.Bold))
            using (var fG = new Font("Arial", 15, FontStyle.Bold))
            using (var fC = new Font("Arial", 13, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromName(zc)))
            using (var bg2 = new SolidBrush(Color.FromName(gearCol))) {
                g.DrawString(t, fN, br, 36, 2, sf);
                g.DrawString("RPM", fL, Brushes.White, 36, 28, sf);
                g.DrawString(gearTxt, (gearTxt == "CAMBIA" ? fC : fG), bg2, 36, 42, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderFuel(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var white = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.White, 2)) {
                g.FillRectangle(white, 6, 11, 18, 26);
                g.FillRectangle(white, 9, 6, 12, 5);
                g.FillRectangle(white, 4, 37, 22, 4);
                g.DrawLine(pen, 24, 20, 31, 20);
                g.DrawLine(pen, 31, 20, 31, 34);
                g.FillRectangle(white, 28, 32, 6, 8);
                g.FillRectangle(Brushes.Black, 9, 16, 12, 9);
            }
            string rk = s.range != null ? Math.Round(s.range.Value).ToString() : "--";
            using (var fN = new Font("Arial", 11, FontStyle.Bold))
            using (var fU = new Font("Arial", 8)) {
                g.DrawString(rk, fN, Brushes.White, 16, 44, sf);
                g.DrawString("km", fU, Brushes.Gray, 16, 58, sf);
            }
            double frac = 0;
            if (s.fuel != null && s.fuelCap != null && s.fuelCap > 0) frac = s.fuel.Value / s.fuelCap.Value;
            if (frac < 0) frac = 0; if (frac > 1) frac = 1;
            bool warn = s.fuelWarn || (s.active && frac < 0.12);
            using (var fill = new SolidBrush(warn ? Color.Red : Color.Lime))
            using (var bg = new SolidBrush(Color.FromArgb(35, 35, 35))) {
                int bx = 50, bw = 10, by = 6, bh = 60;
                g.FillRectangle(bg, bx, by, bw, bh);
                int fh = (int)(bh * frac);
                if (fh > 0) g.FillRectangle(fill, bx, by + bh - fh, bw, fh);
                g.DrawRectangle(Pens.Gray, bx, by, bw, bh);
                using (var divPen = new Pen(Color.White, 1))
                    foreach (var q in new double[] { 0.25, 0.5, 0.75 }) {
                        int dy = (int)(by + bh * (1 - q));
                        g.DrawLine(divPen, bx, dy, bx + bw, dy);
                    }
            }
            return B64(bmp);
        }
    }
    static void DrawDmgIcon(Graphics g, string kind, int x, int y, Brush brush) {
        if (kind == "truck") {
            g.FillRectangle(brush, x, y + 6, 9, 7);
            g.FillRectangle(brush, x + 9, y + 9, 5, 4);
            g.FillEllipse(brush, x + 2, y + 14, 4, 4);
            g.FillEllipse(brush, x + 10, y + 14, 4, 4);
        } else if (kind == "trailer") {
            g.FillRectangle(brush, x, y + 3, 16, 9);
            g.FillEllipse(brush, x + 3, y + 13, 4, 4);
            g.FillEllipse(brush, x + 10, y + 13, 4, 4);
            using (var pen = new Pen(((SolidBrush)brush).Color, 2))
                g.DrawLine(pen, x + 16, y + 8, x + 20, y + 8);
        } else {
            using (var pen = new Pen(((SolidBrush)brush).Color, 2)) {
                g.DrawRectangle(pen, x + 2, y + 4, 14, 14);
                g.DrawLine(pen, x + 2, y + 4, x + 16, y + 18);
                g.DrawLine(pen, x + 16, y + 4, x + 2, y + 18);
            }
        }
    }
    static string RenderDamage(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var fP = new Font("Arial", 15, FontStyle.Bold))
            using (var wht = new SolidBrush(Color.White))
            using (var gry = new SolidBrush(Color.FromArgb(90, 90, 90))) {
                string[] kinds = { "truck", "trailer", "cargo" };
                float?[] vals = { s.truckDmg, s.trailerDmg, s.cargoDmg };
                int y = 2;
                for (int i = 0; i < 3; i++) {
                    DrawDmgIcon(g, kinds[i], 3, y, wht);
                    if (vals[i] != null) {
                        using (var b = new SolidBrush(Color.FromName(DmgCol(vals[i]))))
                            g.DrawString(Math.Round(vals[i].Value) + "%", fP, b, 48, y + 1, sf);
                    } else g.DrawString("--", fP, gry, 48, y + 1, sf);
                    y += 23;
                }
            }
            return B64(bmp);
        }
    }
    static string RenderLights(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var f = new Font("Arial", 10, FontStyle.Bold))
            using (var off = new SolidBrush(Color.FromArgb(70, 70, 70))) {
                string[,] rows = {
                    {"LO", s.lowB ? "1" : null}, {"HI", s.highB ? "1" : null}, {"PK", s.parking ? "1" : null},
                    {"L", s.blinkL ? "1" : null}, {"R", s.blinkR ? "1" : null}, {"BRK", s.brake ? "1" : null},
                    {"MT", s.motor ? "1" : null}, {"ENG", s.engine ? "1" : null}, {"CRZ", s.cruiseOn ? "1" : null}
                };
                int y = 4;
                for (int r = 0; r < 3; r++) {
                    int x = 4;
                    for (int c = 0; c < 3; c++) {
                        Brush b = rows[r * 3 + c, 1] != null ? Brushes.Lime : off;
                        g.DrawString(rows[r * 3 + c, 0], f, b, x + 11, y, sf);
                        x += 23;
                    }
                    y += 22;
                }
            }
            return B64(bmp);
        }
    }
    static string RenderCruise(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            Image cruiseIcon;
            if (imgs.TryGetValue("cruise", out cruiseIcon)) {
                using (var ia = new ImageAttributes()) {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = 0.1f; // 90% transparente
                    ia.SetColorMatrix(cm);
                    g.DrawImage(cruiseIcon, new Rectangle(0, 0, 72, 72), 0, 0,
                        cruiseIcon.Width, cruiseIcon.Height, GraphicsUnit.Pixel, ia);
                }
            }

            double setKmh = s.cruise == null ? 0 : s.cruise.Value * 3.6;
            double actualKmh = s.speed == null ? 0 : s.speed.Value * 3.6;
            // SCS reports cruise speed in m/s; a set speed can be retained while CC is off.
            bool hasSetSpeed = s.cruise != null && s.cruise > 0.5;
            bool active = s.cruiseOn && hasSetSpeed;
            bool inTolerance = active && s.speed != null && Math.Abs(actualKmh - setKmh) <= 5.0;
            string speedText = hasSetSpeed ? Math.Round(setKmh).ToString() : "--";

            using (var shade = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
            using (var fSpeed = new Font("Arial", 21, FontStyle.Bold))
            using (var fLabel = new Font("Arial", 8, FontStyle.Bold))
            using (var fStatus = new Font("Arial", 13, FontStyle.Bold))
            using (var status = new SolidBrush(!active ? Color.Red : (inTolerance ? Color.Lime : Color.Red))) {
                g.FillRectangle(shade, 4, 5, 64, 62);
                g.DrawString(speedText, fSpeed, Brushes.Lime, 36, 8, sf);
                g.DrawString("CRUCERO km/h", fLabel, Brushes.White, 36, 34, sf);
                string statusText = !active ? "OFF" : (s.speed == null ? "--" : (inTolerance ? "OK ±5" : "FUERA ±5"));
                g.DrawString(statusText, fStatus, status, 36, 48, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderCruiseButton(Snap s) {
        // Icono tenue con velocidad configurada visible y testigo verde de activación.
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image cruiseIcon;
            if (imgs.TryGetValue("cruise", out cruiseIcon)) {
                using (var ia = new ImageAttributes()) {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = 0.20f; // 80% transparente, visible detras del numero
                    ia.SetColorMatrix(cm);
                    g.DrawImage(cruiseIcon, new Rectangle(0, 0, 72, 72), 0, 0,
                        cruiseIcon.Width, cruiseIcon.Height, GraphicsUnit.Pixel, ia);
                }
            }
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            string value = s.cruise != null && s.cruise > 0.5 ? Math.Round(s.cruise.Value * 3.6).ToString() : "--";
            using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            using (var fSpeed = new Font("Arial", 27, FontStyle.Bold))
            using (var fLabel = new Font("Arial", 8, FontStyle.Bold)) {
                g.FillRectangle(shade, 4, 7, 64, 58);
                g.DrawString(value, fSpeed, Brushes.Lime, 36, 10, sf);
                g.DrawString("CRUCERO", fLabel, Brushes.White, 36, 47, sf);
            }
            if (s.cruiseOn) {
                double ms = DateTime.Now.TimeOfDay.TotalMilliseconds % 1000.0;
                float a = (float)(0.25 + 0.75 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ms / 1000.0)));
                using (var overlay = new SolidBrush(Color.FromArgb((int)(a * 220), 0, 255, 0)))
                    g.FillEllipse(overlay, 56, 3, 12, 12);
            }
            return B64(bmp);
        }
    }
    static string RenderRoute(Snap s) {
        if (!s.onJob || s.planned == null || s.routeD == null || s.planned <= 0)
            return RenderBig("--", "SIN VIAJE", "Gray", null);
        double remainingKm = s.routeD.Value / 1000.0;
        double travelledKm = Math.Max(0, s.planned.Value - remainingKm);
        string eta = FmtETA(s.routeT);
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
            using (var fL = new Font("Arial", 8))
            using (var fN = new Font("Arial", 14, FontStyle.Bold))
            using (var cy = new SolidBrush(Color.Cyan)) {
                g.DrawString("RECORRIDA", fL, Brushes.Gray, 36, 0, sf);
                g.DrawString(Math.Round(travelledKm) + " km", fN, Brushes.Lime, 36, 11, sf);
                g.DrawString("FALTAN " + eta, fL, Brushes.Gray, 36, 33, sf);
                g.DrawString(Math.Round(remainingKm) + " km", fN, cy, 36, 45, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderHazBtn(Snap s) {
        bool lit = s.hazard && (s.blinkL || s.blinkR);
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image L, R;
            string ls = lit ? "left-c" : "left", rs = lit ? "right-c" : "right";
            if (imgs.TryGetValue(ls, out L) && imgs.TryGetValue(rs, out R)) {
                g.DrawImage(L, 2, 12, 32, 48);
                g.DrawImage(R, 38, 12, 32, 48);
            }
            return B64(bmp);
        }
    }
    static string RenderBeacon(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image b, c;
            bool hasB = imgs.TryGetValue("beacon", out b);
            bool hasC = imgs.TryGetValue("beacon-c", out c);
            if (hasB) g.DrawImage(b, 0, 0, 72, 72);
            if (s.beacon && hasC) {
                double ms = DateTime.Now.TimeOfDay.TotalMilliseconds % 1000.0;
                float a = (float)(0.25 + 0.75 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ms / 1000.0)));
                var cm = new ColorMatrix(new float[][] {
                    new float[]{1,0,0,0,0}, new float[]{0,1,0,0,0}, new float[]{0,0,1,0,0},
                    new float[]{0,0,0,a,0}, new float[]{0,0,0,0,1} });
                using (var ia = new ImageAttributes()) {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(c, new Rectangle(0, 0, 72, 72), 0, 0, c.Width, c.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return B64(bmp);
        }
    }
    static string RenderWipers(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            int mode = wiperMode;
            if (s.wipers == 0) mode = 0;
            else if (mode == 0) mode = 1;
            string iconName = mode > 0 ? "wiper-c" : "wiper";
            if (imgs.TryGetValue(iconName, out icon)) g.DrawImage(icon, 0, 0, 44, 44);
            if (mode > 0) {
                string state = mode == 1 ? "A" : mode.ToString();
                using (var f = new Font("Arial", 24, FontStyle.Bold))
                    g.DrawString(state, f, Brushes.White, 45, 20);
            }
            return B64(bmp);
        }
    }
    static string RenderDiffLock(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            string key = s.diffLock ? "diff-c" : "diff";
            if (!imgs.TryGetValue(key, out icon)) imgs.TryGetValue("diff", out icon);
            if (icon != null) {
                if (s.diffLock) {
                    double phase = DateTime.Now.TimeOfDay.TotalMilliseconds % 1000.0;
                    float alpha = (float)(0.2 + 0.8 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase / 1000.0)));
                    var matrix = new ColorMatrix(new float[][] {
                        new float[]{1,0,0,0,0}, new float[]{0,1,0,0,0}, new float[]{0,0,1,0,0},
                        new float[]{0,0,0,alpha,0}, new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrix);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var orange = new SolidBrush(Color.FromArgb((int)(alpha * 210), 255, 150, 0)))
                        g.FillEllipse(orange, 56, 4, 12, 12);
                } else g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }
    static string RenderEmergencyBrake(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            if (imgs.TryGetValue("emergency-brake", out icon)) {
                float pulse = s.park
                    ? (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerSecond))
                    : 0f;
                // Tinta el icono blanco original en rojo; la alpha produce el pulso.
                var matrix = new ColorMatrix(new float[][] {
                    new float[]{1,0,0,0,0}, new float[]{0,1-pulse,0,0,0}, new float[]{0,0,1-pulse,0,0},
                    new float[]{0,0,0,1,0}, new float[]{0,0,0,0,1} });
                using (var ia = new ImageAttributes()) {
                    ia.SetColorMatrix(matrix);
                    g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return B64(bmp);
        }
    }
    static string RenderKey(string metric, Snap s, string colorName) {
        if (!s.active) return RenderBig("--", "SCS", colorName, "sin juego");
        switch (metric) {
            case "speed": return RenderSpeed(s, colorName);
            case "gear": return RenderBig(FmtGear(s.gear), "MARCHA", colorName, null);
            case "rpm": return RenderRpm(s);
            case "fuel": return RenderFuel(s);
            case "range": {
                    string t = s.range != null ? Math.Round(s.range.Value).ToString() : "--";
                    return RenderBig(t, "AUTONOMIA", colorName, "km");
                }
            case "cons": {
                    string t = s.cons != null ? Math.Round(s.cons.Value * 100, 1).ToString() : "--";
                    return RenderBig(t, "L/100KM", colorName, "consumo");
                }
            case "cruise": return RenderCruiseButton(s);
            case "cruiseSpeed": return RenderCruise(s);
            case "limit": return RenderSpeedLimit(s);
            case "watertemp": return RenderGauge("MOTOR", s.waterTemp, "°C", s.waterWarn != null && s.waterTemp != null && s.waterTemp >= s.waterWarn);
            // El SCS SDK no publica un umbral de temperatura del aceite; usar aviso conservador.
            case "oiltemp": return RenderGauge("ACEITE", s.oilTemp, "°C", s.oilTemp != null && s.oilTemp >= 130.0f);
            case "airpressure": return RenderGauge("AIRE", s.airPressure, "bar", (s.airEmergency != null && s.airPressure != null && s.airPressure <= s.airEmergency) || (s.airWarn != null && s.airPressure != null && s.airPressure <= s.airWarn));
            case "lowB": return RenderPack("low", s.lowB);
            case "highB": return RenderPack("high", HighShown(s));
            case "blinkL": return RenderPack("left", s.blinkL);
            case "blinkLbtn": return RenderPack("left", s.blinkL);
            case "blinkRbtn": return RenderPack("right", s.blinkR);
            case "lightbtn":
                if (s.lowB) return RenderPack("low", true);
                if (s.parking) return RenderPack("park", true);
                return RenderPack("low", false);
            case "highbtn": return RenderPack("high", HighShown(s));
            case "beaconbtn": return RenderBeacon(s);
            case "wiperbtn": return RenderWipers(s);
            case "difflock": return RenderDiffLock(s);
            case "emergencybrake": return RenderEmergencyBrake(s);
            case "blinkR": return RenderPack("right", s.blinkR);
            case "hazard": return RenderPack("hazard", s.hazard);
            case "hazbtn": return RenderHazBtn(s);
            case "brake": return RenderPack("brake", s.brake);
            case "park": return RenderPack("park", s.parking);
            case "turn":
                if (s.hazard) return RenderPack("hazard", true);
                if (s.blinkL) return RenderPack("left", true);
                if (s.blinkR) return RenderPack("right", true);
                return RenderPack("hazard", false);
            case "beams":
                if (HighShown(s)) return RenderPack("high", true);
                if (s.lowB) return RenderPack("low", true);
                if (s.parking) return RenderPack("park", true);
                return RenderPack("low", false);
            case "auto":
                if (s.hazard) return RenderPack("hazard", true);
                if (s.blinkL) return RenderPack("left", true);
                if (s.blinkR) return RenderPack("right", true);
                if (s.brake) return RenderPack("brake", true);
                if (HighShown(s)) return RenderPack("high", true);
                if (s.lowB) return RenderPack("low", true);
                if (s.parking || s.park) return RenderPack("park", true);
                return RenderPack("low", false);
            case "lights": return RenderLights(s);
            case "damage": return RenderDamage(s);
            case "route": return RenderRoute(s);
            default: return RenderBig("--", "SCS", colorName, null);
        }
    }

    // ---- websocket crudo ----
    class Ws {
        TcpClient cli; NetworkStream ns;
        public Ws(string host, int port) { cli = new TcpClient(host, port); ns = cli.GetStream(); }
        public void Handshake(string path) {
            byte[] rk = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(rk);
            string key = Convert.ToBase64String(rk);
            string req = "GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
            byte[] rb = Encoding.ASCII.GetBytes(req);
            ns.Write(rb, 0, rb.Length);
            var sb = new StringBuilder();
            byte[] b1 = new byte[1];
            while (!sb.ToString().Contains("\r\n\r\n")) {
                int n = ns.Read(b1, 0, 1);
                if (n <= 0) throw new Exception("handshake roto");
                sb.Append((char)b1[0]);
            }
            if (!sb.ToString().Contains(" 101 ")) throw new Exception("handshake sin 101");
        }
        static void ReadN(NetworkStream s, byte[] b, int off, int n) {
            while (n > 0) {
                int r = s.Read(b, off, n);
                if (r <= 0) throw new Exception("conexion cerrada");
                off += r; n -= r;
            }
        }
        public bool Poll(int ms) { return ns.DataAvailable || cli.Client.Poll(ms * 1000, SelectMode.SelectRead); }
        // Lee UN mensaje completo. Devuelve null si timeout. opcode en out.
        public string Recv(int timeoutMs, out int opcode) {
            opcode = -1;
            if (!cli.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead) && !ns.DataAvailable) return null;
            byte[] h = new byte[2];
            try { ReadN(ns, h, 0, 2); } catch { throw new Exception("conexion cerrada"); }
            bool fin = (h[0] & 0x80) != 0;
            opcode = h[0] & 0x0F;
            bool masked = (h[1] & 0x80) != 0;
            long len = h[1] & 0x7F;
            if (len == 126) { byte[] e = new byte[2]; ReadN(ns, e, 0, 2); len = (e[0] << 8) | e[1]; }
            else if (len == 127) { byte[] e = new byte[8]; ReadN(ns, e, 0, 8); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | e[i]; }
            byte[] mask = new byte[4];
            if (masked) ReadN(ns, mask, 0, 4);
            byte[] pay = new byte[len];
            if (len > 0) ReadN(ns, pay, 0, (int)len);
            if (masked) for (long i = 0; i < len; i++) pay[i] ^= mask[i % 4];
            if (opcode == 0x8) throw new Exception("close del host");
            if (opcode == 0x9) { SendRaw(0xA, pay); return Recv(0, out opcode); }
            if (opcode == 0xA) return Recv(0, out opcode);
            if (!fin) {
                MemoryStream acc = new MemoryStream();
                if (len > 0) acc.Write(pay, 0, pay.Length);
                while (true) {
                    byte[] h2 = new byte[2];
                    ReadN(ns, h2, 0, 2);
                    bool fin2 = (h2[0] & 0x80) != 0;
                    int op2 = h2[0] & 0x0F;
                    bool m2 = (h2[1] & 0x80) != 0;
                    long l2 = h2[1] & 0x7F;
                    if (l2 == 126) { byte[] e = new byte[2]; ReadN(ns, e, 0, 2); l2 = (e[0] << 8) | e[1]; }
                    else if (l2 == 127) { byte[] e = new byte[8]; ReadN(ns, e, 0, 8); l2 = 0; for (int i = 0; i < 8; i++) l2 = (l2 << 8) | e[i]; }
                    byte[] mk = new byte[4];
                    if (m2) ReadN(ns, mk, 0, 4);
                    byte[] p2 = new byte[l2];
                    if (l2 > 0) ReadN(ns, p2, 0, (int)l2);
                    if (m2) for (long i = 0; i < l2; i++) p2[i] ^= mk[i % 4];
                    if (op2 == 0x8) throw new Exception("close del host");
                    if (op2 == 0x0) {
                        if (l2 > 0) acc.Write(p2, 0, p2.Length);
                        if (fin2) break;
                    }
                }
                return Encoding.UTF8.GetString(acc.ToArray());
            }
            return Encoding.UTF8.GetString(pay);
        }
        public void SendRaw(int opcode, byte[] pay) {
            if (pay == null) pay = new byte[0];
            MemoryStream m = new MemoryStream();
            m.WriteByte((byte)(0x80 | opcode));
            byte[] mask = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
            long len = pay.Length;
            if (len < 126) m.WriteByte((byte)(0x80 | len));
            else if (len < 65536) { m.WriteByte(0x80 | 126); m.WriteByte((byte)(len >> 8)); m.WriteByte((byte)len); }
            else { m.WriteByte((byte)(0x80 | 127)); for (int i = 7; i >= 0; i--) m.WriteByte((byte)((len >> (8 * i)) & 0xFF)); }
            m.Write(mask, 0, 4);
            byte[] mp = new byte[pay.Length];
            for (long i = 0; i < pay.Length; i++) mp[i] = (byte)(pay[i] ^ mask[i % 4]);
            m.Write(mp, 0, mp.Length);
            byte[] all = m.ToArray();
            ns.Write(all, 0, all.Length);
        }
        public void Send(string text) { SendRaw(0x1, Encoding.UTF8.GetBytes(text)); }
        public void Close() { try { cli.Close(); } catch { } }
    }

    class Ctx {
        public string metric = "speed", theme = "green";
        public double interval = 0.15;
        public bool visible, titled;
        public string last;
    }

    // Telemetry only reports wipers enabled/disabled; retain the selected mode
    // while cycling this Stream Deck action: OFF -> A -> 2 -> 3 -> OFF.
    static int wiperMode = 0;

    static string GetS(string json, string key, string def) {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : def;
    }
    static string GetN(string json, string key, string def) {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*([0-9.\\-]+)");
        return m.Success ? m.Groups[1].Value : def;
    }

    static long lastHighTick = 0;
    static int highWas = 0;
    static void FlashWatch() {
        while (true) {
            try {
                var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting("Local\\SCSTelemetry");
                var acc = mmf.CreateViewAccessor(0, 32768, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
                int bad = 0;
                while (true) {
                    try {
                        // No fiarse de bytes residuales: si el SDK no esta activo, el mapa
                        // puede contener basura de la sesion anterior (falso "rafaga detectada" al arrancar).
                        int sdk = acc.ReadByte(O_SDK);
                        if (sdk == 0) { highWas = 0; bad = 0; Thread.Sleep(100); continue; }
                        int v = acc.ReadByte(O_HI);
                        if (v == 1) {
                            System.Threading.Interlocked.Exchange(ref lastHighTick, DateTime.UtcNow.Ticks);
                            if (highWas == 0) { highWas = 1; Log("rafaga detectada sdk=1 hi=1"); }
                        } else {
                            if (highWas == 1) Log("rafaga fin hi=0");
                            highWas = 0;
                        }
                        bad = 0;
                    } catch { if (++bad > 40) break; }
                    Thread.Sleep(15);
                }
                try { acc.Dispose(); mmf.Dispose(); } catch { }
            } catch { }
            Thread.Sleep(1000);
        }
    }
    static bool HighShown(Snap s) {
        if (s.highB) return true;
        long t = System.Threading.Interlocked.Read(ref lastHighTick);
        // Latch 1.5 s para que al menos ~10 loops de render (150 ms) alcancen a mostrar el destello.
        return (DateTime.UtcNow.Ticks - t) < 1500 * 10000;
    }
    static int Main(string[] args) {
        bool test = false, watchHigh = false;
        int port = 0;
        string uuid = null, regEv = "registerPlugin";
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-Test" || args[i] == "--test") test = true;
            else if (args[i] == "-WatchHigh" || args[i] == "--watch-high") watchHigh = true;
            else if (args[i] == "-port" && i + 1 < args.Length) port = int.Parse(args[++i]);
            else if (args[i] == "-pluginUUID" && i + 1 < args.Length) uuid = args[++i];
            else if (args[i] == "-registerEvent" && i + 1 < args.Length) regEv = args[++i];
        }
        plugDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        logFile = Path.Combine(plugDir, "backend.log");
        if (watchHigh) return RunWatchHigh();
        if (test) return RunTest();
        if (port <= 0 || uuid == null) return 1;
        Log("=== inicio ===");
        Preload();
        try { var ft = new Thread(new ThreadStart(FlashWatch)); ft.IsBackground = true; ft.Start(); } catch (Exception ex) { Log("flashwatch: " + ex.Message); }
        Ws ws;
        try {
            ws = new Ws("127.0.0.1", port);
            ws.Handshake("/");
        } catch (Exception ex) { Log("MUERTE-conexion: " + ex.Message); return 0; }
        try { ws.Send("{\"event\":\"" + regEv + "\",\"uuid\":\"" + uuid + "\"}"); }
        catch (Exception ex) { Log("MUERTE-registro: " + ex.Message); return 0; }
        Log("registrado");
        System.IO.MemoryMappedFiles.MemoryMappedFile telemetryMap = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor acc = null;
        long nextMapRetry = 0;
        var ctxs = new Dictionary<string, Ctx>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true) {
            long t0 = sw.ElapsedMilliseconds;
            // drenar mensajes (50ms)
            long deadline = t0 + 50;
            while (sw.ElapsedMilliseconds < deadline) {
                int left = (int)(deadline - sw.ElapsedMilliseconds);
                int op;
                string msg = null;
                try { msg = ws.Recv(Math.Max(1, left), out op); }
                catch (Exception ex) { Log("MUERTE-socket: " + ex.Message); return 0; }
                if (msg == null) break;
                Log("ev rx");
                string ev = GetS(msg, "event", ""), context = GetS(msg, "context", ""), action = GetS(msg, "action", "");
                Log("ev " + ev);
                try {
                    if (ev == "willAppear") {
                        string[] parts = action.Split('.');
                        string b = parts.Length > 0 ? parts[parts.Length - 1] : "monitor";
                        if (b == "") b = "monitor";
                        ctxs[context] = new Ctx { visible = true };
                        // metrica fija por accion
                        ctxs[context].metric = b;
                        string th = GetS(msg, "metric", null);
                        // settings vienen en payload.settings; si accion generica usa metric de settings
                        var mset = Regex.Match(msg, "\"settings\"\\s*:\\s*\\{([^\\}]*)\\}");
                        string m2 = null, th2 = null, iv2 = null;
                        if (mset.Success) {
                            m2 = GetS(mset.Groups[1].Value, "metric", null);
                            th2 = GetS(mset.Groups[1].Value, "theme", null);
                            iv2 = GetN(mset.Groups[1].Value, "interval", null);
                        }
                        if (b == "monitor" && m2 != null) ctxs[context].metric = m2;
                        if (th2 != null) ctxs[context].theme = th2;
                        if (iv2 != null) { double d; if (double.TryParse(iv2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ctxs[context].interval = d; }
                    } else if (ev == "willDisappear") {
                        if (ctxs.ContainsKey(context)) ctxs[context].visible = false;
                    } else if (ev == "keyDown") {
                        if (context != "" && ctxs.ContainsKey(context)) {
                            if (ctxs[context].metric == "hazbtn") { Log("pulsado parqueo -> F"); PressKey(0x46); }
                            else if (ctxs[context].metric == "blinkLbtn") { Log("pulsado izq -> scan 1A"); PressScan(0x1A); }
                            else if (ctxs[context].metric == "blinkRbtn") { Log("pulsado der -> scan 1B"); PressScan(0x1B); }
                            else if (ctxs[context].metric == "lightbtn") { Log("pulsado luces -> L"); PressKey(0x4C); }
                            else if (ctxs[context].metric == "highbtn") { Log("pulsado largas -> K"); PressKey(0x4B); }
                            else if (ctxs[context].metric == "beaconbtn") { Log("pulsado baliza -> O"); PressKey(0x4F); }
                            else if (ctxs[context].metric == "wiperbtn") {
                                wiperMode = (wiperMode + 1) % 4;
                                Log("limpiaparabrisas modo=" + wiperMode + " -> P");
                                PressKey(0x50);
                            }
                            else if (ctxs[context].metric == "difflock") { Log("pulsado diferencial -> V"); PressKey(0x56); }
                            else if (ctxs[context].metric == "emergencybrake") { Log("pulsado freno emergencia -> Space"); PressKey(0x20); }
                            else if (ctxs[context].metric == "cruise") { Log("pulsado crucero -> C"); PressKey(0x43); }
                        }
                    } else if (ev == "didReceiveSettings" || ev == "didReceiveGlobalSettings") {
                        if (context != "" && ctxs.ContainsKey(context)) {
                            var mset = Regex.Match(msg, "\"settings\"\\s*:\\s*\\{([^\\}]*)\\}");
                            if (mset.Success) {
                                string m2 = GetS(mset.Groups[1].Value, "metric", null);
                                string th2 = GetS(mset.Groups[1].Value, "theme", null);
                                string iv2 = GetN(mset.Groups[1].Value, "interval", null);
                                if (ctxs[context].metric == "monitor" && m2 != null) ctxs[context].metric = m2;
                                if (th2 != null) ctxs[context].theme = th2;
                                if (iv2 != null) { double d; if (double.TryParse(iv2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ctxs[context].interval = d; }
                            }
                        }
                    }
                } catch (Exception ex) { Log("ev-err: " + ex.Message); }
            }
            // refresco
            Snap s;
            try { s = GetSnap(acc); } catch { s = new Snap(); }
            if (!s.active) {
                if (acc != null) {
                    try { acc.Dispose(); } catch { }
                    try { if (telemetryMap != null) telemetryMap.Dispose(); } catch { }
                    acc = null; telemetryMap = null;
                    Log("telemetria desconectada; aguardando juego");
                }
                if (sw.ElapsedMilliseconds >= nextMapRetry) {
                    if (TryOpenTelemetry(out telemetryMap, out acc)) Log("mapa Local\\SCSTelemetry conectado");
                    nextMapRetry = sw.ElapsedMilliseconds + 1000;
                }
                s = GetSnap(acc);
            }
            foreach (var kv in ctxs) {
                if (!kv.Value.visible) continue;
                string img;
                try {
                    string th = kv.Value.theme;
                    if (!themes.ContainsKey(th)) th = "green";
                    img = RenderKey(kv.Value.metric, s, themes[th]);
                } catch (Exception ex) { Log("render: " + ex.Message); continue; }
                if (img != kv.Value.last) {
                    try { ws.Send("{\"event\":\"setImage\",\"context\":\"" + kv.Key + "\",\"payload\":{\"image\":\"" + img + "\",\"target\":0}}"); }
                    catch (Exception ex) { Log("MUERTE-envio: " + ex.Message); return 0; }
                    kv.Value.last = img;
                }
                if (!kv.Value.titled) {
                    try { ws.Send("{\"event\":\"setTitle\",\"context\":\"" + kv.Key + "\",\"payload\":{\"title\":\"\",\"target\":0}}"); }
                    catch (Exception ex) { Log("MUERTE-envio2: " + ex.Message); return 0; }
                    kv.Value.titled = true;
                }
            }
            double wait = 2;
            bool any = false;
            foreach (var kv in ctxs) if (kv.Value.visible) { any = true; if (kv.Value.interval < wait) wait = kv.Value.interval; }
            if (!any) wait = 2;
            if (wait < 0.1) wait = 0.1;
            long rest = (long)(wait * 1000 - (sw.ElapsedMilliseconds - t0));
            if (rest > 0) Thread.Sleep((int)rest);
        }
    }

    static int RunWatchHigh() {
        // Diagnostico de rafagas: muestrea SDK/pausa/low/high cada 15 ms durante 60 s.
        // Uso: backend.exe -WatchHigh  (hacer rafagas con el juego enfocado y mirar la salida)
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor acc = null;
        try {
            var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting("Local\\SCSTelemetry");
            acc = mmf.CreateViewAccessor(0, 32768, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        } catch { Console.WriteLine("SIN JUEGO (sin MMF Local\\SCSTelemetry)"); return 0; }
        Console.WriteLine("Muestreando 60 s: haz rafagas (palanca largas) y observa HI 0->1.");
        Console.WriteLine("t(s) sdk paused LO HI | HighShown(latch1.5s)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int lastHi = -1;
        long localLatch = 0;
        while (sw.ElapsedMilliseconds < 60000) {
            try {
                int sdk = acc.ReadByte(O_SDK);
                int pau = acc.ReadByte(O_PAUSED);
                int lo = acc.ReadByte(O_LO);
                int hi = acc.ReadByte(O_HI);
                if (hi == 1 && sdk == 1) localLatch = DateTime.UtcNow.Ticks;
                bool shown = hi == 1 || (DateTime.UtcNow.Ticks - localLatch) < 1500 * 10000;
                if (hi != lastHi) {
                    Console.WriteLine(string.Format("{0,5:F1} sdk={1} pau={2} LO={3} HI={4} shown={5} <-- CAMBIO",
                        sw.ElapsedMilliseconds / 1000.0, sdk, pau, lo, hi, shown ? 1 : 0));
                    lastHi = hi;
                }
            } catch (Exception ex) { Console.WriteLine("ERR " + ex.Message); break; }
            Thread.Sleep(15);
        }
        Console.WriteLine("Fin. Si HI nunca paso a 1, el juego no publica la rafaga en 1584 (revisar tecla/binding).");
        return 0;
    }

    static int RunTest() {
        Preload();
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor acc = null;
        try {
            var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting("Local\\SCSTelemetry");
            acc = mmf.CreateViewAccessor(0, 32768, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        } catch { Console.WriteLine("SIN JUEGO"); return 0; }
        var s = GetSnap(acc);
        if (!s.active) { Console.WriteLine("SIN JUEGO"); return 0; }
        Console.WriteLine("Juego: " + (s.ats ? "ATS" : "ETS2") + " | Pausado: " + s.paused);
        double kmh = s.speed != null ? s.speed.Value * 3.6 : 0;
        Console.WriteLine("Velocidad: " + Math.Round(kmh) + " km/h | Marcha: " + FmtGear(s.gear) + " | RPM: " + (s.rpm != null ? Math.Round(s.rpm.Value).ToString() : "--"));
        Console.WriteLine("Fuel: " + s.fuel + " L | Rango: " + s.range + " km | Danos: " + s.truckDmg + "/" + s.trailerDmg + "/" + s.cargoDmg);
        string dir = Path.Combine(Path.GetTempPath(), "scsmon-test");
        Directory.CreateDirectory(dir);
        foreach (var m in new string[] { "speed", "gear", "rpm", "fuel", "range", "cons", "cruise", "cruiseSpeed", "limit", "watertemp", "oiltemp", "airpressure", "blinkL", "blinkR", "hazard", "lowB", "highB", "brake", "park", "lights", "auto", "turn", "beams", "damage", "route", "hazbtn", "blinkLbtn", "blinkRbtn", "lightbtn", "highbtn", "beaconbtn", "wiperbtn", "difflock", "emergencybrake", "gear", "speed", "rpm", "fuel", "cons", "limit", "watertemp", "oiltemp", "airpressure" }) {
            string img = RenderKey(m, s, "Lime");
            File.WriteAllBytes(Path.Combine(dir, m + ".png"), Convert.FromBase64String(img.Substring(22)));
        }
        Console.WriteLine("PNGs en " + dir);
        return 0;
    }
}
