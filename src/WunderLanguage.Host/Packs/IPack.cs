namespace WunderLanguage.Host.Packs
{
    public interface IPack
    {
        string GameId { get; }
        System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups { get; }
        System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups { get; }
        string TitleMarker { get; }
        string FullTitle { get; }
        // Item 4 latin titles (2026-10-30; revert: revert latin-title-match):
        // pack-declared LATIN title strings for the strict title matcher —
        // known-exact-string only (KC-fold + casefold + alphanumerics; the
        // FULL normalized form must be present in the read). Never fuzzy,
        // never fragments: mined over 5,467 fired bubbles + 108,739 OCR
        // blocks = 0 false-matches (tools/i4_latin_mine.py). Empty = no
        // latin title declared (JA path only).
        System.Collections.Generic.IReadOnlyList<string> LatinTitleForms { get; }
        // Item 4 fallback A (revert: revert canonical-title-fallback): this
        // pack's title logo ART never reads — measured: contrast re-OCR
        // 0/24 variant-runs on the four harvested title shots
        // (tools/i4_ocr_boost.py), full latin form 0/108,739 corpus blocks.
        // true = the title screen is identified by quiet + steadiness (E)
        // + window-title identity, and the canonical FullTitle is POSTED
        // instead of waiting for a logo read that can never come.
        bool TitleLogoUnreadable { get; }
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
        // Dialogue name glossary (cited per game, official manual forms):
        // party names masked through live translation as placeholders and
        // restored after (qmask machinery) so CAT can't mangle them
        // ("tina-foreigner", "Ke-fugu"). Katakana-boundary matched, so
        // モグ never fires inside モグリン. Empty = unknown game, names live.
        System.Collections.Generic.IReadOnlyList<(string ja, string en)> NameGlossary { get; }
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
        // Z2 5-box inventory feed (revert: revert z2-5box-view): pack
        // declares that this MENU fire is its inventory screen + the
        // screen's fixed chrome. The selected item name is the ONLY
        // extracted field (top-right, live-refreshed in place by menu
        // updates); the other four boxes render pack constants, EN goes
        // below the grid. false = any other menu (normal path untouched).
        bool TryInventoryLayout(string jaJoined, string enJoined, out InventoryLayout layout);
    }

    // Z2 5-box inventory feed: what the side panel draws inside the
    // standard outline box (geometry lives in the panel, contents here).
    public sealed class InventoryLayout
    {
        // Top-right: the only extracted/translated field — changes with
        // selection (カンテラ -> ブーメラン).
        public string ItemJa { get; set; } = "";
        public string ItemEn { get; set; } = "";
        // Bottom-left action words (static), middle-right static text,
        // bottom-right label (already English on the real screen).
        public System.Collections.Generic.IReadOnlyList<string> ActionRows { get; set; } = System.Array.Empty<string>();
        public string MidText { get; set; } = "";
        public string BottomRight { get; set; } = "";
        // Rows that fit no box — rendered below the EN strip, never dropped.
        public string Residual { get; set; } = "";
        // Z2 v2 (revert: revert z2-v2-bottle-mirror): bottle-selected
        // state — ペンダント + EQUIPMENT merge into ONE tall box (the bottle
        // art has nothing to translate: blank); deselected fires carry
        // ペンダント again and render the two boxes — derived per fire.
        public bool IsBottle { get; set; }
        // EN mirror grid (below the separator: the same box layout with
        // English text — pack-constant chrome, user's canonical forms).
        public System.Collections.Generic.IReadOnlyList<string> ActionRowsEn { get; set; } = System.Array.Empty<string>();
        public string MidTextEn { get; set; } = "";
    }
}