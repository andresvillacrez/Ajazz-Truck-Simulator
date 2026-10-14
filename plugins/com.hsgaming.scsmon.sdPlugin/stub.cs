using System;
using System.Diagnostics;
using System.IO;
class Stub {
    static int Main() {
        string cmd = Environment.CommandLine.Trim();
        string rest = "";
        if (cmd.StartsWith("\"")) {
            int end = cmd.IndexOf('"', 1);
            if (end >= 0 && end + 1 < cmd.Length) rest = cmd.Substring(end + 1).TrimStart();
        } else {
            int sp = cmd.IndexOf(' ');
            if (sp >= 0) rest = cmd.Substring(sp + 1);
        }
        string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(dir, "telemetry.ps1") + "\" " + rest);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        try {
            using (Process p = Process.Start(psi)) {
                if (p == null) return 1;
                p.WaitForExit();
                return p.ExitCode;
            }
        } catch { return 2; }
    }
}
