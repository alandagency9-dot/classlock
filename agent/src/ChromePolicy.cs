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

        // Values written only while locked.
        static readonly string[] LockValues =
        {
            "RestoreOnStartup", "HomepageLocation", "HomepageIsNewTabPage", "ShowHomeButton",
            "IncognitoModeAvailability", "BrowserGuestModeEnabled", "BrowserAddPersonEnabled",
            "DeveloperToolsAvailability", "DownloadRestrictions"
        };
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
