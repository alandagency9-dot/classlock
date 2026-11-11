using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClassLock
{
    /// <summary>
    /// Runs inside the SYSTEM service. Makes sure the ClassLock app runs in every
    /// signed-in Windows session. If a student ends the app (for example in Task
    /// Manager while unlocked), the service starts it again within about a second,
    /// as that student, on their desktop.
    /// </summary>
    public class SessionLauncher
    {
        const int WTSActive = 0;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [StructLayout(LayoutKind.Sequential)]
        struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr WinStationName;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr info, out int count);
        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr memory);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);
        [DllImport("userenv.dll", SetLastError = true)]
        static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
        [DllImport("userenv.dll")]
        static extern bool DestroyEnvironmentBlock(IntPtr env);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessAsUser(IntPtr token, string app, string cmdLine, IntPtr procAttr, IntPtr threadAttr,
            bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        readonly string appExe;
        readonly Dictionary<int, DateTime> lastStart = new Dictionary<int, DateTime>();

        public SessionLauncher(string appExe)
        {
            this.appExe = appExe;
        }

        /// <summary>Signed-in sessions whose user is at the screen right now.</summary>
        static List<int> ActiveSessions()
        {
            var result = new List<int>();
            IntPtr info;
            int count;
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out info, out count)) return result;
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var s = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(info.ToInt64() + i * size), typeof(WTS_SESSION_INFO));
                    if (s.State == WTSActive && s.SessionId != 0) result.Add(s.SessionId);
                }
            }
            finally { WTSFreeMemory(info); }
            return result;
        }

        /// <summary>Starts the app in each active session where it is not running.</summary>
        public void EnsureRunning(HashSet<int> sessionsWithApp)
        {
            if (!System.IO.File.Exists(appExe)) return;
            foreach (int session in ActiveSessions())
            {
                if (sessionsWithApp.Contains(session)) continue;
                DateTime last;
                if (lastStart.TryGetValue(session, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(5)) continue;
                lastStart[session] = DateTime.UtcNow;
                Start(session);
            }
        }

        void Start(int session)
        {
            IntPtr token = IntPtr.Zero, env = IntPtr.Zero;
            try
            {
                // No user token while the session is still signing in: try again later.
                if (!WTSQueryUserToken(session, out token)) return;
                CreateEnvironmentBlock(out env, token, false);
                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                si.lpDesktop = @"winsta0\default";
                PROCESS_INFORMATION pi;
                if (CreateProcessAsUser(token, appExe, "\"" + appExe + "\"", IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_UNICODE_ENVIRONMENT, env, System.IO.Path.GetDirectoryName(appExe), ref si, out pi))
                {
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    Log.Write("Started the ClassLock app in session " + session + ".");
                }
                else
                {
                    Log.Write("Could not start the ClassLock app in session " + session + ": error " + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception e)
            {
                Log.Write("Could not start the ClassLock app in session " + session + ": " + e.Message);
            }
            finally
            {
                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
    }
}
