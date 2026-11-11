using System;
using Microsoft.Win32;

namespace ClassLock
{
    /// <summary>
    /// Machine-wide Chrome and Windows policies in HKLM. Students (standard
    /// users) cannot change HKLM. Chrome watches these keys and applies
    /// changes within seconds.
    /// The setup program also uses this class to remove everything on uninstall.
    /// </summary>
    public static class ChromePolicy
    {
        const string ChromeKey = @"SOFTWARE\Policies\Google\Chrome";
        const string InstallerKey = @"SOFTWARE\Policies\Microsoft\Windows\Installer";
        const string StoreKey = @"SOFTWARE\Policies\Microsoft\WindowsStore";
        const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        /// <summary>
        /// Windows starts the desktop (explorer.exe) again when it closes. While locked,
        /// ClassLock closes it on purpose, so this is off; it is on again when unlocked.
        /// </summary>
        public static void SetShellAutoRestart(bool on)
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey winlogon = hklm.OpenSubKey(WinlogonKey, true))
            {
                if (winlogon != null) winlogon.SetValue("AutoRestartShell", on ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        // Values written only while locked.
        static readonly string[] LockValues =
        {
            "RestoreOnStartup", "HomepageLocation", "HomepageIsNewTabPage", "ShowHomeButton",
            "IncognitoModeAvailability", "BrowserGuestModeEnabled", "BrowserAddPersonEnabled",
            "DeveloperToolsAvailability", "DownloadRestrictions",
            "PrintingEnabled", "AllowFileSelectionDialogs", "TaskManagerEndProcessEnabled", "BackgroundModeEnabled"
        };

        // Standard Windows group policies, set only while locked (machine-wide, all users).
        const string SystemPolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        const string ExplorerPolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        const string EdgeUiKey = @"SOFTWARE\Policies\Microsoft\Windows\EdgeUI";

        /// <summary>
        /// While locked: Task Manager is disabled, the Shut down / Restart buttons are
        /// hidden (Ctrl+Alt+Del screen and sign-in screen), Switch user and Change
        /// password are hidden, and touch edge swipes do nothing. Removed on unlock.
        /// Shutting down would not escape the lock anyway: the laptop starts locked.
        /// </summary>
        public static void SetWindowsLock(bool on)
        {
            using (RegistryKey hklm = Hklm())
            {
                using (RegistryKey sys = hklm.CreateSubKey(SystemPolicyKey))
                {
                    SetOrDelete(sys, "DisableTaskMgr", on, 1);
                    SetOrDelete(sys, "HideFastUserSwitching", on, 1);
                    SetOrDelete(sys, "DisableChangePassword", on, 1);
                    // 0 hides the power button on the sign-in screen; Windows' own default is 1.
                    sys.SetValue("shutdownwithoutlogon", on ? 0 : 1, RegistryValueKind.DWord);
                }
                using (RegistryKey explorer = hklm.CreateSubKey(ExplorerPolicyKey))
                    SetOrDelete(explorer, "NoClose", on, 1);
                using (RegistryKey edge = hklm.CreateSubKey(EdgeUiKey))
                    SetOrDelete(edge, "AllowEdgeSwipe", on, 0);
            }
        }

        static void SetOrDelete(RegistryKey key, string name, bool set, int value)
        {
            if (set) key.SetValue(name, value, RegistryValueKind.DWord);
            else key.DeleteValue(name, false);
        }
        static readonly string[] LockLists = { "URLBlocklist", "URLAllowlist", "RestoreOnStartupURLs" };

        static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        static void SetList(RegistryKey parent, string name, string[] items)
        {
            parent.DeleteSubKeyTree(name, false);
            using (RegistryKey k = parent.CreateSubKey(name))
            {
                for (int i = 0; i < items.Length; i++) k.SetValue((i + 1).ToString(), items[i], RegistryValueKind.String);
            }
        }

        /// <summary>Always on: no Chrome extensions, no per-user MSI installs, no Microsoft Store.</summary>
        public static void ApplyBaseline()
        {
            using (RegistryKey hklm = Hklm())
            {
                using (RegistryKey chrome = hklm.CreateSubKey(ChromeKey))
                    SetList(chrome, "ExtensionInstallBlocklist", new[] { "*" });
                using (RegistryKey installer = hklm.CreateSubKey(InstallerKey))
                    installer.SetValue("DisableUserInstalls", 1, RegistryValueKind.DWord);
                // Works on Windows Education and Enterprise. The agent also blocks the Store app itself.
                using (RegistryKey store = hklm.CreateSubKey(StoreKey))
                    store.SetValue("RemoveWindowsStore", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>Chrome may open only the allowed sites. No downloads, incognito, guest or dev tools.</summary>
        public static void ApplyLock(string[] allowedSites)
        {
            string home = LockRules.HomeUrl(allowedSites);
            using (RegistryKey hklm = Hklm())
            using (RegistryKey chrome = hklm.CreateSubKey(ChromeKey))
            {
                SetList(chrome, "URLBlocklist", new[] { "*" });
                SetList(chrome, "URLAllowlist", allowedSites ?? new string[0]);
                chrome.SetValue("IncognitoModeAvailability", 1, RegistryValueKind.DWord);
                chrome.SetValue("BrowserGuestModeEnabled", 0, RegistryValueKind.DWord);
                chrome.SetValue("BrowserAddPersonEnabled", 0, RegistryValueKind.DWord);
                chrome.SetValue("DeveloperToolsAvailability", 2, RegistryValueKind.DWord);
                chrome.SetValue("DownloadRestrictions", 3, RegistryValueKind.DWord);
                // No print or file dialogs: they can browse the disk.
                chrome.SetValue("PrintingEnabled", 0, RegistryValueKind.DWord);
                chrome.SetValue("AllowFileSelectionDialogs", 0, RegistryValueKind.DWord);
                chrome.SetValue("TaskManagerEndProcessEnabled", 0, RegistryValueKind.DWord);
                // No hidden Chrome left running after its window closes.
                chrome.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                if (home != null)
                {
                    chrome.SetValue("RestoreOnStartup", 4, RegistryValueKind.DWord);
                    SetList(chrome, "RestoreOnStartupURLs", new[] { home });
                    chrome.SetValue("HomepageLocation", home, RegistryValueKind.String);
                    chrome.SetValue("HomepageIsNewTabPage", 0, RegistryValueKind.DWord);
                    chrome.SetValue("ShowHomeButton", 1, RegistryValueKind.DWord);
                }
                else
                {
                    foreach (string v in new[] { "RestoreOnStartup", "HomepageLocation", "HomepageIsNewTabPage", "ShowHomeButton" })
                        chrome.DeleteValue(v, false);
                    chrome.DeleteSubKeyTree("RestoreOnStartupURLs", false);
                }
            }
        }

        public static void ClearLock()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey chrome = hklm.OpenSubKey(ChromeKey, true))
            {
                if (chrome == null) return;
                foreach (string v in LockValues) chrome.DeleteValue(v, false);
                foreach (string l in LockLists) chrome.DeleteSubKeyTree(l, false);
            }
        }

        /// <summary>Removes every value ClassLock writes. Used by uninstall.</summary>
        public static void RemoveAll()
        {
            ClearLock();
            SetShellAutoRestart(true);
            SetWindowsLock(false);
            using (RegistryKey hklm = Hklm())
            {
                using (RegistryKey chrome = hklm.OpenSubKey(ChromeKey, true))
                    if (chrome != null) chrome.DeleteSubKeyTree("ExtensionInstallBlocklist", false);
                using (RegistryKey installer = hklm.OpenSubKey(InstallerKey, true))
                    if (installer != null) installer.DeleteValue("DisableUserInstalls", false);
                using (RegistryKey store = hklm.OpenSubKey(StoreKey, true))
                    if (store != null) store.DeleteValue("RemoveWindowsStore", false);
            }
        }
    }
}
