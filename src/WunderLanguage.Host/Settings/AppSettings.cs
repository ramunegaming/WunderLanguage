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

        // Encrypted at rest (DPAPI, CurrentUser). Never plaintext on disk.
        public string? DeepLApiKeyEncrypted { get; set; }

        // Runtime only. Never serialized.
        [JsonIgnore]
        public string? DeepLApiKey { get; set; }

        private static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WunderLanguage", "settings.json");

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

                    string? encrypted = null;
                    if (root.TryGetProperty(nameof(DeepLApiKeyEncrypted), out var enc) && enc.ValueKind == JsonValueKind.String)
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

                    FileLogger.Log($"Settings loaded: provider={settings.TranslationProvider}, keyPresent={settings.DeepLApiKey is not null}, encryptedPresent={!string.IsNullOrEmpty(encrypted)}, legacyPresent={!string.IsNullOrEmpty(legacyPlaintext)}");
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

            var payload = new
            {
                TranslationProvider,
                MenuDisplayMode,
                DeepLApiKeyEncrypted = encrypted,
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            FileLogger.Log($"Settings saved: provider={TranslationProvider}, keyPresent={DeepLApiKey is not null}, encryptedPresent={!string.IsNullOrEmpty(encrypted)}");
        }
    }
}