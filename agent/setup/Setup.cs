using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClassLock.Setup
{
    /// <summary>
    /// ClassLockSetup.exe. Needs administrator rights (see Setup.manifest).
    ///   ClassLockSetup.exe               install, with questions
    ///   ClassLockSetup.exe /quiet        install, no windows (for mass deployment)
    ///   ClassLockSetup.exe /uninstall    remove ClassLock and every policy it set
    /// Exit codes: 0 ok, 1 error, 2 Chrome missing, 3 cancelled.
    /// </summary>
    static class SetupProgram
    {
        const string ServiceName = "ClassLock";
        const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string Title = "ClassLock Setup";

        static bool quiet;

        static string ProgramFiles64
        {
            get
            {
                return Environment.GetEnvironmentVariable("ProgramW6432")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }
        }

        static string InstallDir { get { return Path.Combine(ProgramFiles64, "ClassLock"); } }
        static string AppExe { get { return Path.Combine(InstallDir, "ClassLock.exe"); } }
        static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClassLock"); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool uninstall = false;
            foreach (string a in args)
            {
                if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (a.Equals("/quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
            }
            try
            {
                return uninstall ? Uninstall() : Install();
            }
            catch (Exception e)
            {
                Say("Setup failed:\r\n\r\n" + e.Message, MessageBoxIcon.Error);
                return 1;
            }
        }

        static void Say(string text, MessageBoxIcon icon)
        {
            if (!quiet) MessageBox.Show(text, Title, MessageBoxButtons.OK, icon);
        }

        static bool Ask(string text)
        {
            return quiet || MessageBox.Show(text, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // ------------------------------------------------------------------ install

        static int Install()
        {
            if (!Ask("Install ClassLock on this laptop?\r\n\r\n" +
                     "After install:\r\n" +
                     "- Students cannot install apps or run apps the school did not install.\r\n" +
                     "- During class time only Google Chrome works, on the websites the teacher allows.\r\n" +
                     "- A student signs in with the login from the teacher and the class code.\r\n\r\n" +
                     "Students must use standard (not administrator) Windows accounts. " +
                     "Administrator accounts are never blocked."))
                return 3;

            if (FindChrome() == null)
            {
                Say("Google Chrome is not installed for all users.\r\n\r\n" +
                    "Install Chrome for all users first (it must be in C:\\Program Files\\Google\\Chrome), " +
                    "then run this setup again.", MessageBoxIcon.Warning);
                return 2;
            }

            StopService();
            StopApps();

            Directory.CreateDirectory(InstallDir);
            Retry(delegate
            {
                using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClassLock.exe"))
                using (FileStream dst = File.Create(AppExe))
                {
                    if (src == null) throw new InvalidOperationException("ClassLock.exe is missing inside the setup file.");
                    src.CopyTo(dst);
                }
            });

            string bin = "\"" + AppExe + "\" --service";
            if (ServiceExists())
                Sc("config " + ServiceName + " binPath= " + Quote(bin) + " start= auto");
            else
                Sc("create " + ServiceName + " binPath= " + Quote(bin) + " start= auto DisplayName= \"ClassLock Agent\"");
            Sc("description " + ServiceName + " \"Locks this laptop to the websites the teacher allows and blocks app installs.\"");
            Sc("failure " + ServiceName + " reset= 86400 actions= restart/2000/restart/5000/restart/10000");
            Sc("failureflag " + ServiceName + " 1");

            using (var sc = new ServiceController(ServiceName))
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }

            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey run = hklm.CreateSubKey(RunKey))
                run.SetValue("ClassLock", "\"" + AppExe + "\"", RegistryValueKind.String);

            if (!quiet)
            {
                Say("ClassLock is installed.\r\n\r\n" +
                    "The ClassLock window opens for every user at sign-in. The student enters the email and " +
                    "password from the teacher and the class code.", MessageBoxIcon.Information);
                // explorer.exe starts the app without administrator rights, as the signed-in user.
                try { Process.Start("explorer.exe", "\"" + AppExe + "\""); }
                catch (Exception) { /* the app also starts at next sign-in */ }
            }
            return 0;
        }

        // ------------------------------------------------------------------ uninstall

        static int Uninstall()
        {
            if (!Ask("Remove ClassLock from this laptop?\r\n\r\nThe laptop unlocks and app installs are allowed again."))
                return 3;

            StopService();
            if (ServiceExists()) Sc("delete " + ServiceName);
            StopApps();

            ChromePolicy.RemoveAll();
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey run = hklm.OpenSubKey(RunKey, true))
                if (run != null) run.DeleteValue("ClassLock", false);

            Retry(delegate { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); });
            Retry(delegate { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); });

            Say("ClassLock is removed.\r\n\r\nAlso remove this laptop in the teacher panel. " +
                "To install ClassLock again later, the teacher must remove the old laptop entry first.",
                MessageBoxIcon.Information);
            return 0;
        }

        // ------------------------------------------------------------------ helpers

        static string FindChrome()
        {
            string x86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? ProgramFiles64;
            foreach (string root in new[] { ProgramFiles64, x86 })
            {
                string p = Path.Combine(root, @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static bool ServiceExists()
        {
            foreach (ServiceController s in ServiceController.GetServices())
            {
                using (s)
                {
                    if (s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        static void StopService()
        {
            if (!ServiceExists()) return;
            using (var sc = new ServiceController(ServiceName))
            {
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
        }

        /// <summary>Closes the student app in every session so its file can be replaced.</summary>
        static void StopApps()
        {
            foreach (Process p in Process.GetProcessesByName("ClassLock"))
            {
                using (p)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch (Exception) { /* already gone */ }
                }
            }
        }

        static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        static void Sc(string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("sc " + arguments + "\r\n" + output.Trim());
            }
        }

        static void Retry(Action action)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException)
                {
                    if (i >= 9) throw;
                }
                catch (UnauthorizedAccessException)
                {
                    if (i >= 9) throw;
                }
                Thread.Sleep(500);
            }
        }
    }
}
