using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClassLock
{
    public struct Slot
    {
        public int Day;    // 0 = Sunday ... 6 = Saturday
        public int Start;  // minutes after midnight, local time
        public int End;
    }

    /// <summary>The class rules from the last good heartbeat.</summary>
    public class Policy
    {
        public bool Registered;
        public string ClassName;
        public string[] AllowedSites = new string[0];
        public List<Slot> Schedule = new List<Slot>();
        public string ClassLockMode = "schedule";   // schedule | locked | unlocked
        public DateTime? ClassLockUntilUtc;
        public string DeviceLockMode = "class";     // class | locked | unlocked

        public static Policy FromHeartbeat(Dictionary<string, object> d)
        {
            var p = new Policy();
            p.Registered = Json.Bool(d, "registered");
            p.ClassName = Json.Str(d, "class_name");
            var sites = new List<string>();
            foreach (object s in Json.Arr(d, "allowed_sites"))
            {
                string site = s as string;
                if (!string.IsNullOrWhiteSpace(site)) sites.Add(site.Trim());
            }
            p.AllowedSites = sites.ToArray();
            foreach (object o in Json.Arr(d, "schedule"))
            {
                var slot = o as Dictionary<string, object>;
                if (slot == null) continue;
                int? start = LockRules.ToMinutes(Json.Str(slot, "start"));
                int? end = LockRules.ToMinutes(Json.Str(slot, "end"));
                int day = Json.Int(slot, "day", -1);
                if (start == null || end == null || day < 0 || day > 6) continue;
                var x = new Slot();
                x.Day = day;
                x.Start = start.Value;
                x.End = end.Value;
                p.Schedule.Add(x);
            }
            p.ClassLockMode = Json.Str(d, "class_lock_mode") ?? "schedule";
            p.ClassLockUntilUtc = Json.Utc(d, "class_lock_until");
            p.DeviceLockMode = Json.Str(d, "device_lock_mode") ?? "class";
            return p;
        }
    }

    /// <summary>
    /// Same rules as panel/src/lib/lock.ts:
    /// 1. laptop override, 2. class override until lock_until, 3. schedule.
    /// </summary>
    public static class LockRules
    {
        static readonly Regex TimePattern = new Regex(@"^(\d{1,2}):(\d{2})$");

        public static int? ToMinutes(string hhmm)
        {
            if (hhmm == null) return null;
            Match m = TimePattern.Match(hhmm.Trim());
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value);
            int min = int.Parse(m.Groups[2].Value);
            if (h > 24 || min > 59 || (h == 24 && min != 0)) return null;
            return h * 60 + min;
        }

        public static bool IsLocked(Policy p, DateTime localNow, DateTime utcNow, out string reason)
        {
            if (p == null || !p.Registered)
            {
                reason = "Not signed in to a class";
                return false;
            }
            if (p.DeviceLockMode == "locked") { reason = "This laptop is always locked"; return true; }
            if (p.DeviceLockMode == "unlocked") { reason = "This laptop is always unlocked"; return false; }

            bool overrideActive = p.ClassLockMode != "schedule"
                && (p.ClassLockUntilUtc == null || p.ClassLockUntilUtc.Value > utcNow);
            if (overrideActive)
            {
                bool locked = p.ClassLockMode == "locked";
                reason = locked ? "Locked by teacher" : "Unlocked by teacher";
                return locked;
            }

            int day = (int)localNow.DayOfWeek;
            int minutes = localNow.Hour * 60 + localNow.Minute;
            foreach (Slot s in p.Schedule)
            {
                if (s.Day == day && minutes >= s.Start && minutes < s.End)
                {
                    reason = "Class time";
                    return true;
                }
            }
            reason = "Outside class time";
            return false;
        }

        /// <summary>First allowed site as a URL Chrome can open, or null.</summary>
        public static string HomeUrl(string[] sites)
        {
            if (sites == null || sites.Length == 0) return null;
            string s = sites[0].Trim();
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
            return "https://" + s;
        }
    }
}
