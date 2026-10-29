// HS SCS Monitor - backend nativo (C#). Telemetria ATS/ETS2 -> Ajazz.
// Sin PowerShell, sin dependencias: TCP crudo + WebSocket RFC6455 manual,
// MemoryMappedFile, System.Drawing. Offsets: scs-telemetry-common.hpp REV12.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;

class SCSMon {
    // Un solo StringFormat centrado para todos los renders: evita crear/liberar
    // handles GDI+ en cada frame (y la fuga de las versiones anteriores).
    static readonly StringFormat CenterSF = new StringFormat { Alignment = StringAlignment.Center };
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
    static string ForegroundName() {
        try {
            IntPtr h = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        } catch { return "?"; }
    }
    static int SendScan(byte scan, bool ext, bool up) {
        // ETS2/ATS ignoran SendInput con KEYEVENTF_SCANCODE puro (probado con
        // la luz de cruce). Enviar VK + scan, como PressKey, si llega al juego.
        uint code = ext ? (uint)(0xE000 | scan) : (uint)scan;
        uint vk = MapVirtualKey(code, 3); // MAPVK_VSC_TO_VK_EX
        if (vk == 0) vk = MapVirtualKey(scan, 1); // MAPVK_VSC_TO_VK
        KIn d = new KIn();
        d.type = 1; d.wVk = (ushort)vk; d.wScan = scan;
        d.dwFlags = (ext ? 0x0001u : 0u) | (up ? 0x0002u : 0u);
        return (int)SendInput(1, new KIn[] { d }, Marshal.SizeOf(typeof(KIn)));
    }
    static void PressScan(byte scan) {
        PressScanEx(scan, false, false, false, false);
    }
    static readonly object pressHoldLock = new object();
    static void PressScanEx(byte scan, bool ext, bool alt, bool ctrl, bool shift) {
        bool downCtrl = false, downShift = false, downAlt = false;
        try {
            string fg = ForegroundName();
            int n = 0;
            if (ctrl) { n += SendScan(0x1D, false, false); downCtrl = true; }
            if (shift) { n += SendScan(0x2A, false, false); downShift = true; }
            if (alt) { n += SendScan(0x38, false, false); downAlt = true; }
            if (ctrl || shift || alt) Thread.Sleep(25);
            n += SendScan(scan, ext, false);
            n += SendScan(scan, ext, true);
            if (ctrl || shift || alt) Thread.Sleep(25);
            Log("tecla scan " + scan.ToString("X2") + (alt ? " +Alt" : "") + (ctrl ? " +Ctrl" : "") + (shift ? " +Shift" : "") + " -> foco:" + fg + " insertados=" + n);
        } catch (Exception ex) { Log("tecla: " + ex.Message); }
        finally {
            try {
                if (downAlt) SendScan(0x38, false, true);
                if (downShift) SendScan(0x2A, false, true);
                if (downCtrl) SendScan(0x1D, false, true);
            } catch { }
        }
    }
    // Las ventanillas SCS son acciones "mientras se mantiene": un down+up
    // instantaneo no alcanza a moverlas. Mantener la tecla principal en un
    // hilo aparte evita bloquear el bucle de eventos del plugin. El juego
    // engancha el modificador Alt en el keyDown, asi que se puede soltar
    // enseguida y dejar la tecla principal pulsada el resto del recorrido.
    static void PressScanHold(byte scan, bool ext, bool alt, bool ctrl, bool shift, int holdMs) {
        ThreadPool.QueueUserWorkItem(delegate {
            lock (pressHoldLock) {
                bool downCtrl = false, downShift = false, downAlt = false, downMain = false;
                try {
                    string fg = ForegroundName();
                    int n = 0;
                    if (ctrl) { n += SendScan(0x1D, false, false); downCtrl = true; }
                    if (shift) { n += SendScan(0x2A, false, false); downShift = true; }
                    if (alt) { n += SendScan(0x38, false, false); downAlt = true; }
                    if (ctrl || shift || alt) Thread.Sleep(25);
                    n += SendScan(scan, ext, false); downMain = true;
                    if (ctrl || shift || alt) {
                        Thread.Sleep(80);
                        if (downAlt) { SendScan(0x38, false, true); downAlt = false; }
                        if (downShift) { SendScan(0x2A, false, true); downShift = false; }
                        if (downCtrl) { SendScan(0x1D, false, true); downCtrl = false; }
                    }
                    Thread.Sleep(holdMs);
                    n += SendScan(scan, ext, true); downMain = false;
                    Log("tecla scan " + scan.ToString("X2") + (alt ? " +Alt" : "") + (ctrl ? " +Ctrl" : "") + (shift ? " +Shift" : "") + " mantenida " + holdMs + "ms -> foco:" + fg + " insertados=" + n);
                } catch (Exception ex) { Log("tecla: " + ex.Message); }
                finally {
                    try {
                        if (downMain) SendScan(scan, ext, true);
                        if (downAlt) SendScan(0x38, false, true);
                        if (downShift) SendScan(0x2A, false, true);
                        if (downCtrl) SendScan(0x1D, false, true);
                    } catch { }
                }
            }
        });
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
    static void PressChar(char c) {
        try {
            KIn[] d = new KIn[2];
            d[0].type = 1; d[0].wVk = 0; d[0].wScan = (ushort)c; d[0].dwFlags = 0x0004;
            d[1].type = 1; d[1].wVk = 0; d[1].wScan = (ushort)c; d[1].dwFlags = 0x0004 | 0x0002;
            string fg = "?";
            try {
                IntPtr h = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                fg = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            } catch { }
            uint nIns = SendInput(2, d, Marshal.SizeOf(typeof(KIn)));
            Log("caracter '" + c + "' -> foco:" + fg + " insertados=" + nIns);
        } catch (Exception ex) { Log("presschar: " + ex.Message); }
    }

    static string plugDir, logFile;
    static readonly object logLock = new object();
    static void Log(string m) {
        lock (logLock) {
            try {
                var f = new FileInfo(logFile);
                if (f.Exists && f.Length > 200 * 1024) f.Delete();
                File.AppendAllText(logFile, string.Format("{0:HH:mm:ss.fff} [{1}] {2}\r\n", DateTime.Now, System.Diagnostics.Process.GetCurrentProcess().Id, m));
            } catch { }
        }
    }

    // ---- ventanillas: bindings del perfil del juego ----
    // SCS solo lee controls.sii al arrancar. Si lwin*/rwin* no tienen tecla,
    // el plugin agrega un binding seguro al perfil activo y lo reutiliza.
    // Nunca pisa un binding existente.
    class KeyInfo {
        public byte Scan; public bool Ext;
        public KeyInfo(byte scan, bool ext) { Scan = scan; Ext = ext; }
    }
    class WinBind {
        public string Action, Key;
        public byte Scan;
        public bool Ext, Alt, Ctrl, Shift;
        public string Expr;
    }
    static readonly object winLock = new object();
    static readonly Dictionary<string, WinBind> winBinds = new Dictionary<string, WinBind>(StringComparer.OrdinalIgnoreCase);
    static readonly string[] winActions = new string[] { "lwinclose", "lwinopen", "rwinclose", "rwinopen" };
    static readonly string[] winKeyCandidates = new string[] {
        "f8", "comma", "minus", "bslash", "slash", "period", "apostrophe",
        "semicolon", "lbracket", "rbracket", "equal", "grave"
    };
    static readonly Dictionary<string, KeyInfo> keyInfo = new Dictionary<string, KeyInfo>(StringComparer.OrdinalIgnoreCase) {
        { "esc", new KeyInfo(0x01, false) }, { "key1", new KeyInfo(0x02, false) }, { "key2", new KeyInfo(0x03, false) },
        { "key3", new KeyInfo(0x04, false) }, { "key4", new KeyInfo(0x05, false) }, { "key5", new KeyInfo(0x06, false) },
        { "key6", new KeyInfo(0x07, false) }, { "key7", new KeyInfo(0x08, false) }, { "key8", new KeyInfo(0x09, false) },
        { "key9", new KeyInfo(0x0A, false) }, { "key0", new KeyInfo(0x0B, false) }, { "minus", new KeyInfo(0x0C, false) },
        { "equal", new KeyInfo(0x0D, false) }, { "backspace", new KeyInfo(0x0E, false) }, { "tab", new KeyInfo(0x0F, false) },
        { "q", new KeyInfo(0x10, false) }, { "w", new KeyInfo(0x11, false) }, { "e", new KeyInfo(0x12, false) },
        { "r", new KeyInfo(0x13, false) }, { "t", new KeyInfo(0x14, false) }, { "y", new KeyInfo(0x15, false) },
        { "u", new KeyInfo(0x16, false) }, { "i", new KeyInfo(0x17, false) }, { "o", new KeyInfo(0x18, false) },
        { "p", new KeyInfo(0x19, false) }, { "lbracket", new KeyInfo(0x1A, false) }, { "rbracket", new KeyInfo(0x1B, false) },
        { "enter", new KeyInfo(0x1C, false) }, { "lctrl", new KeyInfo(0x1D, false) }, { "a", new KeyInfo(0x1E, false) },
        { "s", new KeyInfo(0x1F, false) }, { "d", new KeyInfo(0x20, false) }, { "f", new KeyInfo(0x21, false) },
        { "g", new KeyInfo(0x22, false) }, { "h", new KeyInfo(0x23, false) }, { "j", new KeyInfo(0x24, false) },
        { "k", new KeyInfo(0x25, false) }, { "l", new KeyInfo(0x26, false) }, { "semicolon", new KeyInfo(0x27, false) },
        { "apostrophe", new KeyInfo(0x28, false) }, { "grave", new KeyInfo(0x29, false) }, { "lshift", new KeyInfo(0x2A, false) },
        { "bslash", new KeyInfo(0x2B, false) }, { "z", new KeyInfo(0x2C, false) }, { "x", new KeyInfo(0x2D, false) },
        { "c", new KeyInfo(0x2E, false) }, { "v", new KeyInfo(0x2F, false) }, { "b", new KeyInfo(0x30, false) },
        { "n", new KeyInfo(0x31, false) }, { "m", new KeyInfo(0x32, false) }, { "comma", new KeyInfo(0x33, false) },
        { "period", new KeyInfo(0x34, false) }, { "slash", new KeyInfo(0x35, false) }, { "rshift", new KeyInfo(0x36, false) },
        { "nummultiply", new KeyInfo(0x37, false) }, { "lalt", new KeyInfo(0x38, false) }, { "space", new KeyInfo(0x39, false) },
        { "caps", new KeyInfo(0x3A, false) }, { "f1", new KeyInfo(0x3B, false) }, { "f2", new KeyInfo(0x3C, false) },
        { "f3", new KeyInfo(0x3D, false) }, { "f4", new KeyInfo(0x3E, false) }, { "f5", new KeyInfo(0x3F, false) },
        { "f6", new KeyInfo(0x40, false) }, { "f7", new KeyInfo(0x41, false) }, { "f8", new KeyInfo(0x42, false) },
        { "f9", new KeyInfo(0x43, false) }, { "f10", new KeyInfo(0x44, false) }, { "numlock", new KeyInfo(0x45, false) },
        { "scrollock", new KeyInfo(0x46, false) }, { "num7", new KeyInfo(0x47, false) }, { "num8", new KeyInfo(0x48, false) },
        { "num9", new KeyInfo(0x49, false) }, { "numminus", new KeyInfo(0x4A, false) }, { "num4", new KeyInfo(0x4B, false) },
        { "num5", new KeyInfo(0x4C, false) }, { "num6", new KeyInfo(0x4D, false) }, { "numplus", new KeyInfo(0x4E, false) },
        { "num1", new KeyInfo(0x4F, false) }, { "num2", new KeyInfo(0x50, false) }, { "num3", new KeyInfo(0x51, false) },
        { "num0", new KeyInfo(0x52, false) }, { "numperiod", new KeyInfo(0x53, false) }, { "f11", new KeyInfo(0x57, false) },
        { "f12", new KeyInfo(0x58, false) }, { "numenter", new KeyInfo(0x1C, true) }, { "rctrl", new KeyInfo(0x1D, true) },
        { "numslash", new KeyInfo(0x35, true) }, { "ralt", new KeyInfo(0x38, true) }, { "home", new KeyInfo(0x47, true) },
        { "uarrow", new KeyInfo(0x48, true) }, { "pgup", new KeyInfo(0x49, true) }, { "larrow", new KeyInfo(0x4B, true) },
        { "rarrow", new KeyInfo(0x4D, true) }, { "end", new KeyInfo(0x4F, true) }, { "darrow", new KeyInfo(0x50, true) },
        { "pgdn", new KeyInfo(0x51, true) }, { "ins", new KeyInfo(0x52, true) }, { "del", new KeyInfo(0x53, true) },
        { "lcmd", new KeyInfo(0x5B, true) }, { "rcmd", new KeyInfo(0x5C, true) }, { "apps", new KeyInfo(0x5D, true) }
    };

    static void ResetWinBinds() {
        lock (winLock) {
            winBinds.Clear();
            WinBind b = new WinBind(); b.Action = "lwinclose"; b.Key = "semicolon"; b.Scan = 0x27; b.Expr = "keyboard.semicolon?0"; winBinds[b.Action] = b;
            b = new WinBind(); b.Action = "lwinopen"; b.Key = "lbracket"; b.Scan = 0x1A; b.Expr = "keyboard.lbracket?0"; winBinds[b.Action] = b;
            b = new WinBind(); b.Action = "rwinclose"; b.Key = "period"; b.Scan = 0x34; b.Expr = "keyboard.period?0"; winBinds[b.Action] = b;
            b = new WinBind(); b.Action = "rwinopen"; b.Key = "minus"; b.Scan = 0x0C; b.Expr = "keyboard.minus?0"; winBinds[b.Action] = b;
        }
    }
    static WinBind GetWinBind(string action) {
        lock (winLock) {
            WinBind b;
            return winBinds.TryGetValue(action, out b) ? b : null;
        }
    }
    static void SetWinBind(WinBind b) {
        lock (winLock) winBinds[b.Action] = b;
    }
    static void RemoveWinBind(string action) {
        lock (winLock) winBinds.Remove(action);
    }
    static WinBind MakeWinBind(string action, string key, bool alt) {
        KeyInfo ki;
        if (!keyInfo.TryGetValue(key, out ki)) return null;
        WinBind b = new WinBind();
        b.Action = action; b.Key = key; b.Scan = ki.Scan; b.Ext = ki.Ext; b.Alt = alt;
        b.Expr = BuildWinExpr(b);
        return b;
    }
    static string BuildWinExpr(WinBind b) {
        string inner = "keyboard." + b.Key + "?0";
        if (b.Alt && !b.Ctrl && !b.Shift) return "modifier(alt_only, " + inner + ")";
        if (b.Ctrl && !b.Alt && !b.Shift) return "modifier(ctrl_only, " + inner + ")";
        if (b.Shift && !b.Alt && !b.Ctrl) return "modifier(shift_only, " + inner + ")";
        if (b.Ctrl && b.Shift && !b.Alt) return "modifier(ctr_shf, " + inner + ")";
        return "modifier(no_modifier, " + inner + ")";
    }
    static string GetMixExpr(string text, string action) {
        Match m = Regex.Match(text, "(?m)^\\s*config_lines\\[\\d+\\]:\\s*\"mix\\s+" + Regex.Escape(action) + "\\s+`([^`]*)`\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
    static WinBind ParseWinBinding(string action, string expr) {
        Match km = Regex.Match(expr, @"keyboard\.([a-z0-9_]+)", RegexOptions.IgnoreCase);
        if (!km.Success) return null;
        string key = km.Groups[1].Value.ToLowerInvariant();
        if (key == "backslash") key = "bslash";
        KeyInfo ki;
        if (!keyInfo.TryGetValue(key, out ki)) { Log("ventanas: tecla desconocida " + key); return null; }
        bool alt = false, ctrl = false, shift = false;
        string pre = expr.Substring(0, km.Index);
        int mi = pre.LastIndexOf("modifier(", StringComparison.OrdinalIgnoreCase);
        if (mi >= 0) {
            string after = pre.Substring(mi + 9);
            int comma = after.IndexOf(',');
            if (comma > 0) {
                string spec = after.Substring(0, comma).Trim().ToLowerInvariant();
                if (spec == "alt_only") alt = true;
                else if (spec == "ctrl_only") ctrl = true;
                else if (spec == "shift_only") shift = true;
                else if (spec == "ctr_shf" || spec == "ctrl_shift" || spec == "shift_ctrl") { ctrl = true; shift = true; }
                else if (spec == "no_modifier") { }
                else { Log("ventanas: modificador no soportado '" + spec + "' en " + action); return null; }
            }
        }
        WinBind b = new WinBind();
        b.Action = action; b.Key = key; b.Scan = ki.Scan; b.Ext = ki.Ext; b.Alt = alt; b.Ctrl = ctrl; b.Shift = shift; b.Expr = expr.Trim();
        return b;
    }
    static void LoadWindowBindings(string path, bool verbose) {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); } catch { return; }
        foreach (string action in winActions) {
            string expr = GetMixExpr(text, action);
            WinBind b = expr == null ? null : ParseWinBinding(action, expr);
            if (b != null) {
                SetWinBind(b);
                if (verbose) Log("ventanas: " + action + " -> " + b.Key + (b.Alt ? " +Alt" : "") + (b.Ctrl ? " +Ctrl" : "") + (b.Shift ? " +Shift" : ""));
            } else {
                RemoveWinBind(action);
                if (verbose) Log("ventanas: " + action + " sin binding de teclado");
            }
        }
    }

    static void AddUnique(List<string> list, string path) {
        if (string.IsNullOrEmpty(path)) return;
        foreach (string x in list) if (string.Equals(x, path, StringComparison.OrdinalIgnoreCase)) return;
        list.Add(path);
    }
    static List<string> GetDocumentRoots() {
        List<string> roots = new List<string>();
        AddUnique(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddUnique(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"));
        string od = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(od)) {
            AddUnique(roots, Path.Combine(od, "Documentos"));
            AddUnique(roots, Path.Combine(od, "Documents"));
        }
        return roots;
    }
    static List<string> FindControlsFiles() {
        List<string> files = new List<string>();
        foreach (string root in GetDocumentRoots()) {
            foreach (string game in new string[] { "Euro Truck Simulator 2", "American Truck Simulator" }) {
                string gdir = Path.Combine(root, game);
                if (!Directory.Exists(gdir)) continue;
                foreach (string sub in new string[] { "profiles", "steam_profiles" }) {
                    string pdir = Path.Combine(gdir, sub);
                    if (!Directory.Exists(pdir)) continue;
                    try {
                        foreach (string profile in Directory.GetDirectories(pdir)) {
                            string f = Path.Combine(profile, "controls.sii");
                            if (File.Exists(f)) AddUnique(files, f);
                        }
                    } catch { }
                    string direct = Path.Combine(pdir, "controls.sii");
                    if (File.Exists(direct)) AddUnique(files, direct);
                }
            }
        }
        return files;
    }
    static string NewestControlsFile(List<string> files) {
        string best = null;
        DateTime bt = DateTime.MinValue;
        foreach (string f in files) {
            try {
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > bt) { bt = t; best = f; }
            } catch { }
        }
        return best;
    }
    static string HexName(string s) {
        byte[] b = Encoding.UTF8.GetBytes(s);
        StringBuilder sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("X2"));
        return sb.ToString();
    }
    static void ScanProfileChunk(FileStream fs, long start, long end, ref string last) {
        if (end <= start) return;
        int size = (int)Math.Min(end - start, 4L * 1024 * 1024);
        byte[] buf = new byte[size];
        fs.Seek(start, SeekOrigin.Begin);
        int read = 0;
        while (read < size) {
            int n = fs.Read(buf, read, size - read);
            if (n <= 0) break;
            read += n;
        }
        string chunk = Encoding.UTF8.GetString(buf, 0, read);
        foreach (string line in chunk.Split('\n')) {
            Match m = Regex.Match(line, @"(?:New profile selected|Set profile finished):\s*'([^']+)'");
            if (m.Success) last = m.Groups[1].Value;
        }
    }
    static string LastProfileName(string log) {
        string last = null;
        try {
            using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                long len = fs.Length;
                const long head = 128 * 1024;
                const long tail = 4 * 1024 * 1024;
                if (len <= head + tail) {
                    ScanProfileChunk(fs, 0, len, ref last);
                } else {
                    ScanProfileChunk(fs, 0, head, ref last);
                    ScanProfileChunk(fs, len - tail, len, ref last);
                }
            }
        } catch { }
        return last;
    }
    static string FindActiveControls(string game) {
        foreach (string root in GetDocumentRoots()) {
            string gdir = Path.Combine(root, game);
            string log = Path.Combine(gdir, "game.log.txt");
            if (!File.Exists(log)) continue;
            string name = LastProfileName(log);
            if (string.IsNullOrEmpty(name)) continue;
            string hex = HexName(name);
            List<string> cand = new List<string>();
            foreach (string sub in new string[] { "steam_profiles", "profiles" }) {
                string f = Path.Combine(gdir, sub, hex, "controls.sii");
                if (File.Exists(f)) cand.Add(f);
            }
            if (cand.Count > 0) return NewestControlsFile(cand);
        }
        return null;
    }
    static string FindActiveControlsForRunningGame() {
        try {
            bool ets = System.Diagnostics.Process.GetProcessesByName("eurotrucks2").Length > 0;
            bool ats = System.Diagnostics.Process.GetProcessesByName("amtrucks").Length > 0;
            if (ets && !ats) return FindActiveControls("Euro Truck Simulator 2");
            if (ats && !ets) return FindActiveControls("American Truck Simulator");
            string a = FindActiveControls("Euro Truck Simulator 2");
            string b = FindActiveControls("American Truck Simulator");
            if (a == null) return b;
            if (b == null) return a;
            return File.GetLastWriteTimeUtc(a) >= File.GetLastWriteTimeUtc(b) ? a : b;
        } catch { return null; }
    }
    static bool IsGameRunning() {
        try {
            return System.Diagnostics.Process.GetProcessesByName("eurotrucks2").Length > 0 ||
                   System.Diagnostics.Process.GetProcessesByName("amtrucks").Length > 0;
        } catch { return true; }
    }

    static bool PatchWindowFile(string path, Dictionary<string, WinBind> auto, bool verbose) {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) { if (verbose) Log("ventanas: leer " + path + ": " + ex.Message); return false; }
        bool touched = false;
        List<string> patched = new List<string>();
        foreach (string action in winActions) {
            Match m = Regex.Match(text, @"(?m)^(\s*config_lines\[\d+\]:\s*""mix\s+" + Regex.Escape(action) + @"\s+`)([^`]*)(`"")", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string expr = m.Groups[2].Value;
            if (Regex.IsMatch(expr, @"keyboard\.[a-z0-9_]+", RegexOptions.IgnoreCase)) continue;
            WinBind b;
            if (!auto.TryGetValue(action, out b) || b == null) continue;
            string newexpr = b.Expr + " | " + expr;
            text = text.Substring(0, m.Index) + m.Groups[1].Value + newexpr + m.Groups[3].Value + text.Substring(m.Index + m.Length);
            touched = true;
            patched.Add(action);
        }
        if (!touched) return false;
        foreach (string action in patched) {
            string expr = GetMixExpr(text, action);
            if (expr == null || !Regex.IsMatch(expr, @"keyboard\.[a-z0-9_]+", RegexOptions.IgnoreCase)) {
                Log("ventanas: validacion fallida (" + action + ") en " + path);
                return false;
            }
        }
        if (IsGameRunning()) {
            if (verbose) Log("ventanas: el juego arranco durante el parcheo; no se escribe " + path);
            return false;
        }
        string bak = path + ".hs-window-backup";
        try { if (!File.Exists(bak)) File.Copy(path, bak, false); }
        catch (Exception ex) { if (verbose) Log("ventanas: backup " + ex.Message); }
        string tmp = path + ".hs-window-tmp";
        try {
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
            if (verbose) Log("ventanas: bindings agregados en " + path);
            return true;
        } catch (Exception ex) {
            Log("ventanas: escribir " + path + ": " + ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }
    static void AddProfileCopies(List<string> list, string active) {
        AddUnique(list, active);
        try {
            string profileDir = Path.GetDirectoryName(active);
            string hex = Path.GetFileName(profileDir);
            string gameDir = Path.GetDirectoryName(Path.GetDirectoryName(profileDir));
            if (gameDir == null) return;
            foreach (string sub in new string[] { "profiles", "steam_profiles" }) {
                string f = Path.Combine(gameDir, sub, hex, "controls.sii");
                if (File.Exists(f)) AddUnique(list, f);
            }
        } catch { }
    }
    static bool NeedsWindowKey(string text, string action) {
        string expr = GetMixExpr(text, action);
        return expr == null || !Regex.IsMatch(expr, @"keyboard\.[a-z0-9_]+", RegexOptions.IgnoreCase);
    }
    static Dictionary<string, WinBind> BuildAutoWindowBinds(string path, bool verbose) {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) { if (verbose) Log("ventanas: leer " + path + ": " + ex.Message); return null; }
        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"keyboard\.([a-z0-9_]+)", RegexOptions.IgnoreCase))
            used.Add(m.Groups[1].Value);
        List<string> free = new List<string>();
        foreach (string k in winKeyCandidates) if (!used.Contains(k)) free.Add(k);
        bool needLClose = NeedsWindowKey(text, "lwinclose");
        bool needLOpen = NeedsWindowKey(text, "lwinopen");
        bool needRClose = NeedsWindowKey(text, "rwinclose");
        bool needROpen = NeedsWindowKey(text, "rwinopen");
        Dictionary<string, WinBind> auto = new Dictionary<string, WinBind>(StringComparer.OrdinalIgnoreCase);
        if (!needLClose && !needLOpen && !needRClose && !needROpen) return auto;
        int fi = 0;
        if (needLClose || needLOpen) {
            if (fi < free.Count) {
                string key = free[fi++];
                if (needLClose) auto["lwinclose"] = MakeWinBind("lwinclose", key, false);
                if (needLOpen) auto["lwinopen"] = MakeWinBind("lwinopen", key, true);
            } else if (verbose) Log("ventanas: sin tecla libre para ventanilla izquierda");
        }
        if (needRClose) {
            if (fi < free.Count) auto["rwinclose"] = MakeWinBind("rwinclose", free[fi++], true);
            else if (verbose) Log("ventanas: sin tecla libre para cerrar ventanilla derecha");
        }
        if (needROpen) {
            if (fi < free.Count) auto["rwinopen"] = MakeWinBind("rwinopen", free[fi++], true);
            else if (verbose) Log("ventanas: sin tecla libre para abrir ventanilla derecha");
        }
        if (verbose) {
            foreach (string a in winActions) {
                WinBind b;
                if (auto.TryGetValue(a, out b) && b != null)
                    Log("ventanas: " + path + " -> " + a + " = " + b.Key + (b.Alt ? " +Alt" : ""));
            }
        }
        return auto;
    }
    static bool EnsureWindowBindings(bool verbose) {
        bool changed = false;
        try {
            bool running = IsGameRunning();
            List<string> files = FindControlsFiles();
            if (files.Count == 0) {
                if (verbose) Log("ventanas: no se encontro controls.sii");
                return false;
            }
            List<string> targets = new List<string>();
            string ets = FindActiveControls("Euro Truck Simulator 2");
            string ats = FindActiveControls("American Truck Simulator");
            if (ets != null) AddProfileCopies(targets, ets);
            if (ats != null) AddProfileCopies(targets, ats);
            if (targets.Count == 0) targets = files;

            if (running) {
                if (verbose) Log("ventanas: juego en ejecucion; no se modifica controls.sii");
            } else {
                foreach (string f in targets) {
                    Dictionary<string, WinBind> auto = BuildAutoWindowBinds(f, verbose);
                    if (auto != null && auto.Count > 0 && PatchWindowFile(f, auto, verbose)) changed = true;
                }
            }

            string active = FindActiveControlsForRunningGame();
            if (active == null) active = NewestControlsFile(targets);
            if (active == null) active = NewestControlsFile(files);
            if (active != null) {
                if (verbose) Log("ventanas: perfil activo " + active);
                LoadWindowBindings(active, verbose);
            }
        } catch (Exception ex) { Log("ventanas: " + ex.Message); }
        return changed;
    }
    static void RefreshWindowBindings() {
        try {
            string active = FindActiveControlsForRunningGame();
            if (active == null) active = NewestControlsFile(FindControlsFiles());
            if (active != null) LoadWindowBindings(active, false);
        } catch { }
    }
    static void WindowBindingWatch() {
        while (true) {
            try {
                if (!IsGameRunning()) {
                    bool changed = EnsureWindowBindings(false);
                    if (changed) Log("ventanas: bindings aplicados; reinicia el juego si estaba abierto");
                }
            } catch (Exception ex) { Log("ventanas-watch: " + ex.Message); }
            Thread.Sleep(15000);
        }
    }

    // ---- snapshot ----
    class Snap {
        public bool active, paused, ats;
        public float? speed, rpm, fuel, fuelCap, range, cons, cruise, limit, rpmMax;
        public float? routeD, routeT;
        public uint? planned;
        public int? gear;
        public bool park, motorBrake, blinkL, blinkR, lowB, highB, brake, engine, cruiseOn, parking, hazard, fuelWarn, onJob, beacon, diffLock, trailerAttached;
        public int wipers;
        public float? truckDmg, trailerDmg, cargoDmg, engineWear;
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
            s.motorBrake = RB(a, O_MOTOR); s.engine = RB(a, O_ENG); s.parking = RB(a, O_PARKING);
            s.hazard = RB(a, O_HAZ); s.fuelWarn = RB(a, O_FWARN); s.beacon = RB(a, O_BEACON); s.diffLock = RB(a, O_DIFF);
            s.wipers = a.ReadByte(O_WIPERS);
            float? ew = RF(a, O_WENG);
            if (ew != null) s.engineWear = ew * 100;
            s.onJob = RB(a, O_ONJOB); s.planned = RU(a, O_PLANNED);
            s.routeD = RF(a, O_ROUTED); s.routeT = RF(a, O_ROUTET);
            float? mx = null;
            foreach (var v in new float?[] { RF(a, O_WENG), RF(a, O_WTRA), RF(a, O_WCAB), RF(a, O_WCHA), RF(a, O_WWHE) })
                if (v != null && (mx == null || v > mx)) mx = v;
            if (mx != null) s.truckDmg = mx * 100;
            bool att = RB(a, O_TATT);
            s.trailerAttached = att;
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
        if (v >= 90) return ((int)(v / 60)).ToString() + "m";
        return ((int)v).ToString() + "s";
    }

    // ---- pulso y paleta de alertas ----
    static readonly Color PulseRed = Color.FromArgb(255, 60, 45);
    static readonly Color PulseAmber = Color.FromArgb(255, 160, 30);
    static readonly Color PulseGreen = Color.FromArgb(255, 0, 235, 110);
    static readonly Color PulseCyan = Color.FromArgb(255, 0, 220, 255);

    // Un solo pulso de 1 s entre 0,35 y 1,0: todas las alertas respiran al
    // mismo ritmo. Se muestrea con Tick100, igual que antes, para no subir CPU.
    static float PulseAlpha() {
        double ms = Environment.TickCount % 1000;
        if (ms < 0) ms += 1000;
        return (float)(0.35 + 0.65 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ms / 1000.0)));
    }
    // Parpadeo urgente (2 Hz) para el exceso de limite; la clave usa Tick250.
    static bool UrgentBlink() { return (Environment.TickCount / 250) % 2 == 0; }

    // ---- clave de estado para render bajo demanda ----
    // Solo incluye lo que cambia los pixeles (valores ya redondeados como se
    // muestran). Las animaciones agregan un bucket de tiempo.
    static string R0(float? v) { return v == null ? "--" : Math.Round(v.Value).ToString(CultureInfo.InvariantCulture); }
    static string R1(float? v) { return v == null ? "--" : Math.Round(v.Value, 1).ToString(CultureInfo.InvariantCulture); }
    static string R0(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }
    static string Kmh(float? ms) { return ms == null ? "--" : Math.Abs(Math.Round(ms.Value * 3.6)).ToString(CultureInfo.InvariantCulture); }
    static string Bit(bool b) { return b ? "1" : "0"; }
    static string Tick100() { return (Environment.TickCount / 100).ToString(CultureInfo.InvariantCulture); }
    static string Tick250() { return (Environment.TickCount / 250).ToString(CultureInfo.InvariantCulture); }
    static string RpmZone(Snap s) {
        if (s.rpm == null || s.rpmMax == null || s.rpmMax <= 0) return "-";
        double f = s.rpm.Value / s.rpmMax.Value;
        if (f >= 0.9) return "R";
        if (f >= 0.75) return "O";
        if (f >= 0.6) return "Y";
        return "G";
    }
    static string FuelKey(Snap s) {
        double frac = 0;
        if (s.fuel != null && s.fuelCap != null && s.fuelCap > 0) frac = s.fuel.Value / s.fuelCap.Value;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        bool warn = s.fuelWarn || (s.active && frac < 0.12);
        return R0(s.range) + "," + ((int)(60 * frac)) + "," + Bit(warn);
    }
    static string DamageKey(Snap s) {
        return R0(s.truckDmg) + DmgCol(s.truckDmg) + "|" + R0(s.trailerDmg) + DmgCol(s.trailerDmg) + "|" + R0(s.cargoDmg) + DmgCol(s.cargoDmg);
    }
    static string CruiseKey(Snap s) {
        double setKmh = s.cruise == null ? 0 : s.cruise.Value * 3.6;
        double actualKmh = s.speed == null ? 0 : s.speed.Value * 3.6;
        bool hasSetSpeed = s.cruise != null && s.cruise > 0.5;
        bool active = s.cruiseOn && hasSetSpeed;
        bool inTolerance = active && s.speed != null && Math.Abs(actualKmh - setKmh) <= 5.0;
        return Kmh(s.cruise) + "," + Bit(hasSetSpeed) + "," + Bit(active) + "," + Bit(inTolerance) + "," + Bit(s.speed != null);
    }
    static string LimitKey(Snap s) {
        double limitKmh = s.limit == null ? 0 : s.limit.Value * 3.6;
        double speedKmh = s.speed == null ? 0 : Math.Abs(s.speed.Value * 3.6);
        bool valid = s.limit != null && s.limit > 0.5;
        bool exceeded = valid && speedKmh > limitKmh + 1.0;
        return Kmh(s.limit) + "," + Bit(valid) + "," + Bit(exceeded) + (exceeded ? "," + Tick250() : "");
    }
    static string RouteKey(Snap s) {
        if (!s.onJob || s.planned == null || s.routeD == null || s.planned <= 0) return "nojob";
        double remainingKm = s.routeD.Value / 1000.0;
        double travelledKm = Math.Max(0, s.planned.Value - remainingKm);
        return R0(travelledKm) + "," + R0(remainingKm) + "," + FmtETA(s.routeT) + "," + s.planned.Value;
    }
    static string StateKey(string metric, Snap s, string theme) {
        // El nombre de la metrica forma parte de la clave: si una accion generica
        // cambia de metrica, nunca puede reutilizar la imagen de la anterior.
        string p = metric + "|" + Bit(s.active) + theme + "|";
        switch (metric) {
            case "enginebtn": return p + Bit(s.engine);
            case "radiobtn": return p + Bit(radioOn) + (radioOn ? Tick100() : "");
            case "checkengine": {
                    bool on = s.active && s.engine && s.engineWear != null && s.engineWear.Value >= 5.0f;
                    return p + Bit(on) + (on ? Tick100() : "");
                }
            case "enginerange": return p + Bit(s.engine) + R0(s.range);
            case "enginegear": return p + Bit(s.engine) + FmtGear(s.gear);
            case "windowleft": return p + Bit(windowLeftOpen);
            case "windowright": return p + Bit(windowRightOpen);
            case "difflock": return p + Bit(s.diffLock) + (s.diffLock ? Tick100() : "");
            case "trailerattach": return p + Bit(s.trailerAttached) + (s.trailerAttached ? Tick100() : "");
            case "emergencybrake": return p + Bit(s.park) + (s.park ? Tick100() : "");
            case "cruise": return p + Kmh(s.cruise) + Bit(s.cruise != null && s.cruise > 0.5) + Bit(s.cruiseOn) + (s.cruiseOn ? Tick100() : "");
            case "hazbtn": return p + Bit(s.hazard) + Bit(s.blinkL) + Bit(s.blinkR);
            case "blinkLbtn": return p + Bit(s.blinkL);
            case "blinkRbtn": return p + Bit(s.blinkR);
            case "lightbtn": return p + Bit(s.lowB) + Bit(s.parking);
            case "highbtn": return p + Bit(HighShown(s));
            case "beaconbtn": return p + Bit(s.beacon) + (s.beacon ? Tick100() : "");
            case "wiperbtn": return p + s.wipers + "," + wiperMode;
            case "lowB": return p + Bit(s.lowB);
            case "highB": return p + Bit(HighShown(s));
            case "blinkL": return p + Bit(s.blinkL);
            case "blinkR": return p + Bit(s.blinkR);
            case "hazard": return p + Bit(s.hazard);
            case "brake": return p + Bit(s.brake);
            case "park": return p + Bit(s.park) + (s.park ? Tick100() : "");
            case "engine": return p + Bit(s.engine);
            case "turn": return p + Bit(s.hazard) + Bit(s.blinkL) + Bit(s.blinkR);
            case "beams": return p + Bit(HighShown(s)) + Bit(s.lowB) + Bit(s.parking);
            case "auto": return p + Bit(s.hazard) + Bit(s.blinkL) + Bit(s.blinkR) + Bit(s.brake) + Bit(HighShown(s)) + Bit(s.lowB) + Bit(s.parking) + Bit(s.park);
            case "lights": return p + Bit(s.lowB) + Bit(s.highB) + Bit(s.parking) + Bit(s.blinkL) + Bit(s.blinkR) + Bit(s.brake) + Bit(s.motorBrake) + Bit(s.engine) + Bit(s.cruiseOn);
            case "speed": return p + Kmh(s.speed);
            case "gear": return p + FmtGear(s.gear);
            case "rpm": return p + R0(s.rpm) + R0(s.rpmMax) + FmtGear(s.gear) + RpmZone(s);
            case "fuel": return p + FuelKey(s);
            case "range": return p + R0(s.range);
            case "cons": return p + R1(s.cons * 100);
            case "cruiseSpeed": return p + CruiseKey(s);
            case "limit": return p + LimitKey(s);
            case "watertemp": return p + R0(s.waterTemp) + Bit(s.waterWarn != null && s.waterTemp != null && s.waterTemp >= s.waterWarn) + R0(s.waterWarn);
            case "oiltemp": return p + R0(s.oilTemp) + Bit(s.oilTemp != null && s.oilTemp >= 130.0f);
            case "airpressure": return p + R0(s.airPressure) + Bit((s.airEmergency != null && s.airPressure != null && s.airPressure <= s.airEmergency) || (s.airWarn != null && s.airPressure != null && s.airPressure <= s.airWarn)) + R0(s.airWarn);
            case "damage": return p + DamageKey(s);
            case "route": return p + RouteKey(s);
            default: return p + "static";
        }
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
    static readonly Dictionary<string, Image> imgs = new Dictionary<string, Image>();     // 72x72
    static readonly Dictionary<string, Image> imgsBig = new Dictionary<string, Image>();  // original 144x144
    static readonly Dictionary<string, string> iconB64 = new Dictionary<string, string>();
    static string blackB64;
    static string hazOffB64, hazOnB64;
    static void Preload() {
        foreach (var n in new string[] { "left", "right", "hazard", "low", "high", "brake", "park", "beacon", "cruise", "wiper", "diff", "emergency-brake", "coupling", "parking-brake-1", "window-left-down", "window-left-up", "window-right-down", "window-right-up", "engine", "radio" })
            foreach (var suf in new string[] { "", "-c" }) {
                string p = Path.Combine(plugDir, "pk-" + n + suf + ".png");
                if (!File.Exists(p)) continue;
                try {
                    string key = n + suf;
                    Image big = Image.FromFile(p);
                    imgsBig[key] = big;
                    // Preescalado unico 144->72 con la interpolacion por defecto:
                    // mismo resultado que dibujar el original en cada frame.
                    var small = new Bitmap(72, 72);
                    using (var g = Graphics.FromImage(small)) {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;
                        g.DrawImage(big, 0, 0, 72, 72);
                    }
                    imgs[key] = small;
                    // Tile de icono ya compuesto y codificado (RenderPack).
                    using (var tile = NewBmp())
                    using (var g = Graphics.FromImage(tile)) {
                        g.Clear(Color.Black);
                        g.DrawImage(big, 0, 0, 72, 72);
                        iconB64[key] = B64(tile);
                    }
                } catch { }
            }
        using (var tile = NewBmp())
        using (var g = Graphics.FromImage(tile)) {
            g.Clear(Color.Black);
            blackB64 = B64(tile);
        }
        hazOffB64 = BuildHaz(false);
        hazOnB64 = BuildHaz(true);
    }
    static string RenderPack(string name, bool on) {
        string b64;
        if (iconB64.TryGetValue(on ? name + "-c" : name, out b64)) return b64;
        return blackB64;
    }
    static string RenderBig(string top, string label, string colorName, string sub) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            Color col = Color.FromName(colorName);
            var sf = CenterSF;
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
            var sf = CenterSF;
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
            var sf = CenterSF;
            double limitKmh = s.limit == null ? 0 : s.limit.Value * 3.6;
            double speedKmh = s.speed == null ? 0 : Math.Abs(s.speed.Value * 3.6);
            bool valid = s.limit != null && s.limit > 0.5;
            bool exceeded = valid && speedKmh > limitKmh + 1.0;
            Color ringColor = exceeded && UrgentBlink() ? PulseAmber : PulseRed;
            using (var fLabel = new Font("Arial", 8, FontStyle.Bold))
            using (var fNumber = new Font("Arial", 24, FontStyle.Bold))
            using (var pen = new Pen(ringColor, 3))
            using (var numberBrush = new SolidBrush(exceeded ? PulseRed : Color.White)) {
                g.DrawString("LIMITE", fLabel, Brushes.White, 36, 3, sf);
                g.DrawEllipse(pen, 8, 15, 56, 54);
                string value = valid ? Math.Round(limitKmh).ToString() : "--";
                g.DrawString(value, fNumber, numberBrush, 36, 24, sf);
            }
            return B64(bmp);
        }
    }
    static string RenderGauge(string label, float? value, string unit, bool warning, float min, float max, float warnAt, bool lowIsBad) {
        float warnFrac = (warnAt - min) / (max - min);
        if (warnFrac < 0) warnFrac = 0;
        if (warnFrac > 1) warnFrac = 1;
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = CenterSF;
            Color color = warning ? Color.Red : Color.Lime;
            const float cx = 36f, cy = 36f, r = 31f;
            using (var track = new Pen(Color.FromArgb(42, 42, 42), 4f))
                g.DrawArc(track, cx - r, cy - r, r * 2, r * 2, 135f, 270f);
            // Zona de peligro: al final para temperatura, al principio para aire.
            using (var danger = new Pen(Color.FromArgb(100, 255, 45, 45), 4f)) {
                if (lowIsBad) g.DrawArc(danger, cx - r, cy - r, r * 2, r * 2, 135f, 270f * warnFrac);
                else g.DrawArc(danger, cx - r, cy - r, r * 2, r * 2, 135f + 270f * warnFrac, 270f * (1f - warnFrac));
            }
            if (value != null) {
                float frac = (value.Value - min) / (max - min);
                if (frac < 0) frac = 0;
                if (frac > 1) frac = 1;
                if (frac > 0.002f) {
                    using (var prog = new Pen(color, 4f))
                        g.DrawArc(prog, cx - r, cy - r, r * 2, r * 2, 135f, 270f * frac);
                }
            }
            // Marca de umbral sobre el anillo.
            double ang = (135.0 + 270.0 * warnFrac) * Math.PI / 180.0;
            using (var tick = new Pen(Color.FromArgb(210, 255, 255, 255), 1.6f)) {
                g.DrawLine(tick,
                    (float)(cx + Math.Cos(ang) * (r - 6)), (float)(cy + Math.Sin(ang) * (r - 6)),
                    (float)(cx + Math.Cos(ang) * (r + 1)), (float)(cy + Math.Sin(ang) * (r + 1)));
            }
            using (var fLabel = new Font("Arial", 6, FontStyle.Bold))
            using (var fValue = new Font("Arial", 22, FontStyle.Bold))
            using (var fUnit = new Font("Arial", 8, FontStyle.Bold))
            using (var bValue = new SolidBrush(color)) {
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
            var sf = CenterSF;
            // Barra de 60x4: conserva el numero grande (18 pt) y muestra el
            // avance hacia el corte. El ultimo 10 % es la zona roja.
            const int barX = 6, barY = 64, barW = 60, barH = 4;
            using (var track = new SolidBrush(Color.FromArgb(45, 45, 45)))
                g.FillRectangle(track, barX, barY, barW, barH);
            using (var redline = new SolidBrush(Color.FromArgb(110, 255, 45, 45)))
                g.FillRectangle(redline, barX + (int)(barW * 0.9f), barY, barW - (int)(barW * 0.9f), barH);
            if (s.rpm != null && s.rpmMax != null && s.rpmMax > 0) {
                float frac = s.rpm.Value / s.rpmMax.Value;
                if (frac < 0) frac = 0;
                if (frac > 1) frac = 1;
                int fill = (int)Math.Round(barW * frac);
                if (fill > 0) {
                    using (var prog = new SolidBrush(Color.FromName(zc)))
                        g.FillRectangle(prog, barX, barY, fill, barH);
                }
            }
            using (var fN = new Font("Arial", 18, FontStyle.Bold))
            using (var fL = new Font("Arial", 9, FontStyle.Bold))
            using (var fG = new Font("Arial", 15, FontStyle.Bold))
            using (var fC = new Font("Arial", 11, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromName(zc)))
            using (var bg2 = new SolidBrush(Color.FromName(gearCol))) {
                g.DrawString(t, fN, br, 36, 2, sf);
                g.DrawString("RPM", fL, Brushes.White, 36, 28, sf);
                g.DrawString(gearTxt, (gearTxt == "CAMBIA" ? fC : fG), bg2, 36, (gearTxt == "CAMBIA" ? 45 : 42), sf);
            }
            return B64(bmp);
        }
    }
    static string RenderFuel(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var sf = CenterSF;
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
            var sf = CenterSF;
            using (var fP = new Font("Arial", 15, FontStyle.Bold))
            using (var fP100 = new Font("Arial", 12, FontStyle.Bold))
            using (var iconBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
            using (var gry = new SolidBrush(Color.FromArgb(90, 90, 90))) {
                string[] kinds = { "truck", "trailer", "cargo" };
                float?[] vals = { s.truckDmg, s.trailerDmg, s.cargoDmg };
                int y = 2;
                for (int i = 0; i < 3; i++) {
                    string col = vals[i] == null ? null : DmgCol(vals[i]);
                    // Barra vertical de severidad proporcional: en danos bajos
                    // se reduce a un punto y no compite con el porcentaje.
                    using (var track = new SolidBrush(Color.FromArgb(40, 40, 40)))
                        g.FillRectangle(track, 0, y + 2, 3, 18);
                    if (vals[i] != null) {
                        double d = vals[i].Value;
                        if (d < 0) d = 0;
                        if (d > 100) d = 100;
                        int h = (int)Math.Round(18 * d / 100.0);
                        if (h < 2) h = 2;
                        using (var fill = new SolidBrush(Color.FromName(col)))
                            g.FillRectangle(fill, 0, y + 2 + 18 - h, 3, h);
                    }
                    DrawDmgIcon(g, kinds[i], 3, y, iconBrush);
                    if (vals[i] != null) {
                        int pct = (int)Math.Round(vals[i].Value);
                        using (var b = new SolidBrush(Color.FromName(col)))
                            g.DrawString(pct + "%", pct >= 100 ? fP100 : fP, b, 48, y + (pct >= 100 ? 4 : 1), sf);
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
            var sf = CenterSF;
            using (var f = new Font("Arial", 10, FontStyle.Bold))
            using (var off = new SolidBrush(Color.FromArgb(70, 70, 70))) {
                string[,] rows = {
                    {"LO", s.lowB ? "1" : null}, {"HI", s.highB ? "1" : null}, {"PK", s.parking ? "1" : null},
                    {"L", s.blinkL ? "1" : null}, {"R", s.blinkR ? "1" : null}, {"BRK", s.brake ? "1" : null},
                    {"MT", s.motorBrake ? "1" : null}, {"ENG", s.engine ? "1" : null}, {"CRZ", s.cruiseOn ? "1" : null}
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
            var sf = CenterSF;
            Image cruiseIcon;
            if (imgs.TryGetValue("cruise", out cruiseIcon)) {
                using (var ia = new ImageAttributes()) {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = 0.32f;
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

            Color statusColor = !active ? PulseRed : (inTolerance ? PulseGreen : PulseAmber);
            Color pillColor = !active ? Color.FromArgb(80, 90, 0, 0) : (inTolerance ? Color.FromArgb(80, 0, 90, 0) : Color.FromArgb(80, 90, 65, 0));
            using (var shade = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (var fSpeed = new Font("Arial", 22, FontStyle.Bold))
            using (var fLabel = new Font("Arial", 7, FontStyle.Bold))
            using (var fStatus = new Font("Arial", 11, FontStyle.Bold))
            using (var status = new SolidBrush(statusColor))
            using (var pill = new SolidBrush(pillColor)) {
                g.FillRectangle(shade, 4, 4, 64, 64);
                g.DrawString(speedText, fSpeed, Brushes.Lime, 36, 8, sf);
                g.DrawString("CRUCERO km/h", fLabel, Brushes.White, 36, 37, sf);
                string statusText = !active ? "OFF" : (s.speed == null ? "--" : (inTolerance ? "OK" : "FUERA"));
                g.FillRectangle(pill, 4, 45, 64, 16);
                g.DrawString(statusText, fStatus, status, 36, 46, sf);
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
                    cm.Matrix33 = 0.35f; // visible detras del numero
                    ia.SetColorMatrix(cm);
                    g.DrawImage(cruiseIcon, new Rectangle(0, 0, 72, 72), 0, 0,
                        cruiseIcon.Width, cruiseIcon.Height, GraphicsUnit.Pixel, ia);
                }
            }
            var sf = CenterSF;
            string value = s.cruise != null && s.cruise > 0.5 ? Math.Round(s.cruise.Value * 3.6).ToString() : "--";
            using (var shade = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            using (var fSpeed = new Font("Arial", 27, FontStyle.Bold))
            using (var fLabel = new Font("Arial", 8, FontStyle.Bold)) {
                g.FillRectangle(shade, 4, 7, 64, 58);
                g.DrawString(value, fSpeed, Brushes.Lime, 36, 10, sf);
                g.DrawString("CRUCERO", fLabel, Brushes.White, 36, 47, sf);
            }
            if (s.cruiseOn) {
                float a = PulseAlpha();
                using (var overlay = new SolidBrush(Color.FromArgb((int)(a * 220), PulseGreen)))
                    g.FillEllipse(overlay, 56, 3, 12, 12);
            }
            return B64(bmp);
        }
    }
    static string RenderRoute(Snap s) {
        if (!s.onJob || s.planned == null || s.routeD == null || s.planned <= 0)
            return RenderBig("--", "SIN VIAJE", "Gray", null);
        double remainingKm = s.routeD.Value / 1000.0;
        double plannedKm = s.planned.Value;
        double travelledKm = Math.Max(0, plannedKm - remainingKm);
        double frac = plannedKm > 0 ? travelledKm / plannedKm : 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        string eta = FmtETA(s.routeT);
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = CenterSF;
            using (var fL = new Font("Arial", 7))
            using (var fN = new Font("Arial", 19, FontStyle.Bold))
            using (var fE = new Font("Arial", 7, FontStyle.Bold))
            using (var cy = new SolidBrush(Color.Cyan))
            using (var gray = new SolidBrush(Color.FromArgb(150, 150, 150)))
            using (var track = new SolidBrush(Color.FromArgb(45, 45, 45)))
            using (var lime = new SolidBrush(Color.Lime)) {
                g.DrawString("FALTAN km", fL, gray, 36, 1, sf);
                g.DrawString(Math.Round(remainingKm).ToString(), fN, cy, 36, 8, sf);
                g.DrawString("RESTAN " + eta, fE, Brushes.White, 36, 35, sf);
                const int barX = 6, barY = 48, barW = 60, barH = 4;
                g.FillRectangle(track, barX, barY, barW, barH);
                int fill = (int)Math.Round(barW * frac);
                if (fill > 0) g.FillRectangle(lime, barX, barY, fill, barH);
                g.DrawString(Math.Round(travelledKm) + " / " + Math.Round(plannedKm) + " km", fL, gray, 36, 56, sf);
            }
            return B64(bmp);
        }
    }
    static string BuildHaz(bool lit) {
        Image L, R;
        string ls = lit ? "left-c" : "left", rs = lit ? "right-c" : "right";
        if (!imgsBig.TryGetValue(ls, out L) || !imgsBig.TryGetValue(rs, out R)) return blackB64;
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            g.DrawImage(L, 2, 12, 32, 48);
            g.DrawImage(R, 38, 12, 32, 48);
            return B64(bmp);
        }
    }
    static string RenderHazBtn(Snap s) {
        bool lit = s.hazard && (s.blinkL || s.blinkR);
        return lit ? hazOnB64 : hazOffB64;
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
                float a = PulseAlpha();
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
            if (imgsBig.TryGetValue(iconName, out icon)) g.DrawImage(icon, 0, 0, 44, 44);
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
                    float alpha = PulseAlpha();
                    var matrix = new ColorMatrix(new float[][] {
                        new float[]{1,0,0,0,0}, new float[]{0,1,0,0,0}, new float[]{0,0,1,0,0},
                        new float[]{0,0,0,alpha,0}, new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrix);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var orange = new SolidBrush(Color.FromArgb((int)(alpha * 210), PulseAmber)))
                        g.FillEllipse(orange, 56, 4, 12, 12);
                } else g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }
    static string RenderTrailerAttach(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            string key = s.trailerAttached ? "coupling-c" : "coupling";
            if (!imgs.TryGetValue(key, out icon)) imgs.TryGetValue("coupling", out icon);
            if (icon != null) {
                if (s.trailerAttached) {
                    float alpha = PulseAlpha();
                    var matrix = new ColorMatrix(new float[][] {
                        new float[]{1,0,0,0,0}, new float[]{0,1,0,0,0}, new float[]{0,0,1,0,0},
                        new float[]{0,0,0,alpha,0}, new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrix);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var green = new SolidBrush(Color.FromArgb((int)(alpha * 210), PulseGreen)))
                        g.FillEllipse(green, 56, 4, 12, 12);
                } else g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }
    static string RenderParkingBrake(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            string key = s.park ? "parking-brake-1-c" : "parking-brake-1";
            if (!imgs.TryGetValue(key, out icon)) imgs.TryGetValue("parking-brake-1", out icon);
            if (icon != null) {
                if (s.park) {
                    float alpha = PulseAlpha();
                    var matrix = new ColorMatrix(new float[][] {
                        new float[]{1,0,0,0,0}, new float[]{0,1,0,0,0}, new float[]{0,0,1,0,0},
                        new float[]{0,0,0,alpha,0}, new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrix);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var red = new SolidBrush(Color.FromArgb((int)(alpha * 210), PulseRed)))
                        g.FillEllipse(red, 56, 4, 12, 12);
                } else g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }
    static bool windowLeftOpen = false;
    static bool windowRightOpen = false;

    static string RenderWindowLeft(Snap s) {
        return RenderWindowIcon("window-left-down", windowLeftOpen);
    }
    static string RenderWindowRight(Snap s) {
        return RenderWindowIcon("window-right-down", windowRightOpen);
    }

    static string RenderWindowIcon(string iconName, bool isOpen) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            if (imgs.TryGetValue(iconName, out icon)) {
                if (isOpen) {
                    var matrixCyan = new ColorMatrix(new float[][] {
                        new float[]{0,0,0,0,0},
                        new float[]{0,1,0,0,0},
                        new float[]{0,0,1,0,0},
                        new float[]{0,0,0,1,0},
                        new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrixCyan);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                } else {
                    g.DrawImage(icon, 0, 0, 72, 72);
                }
            }
            return B64(bmp);
        }
    }

    static bool radioOn = false;

    static string RenderEngineState(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            bool isOn = s.active && s.engine;
            string key = isOn ? "engine-c" : "engine";
            if (!imgs.TryGetValue(key, out icon)) imgs.TryGetValue("engine", out icon);
            if (icon != null) {
                g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }

    static string RenderRadioBtn(Snap s) {
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            Image icon;
            string key = radioOn ? "radio-c" : "radio";
            if (!imgs.TryGetValue(key, out icon)) imgs.TryGetValue("radio", out icon);
            if (icon != null) {
                if (radioOn) {
                    float alpha = PulseAlpha();
                    var matrixCyan = new ColorMatrix(new float[][] {
                        new float[]{0,0,0,0,0}, new float[]{0,alpha,0,0,0}, new float[]{0,0,alpha,0,0},
                        new float[]{0,0,0,1,0}, new float[]{0,0,0,0,1} });
                    using (var ia = new ImageAttributes()) {
                        ia.SetColorMatrix(matrixCyan);
                        g.DrawImage(icon, new Rectangle(0, 0, 72, 72), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var cyan = new SolidBrush(Color.FromArgb((int)(alpha * 210), PulseCyan)))
                        g.FillEllipse(cyan, 56, 4, 12, 12);
                } else g.DrawImage(icon, 0, 0, 72, 72);
            }
            return B64(bmp);
        }
    }

    static string RenderCheckEngine(Snap s) {
        // Mismo criterio que la app Windows: motor encendido y desgaste >= 5 %.
        bool checkEngineActive = s.active && s.engine && s.engineWear != null && s.engineWear.Value >= 5.0f;
        using (var bmp = NewBmp())
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Black);
            if (!checkEngineActive) return B64(bmp);
            
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sf = CenterSF;
            float alpha = PulseAlpha();
            using (var f1 = new Font("Arial", 11, FontStyle.Bold))
            using (var f2 = new Font("Arial", 14, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb((int)(255 * alpha), PulseRed))) {
                g.DrawString("CHECK", f1, br, 36, 12, sf);
                g.DrawString("ENGINE", f2, br, 36, 36, sf);
            }
            return B64(bmp);
        }
    }

    static string RenderEngineRange(Snap s) {
        if (!s.active || !s.engine) {
            using (var bmp = NewBmp()) {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
                return B64(bmp);
            }
        }
        string t = s.range != null ? Math.Round(s.range.Value).ToString() : "--";
        return RenderBig(t, "AUTONOMIA", "green", "km");
    }

    static string RenderEngineGear(Snap s) {
        if (!s.active || !s.engine || s.gear == null) {
            using (var bmp = NewBmp()) {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
                return B64(bmp);
            }
        }
        int gVal = s.gear.Value;
        string gearTxt = "--";
        string colorName = "White";
        if (gVal > 0) { gearTxt = gVal.ToString(); colorName = "White"; }
        else if (gVal == 0) { gearTxt = "N"; colorName = "DodgerBlue"; }
        else { gearTxt = "R"; colorName = "Red"; }

        return RenderBig(gearTxt, "MARCHA", colorName, null);
    }

    static string RenderKey(string metric, Snap s, string colorName) {
        switch (metric) {
            case "enginebtn": return RenderEngineState(s);
            case "radiobtn": return RenderRadioBtn(s);
            case "checkengine": return RenderCheckEngine(s);
            case "enginerange": return RenderEngineRange(s);
            case "enginegear": return RenderEngineGear(s);
            case "windowleft": return RenderWindowLeft(s);
            case "windowright": return RenderWindowRight(s);
            case "difflock": return RenderDiffLock(s);
            case "trailerattach": return RenderTrailerAttach(s);
            case "emergencybrake": return RenderParkingBrake(s);
            case "cruise": return RenderCruiseButton(s);
            case "hazbtn": return RenderHazBtn(s);
            case "blinkLbtn": return RenderPack("left", s.blinkL);
            case "blinkRbtn": return RenderPack("right", s.blinkR);
            case "lightbtn":
                if (s.lowB) return RenderPack("low", true);
                if (s.parking) return RenderPack("park", true);
                return RenderPack("low", false);
            case "highbtn": return RenderPack("high", HighShown(s));
            case "beaconbtn": return RenderBeacon(s);
            case "wiperbtn": return RenderWipers(s);
            case "lowB": return RenderPack("low", s.lowB);
            case "highB": return RenderPack("high", HighShown(s));
            case "blinkL": return RenderPack("left", s.blinkL);
            case "blinkR": return RenderPack("right", s.blinkR);
            case "hazard": return RenderPack("hazard", s.hazard);
            case "brake": return RenderPack("brake", s.brake);
            case "park": return RenderParkingBrake(s);
            case "engine": return RenderEngineState(s);
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
            default:
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
                    case "cruiseSpeed": return RenderCruise(s);
                    case "limit": return RenderSpeedLimit(s);
                    case "watertemp": {
                            bool warn = s.waterWarn != null && s.waterTemp != null && s.waterTemp >= s.waterWarn;
                            return RenderGauge("MOTOR", s.waterTemp, "°C", warn, 40f, 120f, s.waterWarn ?? 105f, false);
                        }
                    case "oiltemp": {
                            bool warn = s.oilTemp != null && s.oilTemp >= 130.0f;
                            return RenderGauge("ACEITE", s.oilTemp, "°C", warn, 40f, 150f, 130f, false);
                        }
                    case "airpressure": {
                            bool warn = (s.airEmergency != null && s.airPressure != null && s.airPressure <= s.airEmergency) ||
                                        (s.airWarn != null && s.airPressure != null && s.airPressure <= s.airWarn);
                            return RenderGauge("AIRE", s.airPressure, "PSI", warn, 0f, 150f, s.airWarn ?? 69.6f, true);
                        }
                    case "damage": return RenderDamage(s);
                    case "route": return RenderRoute(s);
                    default: return RenderBig("--", "SCS", colorName, null);
                }
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
        public string lastKey;
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
        bool test = false, watchHigh = false, winInfo = false, winFix = false;
        int port = 0;
        string uuid = null, regEv = "registerPlugin";
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-Test" || args[i] == "--test") test = true;
            else if (args[i] == "-WatchHigh" || args[i] == "--watch-high") watchHigh = true;
            else if (args[i] == "-WindowsInfo" || args[i] == "--windows-info") winInfo = true;
            else if (args[i] == "-WindowsFix" || args[i] == "--windows-fix") winFix = true;
            else if (args[i] == "-port" && i + 1 < args.Length) port = int.Parse(args[++i]);
            else if (args[i] == "-pluginUUID" && i + 1 < args.Length) uuid = args[++i];
            else if (args[i] == "-registerEvent" && i + 1 < args.Length) regEv = args[++i];
        }
        plugDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        logFile = Path.Combine(plugDir, "backend.log");
        if (winInfo || winFix) return RunWindowsDiag(winFix);
        if (watchHigh) return RunWatchHigh();
        if (test) return RunTest();
        if (port <= 0 || uuid == null) return 1;
        Log("=== inicio ===");
        Preload();
        ResetWinBinds();
        try { EnsureWindowBindings(true); } catch (Exception ex) { Log("ventanas: " + ex.Message); }
        try { var ft = new Thread(new ThreadStart(FlashWatch)); ft.IsBackground = true; ft.Start(); } catch (Exception ex) { Log("flashwatch: " + ex.Message); }
        try { var wt = new Thread(new ThreadStart(WindowBindingWatch)); wt.IsBackground = true; wt.Start(); } catch (Exception ex) { Log("ventanas-watch: " + ex.Message); }
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
        var pressDownTimes = new Dictionary<string, long>();
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
                        Log("willAppear msg: " + msg);
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
                        if (ctxs.Remove(context)) pressDownTimes.Remove(context);
                    } else if (ev == "keyDown") {
                        if (context != "" && ctxs.ContainsKey(context)) {
                            pressDownTimes[context] = sw.ElapsedMilliseconds;
                            if (ctxs[context].metric == "windowleft" || ctxs[context].metric == "windowright") RefreshWindowBindings();
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
                            else if (ctxs[context].metric == "trailerattach") { Log("pulsado acople trailer -> T"); PressKey(0x54); }
                            else if (ctxs[context].metric == "emergencybrake") { Log("pulsado freno estacionamiento -> Space"); PressKey(0x20); }
                            else if (ctxs[context].metric == "cruise") { Log("pulsado crucero -> C"); PressKey(0x43); }
                            else if (ctxs[context].metric == "enginebtn") { Log("pulsado encendido motor -> E"); PressKey(0x45); }
                            else if (ctxs[context].metric == "radiobtn") { 
                                radioOn = !radioOn;
                                Log("pulsado radio -> R (estado: " + radioOn + ")"); 
                                PressKey(0x52); 
                            }
                        }
                    } else if (ev == "keyUp") {
                        if (context != "" && ctxs.ContainsKey(context) && pressDownTimes.ContainsKey(context)) {
                            long duration = sw.ElapsedMilliseconds - pressDownTimes[context];
                            string m = ctxs[context].metric;
                            if (m == "windowleft") {
                                bool up = duration < 400;
                                WinBind wb = GetWinBind(up ? "lwinclose" : "lwinopen");
                                if (wb == null) {
                                    Log("ventanilla izq " + (up ? "tap" : "hold") + ": sin tecla en controls.sii");
                                } else {
                                    Log("ventanilla izq " + (up ? "tap -> subir" : "hold -> bajar") + " (" + wb.Key + (wb.Alt ? " +Alt" : "") + ")");
                                    windowLeftOpen = !up;
                                    PressScanHold(wb.Scan, wb.Ext, wb.Alt, wb.Ctrl, wb.Shift, 1400);
                                }
                            } else if (m == "windowright") {
                                bool up = duration < 400;
                                WinBind wb = GetWinBind(up ? "rwinclose" : "rwinopen");
                                if (wb == null) {
                                    Log("ventanilla der " + (up ? "tap" : "hold") + ": sin tecla en controls.sii");
                                } else {
                                    Log("ventanilla der " + (up ? "tap -> subir" : "hold -> bajar") + " (" + wb.Key + (wb.Alt ? " +Alt" : "") + ")");
                                    windowRightOpen = !up;
                                    PressScanHold(wb.Scan, wb.Ext, wb.Alt, wb.Ctrl, wb.Shift, 1400);
                                }
                            }
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
                string th = kv.Value.theme;
                if (!themes.ContainsKey(th)) th = "green";
                string key = StateKey(kv.Value.metric, s, th);
                string img;
                if (key == kv.Value.lastKey && kv.Value.last != null) {
                    img = kv.Value.last;
                } else {
                    try {
                        img = RenderKey(kv.Value.metric, s, themes[th]);
                    } catch (Exception ex) { Log("render: " + ex.Message); continue; }
                    kv.Value.lastKey = key;
                }
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

    static int RunWindowsDiag(bool fix) {
        ResetWinBinds();
        Console.WriteLine("Ventanas SCS - diagnostico");
        Console.WriteLine("Juego en ejecucion: " + (IsGameRunning() ? "si" : "no"));
        List<string> files = FindControlsFiles();
        Console.WriteLine("Perfiles encontrados: " + files.Count);
        foreach (string f in files) {
            Console.WriteLine("  " + f);
            string text;
            try { text = File.ReadAllText(f, Encoding.UTF8); }
            catch (Exception ex) { Console.WriteLine("    ERROR " + ex.Message); continue; }
            foreach (string a in winActions) {
                string e = GetMixExpr(text, a);
                Console.WriteLine("    " + a + ": " + (e == null ? "(falta)" : e));
            }
        }
        string active = FindActiveControlsForRunningGame();
        Console.WriteLine("Perfil activo: " + (active == null ? "(no detectado)" : active));
        if (active != null) LoadWindowBindings(active, false);
        if (fix) {
            bool changed = EnsureWindowBindings(true);
            Console.WriteLine("Cambios aplicados: " + (changed ? "si" : "no"));
            active = FindActiveControlsForRunningGame();
            if (active != null) LoadWindowBindings(active, false);
        }
        foreach (string a in winActions) {
            WinBind b = GetWinBind(a);
            Console.WriteLine("  " + a + " -> " + (b == null ? "SIN BINDING" : b.Key + (b.Alt ? " +Alt" : "") + (b.Ctrl ? " +Ctrl" : "") + (b.Shift ? " +Shift" : "")));
        }
        return 0;
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
        Console.WriteLine("Fuel: " + s.fuel + " L | Rango: " + s.range + " km | Danos: " + s.truckDmg + "/" + s.trailerDmg + "/" + s.cargoDmg + " | Desgaste motor: " + s.engineWear + " %");
        string dir = Path.Combine(Path.GetTempPath(), "scsmon-test");
        Directory.CreateDirectory(dir);
        foreach (var m in new string[] { "speed", "gear", "rpm", "fuel", "range", "cons", "cruise", "cruiseSpeed", "limit", "watertemp", "oiltemp", "airpressure", "blinkL", "blinkR", "hazard", "lowB", "highB", "brake", "park", "lights", "auto", "turn", "beams", "damage", "route", "hazbtn", "blinkLbtn", "blinkRbtn", "lightbtn", "highbtn", "beaconbtn", "wiperbtn", "difflock", "emergencybrake", "engine", "enginebtn", "checkengine", "enginerange", "enginegear", "trailerattach", "windowleft", "windowright", "radiobtn" }) {
            string img = RenderKey(m, s, "Lime");
            File.WriteAllBytes(Path.Combine(dir, m + ".png"), Convert.FromBase64String(img.Substring(22)));
        }
        Console.WriteLine("PNGs en " + dir);
        return 0;
    }
}
