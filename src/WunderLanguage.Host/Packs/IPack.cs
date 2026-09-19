namespace WunderLanguage.Host.Packs
{
    public interface IPack
    {
        string GameId { get; }
        System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups { get; }
        System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups { get; }
        string TitleMarker { get; }
        string FullTitle { get; }
        System.Collections.Generic.IReadOnlyList<(string ja, string en)> TitleMap { get; }
        bool IsTransitionBlend(string text);
        string ApplySweepFixups(string fixed_);
        string ApplyGuardFixups(string fixed_);
        string ApplyDaughterFixups(string fixed_);
        string ApplySacrificeFixups(string fixed_);
        string ApplyDecisionFixups(string fixed_);
        string ApplyRewardFixups(string fixed_);
        string ApplyButtonContextFixups(string fixed_);
        string ApplyVoiceTailFixups(string fixed_);
        string? ApplyTitleFixup(string japaneseSource);
        string ApplyEnNounFixups(string english, string japaneseSource);
        string ApplyGameOverRowFixups(string p);
        bool IsMenuHeaderRow(string ascii);
        bool KeepSpeakerBrackets { get; }
        System.Collections.Generic.IReadOnlyList<string> Signatures { get; }
    }
}