namespace WunderLanguage.Host.Packs
{
    public static class UniversalPack
    {
        public static readonly (string from, string to)[] OcrFixups = { };
        public static readonly (string from, string to)[] DisplayFixups = { };
        public const string TitleMarker = "";
        public const string FullTitle = "";
        public static readonly (string ja, string en)[] TitleMap = { };
        public static bool IsTransitionBlend(string text) => false;
        public static string ApplySweepFixups(string fixed_) => fixed_;
        public static string ApplyGuardFixups(string fixed_) => fixed_;
        public static string ApplyDaughterFixups(string fixed_) => fixed_;
        public static string ApplySacrificeFixups(string fixed_) => fixed_;
        public static string ApplyDecisionFixups(string fixed_) => fixed_;
        public static string ApplyRewardFixups(string fixed_) => fixed_;
        public static string ApplyButtonContextFixups(string fixed_) => fixed_;
        public static string ApplyVoiceTailFixups(string fixed_) => fixed_;
        public static string? ApplyTitleFixup(string japaneseSource) => null;
        public static string ApplyEnNounFixups(string english, string japaneseSource) => english;
        public static readonly string[] Signatures = { };
        public static bool KeepSpeakerBrackets => false;
        public static string ApplyGameOverRowFixups(string p) => p;
        public static bool IsMenuHeaderRow(string ascii)
        {
            string t = ascii.Trim();
            if (t.Length == 0) return false;
            string u = t.ToUpperInvariant();
            if (u.Contains("GAME OVER") || u.Contains("PLAYER") || u.Contains("SELECT") || u.Contains("ITEM")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"(?<![A-Z])DO(?![A-Z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Z ]{4,}$")) return true;
            return false;
        }
    }
    public sealed class UniversalPackInstance : IPack
    {
        public static readonly UniversalPackInstance Shared = new();
        public string GameId => "universal";
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups => UniversalPack.OcrFixups;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups => UniversalPack.DisplayFixups;
        public string TitleMarker => UniversalPack.TitleMarker;
        public string FullTitle => UniversalPack.FullTitle;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> TitleMap => UniversalPack.TitleMap;
        public bool IsTransitionBlend(string text) => UniversalPack.IsTransitionBlend(text);
        public string ApplySweepFixups(string fixed_) => UniversalPack.ApplySweepFixups(fixed_);
        public string ApplyGuardFixups(string fixed_) => UniversalPack.ApplyGuardFixups(fixed_);
        public string ApplyDaughterFixups(string fixed_) => UniversalPack.ApplyDaughterFixups(fixed_);
        public string ApplySacrificeFixups(string fixed_) => UniversalPack.ApplySacrificeFixups(fixed_);
        public string ApplyDecisionFixups(string fixed_) => UniversalPack.ApplyDecisionFixups(fixed_);
        public string ApplyRewardFixups(string fixed_) => UniversalPack.ApplyRewardFixups(fixed_);
        public string ApplyButtonContextFixups(string fixed_) => UniversalPack.ApplyButtonContextFixups(fixed_);
        public string ApplyVoiceTailFixups(string fixed_) => UniversalPack.ApplyVoiceTailFixups(fixed_);
        public string? ApplyTitleFixup(string japaneseSource) => UniversalPack.ApplyTitleFixup(japaneseSource);
        public string ApplyEnNounFixups(string english, string japaneseSource) => UniversalPack.ApplyEnNounFixups(english, japaneseSource);
        public string ApplyGameOverRowFixups(string p) => UniversalPack.ApplyGameOverRowFixups(p);
        public bool KeepSpeakerBrackets => UniversalPack.KeepSpeakerBrackets;
        public System.Collections.Generic.IReadOnlyList<string> Signatures => UniversalPack.Signatures;
        public bool IsMenuHeaderRow(string ascii) => UniversalPack.IsMenuHeaderRow(ascii);
    }
}