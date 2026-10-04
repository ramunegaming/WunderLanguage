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
        // Battle monster vocabulary (cited per game): known monster names for
        // splitting merged multi-type rows into one line each. Longest-first,
        // same as the session memo — pack entries just pre-warm what the memo
        // would learn next fight. Empty = unknown game, memo only.
        System.Collections.Generic.IReadOnlyList<string> MonsterVocabulary { get; }
        // Battle command vocabulary (cited per game) for splitting merged action
        // rows into positional slots. Empty = unknown game, verbatim passthrough.
        System.Collections.Generic.IReadOnlyList<string> BattleCommands { get; }
        // Battle item vocabulary (cited per game) for splitting merged
        // inventory columns into per-item rows. Empty = unknown game.
        System.Collections.Generic.IReadOnlyList<string> ItemVocabulary { get; }
        // Weapon-select slot labels (cited per game, e.g. FFVI equip slots)
        // marking the on-top equip box. Empty = unknown game, no weapon box.
        System.Collections.Generic.IReadOnlyList<string> WeaponLabels { get; }
        // Battle UI glossary (cited per game): known chrome (commands,
        // items, equip labels) with canonical EN. Exact-match instant
        // translation — no CAT/DeepL burn, no JA-verbatim flicker while the
        // refresh leg crawls. Empty = unknown game, everything live.
        System.Collections.Generic.IReadOnlyList<(string ja, string en)> BattleGlossary { get; }
        // Vision item 3 — per-game calibration (estimated in learning
        // mode, required at publish). Neutral defaults reproduce today's
        // global behavior exactly; tuned packs override per citation.
        // BoxScoreThreshold: boxes below drop before assembly (0.0 = all).
        // BoxInsetPx: OCR crop inset inside detected borders (0.0 = none).
        // NmsIou: overlap suppression threshold (1.0 = none).
        // FontScale: panel font multiplier (1.0 = global size).
        double BoxScoreThreshold { get; }
        double BoxInsetPx { get; }
        double NmsIou { get; }
        double FontScale { get; }
    }
}