using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WunderLanguage.Host.Settings
{
    public class AppSettings
    {
        public string TranslationProvider { get; set; } = "DeepL"; // "DeepL" or "Offline"
        public string MenuDisplayMode { get; set; } = "Text"; // "Text" or "Ascii"
        // Diagnostics mode (file-only, default OFF): gates dev artifacts —
        // corpus recording, troubleshoot snapshots, per-loop verbose log
        // lines. Enabled via "Diagnostics": true in settings.json or the
        // --diagnostics flag. Translation cache + mined corrections stay
        // always-on (user-facing benefits, tiny).
        public bool Diagnostics { get; set; } = false;

        // Pop-out feed (batch: revert popout-feed). Where the detached feed
        // lived last run: mode + position, so a restart never silently
        // re-docks or parks the panel at 0,0. 0/0 = "no stored position"
        // (a freshly-calibrated default is seeded instead).
        public bool FeedDetached { get; set; } = false;
        public double FeedLeft { get; set; } = 0;
        public double FeedTop { get; set; } = 0;
        // Feed resize (revert: revert feed-resize): last user-chosen size
        // of the popped-out feed, DIP. 0 = never resized (natural size).
        public double FeedWidth { get; set; } = 0;
        public double FeedHeight { get; set; } = 0;

        // Encrypted at rest (DPAPI, CurrentUser). Never plaintext on disk.
        public string? DeepLApiKeyEncrypted { get; set; }

        // Runtime only. Never serialized.
        [JsonIgnore]
        public string? DeepLApiKey { get; set; }

        private static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WunderLanguage", "settings.json");

        // API key lives apart from human-edited settings (perf/hygiene
        // batch: a 500-char blob next to 3 readable lines invites bad
        // edits). Same DPAPI protection, separate file. Reads fall back
        // to the legacy embedded blob (migrates on next Save).
        // Revert: delete KeyPath + fallback, restore payload field.
        private static string KeyPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WunderLanguage", ".deeplkey");

        // Pop-out feed geometry read block (revert: revert popout-feed /
        // revert feed-resize own their fields). Kept as its own method so
        // the --feedresize gate can round-trip payload -> fields with this
        // exact code and zero file I/O.
        internal static void ReadFeedFields(AppSettings settings, JsonElement root)
        {
            if (root.TryGetProperty(nameof(FeedDetached), out var fd) && (fd.ValueKind == JsonValueKind.True || fd.ValueKind == JsonValueKind.False))
                settings.FeedDetached = fd.GetBoolean();
            if (root.TryGetProperty(nameof(FeedLeft), out var fL) && fL.ValueKind == JsonValueKind.Number)
                settings.FeedLeft = fL.GetDouble();
            if (root.TryGetProperty(nameof(FeedTop), out var fT) && fT.ValueKind == JsonValueKind.Number)
                settings.FeedTop = fT.GetDouble();
            if (root.TryGetProperty(nameof(FeedWidth), out var fW) && fW.ValueKind == JsonValueKind.Number)
                settings.FeedWidth = fW.GetDouble();
            if (root.TryGetProperty(nameof(FeedHeight), out var fH) && fH.ValueKind == JsonValueKind.Number)
                settings.FeedHeight = fH.GetDouble();
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var settings = new AppSettings();

                    if (root.TryGetProperty(nameof(TranslationProvider), out var tp) && tp.ValueKind == JsonValueKind.String)
                    {
                        string p = tp.GetString() ?? "DeepL";
                        if (p == "Offline" || p == "DeepL")
                            settings.TranslationProvider = p;
                        else if (p == "Argos")
                            settings.TranslationProvider = "Offline";
                        else
                            settings.TranslationProvider = "DeepL";
                    }

                    if (root.TryGetProperty(nameof(MenuDisplayMode), out var md) && md.ValueKind == JsonValueKind.String)
                    {
                        string m = md.GetString() ?? "Text";
                        settings.MenuDisplayMode = (m == "Ascii") ? "Ascii" : "Text";
                    }

                    if (root.TryGetProperty(nameof(Diagnostics), out var dg) && (dg.ValueKind == JsonValueKind.True || dg.ValueKind == JsonValueKind.False))
                        settings.Diagnostics = dg.GetBoolean();

                    // Pop-out feed (revert: revert popout-feed) + feed resize
                    // (revert: revert feed-resize) - extracted as one block
                    // so the --feedresize gate can round-trip the payload
                    // through the REAL read path without touching the file.
                    ReadFeedFields(settings, root);

                    string? encrypted = null;
                    try
                    {
                        if (File.Exists(KeyPath))
                            encrypted = File.ReadAllText(KeyPath).Trim();
                    }
                    catch { }
                    if (string.IsNullOrEmpty(encrypted)
                        && root.TryGetProperty(nameof(DeepLApiKeyEncrypted), out var enc) && enc.ValueKind == JsonValueKind.String)
                        encrypted = enc.GetString();

                    // Legacy plaintext installs (pre-DPAPI).
                    string? legacyPlaintext = null;
                    if (root.TryGetProperty("DeepLApiKey", out var leg) && leg.ValueKind == JsonValueKind.String)
                        legacyPlaintext = leg.GetString();

                    if (!string.IsNullOrEmpty(encrypted))
                    {
                        try
                        {
                            byte[] cipher = Convert.FromBase64String(encrypted);
                            byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                            settings.DeepLApiKey = Encoding.UTF8.GetString(plain);
                        }
                        catch
                        {
                            // Wrong user/machine or corrupt blob - force re-enter, don't crash.
                            settings.DeepLApiKey = null;
                        }
                    }
                    else if (!string.IsNullOrEmpty(legacyPlaintext))
                    {
                        // Migrates to encrypted on next Save.
                        settings.DeepLApiKey = legacyPlaintext;
                    }

                    FileLogger.Log($"Settings loaded: provider={settings.TranslationProvider}, keyPresent={settings.DeepLApiKey is not null}, encryptedPresent={!string.IsNullOrEmpty(encrypted)}, legacyPresent={!string.IsNullOrEmpty(legacyPlaintext)}, feedDetached={settings.FeedDetached}, feedPos=({settings.FeedLeft},{settings.FeedTop})");
                    return settings;
                }
            }
            catch
            {
                // Corrupt settings file - fall back to defaults rather than crash on startup.
            }

            return new AppSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);

            string? encrypted = null;
            if (!string.IsNullOrWhiteSpace(DeepLApiKey))
            {
                byte[] plain = Encoding.UTF8.GetBytes(DeepLApiKey);
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                encrypted = Convert.ToBase64String(cipher);
            }

            var payload = BuildPayload();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            try
            {
                if (!string.IsNullOrWhiteSpace(DeepLApiKey))
                    File.WriteAllText(KeyPath, encrypted ?? "");
                else if (File.Exists(KeyPath))
                    File.Delete(KeyPath);
            }
            catch { }
            FileLogger.Log($"Settings saved: provider={TranslationProvider}, keyPresent={DeepLApiKey is not null}, encryptedPresent={!string.IsNullOrEmpty(encrypted)}");
        }

        // The exact persisted shape as its own method so the --feedresize
        // gate can assert these fields are included WITHOUT Save()ing over
        // the real settings.json (Save also rotates the key file).
        // Revert: revert feed-resize (delete this + the gate's use; the
        // popout-feed fields below predate it).
        internal object BuildPayload() => new
        {
            TranslationProvider,
            MenuDisplayMode,
            Diagnostics,
            // Pop-out feed (revert: revert popout-feed). Must be in the
            // payload or the next SettingsPopup.Save silently wipes both
            // the detach state and where the feed was parked.
            FeedDetached,
            FeedLeft,
            FeedTop,
            // Feed resize (revert: revert feed-resize). Same rule: absent
            // from the payload and the next save silently resets the size.
            FeedWidth,
            FeedHeight,
        };
    }

    // Process-wide diagnostics gate (see AppSettings.Diagnostics). Set once
    // at startup from settings file / --diagnostics flag; read everywhere.
    // Revert: delete class + gate sites.
    public static class Diag
    {
        public static bool Enabled { get; set; }
    }
}