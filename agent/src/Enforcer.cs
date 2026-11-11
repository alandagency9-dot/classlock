using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ClassLock
{
    /// <summary>
    /// Runs inside the SYSTEM service. Applies the Chrome policy when the lock
    /// state changes, and stops student programs that the rules do not allow.
    /// </summary>
    public class Enforcer
    {
        readonly SystemPaths paths = SystemPaths.Current();
        ProcessRules rules;
        bool? appliedLocked;
        string appliedSites;

        // Admin status per account SID. Cleared every 10 minutes.
        readonly Dictionary<string, bool> adminCache = new Dictionary<string, bool>();
        DateTime adminCacheReset = DateTime.UtcNow;

        public Enforcer()
        {
            ReloadAllowlist();
        }

        public void ReloadAllowlist()
        {
            rules = new ProcessRules(paths, Store.LoadExtraAllowed());
        }

        /// <summary>Writes the Chrome policy only when something changed.</summary>
        public void ApplyPolicy(bool locked, string[] sites)
        {
            string sitesKey = string.Join("\n", sites ?? new string[0]);
            if (appliedLocked == locked && (!locked || appliedSites == sitesKey)) return;

            bool becameLocked = locked && appliedLocked != true;
            bool becameUnlocked = !locked && appliedLocked == true;
            try
            {
                if (locked) ChromePolicy.ApplyLock(sites);
                else ChromePolicy.ClearLock();
                ChromePolicy.SetShellAutoRestart(!locked);
                ChromePolicy.SetWindowsLock(locked);
                appliedLocked = locked;
                appliedSites = sitesKey;
                Log.Write(locked ? "Locked. Allowed sites: " + sitesKey.Replace("\n", ", ") : "Unlocked.");
            }
            catch (Exception e)
            {
                Log.Write("Could not write Chrome policy: " + e.Message);
                return;
            }

            // Lock: close Chrome once so open tabs on other sites are gone. The ClassLock
            // app opens it again full screen (kiosk) on the allowed site.
            // Unlock: close the full-screen Chrome so the student gets a normal Chrome.
            if (becameLocked || becameUnlocked) StopUserProcesses("chrome");
        }

        void StopUserProcesses(string name)
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.SessionId != 0) Native.Kill(p.Id);
                }
            }
        }

        bool IsAdmin(string sid, bool tokenSaysAdmin)
        {
            if (DateTime.UtcNow - adminCacheReset > TimeSpan.FromMinutes(10))
            {
                adminCache.Clear();
                adminCacheReset = DateTime.UtcNow;
            }
            bool cached;
            if (adminCache.TryGetValue(sid, out cached)) return cached;
            adminCache[sid] = tokenSaysAdmin;
            return tokenSaysAdmin;
        }

        // Some Windows parts restart again and again. Log each path at most every 10 minutes.
        readonly Dictionary<string, DateTime> lastLogged = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        void LogStopped(string path, string reason)
        {
            DateTime when;
            if (lastLogged.TryGetValue(path, out when) && DateTime.UtcNow - when < TimeSpan.FromMinutes(10)) return;
            if (lastLogged.Count > 500) lastLogged.Clear();
            lastLogged[path] = DateTime.UtcNow;
            Log.Write("Stopped " + path + " (" + reason + ")");
        }

        /// <summary>
        /// True when the last scan found a signed-in Windows account that is an
        /// administrator. ClassLock never blocks administrators, so such a laptop is
        /// not protected. The teacher panel shows a warning for it.
        /// </summary>
        public volatile bool AdminSignedIn;

        SessionLauncher launcher;

        /// <summary>Sessions where the ClassLock app (from the install folder) runs.</summary>
        HashSet<int> AppSessions()
        {
            var sessions = new HashSet<int>();
            bool admin = false;
            string appExe = System.IO.Path.Combine(paths.InstallDir, "ClassLock.exe");
            foreach (Process p in Process.GetProcessesByName("ClassLock"))
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != 0 && string.Equals(Native.ProcessPath(p.Id), appExe, StringComparison.OrdinalIgnoreCase))
                        {
                            sessions.Add(p.SessionId);
                            bool isAdmin;
                            if (Native.IsPersonSid(Native.ProcessOwner(p.Id, out isAdmin)) && isAdmin) admin = true;
                        }
                    }
                    catch (Exception)
                    {
                        // The process ended while we looked at it.
                    }
                }
            }
            AdminSignedIn = admin;
            return sessions;
        }

        /// <summary>One pass over all programs in user sessions.</summary>
        public void Scan(bool locked)
        {
            HashSet<int> appSessions = AppSessions();
            if (launcher == null) launcher = new SessionLauncher(System.IO.Path.Combine(paths.InstallDir, "ClassLock.exe"));
            try { launcher.EnsureRunning(appSessions); }
            catch (Exception e) { Log.Write("Session check failed: " + e.Message); }
            Process[] all = Process.GetProcesses();
            foreach (Process p in all)
            {
                try
                {
                    if (p.SessionId == 0) continue; // services; this agent runs here too
                    string path = Native.ProcessPath(p.Id);
                    Verdict v = rules.Decide(path, locked);
                    if (v.Allow && rules.HideDesktop(path, locked, appSessions.Contains(p.SessionId)))
                        v = Verdict.No("desktop hidden during class");
                    if (v.Allow) continue;

                    bool tokenSaysAdmin;
                    string sid = Native.ProcessOwner(p.Id, out tokenSaysAdmin);
                    if (!Native.IsPersonSid(sid)) continue;           // SYSTEM and services are never stopped
                    // Unlocked: administrators (IT) are never stopped, so they can install and fix things.
                    // Locked: the class lock applies to whoever is at the laptop, administrators too.
                    // IT unlocks the laptop in the panel first to work on it.
                    if (!locked && IsAdmin(sid, tokenSaysAdmin)) continue;

                    if (Native.Kill(p.Id)) LogStopped(path, v.Reason);
                }
                catch (Exception)
                {
                    // The process ended while we looked at it.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        /// <summary>Dry run for the test program: what would be stopped now.</summary>
        public static List<string> Preview(bool locked, ProcessRules rules)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.SessionId == 0) continue;
                    string path = Native.ProcessPath(p.Id);
                    Verdict v = rules.Decide(path, locked);
                    if (!v.Allow) result.Add(path + "  [" + v.Reason + "]");
                }
            }
            var sorted = new List<string>(result);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return sorted;
        }
    }
}
