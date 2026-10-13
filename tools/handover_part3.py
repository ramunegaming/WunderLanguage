# Part 3: state, truth set, taxonomy, dictionary, policies, milestones, open items.
SECTIONS = [
("8. Current state per game (measured, not claimed)", [
"b:FFVI old corpus: 117 fires, overlap 76/83, missed 7, extra 41, NE 0.36, latency budget OK. FFVI new corpus: 104 fires, overlap 46/50, missed 4, extra 58. Zelda long: 71-72 fires, overlap 50/60, missed 10, extra 21-22, panellat EXCEEDED pre-existing (identical stash baseline — not a regression). Zelda intro: 4 growth fires, overlap 0 (fast-scroll pages outrun stability). Paper Mario clean run: 485 reads, 28 fires all intro, sim reproduces 28/28 with zero extras; gameplay silence = sidecar deaths plus focus holds plus static skips (diagnosed, not policy). Dims banked per session (PM 788x583). Exact-match overlap understates under variant churn (proven: clean-form recovery scores as miss plus extra).",
]),
("9. Truth eval set v1 (Step 1, 7e4977a)", [
"b:eval/truth.jsonl: 40 rows from 36 banked shots, game pane read directly (never OCR as ground truth). Dev 28 (17/18h sessions, tuning allowed), heldout 12 (22/23h, verification only). Verdicts: 17 miss-complete, 6 false-pub, 4 fidelity-error, 5 correct-silence controls, 2 hits, plus stale/dup/menu cases. Contexts span dialogue, typewriter partials (labeled by visible text), crawl pages, signs, 2 decision-box misses, game-over/menu, JA and Latin titles, prompt-leak garbage, ?-junk, blends, dups. Initial set only — no Paper Mario yet, not cross-game proof. Data-only batch. Revert: revert truth-set.",
]),
("10. Failure taxonomy and residue inventory", [
"b:Recognition (OCR misreads; beam N-best available but discarded until Step 2 banking), candidate formation (singletons never form — Tier 1/2 scope), commit policy (stability vs completeness; singleton residue ~35 FFVI-new + ~39 Zelda-long + 3 intro), timing (weak-hold 16s p50 Zelda; intro outrun), segmentation (ケアルファイア merge), temporal association (moving text, blends), false positives (menu garbage, prompt leak, ?-junk, notification hallucinations), duplicates (coal-mine double post, repeat fires), context (pack-less PM, unbanked names), downstream (translation follows JA errors: 嫁 soldiers, Quarfia). Title failures: sword-scene mistitle (vetoed), fast-skipped titles, 5-minute-late TITLE. Mash behavior: policy question open (keep stale / fast-track / expire — recommended expire; no build until answered).",
]),
("11. Dictionary stance and Step-3 experiment (agreed, unbuilt)", [
"p:Position: premade package yes (SudachiPy first choice for measurement; frozen wordlist for integration; license verified before shipping), re-rank N-best never rewrite, visual evidence primary, pack/memo names exempt, incomplete shapes exempt, frequency never overrules visual support. OCR verified vision-only (no LM, no dictionary); beam strings exist and are now banked.",
"p:Accepted experiment: truth set with dev/heldout split; four questions (candidate presence, re-rank wins, damage split by names/unusual/partial, CER plus exact-line plus latency); scoring frozen before measuring (beam-rank dominant, lexicon breaks near-ties, clear margin or no change; correct-reading-absent logged as generation limit); acceptance is corrections >=3x regressions with zero name damage, p95 +<=50ms/line, candidate-absent rate reported; fail means keep beam-diversity flagging plus fixup mining. Integration is a separate reversible batch only on pass, with corpora re-runs and cross-game checks.",
]),
("12. Title, sprite, and decision-box policy (all agreed, mostly unbuilt)", [
"b:Logo posts TITLE; press-start prompt posts as in-game text right after; English credits never post (audited: zero Latin-only publications). Zelda sprites: heart to <3 inline, sign arrows <- / down-arrow inline, Link-face ignored, brackets 1:1, leading-glyph junk treated as sprite misreads. Decision boxes (Zelda arrow, PM pointing hand): positioned question plus options in exact in-game positions, pointer sprite never posted. Sprite/positioned-box builds are queued behind Paper Mario data (Step 10).",
]),
("13. Milestone path", [
"h:Done (all gated, all committed)",
"b:Steps 1-2 (truth set, beam banking). Tiers 1-2 (fuzzy-window, anchored-singleton). Loops 1-5 (global fixups, confusion re-mine, memo persist plus Stop closeout, vocab proposals, profiles). Title hardening, ROM-ID identity plus paren fix, engine notice, death forensics, PM pack v1, shot labeling, capture-settings and emulator-version policies.",
"h:Planned in order",
"b:Step 3 dictionary experiment (needs truth plus beams: both banked). Steps 4-6 Zelda/FFVI fixes driven by triage (title live proofs, missed-line sweep, LM-fluency gate only on evidence). Steps 7-8 Paper Mario baseline play then pack cross-game check (promote only what generalizes). Step 9 loop payoffs (re-mine and promotion review with PM data; profile tuning only on measured spread). Step 10 big queued builds in evidence order (decision boxes, sprites, shop UI on user signal). Never a rigid chain blocking onboarding: baseline play proceeds in parallel.",
]),
("14. Open questions for the user (nothing proceeds on these without answers)", [
"b:Mash policy a/b/c (keep stale / fast-track / expire to empty). Shop-play signal (shop UI waits). Wedge/Biggs official English (user call). EN glossary confirmations for the PM pack. Which of death-instrumentation findings vs PM pack vs dictionary experiment goes next after this handover.",
]),
("15. Live proofs owed (confirm in the next play triage)", [
"b:[memo] saved/loaded lines. Undoubled ROM-ID plus normalized line on Dolphin. TITLE-then-prompt on Paper Mario; mid-game TITLE silence. Engine notice and forensics paths (need a real 4-death session — hopefully never). PM pack routing, banking IDs, TITLE on the PM logo. Beam content in new corpus lines (banking already proven once: 314/343).",
]),
("16. Working conventions (binding)", [
"b:Run protocol (user plays, exact instructions plus keeper state, single confirmatory runs, solo reads, taskkill-first on red). Gates (replay drift 0, simreplay parity plus JA-set diffs, panellat budgets, build +0/0). Revert phrase per batch (ledger above). Comms short and direct, text-only except geometry/truth shots. Objectivity over agreement; investigate before confirming beliefs. Verify own solutions by execution.",
]),
("17. File map and key pointers", [
"b:Host: Capture/CaptureController.cs (pipeline, stability, commit roads, miner, memo, profiles, singleton/fuzzy branches), Capture/SimReplay.cs (filename pack routing, overlap/missed/extra reports), Packs/ (IPack contract, PackRegistry routing and content votes, per-game packs incl. PaperMario/), Sidecar/ (process manager with budget/notice/forensics, job object), Ocr/OcrClient.cs (lenient parse incl. beams), Translation/ (DeepL/CAT/cache/gates), Windows/ (panel history, menu/battle frames, shot saver with session labels). Sidecar: ocr-service/main.py (detection, beamed recognition, Div, beams banking). Tools: mine-confusion.py (loop 2), build-handover-pdf.py (this document). Evidence: eval/truth.jsonl (README documents split/verdicts), BOOKS.md (full ledger with commits and reverts), PERFORMANCE.md (SLOs). Corpus/sidecars/caches: never commit translation_cache.json (checkout after replays).",
]),
("18. Known risks and watch items", [
"b:Sidecar mid-session deaths (4x in the PM clean run; cause class still unproven — forensics will name it). One crash-truncated tail gzip in the corpus dir (not blocking). One orphaned PM miner file under the pre-paren versioned id (negligible). Zelda-long latency budget exceedance is pre-existing, not a regression. Exact-match metrics understate variant correctness (accounted in verdicts, not in counters). Sim and replay never touch bitmaps, the loop, or the quiet-title path — vision and title changes are live-provable only. Static-frame skip plus focus holds explain most gameplay silence; distinguish them from policy before building.",
]),
]
