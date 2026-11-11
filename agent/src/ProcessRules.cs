using System;
using System.Collections.Generic;
using System.IO;

namespace ClassLock
{
    /// <summary>Folders the rules depend on. Tests pass fixed values.</summary>
    public class SystemPaths
    {
        public string Windows;
        public string ProgramFiles;
        public string ProgramFilesX86;
        public string ProgramData;
        public string InstallDir;

        public static SystemPaths Current()
        {
            var p = new SystemPaths();
            p.Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            p.ProgramFiles = Environment.GetEnvironmentVariable("ProgramW6432")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            p.ProgramFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? p.ProgramFiles;
            p.ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            p.InstallDir = Path.Combine(p.ProgramFiles, "ClassLock");
            return p;
        }
    }

    public struct Verdict
    {
        public bool Allow;
        public string Reason;

        public static Verdict Yes() { var v = new Verdict(); v.Allow = true; v.Reason = ""; return v; }
        public static Verdict No(string reason) { var v = new Verdict(); v.Allow = false; v.Reason = reason; return v; }
    }

    /// <summary>
    /// Decides if a program that a student started may keep running.
    /// Only programs from folders that a standard user cannot write to
    /// (Windows, Program Files) may run. So a student cannot install or run
    /// a downloaded app, a portable app, or an app from a USB drive.
    /// While locked, only Google Chrome (and the ClassLock app) may run.
    /// Administrator accounts are never checked (see Enforcer).
    /// </summary>
    public class ProcessRules
    {
        // Blocked for students all the time: shells, script hosts, installers, app stores.
        static readonly HashSet<string> AlwaysBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
            "mshta.exe", "regedit.exe", "reg.exe", "regsvr32.exe", "msiexec.exe", "mmc.exe", "wsl.exe",
            "wslhost.exe", "bash.exe", "wt.exe", "windowsterminal.exe", "openconsole.exe", "winget.exe",
            "winstore.app.exe", "appinstaller.exe", "certutil.exe", "bitsadmin.exe", "installutil.exe",
            "msbuild.exe", "csc.exe", "vbc.exe", "jsc.exe", "cmstp.exe", "schtasks.exe", "at.exe",
            "runas.exe", "msdt.exe", "forfiles.exe", "pcalua.exe"
        };

        // Windows programs that are fine outside class but not during class.
        static readonly HashSet<string> BlockedWhileLocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "taskmgr.exe", "systemsettings.exe", "control.exe", "notepad.exe", "write.exe", "wordpad.exe",
            "mspaint.exe", "snippingtool.exe", "calc.exe", "quickassist.exe", "mstsc.exe", "msra.exe",
            "iexplore.exe", "msedge.exe", "charmap.exe", "xpsrchvw.exe", "wmplayer.exe", "dxdiag.exe",
            "msinfo32.exe", "perfmon.exe", "resmon.exe", "eventvwr.exe"
        };

        // Folders inside C:\Windows where a standard user can write files.
        static readonly string[] WritableInsideWindows =
        {
            @"temp", @"tasks", @"tracing", @"registration\crmlog", @"debug",
            @"system32\fxstmp", @"system32\com\dmp", @"system32\microsoft\crypto\rsa\machinekeys",
            @"system32\spool", @"system32\tasks", @"syswow64\fxstmp", @"syswow64\com\dmp", @"syswow64\tasks"
        };

        // Store apps (Program Files\WindowsApps) allowed outside class, by package name prefix.
        static readonly string[] StorePackagesAllowed =
        {
            "microsoft.", "microsoftwindows.", "microsoftcorporationii.", "msteams", "microsoftteams"
        };

        readonly SystemPaths paths;
        readonly HashSet<string> extraAllowed;

        public ProcessRules(SystemPaths paths, IEnumerable<string> extraAllowed)
        {
            this.paths = paths;
            this.extraAllowed = new HashSet<string>(extraAllowed ?? new string[0], StringComparer.OrdinalIgnoreCase);
        }

        static bool Under(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string f = folder.TrimEnd('\\') + "\\";
            return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsChrome(string path, SystemPaths paths)
        {
            return Under(path, Path.Combine(paths.ProgramFiles, @"Google\Chrome\Application"))
                || Under(path, Path.Combine(paths.ProgramFilesX86, @"Google\Chrome\Application"));
        }

        bool InWindowsUserWritable(string path)
        {
            foreach (string sub in WritableInsideWindows)
                if (Under(path, Path.Combine(paths.Windows, sub))) return true;
            return false;
        }

        bool InProgramFiles(string path)
        {
            return Under(path, paths.ProgramFiles) || Under(path, paths.ProgramFilesX86);
        }

        static string StorePackage(string path, string programFiles)
        {
            string root = Path.Combine(programFiles, "WindowsApps") + "\\";
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = path.Substring(root.Length);
            int slash = rest.IndexOf('\\');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }

        public Verdict Decide(string path, bool locked)
        {
            if (string.IsNullOrEmpty(path)) return Verdict.Yes(); // cannot identify: leave alone
            string name = Path.GetFileName(path);

            if (Under(path, paths.InstallDir)) return Verdict.Yes();
            if (extraAllowed.Contains(name) || extraAllowed.Contains(path)) return Verdict.Yes();
            // Windows parts outside C:\Windows that must keep running: the touch
            // keyboard and the Windows Defender helper. Only admins can write here.
            if (Under(path, Path.Combine(paths.ProgramFiles, @"Common Files\microsoft shared\ink"))) return Verdict.Yes();
            if (Under(path, Path.Combine(paths.ProgramData ?? "", @"Microsoft\Windows Defender\Platform"))) return Verdict.Yes();
            if (AlwaysBlocked.Contains(name)) return Verdict.No("blocked tool");

            if (Under(path, paths.Windows) && !InWindowsUserWritable(path))
            {
                if (locked && BlockedWhileLocked.Contains(name)) return Verdict.No("not allowed during class");
                return Verdict.Yes();
            }

            if (locked)
            {
                if (IsChrome(path, paths)) return Verdict.Yes();
                // The Windows shell (Start menu search, widgets host) uses WebView2.
                if (Under(path, Path.Combine(paths.ProgramFilesX86, @"Microsoft\EdgeWebView"))) return Verdict.Yes();
                return Verdict.No("only Chrome during class");
            }

            if (InProgramFiles(path))
            {
                string package = StorePackage(path, paths.ProgramFiles);
                if (package == null) return Verdict.Yes();
                foreach (string prefix in StorePackagesAllowed)
                    if (package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Verdict.Yes();
                return Verdict.No("store app");
            }

            return Verdict.No("not installed by the school");
        }
    }
}
