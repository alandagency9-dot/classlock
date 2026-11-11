using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace ClassLock
{
    /// <summary>
    /// The Windows service. Runs as SYSTEM, starts with Windows, restarts if it
    /// crashes, and standard users cannot stop it.
    ///   - Heartbeat thread: every 10 s, reports to the server and gets the class rules.
    ///   - Enforce thread:   every 0.5 to 1 s, applies the lock and stops programs.
    ///   - Pipe thread:      answers the laptop app (status, join a class).
    /// When the internet is down, the last rules stay in force.
    /// </summary>
    public class AgentService : ServiceBase
    {
        public const string Name = "ClassLock";

        readonly object gate = new object();
        readonly AutoResetEvent heartbeatNow = new AutoResetEvent(false);
        volatile bool stopping;

        AgentState state;
        Policy policy;
        bool online;
        string lastError;
        bool currentLocked;
        string currentReason = "";
        Enforcer enforcer;

        public AgentService()
        {
            ServiceName = Name;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            Store.EnsureDir();
            state = Store.Load();
            Store.Save(state); // keeps a newly made secret
            if (!string.IsNullOrEmpty(state.PolicyJson) && state.DeviceId != null)
            {
                try { policy = Policy.FromHeartbeat(Json.ReadObject(state.PolicyJson)); }
                catch (Exception e) { Log.Write("Saved rules are unreadable: " + e.Message); }
            }
            try { ChromePolicy.ApplyBaseline(); }
            catch (Exception e) { Log.Write("Could not apply baseline policy: " + e.Message); }

            enforcer = new Enforcer();
            StartThread(HeartbeatLoop);
            StartThread(EnforceLoop);
            StartThread(PipeLoop);
            Log.Write("ClassLock " + BuildConfig.Version + " started.");
        }

        protected override void OnStop()
        {
            stopping = true;
            heartbeatNow.Set();
            Log.Write("ClassLock stopped.");
        }

        static void StartThread(ThreadStart work)
        {
            var t = new Thread(work);
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ heartbeat

        void HeartbeatLoop()
        {
            while (!stopping)
            {
                Heartbeat();
                heartbeatNow.WaitOne(10000);
            }
        }

        void Heartbeat()
        {
            string id, secret;
            bool locked;
            lock (gate)
            {
                id = state.DeviceId;
                secret = state.Secret;
                locked = currentLocked;
            }
            if (id == null) return;

            try
            {
                Dictionary<string, object> res = Api.Heartbeat(id, secret, locked);
                Policy p = Policy.FromHeartbeat(res);
                lock (gate)
                {
                    online = true;
                    lastError = null;
                    state.LastContactUtc = DateTime.UtcNow.ToString("o");
                    if (!p.Registered)
                    {
                        Log.Write("The teacher removed this laptop from the class.");
                        state.DeviceId = null;
                        state.PolicyJson = null;
                        policy = null;
                        Store.Save(state);
                        return;
                    }
                    policy = p;
                    string json = Json.Write(res);
                    // server_time changes every call; save only when the rules change.
                    res.Remove("server_time");
                    string rules = Json.Write(res);
                    string savedRules = state.PolicyJson;
                    if (savedRules != null)
                    {
                        try
                        {
                            var old = Json.ReadObject(savedRules);
                            old.Remove("server_time");
                            savedRules = Json.Write(old);
                        }
                        catch (Exception) { savedRules = null; }
                    }
                    if (rules != savedRules)
                    {
                        state.PolicyJson = json;
                        Store.Save(state);
                    }
                }
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    if (online) Log.Write("Server not reachable: " + e.Message);
                    online = false;
                    lastError = e.Message;
                }
            }
        }

        // ------------------------------------------------------------------ enforce

        void EnforceLoop()
        {
            int pass = 0;
            while (!stopping)
            {
                bool locked;
                string[] sites;
                lock (gate)
                {
                    string reason;
                    locked = LockRules.IsLocked(policy, DateTime.Now, DateTime.UtcNow, out reason);
                    sites = policy != null ? policy.AllowedSites : new string[0];
                    if (locked != currentLocked) heartbeatNow.Set(); // tell the teacher now
                    currentLocked = locked;
                    currentReason = reason;
                }
                try
                {
                    enforcer.ApplyPolicy(locked, sites);
                    enforcer.Scan(locked);
                    if (++pass % 300 == 0) enforcer.ReloadAllowlist();
                }
                catch (Exception e)
                {
                    Log.Write("Enforce error: " + e.Message);
                }
                Thread.Sleep(locked ? 500 : 1000);
            }
        }

        // ------------------------------------------------------------------ pipe

        void PipeLoop()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));

            while (!stopping)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(Pipe.Name, PipeDirection.InOut, 4,
                        PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security);
                    server.WaitForConnection();
                    NamedPipeServerStream connected = server;
                    server = null;
                    StartThread(delegate { HandleClient(connected); });
                }
                catch (Exception e)
                {
                    if (server != null) server.Dispose();
                    Log.Write("Pipe error: " + e.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        void HandleClient(NamedPipeServerStream pipe)
        {
            // A client that never sends anything is cut off after 45 seconds.
            using (pipe)
            using (new Timer(delegate { try { pipe.Dispose(); } catch (Exception) { } }, null, 45000, Timeout.Infinite))
            {
                try
                {
                    string line = Pipe.ReadLine(pipe);
                    if (line == null) return;
                    Dictionary<string, object> req = Json.ReadObject(line);
                    string cmd = Json.Str(req, "cmd");
                    Dictionary<string, object> res = cmd == "register" ? Register(req) : Status();
                    Pipe.WriteLine(pipe, Json.Write(res));
                }
                catch (Exception e)
                {
                    Log.Write("Client error: " + e.Message);
                }
            }
        }

        Dictionary<string, object> Status()
        {
            var r = new Dictionary<string, object>();
            lock (gate)
            {
                bool registered = state.DeviceId != null;
                r["registered"] = registered;
                r["locked"] = currentLocked;
                r["reason"] = registered && policy == null ? "Connecting to the class" : currentReason;
                r["className"] = policy != null ? policy.ClassName : null;
                r["sites"] = policy != null ? policy.AllowedSites : new string[0];
                r["home"] = policy != null ? LockRules.HomeUrl(policy.AllowedSites) : null;
                r["online"] = online;
                r["error"] = lastError;
            }
            return r;
        }

        Dictionary<string, object> Register(Dictionary<string, object> req)
        {
            var r = new Dictionary<string, object>();
            string code = (Json.Str(req, "code") ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                r["ok"] = false;
                r["error"] = "Enter the class code.";
                return r;
            }
            lock (gate)
            {
                // Only the teacher can move a laptop: removing it in the panel
                // clears DeviceId, and then the code can be typed again.
                if (state.DeviceId != null)
                {
                    r["ok"] = false;
                    r["error"] = "This laptop is already connected. To change the class, the teacher removes it in the panel first.";
                    return r;
                }
            }
            try
            {
                string secret;
                lock (gate) secret = state.Secret;
                Dictionary<string, object> res = Api.RegisterDevice(code, MachineId(), Environment.MachineName, secret);
                string deviceId = Json.Str(res, "device_id");
                if (string.IsNullOrEmpty(deviceId)) throw new ApiException("Unexpected answer from the server.");
                lock (gate)
                {
                    state.DeviceId = deviceId;
                    Store.Save(state);
                }
                heartbeatNow.Set();
                Log.Write("Registered to class \"" + Json.Str(res, "class_name") + "\"");
                r["ok"] = true;
                r["className"] = Json.Str(res, "class_name");
            }
            catch (Exception e)
            {
                if (!(e is ApiException)) Log.Write("Register error: " + e);
                r["ok"] = false;
                r["error"] = e is ApiException ? e.Message : "Could not join the class: " + e.GetBaseException().Message;
            }
            return r;
        }

        static string MachineId()
        {
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                object v = k != null ? k.GetValue("MachineGuid") : null;
                if (v == null) throw new ApiException("Could not read the laptop's ID.");
                return v.ToString();
            }
        }
    }
}
