using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ClassLockTeacher
{
    /// <summary>
    /// ClassLock-Teacher.exe: the teacher panel as one file. The panel page is
    /// built into this EXE. It is saved to %LOCALAPPDATA%\ClassLockTeacher and
    /// opened in its own Chrome or Edge window (app mode, no tabs or address bar).
    /// The page talks straight to the ClassLock database; no server is needed.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassLockTeacher");
                Directory.CreateDirectory(dir);
                string page = Path.Combine(dir, "ClassLock-Teacher.html");
                using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClassLock-Teacher.html"))
                using (FileStream dst = File.Create(page))
                {
                    src.CopyTo(dst);
                }
                if (args.Length > 0 && args[0] == "/extract") return 0; // used by the build test

                string url = new Uri(page).AbsoluteUri;
                string browser = FindBrowser();
                if (browser != null)
                {
                    // Own browser profile: the login is remembered and it never mixes with normal browsing.
                    var psi = new ProcessStartInfo(browser,
                        "--app=\"" + url + "\" --user-data-dir=\"" + Path.Combine(dir, "browser") + "\"" +
                        " --no-first-run --no-default-browser-check --window-size=1200,860");
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                else
                {
                    Process.Start(page); // default browser
                }
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show("ClassLock Teacher could not start:\r\n" + e.Message, "ClassLock Teacher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static string FindBrowser()
        {
            string pf = Environment.GetEnvironmentVariable("ProgramW6432") ??
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates =
            {
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }
    }
}
