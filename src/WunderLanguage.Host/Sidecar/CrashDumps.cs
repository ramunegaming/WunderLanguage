using System.IO;
using Microsoft.Win32;

namespace WunderLanguage.Host.Sidecar
{
    // Survival batch 1 (WI2 Ch10 WER LocalDumps): per-user crash dumps with
    // zero code paths — the OS writes a minidump when the host dies, so a
    // foreign machine debugs itself. HKCU needs no admin (survives non-admin
    // users); HKLM attempted best-effort for machine-wide coverage.
    // Revert: delete this file + the EnsureLocalDumps() call in App.xaml.cs.
    public static class CrashDumps
    {
        public static void EnsureLocalDumps()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WunderLanguage", "dumps");
                try { Directory.CreateDirectory(dir); } catch { }
                WriteKey(Registry.CurrentUser, dir);
                try { WriteKey(Registry.LocalMachine, dir); } catch { }
                FileLogger.Log($"[wer] localdumps dir={dir}");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[wer] ensure failed: {ex.Message}");
            }
        }

        private static void WriteKey(RegistryKey hive, string dir)
        {
            try
            {
                using var key = hive.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\WunderLanguage.Host.exe");
                if (key is null) return;
                key.SetValue("DumpFolder", dir, RegistryValueKind.ExpandString);
                key.SetValue("DumpCount", 10, RegistryValueKind.DWord);
                key.SetValue("DumpType", 1, RegistryValueKind.DWord); // 1 = mini
            }
            catch { }
        }
    }
}
