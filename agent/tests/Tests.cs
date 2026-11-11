using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace ClassLock.Tests
{
    /// <summary>
    /// ClassLockTests.exe              runs the checks, exit code = number of failures
    /// ClassLockTests.exe --simulate   lists which running programs the agent would stop
    ///                                 right now (dry run: nothing is stopped, nothing is written)
    /// </summary>
    static class TestProgram
    {
        static int failures;
        static int passes;

        static void Check(bool ok, string name)
        {
            if (ok) passes++;
            else
            {
                failures++;
                Console.WriteLine("FAIL  " + name);
            }
        }

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--simulate") return Simulate();

            TimeRules();
            PolicyParsing();
            ProgramRules();
            PipeFraming();

            Console.WriteLine(passes + " passed, " + failures + " failed");
            return failures;
        }

        static Policy Make(string classMode, DateTime? untilUtc, string deviceMode, params Slot[] slots)
        {
            var p = new Policy();
            p.Registered = true;
            p.ClassLockMode = classMode;
            p.ClassLockUntilUtc = untilUtc;
            p.DeviceLockMode = deviceMode;
            p.Schedule.AddRange(slots);
            return p;
        }

        static Slot S(int day, string start, string end)
        {
            var s = new Slot();
            s.Day = day;
            s.Start = LockRules.ToMinutes(start).Value;
            s.End = LockRules.ToMinutes(end).Value;
            return s;
        }

        static void TimeRules()
        {
            Check(LockRules.ToMinutes("08:30") == 510, "ToMinutes 08:30");
            Check(LockRules.ToMinutes("8:05") == 485, "ToMinutes 8:05");
            Check(LockRules.ToMinutes("24:00") == 1440, "ToMinutes 24:00");
            Check(LockRules.ToMinutes("24:01") == null, "ToMinutes 24:01 invalid");
            Check(LockRules.ToMinutes("noon") == null, "ToMinutes text invalid");

            // Wednesday 11 Nov 2026.
            var wed = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Local);
            var utc = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            Policy p = Make("schedule", null, "class", S(3, "08:00", "14:30"));
            string reason;
            Check(!LockRules.IsLocked(p, wed.AddHours(7).AddMinutes(59), utc, out reason), "before class unlocked");
            Check(LockRules.IsLocked(p, wed.AddHours(8), utc, out reason), "class start locked");
            Check(LockRules.IsLocked(p, wed.AddHours(14).AddMinutes(29), utc, out reason), "class end-1 locked");
            Check(!LockRules.IsLocked(p, wed.AddHours(14).AddMinutes(30), utc, out reason), "class end unlocked");
            Check(!LockRules.IsLocked(p, wed.AddDays(3).AddHours(10), utc, out reason), "Saturday unlocked");

            Policy unlockUntil = Make("unlocked", utc.AddHours(1), "class", S(3, "08:00", "14:30"));
            Check(!LockRules.IsLocked(unlockUntil, wed.AddHours(10), utc, out reason), "teacher unlock active");
            Check(LockRules.IsLocked(unlockUntil, wed.AddHours(10), utc.AddHours(2), out reason), "teacher unlock expired");

            Policy lockForever = Make("locked", null, "class");
            Check(LockRules.IsLocked(lockForever, wed.AddHours(22), utc, out reason), "teacher lock no end");
            Check(!LockRules.IsLocked(Make("locked", null, "unlocked"), wed, utc, out reason), "laptop override unlock");
            Check(LockRules.IsLocked(Make("schedule", null, "locked"), wed, utc, out reason), "laptop override lock");
            Check(!LockRules.IsLocked(null, wed, utc, out reason), "no policy unlocked");

            Check(LockRules.HomeUrl(new[] { "classroom.google.com" }) == "https://classroom.google.com", "home adds https");
            Check(LockRules.HomeUrl(new[] { "http://x.org/a" }) == "http://x.org/a", "home keeps scheme");
            Check(LockRules.HomeUrl(new string[0]) == null, "home empty");
        }

        static void PolicyParsing()
        {
            string json = "{\"registered\":true,\"class_name\":\"8B\",\"student_name\":\"Sara\"," +
                "\"allowed_sites\":[\"classroom.google.com\",\" accounts.google.com \"]," +
                "\"schedule\":[{\"day\":1,\"start\":\"08:00\",\"end\":\"14:00\"},{\"day\":9,\"start\":\"x\",\"end\":\"y\"}]," +
                "\"class_lock_mode\":\"locked\",\"class_lock_until\":\"2026-11-11T12:30:00.123456+00:00\"," +
                "\"device_lock_mode\":\"class\",\"server_time\":\"2026-11-11T10:00:00+00:00\"}";
            Policy p = Policy.FromHeartbeat(Json.ReadObject(json));
            Check(p.Registered && p.ClassName == "8B", "parse names");
            Check(p.AllowedSites.Length == 2 && p.AllowedSites[1] == "accounts.google.com", "parse sites trimmed");
            Check(p.Schedule.Count == 1 && p.Schedule[0].Start == 480, "parse schedule drops bad rows");
            Check(p.ClassLockMode == "locked", "parse lock mode");
            Check(p.ClassLockUntilUtc.HasValue
                  && p.ClassLockUntilUtc.Value == new DateTime(2026, 11, 11, 12, 30, 0, 123, DateTimeKind.Utc).AddTicks(4560),
                  "parse lock_until as UTC");

            Policy nulls = Policy.FromHeartbeat(Json.ReadObject(
                "{\"registered\":true,\"allowed_sites\":[],\"schedule\":[],\"class_lock_mode\":\"schedule\",\"class_lock_until\":null}"));
            Check(nulls.ClassLockUntilUtc == null && nulls.AllowedSites.Length == 0, "parse nulls");
            Check(!Policy.FromHeartbeat(Json.ReadObject("{\"registered\":false}")).Registered, "parse removed laptop");
        }

        static void ProgramRules()
        {
            var paths = new SystemPaths();
            paths.Windows = @"C:\Windows";
            paths.ProgramFiles = @"C:\Program Files";
            paths.ProgramFilesX86 = @"C:\Program Files (x86)";
            paths.ProgramData = @"C:\ProgramData";
            paths.InstallDir = @"C:\Program Files\ClassLock";
            var rules = new ProcessRules(paths, new[] { "OneDrive.exe" });

            Check(rules.Decide(@"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe", true).Allow, "touch keyboard allowed locked");
            Check(rules.Decide(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.1-0\DefenderSessionHelper.exe", true).Allow,
                "Defender helper allowed locked");
            Check(!rules.Decide(@"C:\ProgramData\games\game.exe", false).Allow, "other ProgramData blocked");

            string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            Check(rules.Decide(chrome, true).Allow, "chrome allowed locked");
            Check(rules.Decide(chrome, false).Allow, "chrome allowed unlocked");
            Check(rules.Decide(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe", true).Allow, "chrome x86 allowed");
            Check(rules.Decide(@"C:\Program Files\ClassLock\ClassLock.exe", true).Allow, "own app allowed");
            Check(rules.Decide(@"C:\Windows\explorer.exe", true).Allow, "explorer allowed locked");
            Check(rules.Decide(@"C:\Windows\System32\ctfmon.exe", true).Allow, "keyboard host allowed locked");

            string notepad = @"C:\Windows\System32\notepad.exe";
            Check(!rules.Decide(notepad, true).Allow, "notepad blocked locked");
            Check(rules.Decide(notepad, false).Allow, "notepad allowed unlocked");
            Check(!rules.Decide(@"C:\Windows\System32\Taskmgr.exe", true).Allow, "task manager blocked locked");

            Check(!rules.Decide(@"C:\Windows\System32\cmd.exe", false).Allow, "cmd always blocked");
            Check(!rules.Decide(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false).Allow, "powershell always blocked");
            Check(!rules.Decide(@"C:\Windows\System32\msiexec.exe", false).Allow, "msiexec always blocked");

            Check(!rules.Decide(@"C:\Users\sara\Downloads\RobloxPlayerInstaller.exe", false).Allow, "download blocked");
            Check(!rules.Decide(@"C:\Users\sara\AppData\Local\Discord\app-1.0\Discord.exe", false).Allow, "per-user app blocked");
            Check(!rules.Decide(@"D:\games\game.exe", false).Allow, "USB app blocked");
            Check(!rules.Decide(@"C:\Windows\Temp\x.exe", false).Allow, "Windows\\Temp blocked");
            Check(!rules.Decide(@"C:\Windows\System32\spool\drivers\color\x.exe", false).Allow, "spool color blocked");
            Check(!rules.Decide(@"C:\Users\sara\Downloads\chrome.exe", true).Allow, "fake chrome blocked");

            Check(rules.Decide(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", false).Allow, "Word allowed unlocked");
            Check(!rules.Decide(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", true).Allow, "Word blocked locked");
            Check(!rules.Decide(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", true).Allow, "Edge blocked locked");
            Check(rules.Decide(@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\120.0\msedgewebview2.exe", true).Allow,
                "WebView2 allowed");

            string calc = @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.0.0_x64__8wekyb3d8bbwe\CalculatorApp.exe";
            Check(rules.Decide(calc, false).Allow, "Microsoft store app allowed unlocked");
            Check(!rules.Decide(calc, true).Allow, "Microsoft store app blocked locked");
            Check(!rules.Decide(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.0_x86__zpdnekdrzrea0\Spotify.exe", false).Allow,
                "third-party store app blocked");
            Check(!rules.Decide(@"C:\Program Files\WindowsApps\Microsoft.WindowsStore_22.0_x64__8wekyb3d8bbwe\WinStore.App.exe", false).Allow,
                "Store app blocked");

            Check(rules.Decide(@"C:\Users\sara\AppData\Local\Microsoft\OneDrive\OneDrive.exe", false).Allow, "extra allowlist by name");
            Check(rules.Decide(null, true).Allow, "unknown path left alone");

            Check(Native.IsPersonSid("S-1-5-21-1-2-3-1001"), "local account is person");
            Check(Native.IsPersonSid("S-1-12-1-1-2-3-4"), "Entra account is person");
            Check(!Native.IsPersonSid("S-1-5-18"), "SYSTEM is not person");
            Check(!Native.IsPersonSid("S-1-5-90-0-1"), "window manager is not person");
        }

        static void PipeFraming()
        {
            var ms = new MemoryStream();
            Pipe.WriteLine(ms, "{\"cmd\":\"status\",\"x\":\"é\"}");
            ms.Position = 0;
            Check(Pipe.ReadLine(ms) == "{\"cmd\":\"status\",\"x\":\"é\"}", "pipe round trip UTF-8");
            Check(Pipe.ReadLine(ms) == null, "pipe end of stream");

            var big = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', Pipe.MaxLine + 10) + "\n"));
            bool threw = false;
            try { Pipe.ReadLine(big); }
            catch (IOException) { threw = true; }
            Check(threw, "pipe rejects long message");
        }

        static int Simulate()
        {
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Console.WriteLine("Dry run. Nothing is stopped and nothing is written.");
            Console.WriteLine("Note: the real agent never stops programs of administrator accounts.");
            Console.WriteLine("This window runs elevated as administrator: " + admin);
            var rules = new ProcessRules(SystemPaths.Current(), new string[0]);
            foreach (bool locked in new[] { false, true })
            {
                List<string> list = Enforcer.Preview(locked, rules);
                Console.WriteLine();
                Console.WriteLine((locked ? "LOCKED (class time)" : "UNLOCKED") + ": " + list.Count +
                    " program(s) of a student would be stopped:");
                foreach (string line in list) Console.WriteLine("  " + line);
            }
            return 0;
        }
    }
}
