using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;

// Self-extracting launcher for ZombieShooter. The game build is embedded as a
// zip resource (game.zip). On run: unpack to %LOCALAPPDATA%\ZombieShooter,
// drop a desktop shortcut, then launch the game. Compiled by Build-Installer.ps1.
static class Setup
{
    static void Main()
    {
        try
        {
            string dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZombieShooter");
            Directory.CreateDirectory(dest);

            var asm = Assembly.GetExecutingAssembly();
            string resName = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith("game.zip", StringComparison.OrdinalIgnoreCase)) { resName = n; break; }
            if (resName == null) throw new Exception("Embedded game.zip not found.");

            using (var rs = asm.GetManifestResourceStream(resName))
            using (var za = new ZipArchive(rs, ZipArchiveMode.Read))
            {
                foreach (var e in za.Entries)
                {
                    // Entry separators may be '/' or '\' depending on the zip tool.
                    string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar)
                                           .Replace('\\', Path.DirectorySeparatorChar);
                    string outPath = Path.Combine(dest, rel);
                    // A directory entry has an empty Name (path ends in a separator).
                    if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(outPath); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    using (var es = e.Open())
                    using (var os = File.Create(outPath))
                        es.CopyTo(os);
                }
            }

            string exe = Path.Combine(dest, "ZombieShooter.exe");

            // Best-effort desktop shortcut via WScript.Shell COM (reflection, no extra refs).
            try
            {
                string lnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "ZombieShooter.lnk");
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { dest });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }

            Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = dest, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "ZombieShooter_setup_error.txt"), ex.ToString());
            }
            catch { }
        }
    }
}
