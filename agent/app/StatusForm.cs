using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassLock
{
    /// <summary>
    /// The laptop window. First it asks for the class code, one time.
    /// After that it sits in the tray, shows the lock state, and while locked
    /// keeps Chrome open on the allowed website.
    /// </summary>
    public class StatusForm : Form
    {
        readonly Panel signInPanel = new Panel();
        readonly Panel statusPanel = new Panel();
        readonly TextBox codeBox = new TextBox();
        readonly Button signInButton = new Button();
        readonly Label signInError = new Label();
        readonly Label stateLabel = new Label();
        readonly Label detailLabel = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        bool? lastLocked;
        bool registered;
        bool busy;
        DateTime lastChromeLaunch = DateTime.MinValue;

        public StatusForm()
        {
            Text = "ClassLock";
            Icon = SystemIcons.Shield;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 300);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;

            BuildSignIn();
            BuildStatus();
            Controls.Add(signInPanel);
            Controls.Add(statusPanel);

            tray.Icon = SystemIcons.Shield;
            tray.Text = "ClassLock";
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open ClassLock", null, delegate { ShowWindow(); });
            tray.ContextMenuStrip = menu;

            timer.Interval = 3000;
            timer.Tick += delegate { UpdateStatus(false); };
            timer.Start();

            ShowPanel(false);
            UpdateStatus(true);
        }

        // ------------------------------------------------------------------ layout

        static Label MakeLabel(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 22);
            return l;
        }

        void BuildSignIn()
        {
            signInPanel.Dock = DockStyle.Fill;
            var title = MakeLabel("Connect this laptop", 24, 16, 330);
            title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            title.Height = 30;
            signInPanel.Controls.Add(title);

            var help = MakeLabel("Type the class code from the teacher. You only do this once.", 24, 52, 330);
            help.Height = 44;
            signInPanel.Controls.Add(help);

            signInPanel.Controls.Add(MakeLabel("Class code", 24, 104, 330));
            codeBox.CharacterCasing = CharacterCasing.Upper;
            codeBox.MaxLength = 12;
            codeBox.Font = new Font("Consolas", 16f, FontStyle.Bold);
            codeBox.SetBounds(24, 128, 200, 34);
            signInPanel.Controls.Add(codeBox);

            signInError.ForeColor = Color.Firebrick;
            signInError.SetBounds(24, 172, 330, 60);
            signInPanel.Controls.Add(signInError);

            signInButton.Text = "Connect";
            signInButton.SetBounds(24, 240, 150, 36);
            signInButton.Click += delegate { SignIn(); };
            signInPanel.Controls.Add(signInButton);

            AcceptButton = signInButton;
        }

        void BuildStatus()
        {
            statusPanel.Dock = DockStyle.Fill;
            stateLabel.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            stateLabel.SetBounds(24, 20, 330, 36);
            statusPanel.Controls.Add(stateLabel);

            detailLabel.SetBounds(24, 64, 330, 220);
            statusPanel.Controls.Add(detailLabel);
        }

        void ShowPanel(bool status)
        {
            statusPanel.Visible = status;
            signInPanel.Visible = !status;
            if (!status) codeBox.Focus();
        }

        void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void SetVisibleCore(bool value)
        {
            // Start hidden in the tray when the laptop is already in a class.
            if (!IsHandleCreated && value && registeredAtStart)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        readonly bool registeredAtStart = QueryRegisteredQuick();

        static bool QueryRegisteredQuick()
        {
            try
            {
                var req = new Dictionary<string, object>();
                req["cmd"] = "status";
                return Json.Bool(Pipe.Call(req, 3000), "registered");
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing the window only hides it. Windows shutdown and logoff still close it.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ logic

        void UpdateStatus(bool first)
        {
            if (busy) return;
            Dictionary<string, object> s;
            try
            {
                var req = new Dictionary<string, object>();
                req["cmd"] = "status";
                s = Pipe.Call(req, 3000);
            }
            catch (Exception)
            {
                // Without the service nothing is enforced: give the student a normal desktop.
                KeyBlocker.Active = false;
                EnsureDesktop();
                stateLabel.Text = "Service not running";
                stateLabel.ForeColor = Color.Firebrick;
                detailLabel.Text = "The ClassLock service is not running on this laptop. Tell your teacher.";
                tray.Text = "ClassLock: service not running";
                if (first) ShowPanel(true);
                return;
            }

            bool wasRegistered = registered;
            registered = Json.Bool(s, "registered");
            bool locked = Json.Bool(s, "locked");
            string reason = Json.Str(s, "reason") ?? "";
            string home = Json.Str(s, "home");
            var sites = new List<string>();
            foreach (object o in Json.Arr(s, "sites")) sites.Add(Convert.ToString(o));

            if (!registered)
            {
                KeyBlocker.Active = false;
                EnsureDesktop();
                if (first || wasRegistered)
                {
                    ShowPanel(false);
                    ShowWindow();
                }
                tray.Text = "ClassLock: not in a class";
                return;
            }
            if (first) ShowPanel(true);

            stateLabel.Text = locked ? "Locked" : "Unlocked";
            stateLabel.ForeColor = locked ? Color.Firebrick : Color.SeaGreen;
            string detail = reason + "\r\n\r\n" +
                "Class: " + (Json.Str(s, "className") ?? "-") + "\r\n";
            if (locked)
                detail += "\r\nOnly Google Chrome works now, and only on:\r\n" +
                    (sites.Count > 0 ? string.Join("\r\n", sites) : "(no websites yet)") + "\r\n";
            else
                detail += "\r\nInstalling apps is always blocked.\r\n";
            if (!Json.Bool(s, "online")) detail += "\r\nOffline. The last class rules stay on.";
            if (isAdmin)
                detail += "\r\nWARNING: this Windows account is an administrator. ClassLock never locks " +
                          "administrators. Students must use a standard Windows account.";
            detailLabel.Text = detail;
            string tip = "ClassLock: " + (locked ? "locked" : "unlocked");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (lastLocked.HasValue && lastLocked.Value != locked)
            {
                tray.ShowBalloonTip(5000, "ClassLock",
                    locked ? "Class time. Only " + (sites.Count > 0 ? sites[0] : "the class website") + " is allowed now."
                           : "The laptop is unlocked.", ToolTipIcon.Info);
            }
            lastLocked = locked;

            // Locked: full-screen Chrome only. No desktop, taskbar or Start menu (the
            // service closes them), and the keys that close Chrome or switch programs
            // do nothing. Administrators are never locked.
            bool kiosk = locked && !isAdmin;
            timer.Interval = kiosk ? 1000 : 3000;
            KeyBlocker.Active = kiosk;
            if (kiosk)
            {
                desktopHidden = true;
                if (home != null) KeepChromeOpen(home);
            }
            else
            {
                EnsureDesktop();
            }
        }

        readonly bool isAdmin = CurrentUserIsAdmin();
        readonly DateTime startedUtc = DateTime.UtcNow;
        bool desktopHidden;
        DateTime lastDesktopStart = DateTime.MinValue;

        static bool CurrentUserIsAdmin()
        {
            try
            {
                bool admin;
                using (Process me = Process.GetCurrentProcess()) Native.ProcessOwner(me.Id, out admin);
                return admin;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool RunsInMySession(string name)
        {
            int session;
            using (Process me = Process.GetCurrentProcess()) session = me.SessionId;
            foreach (Process p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.SessionId == session) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Starts the desktop and taskbar again after a lock. Also when this app was
        /// started while the desktop was missing (for example the service restarted it).
        /// Not in the first 20 seconds after sign-in, when Windows starts it by itself.
        /// </summary>
        void EnsureDesktop()
        {
            if (!desktopHidden && DateTime.UtcNow - startedUtc < TimeSpan.FromSeconds(20)) return;
            if (RunsInMySession("explorer"))
            {
                desktopHidden = false;
                return;
            }
            if (DateTime.UtcNow - lastDesktopStart < TimeSpan.FromSeconds(5)) return;
            lastDesktopStart = DateTime.UtcNow;
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(SystemPaths.Current().Windows, "explorer.exe"));
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception)
            {
                // Try again on the next tick.
            }
        }

        void KeepChromeOpen(string home)
        {
            if (DateTime.UtcNow - lastChromeLaunch < TimeSpan.FromSeconds(3)) return;
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("chrome"))
            {
                using (p)
                {
                    if (p.SessionId == session) return;
                }
            }
            string chrome = FindChrome();
            if (chrome == null) return;
            lastChromeLaunch = DateTime.UtcNow;
            try
            {
                // Kiosk: full screen, no tabs, no address bar, no close button.
                var psi = new ProcessStartInfo(chrome, "--kiosk --no-first-run --no-default-browser-check \"" +
                    home.Replace("\"", "") + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception)
            {
                // Chrome may be blocked from starting for a moment; try again later.
            }
        }

        static string FindChrome()
        {
            SystemPaths paths = SystemPaths.Current();
            foreach (string root in new[] { paths.ProgramFiles, paths.ProgramFilesX86 })
            {
                string p = Path.Combine(root, @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        void SignIn()
        {
            if (busy) return;
            busy = true;
            signInButton.Enabled = false;
            signInError.ForeColor = Color.DimGray;
            signInError.Text = "Connecting…";

            var req = new Dictionary<string, object>();
            req["cmd"] = "register";
            req["code"] = codeBox.Text.Trim();

            SynchronizationContext ui = SynchronizationContext.Current;
            Task.Factory.StartNew(() =>
            {
                try { return Pipe.Call(req, 40000); }
                catch (Exception e)
                {
                    var err = new Dictionary<string, object>();
                    err["ok"] = false;
                    err["error"] = "The ClassLock service did not answer: " + e.Message;
                    return err;
                }
            }).ContinueWith(t =>
            {
                ui.Post(delegate
                {
                    busy = false;
                    signInButton.Enabled = true;
                    Dictionary<string, object> res = t.Result;
                    if (Json.Bool(res, "ok"))
                    {
                        codeBox.Text = "";
                        signInError.Text = "";
                        registered = true;
                        ShowPanel(true);
                        stateLabel.Text = "Connected";
                        stateLabel.ForeColor = Color.SeaGreen;
                        detailLabel.Text = "This laptop is now in the class " + Json.Str(res, "className") + ".";
                        UpdateStatus(false);
                    }
                    else
                    {
                        signInError.ForeColor = Color.Firebrick;
                        signInError.Text = Json.Str(res, "error") ?? "Could not join the class.";
                    }
                }, null);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tray.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
