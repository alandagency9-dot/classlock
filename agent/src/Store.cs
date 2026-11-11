using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ClassLock
{
    /// <summary>What the service remembers between restarts.</summary>
    public class AgentState
    {
        public string DeviceId;      // null until a student registers the laptop
        public string Secret;        // proves to the server that we are this laptop
        public string PolicyJson;    // last heartbeat answer, used when offline
        public string LastContactUtc;
    }

    /// <summary>
    /// Files in C:\ProgramData\ClassLock. Only SYSTEM and administrators can
    /// read or change them, so a student cannot read the device secret or
    /// change the rules.
    /// </summary>
    public static class Store
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClassLock");
        static readonly string StateFile = Path.Combine(Dir, "state.json");
        public static readonly string AllowlistFile = Path.Combine(Dir, "extra-allowed.txt");

        public static void EnsureDir()
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir, sec);
            else Directory.SetAccessControl(Dir, sec);

            if (!File.Exists(AllowlistFile))
            {
                File.WriteAllText(AllowlistFile,
                    "# Extra programs students may run, one per line.\r\n" +
                    "# Use a file name (example: OneDrive.exe) or a full path.\r\n" +
                    "# Only administrators can edit this file. Restart the ClassLock service after a change.\r\n");
            }
        }

        public static AgentState Load()
        {
            var s = new AgentState();
            try
            {
                if (File.Exists(StateFile))
                {
                    var d = Json.ReadObject(File.ReadAllText(StateFile, Encoding.UTF8));
                    s.DeviceId = Json.Str(d, "deviceId");
                    s.Secret = Json.Str(d, "secret");
                    s.PolicyJson = Json.Str(d, "policyJson");
                    s.LastContactUtc = Json.Str(d, "lastContactUtc");
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read state: " + e.Message);
            }
            if (string.IsNullOrEmpty(s.Secret)) s.Secret = NewSecret();
            return s;
        }

        public static void Save(AgentState s)
        {
            var d = new Dictionary<string, object>();
            d["deviceId"] = s.DeviceId;
            d["secret"] = s.Secret;
            d["policyJson"] = s.PolicyJson;
            d["lastContactUtc"] = s.LastContactUtc;
            string tmp = StateFile + ".tmp";
            File.WriteAllText(tmp, Json.Write(d), new UTF8Encoding(false));
            if (File.Exists(StateFile)) File.Replace(tmp, StateFile, null);
            else File.Move(tmp, StateFile);
        }

        public static string[] LoadExtraAllowed()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(AllowlistFile)) return list.ToArray();
                foreach (string line in File.ReadAllLines(AllowlistFile))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith("#")) list.Add(t);
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not read extra-allowed.txt: " + e.Message);
            }
            return list.ToArray();
        }

        static string NewSecret()
        {
            var bytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            var sb = new StringBuilder(64);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string file = Path.Combine(Store.Dir, "agent.log");
                    if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024)
                    {
                        string old = file + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n");
                }
            }
            catch (Exception)
            {
                // Logging must never stop the agent.
            }
        }
    }
}
