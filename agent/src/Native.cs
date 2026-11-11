using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ClassLock
{
    /// <summary>Win32 calls for reading and stopping other processes.</summary>
    public static class Native
    {
        const uint PROCESS_TERMINATE = 0x0001;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint TOKEN_QUERY = 0x0008;
        const int TokenUser = 1;
        const int TokenElevationType = 18;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

        /// <summary>Full path of the program file, or null when Windows does not allow us to read it.</summary>
        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool Kill(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_TERMINATE, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return TerminateProcess(h, 1); }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// Reads the account that owns a process. isAdmin is true when that
        /// account is an administrator, also when the window is not elevated
        /// (a UAC "split token" exists only for administrators).
        /// Returns null when the token cannot be read.
        /// </summary>
        public static string ProcessOwner(int pid, out bool isAdmin)
        {
            isAdmin = false;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(h, TOKEN_QUERY, out token)) return null;

                string sid = null;
                int len;
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out len);
                if (len > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal(len);
                    try
                    {
                        if (GetTokenInformation(token, TokenUser, buf, len, out len))
                            sid = new SecurityIdentifier(Marshal.ReadIntPtr(buf)).Value;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }

                IntPtr typeBuf = Marshal.AllocHGlobal(4);
                try
                {
                    int ret;
                    if (GetTokenInformation(token, TokenElevationType, typeBuf, 4, out ret))
                    {
                        int type = Marshal.ReadInt32(typeBuf);
                        // 2 = elevated admin, 3 = admin running non-elevated.
                        if (type == 2 || type == 3) isAdmin = true;
                        else
                        {
                            // 1 = no split token: a standard user, or UAC is off.
                            using (var id = new WindowsIdentity(token))
                                isAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(typeBuf); }
                return sid;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(h);
            }
        }

        /// <summary>True for normal people accounts (local, domain, Microsoft or Entra ID).
        /// False for SYSTEM, service and window-manager accounts.</summary>
        public static bool IsPersonSid(string sid)
        {
            return sid != null && (sid.StartsWith("S-1-5-21-") || sid.StartsWith("S-1-12-1-"));
        }
    }
}
