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
            try
            {
                if (locked) ChromePolicy.ApplyLock(sites);
                else ChromePolicy.ClearLock();
                appliedLocked = locked;
                appliedSites = sitesKey;
                Log.Write(locked ? "Locked. Allowed sites: " + sitesKey.Replace("\n", ", ") : "Unlocked.");
            }
            catch (Exception e)
            {
                Log.Write("Could not write Chrome policy: " + e.Message);
                return;
            }

            // Close Chrome once so open tabs on other sites are gone.
            // The ClassLock app opens Chrome again on the allowed site.
            if (becameLocked) StopUserProcesses("chrome");
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

        /// <summary>One pass over all programs in user sessions.</summary>
        public void Scan(bool locked)
        {
            Process[] all = Process.GetProcesses();
            foreach (Process p in all)
            {
                try
                {
                    if (p.SessionId == 0) continue; // services; this agent runs here too
                    string path = Native.ProcessPath(p.Id);
                    Verdict v = rules.Decide(path, locked);
                    if (v.Allow) continue;

                    bool tokenSaysAdmin;
                    string sid = Native.ProcessOwner(p.Id, out tokenSaysAdmin);
                    if (!Native.IsPersonSid(sid)) continue;           // SYSTEM and services are never stopped
                    if (IsAdmin(sid, tokenSaysAdmin)) continue;      // teachers and IT are never stopped

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
