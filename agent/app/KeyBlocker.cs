using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClassLock
{
    /// <summary>
    /// While the laptop is locked, swallows the keys that close Chrome, close tabs,
    /// open new windows or switch to other programs (Alt+F4, Ctrl+W, Alt+Tab, the
    /// Windows key, ...). Ctrl+Alt+Del cannot be blocked by any program; it only
    /// offers lock, sign out and Task Manager, and Task Manager is closed at once.
    /// The hook runs on its own thread so a slow status call never delays typing.
    /// </summary>
    public static class KeyBlocker
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        const int LLKHF_ALTDOWN = 0x20;
        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_ESCAPE = 0x1B, VK_TAB = 0x09, VK_F4 = 0x73, VK_F11 = 0x7A;
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_APPS = 0x5D;

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr extraInfo;
        }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        static readonly HookProc Proc = Callback; // kept in a field so it is never garbage collected
        static volatile bool active;
        static bool started;

        /// <summary>Turns blocking on or off. Starts the hook thread the first time.</summary>
        public static bool Active
        {
            get { return active; }
            set
            {
                active = value;
                if (value && !started)
                {
                    started = true;
                    var t = new Thread(Run);
                    t.IsBackground = true;
                    t.Name = "KeyBlocker";
                    t.Start();
                }
            }
        }

        static void Run()
        {
            using (Process me = Process.GetCurrentProcess())
            using (ProcessModule module = me.MainModule)
            {
                SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(module.ModuleName), 0);
            }
            Application.Run(); // message loop for this thread; the hook needs it
        }

        static bool Down(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>True for a key press that must not reach Chrome or Windows while locked.</summary>
        public static bool ShouldBlock(int vk, bool ctrl, bool alt, bool shift)
        {
            if (vk == VK_LWIN || vk == VK_RWIN || vk == VK_APPS || vk == VK_F11) return true;
            if (alt && (vk == VK_F4 || vk == VK_TAB || vk == VK_ESCAPE || vk == 0x20 /* Alt+Space menu */)) return true;
            if (ctrl && vk == VK_ESCAPE) return true;   // Start menu
            if (ctrl && vk == VK_F4) return true;       // close tab
            if (ctrl)
            {
                // W close tab, N new window, T new tab, Q quit, O open file, S save, P print,
                // J downloads, H history, U page source, L/K address bar.
                switch ((char)vk)
                {
                    case 'W': case 'N': case 'T': case 'Q': case 'O': case 'S':
                    case 'P': case 'J': case 'H': case 'U': case 'L': case 'K':
                        return true;
                }
                if (shift && ((char)vk == 'I' || (char)vk == 'C' || (char)vk == 'B' || (char)vk == 'M'))
                    return true;                          // developer tools, bookmarks, profiles
            }
            return false;
        }

        static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && active)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool alt = (k.flags & LLKHF_ALTDOWN) != 0;
                    if (ShouldBlock(k.vkCode, Down(VK_CONTROL), alt, Down(VK_SHIFT))) return new IntPtr(1);
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }
}
