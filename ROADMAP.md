# WunderLanguage — Development Roadmap & Milestone Tracker

Statuses: **Complete** · **Needs play session** (shipped, live proof owed) · **In progress** · **Planned**.
Note: pre-bottleneck milestone definitions (M1–M7) predate this ledger — only M7b/M7c slices are recorded. This tracker supersedes them with the bottleneck/loop/step structure. Revert phrases with each shipped batch.

## Current state (refreshed 2026-11-15)

- Checkpoint of record: `CHECKPOINT-2026-11-15.md` (local file, gitignored — fullscreen wave + leg-4 UX trio status, revert phrases, environment findings). Superseded: `CHECKPOINT-2026-11-13.md` (batches 8/9/10 status, batch-10 root cause), `CHECKPOINT-2026-11-09.md` (issue-1 phantom-fire closure, offline complete, live gate still owed — ledgers deliberately not updated for it until live confirmation), `CHECKPOINT-2026-10-26.md`, `CHECKPOINT-2026-10-21.md`, `CHECKPOINT-2026-10-20.md`.
- Latest ledger entries: ROADMAP tail = 2026-11-15 — fullscreen wave (`3120ac1`/`0e894cc`/`b21db62`) live-confirmed at Okami leg 3, feed-selfmask (`2be72c8`) live-confirmed at leg 4; three leg-4 UX batches (`ecc4d8f` opacity-live, `15fa964` opacity-release, `525c991` edge-resize) offline-green, live-owed with NieR leg 1. BOOKS tail = same date, root-cause chains (stale-rect fullscreen trio, feed-selfmask, stale-coverage opacity, NCHITTEST sizing, transparent-corner alpha).
- Next: Okami onboarding complete (legs 1–4). NieR Replicant leg 1 — attach windowed first (the picker lists only visible windows), then F11, 10–15 min capture, quit normally — then KH3D → Persona 5 → Yakuza 0, then the beta/alpha release build (flywheel logging + one-command log collection).
- Outstanding live gates: leg-4 UX trio checks ride NieR leg 1 (fade to 0.75 after drag release; edge + corner resize; `[feed-selfmask]` lines present); batch 10 relaunch-into-fullscreen check; batches 2/3 HDR proofs (no HDR on this machine); `occlusion-see` adjudication (must NOT go in before a live leg). Open items: P6 shop-play signal, PM glossary EN confirmations, CAT translation disabled (free commit < 11500MB at startup).

### ✅ Z1 WRAPPED (live run 2026-10-21 08:27)
- **11/11 blocks, one bubble each**, byte-identical vs the reference script
  (char-for-char: gap 　, … tails, only アグニム + ささげていった blocks keep 。,
  ささげられる, 消えていました, no fabricated 。 anywhere). Title posted once.
- Both defects closed live: two-anchor (`2bdb05b`) + intro-punct (`4644d39`).
- Evidence: `app-log-intro-live-pass-2026-10-21.log` (474KB, copied out of the
  wipe-per-launch app.log).

### Committed (git log)
- `cc4decb` しかし-line tail strip pair — gated (revert: revert intro-punct)
- `2bdb05b` Z1 two-anchor + quote/bracket strip (revert: revert two-anchor)
- `4644d39` Zelda intro 1:1 fixup pairs + ころ… anchor (revert: revert intro-punct)
- `1e71ec0` BOOKS log entries for both

### Uncommitted (working tree)
- Untracked docs: `CHECKPOINT-2026-10-20.md`, `CHECKPOINT-2026-10-21.md`
- `App.xaml.cs`, `SidecarProcessManager.cs`, `AppShell.cs` — W1b parked files — DO NOT commit
- Untracked: WunderLanguage-Portfolio.pdf, battle-ui-mockup.svg, ocr-service/cat_service.py, tools/*.py

### Optional (evidence-first, not blocking)
- **Defensive pairs** — strip 。 from other no-。 blocks (大地, 伝えられていました,
  みつかり, 消えていました, はずでした, ころ). 0 fabrication observed in 49 corpora;
  mechanism proven on しかし-line. Add only if wanted for future-proofing.

### Gate results (offline)
- two-anchor: 412-corpus A/B simreplay PASS (0 orphans), live 11/11 ✓
- intro-punct: ReplayChecker 50 Zelda corpora — 343 intended drifts + 57 pre-existing,
  0 collisions; non-Zelda 0 key hits
- しかし-line pair (`cc4decb`): A/B ReplayChecker 52-corpus sweep — baseline 402 vs
  pair 403 drifts: 395 identical, 7 changed + 1 added, **all 8 deltas = the designed
  。 strip on the わきでてきた clause**, 0 removed; non-Zelda 364 corpora 0 key hits;
  build +0 warnings. Live block 5 clean (exe under test contained the pair).

### Key files
- `src/WunderLanguage.Host/Capture/CaptureController.cs` — two-anchor, regex
- `src/WunderLanguage.Host/Packs/Zelda/ZeldaPack.cs` — fixup pairs
- `C:\Users\RamuneGaming\AppData\Local\Temp\opencode\gate\` — A/B logs (rp_*/base_*,
  ab_result.txt, nonzelda_scan.txt). NOTE: old harness scripts (gate_diff.py,
  sibling_check.py, zelda_replay.py, drift_classified.txt) lost to temp cleanup —
  this session's A/B diff was done ad hoc in PowerShell.
- `C:\Users\RamuneGaming\AppData\Roaming\WunderLanguage\corpus\` — 417 corpora (incl. today's intro run)

### Next steps
1. ~~Decide しかし-line pair~~ done → `cc4decb`
2. ~~Live intro run~~ done → PASS 11/11 (log: `app-log-intro-live-pass-2026-10-21.log`)
3. Campaign continues with **G1** (debugging/hardware audit — audit first, no rewrites)

## Campaign plan (2026-10-20 — session restart, the wrap order)

Agreed strategy: wrap Zelda, wrap FFVI, focused Paper Mario week, then extend the resolution matrix. Every onboard must strengthen the shared game-blind systems so untested future games inherit the gains (Scalability — never a collection of unrelated hand-tuned implementations).

### Immediate queue (work in this order)
1. **✅ Z1 — Zelda intro 11/11 — DONE (live pass 2026-10-21).** Standing demand: nothing else until it works. Two defects: **A** block 8 double-post (similarity 0.89 sits inside the 0.85–0.90 gap, both fire via script-anchored); **B** block 11 wobble twin (ささげられる vs ささばられる posted as separate lines). Fix by extending the B19 twin/wobble machinery, not by bolting on a threshold. Proof: fresh live run + screenshot; commit with revert phrase. Lesson on file: proper edit tools only — no PowerShell line-splicing on CaptureController.cs (213 build errors, reverted). **Closed: 11/11 blocks byte-identical vs official script, one bubble each, 1:1 punctuation - see BOOKS session state (2026-10-21).**
2. **G1 - Debugging-workflow & hardware audit** (approved brief; audit first — no logger/architecture rewrites). Deliverable: prioritized improvement plan with affected files. Known breakpoint spots: CaptureController.cs 1202–1219 (scriptFresh), 1349–1392 (growth-fast/wobble-publish); main.py 1108–1130 (/ocr), 767–780 (_script_match).
3. **C1 — Category measurement pass** — ✅ **DONE 2026-10-22** (observe-only `CatStats` harness, revert: `revert catstats`; artifact `gate\catstats_c1_v3.log`): 431 corpora / 45,369 lines through the REAL pipeline offline, two truth levels (sim attempts = pre-guard; publish-truth = stamped live bubbles joined to sampled frame state). Frame census: 88.9% in-game, 7.0% transient menu-shaped (crawls), 3.1% stable menu, 0.86% battle-shaped. Attempts: 8.1% of dialogue fires land on non-ingame frames; the live funnel absorbs nearly all where a bolt-on exists (FFVI 220→1) but has none elsewhere (Zelda 18→14). Publish truth: 16/563 flagged (2.8%), ALL flagged corpora content-spot-checked → **real junk = menu garbage, 3–4 instances, a UNIVERSAL class** (Zelda save-menu rows, FFVI magic-menu spell lists, PM date-screen garbage ≈0.6% of stamped publishes) + **14 battle flags were FALSE POSITIVES** (correct dialogue on date-stamped text screens — BattleFrame.Classify misreads non-FFVI layouts). **DECISION: Option 1 (display tagging)** + evidence-triggered guards stay; no universal routing layer (see watch item). C2 = the menu-garbage class fix.
4. **C2 — Menu-garbage class fix** — ✅ **DONE 2026-10-22** (live gate evaluated on the 20:25–20:35 run — verdict at end of item; revert: `revert c2-menu-hold`): single-choke hold inside `PublishDecisionAsync` (post sim-record, pre-RecordFire, live-only) withholds dialogue fires whose candidate matches the STABLE menu's rows (`HeldByStableMenu`: same-frame OCR identity via equality/fragment/merged-rows containment, length floors ≥4/≥8); omission-only, candidate stays (fires later or dies by staleness = delayed label, never a suppressed line); transient menu-shaped text (crawls) and updates untouched. Gates: replay A/B ZERO DELTA (stash/pop, identical extraction) + catstats full-sweep A/B ZERO DELTA on all 431 corpora + SUMMARY. **Part-2 RE-SCOPED** (evidence-based): loosening BattleFrame would ship C1's convicted false-positive class into a routing path — instead an observe-only probe (`bFfviProbe` = Classify with FFVI labels on non-FFVI corpora): 102 loops total, all in real-battle Zelda sessions (59/23), universal ≈ boundary frames — label-free shape detection stays future work, must pass the Zelda date-screen test. Named residual: cursor-moving menus stay transient (unheld) → F2x/tier-2. **Live gate verdict (2026-10-22 run): outcome criteria MET** — zero dialogue bubbles of stable-menu rows (GAME OVER/save, inventory, PLAYER SELECT), MENU mints unchanged, dialogue fired after menus; the specific `[publish] menu-hold` line never appeared because upstream guards pre-empted every instance (subpart-suppressed on the save rows) = hold is an unengaged backstop, first-fire evidence still unobserved. C2 = CLOSED with that note.
5. **T3 — Boot-title fastpath** — ✅ **CODED + OFFLINE-GATED 2026-10-22** (live gate owed; revert: `revert title-fastpath`): first-visit title post failed because the episode detector needs 3 evidence loops and the boot logo window offered ≤2 settled reads before the game's own **auto-advance timer** moved on (corrected 2026-10-22: title→intro is automatic, not player-driven — the window is game-determined and identical for every player; full root-cause chain in BOOKS T3+C3 entry; NOT pack lag — the window-title heal ran 1s after the logo appeared)). One settled block containing the full TitleNorm'd pack title (bn ⊇ pn, or the unvoiced variant) now accelerates `_titleEpisodeHits` straight to the publish threshold; the veto block is unchanged; Latin titles stay inert behind HasJapanese; T2-mined justification (0 false matches in 3,632 dialogue reads). Offline-inert by construction (title path does not run in replay/catstats — measured) → the live gate is the only gate: cold boot must post the title on the FIRST visit even at the game's own auto-advance speed (no input — the title→intro transition is a timer); return visits still exactly one post per visit.
6. **C3 — Overlay readback run-guard** — ✅ **CODED + OFFLINE-GATED 2026-10-22** (live gate owed; revert: `revert readback-run-guard`): 4-of-4 flagged "hallucination" posts were the overlay re-reading its own recent posts with mutated edges (the feed renders inside the capture region; every pre-existing dupe rule is head-anchored and the fire history is capped at 8 fires/10 loops — a middle-run mutation older than 10 loops leaked everything). Fix = `_postedTexts` long-horizon store (RecordFire, 400-loop horizon/cap 200, B19's cap-8/window-10 untouched) + one rule at the `FireHistoryCovers` tail: contiguous shared run ≥8 chars at ANY position parks pre-translate; strict growth (incoming containing shown) and exact/superset re-shows exempt. Gate: catstats full-sweep A/B base 108 → 615 parks — **all 321 unique new-parked texts audited line-by-line = duplicate/wobble/merge/battle-command/party-row/item-menu re-reads of content already shown, zero clean first-time dialogue lines**. Replay delta=0 proves fixup-path neutrality only (the replay harness runs extraction, measured caveat on file). Live gate: a replayed session must show zero overlay re-reads ([publish] history-parked lines carry the class) with no real dialogue missing.
7. **D1 - DECISIONS category (agreed item 2)** — **CODED + OFFLINE-GATED 2026-10-23; first live session evaluated + D-SUB sub-line fix + D2 era-tiering in (revert: `revert d1-decisions`, `revert decision-sublines`, `revert decision-tiering`)**. C1 Option-1 display tagging applies (tag at display, no routing/suppression change). Root causes from the 6 troubleshoot fixtures (now in `Roaming\WunderLanguage\troubleshoot\`, forensics in BOOKS D1 entry): the `<3 blocks` gate dropped 2-block decision frames; tall cls-mirror blocks arrive BOTH Y-grouped and re-attached, so ？-bearing merged text landed in the options bucket and the question check rejected the box (killed the shop); the builder hard-required ？-plus-options shape; OCR flattens multi-line boxes into one block (unsplittable without sub-line geometry). Final design — all universal structure, no per-game strings: tall-block contract (tall JA blocks appear exactly once as their own row); decision check runs FIRST (`decHit` gates the two fragment gates); ？-echo pieces SKIPped + `options.Distinct()`; options-only branch for no-？pickers (`texts.Count` 3–4, length-spread <2x, sentence punctuation rejected); question gates = length ≤20, no speaker bracket 「 (prompt uses 『 in the one real quoted prompt), no space (a prompt is one unbroken phrase — also catches the detached-？battle rows); option-nature gate (must be hiragana-bearing, pure digit, or bracket-led — else it is a name cell like ウェッジビックス). Sweep ladder on the 433-corpus `--catstats` A/B: 186 → 120 → 41 → 23 → **13 mints = 9/9 real fixtures** (raft, crossroads, status, party, save-point ×2, explain, PLAYER SELECT, gambler) **+ 4 named FPs** (Zelda hermit Q + hiragana callout; label-only under the safety valve). Base A/B (base = `f536a06`): all 8 base DECISION mints identified via publish-context as battle/name-pair FPs (4 of them NRE immediately after — the pre-existing `[menu] ERROR` class, 391 in both C3 logs); zero real decisions lost; MENU 301→300 = two FFVI MENU(3)→DECISION swaps with content intact + Zelda hermit re-shaping; TITLE 16=16, gameover 17=17. Named residuals: shop `かう` sits on a chest sprite so the detector never fires a clean box (mints Q + `いらない` until the detector keeps the line); hermit 4 FP; flattened no-？pickers stay MENU by design (2-row no-question). **First live session 2026-10-23 03:36–03:48 (cold boot + decisions route): D1 minted live (DECISION tag + rows at 03:46:54) but short — the merged box dropped `逃げだす`, and the picker posted IN-GAME TEXT → root cause = chain-merged multi-line block + single-line recognizer; D-SUB (revert `revert decision-sublines`) recs detector sub-boxes for question-bearing n≥2 h≥40 blocks and ships per-line `lines`; gambler frame test recovered all 3 rows; sweep A/B: 433 pre-session corpora byte-identical (12 distinct row-sets = v5), delta = the 2 new corpora from the session itself; corpora schema strips `lines` → sub-line path is live-only verified (caveat on file).** T3 **CLOSED** (title posted on first visit 03:37:34.997, exactly one post per visit ×3 visits) + C3 **CLOSED** (4 history-parked intro re-reads, zero re-posts, no dialogue missing, 0 errors) from the same session's log. D1 live gate remaining: gambler DECISION with BOTH options + shop DECISION tag, dialogue path byte-identical. **D2 decision tiering (2026-10-23, scaling hole raised in review: the absolute caps are SNES-era-calibrated and would degrade verbose modern prompts; constraint: no new detector, structure-only):** ？-tail prompt gate on sub-line-sourced question rows (must END with ？ — mid-？ dialogue rows die at any length; merged/replay rows keep the lenient first-？ split) + question/option caps tiered on option-cluster strength (strong = ≥2 judgeable options each ≤ prompt length → 40/16; weak → exact 20/12). Sweep A/B on all 435 corpora: base == r1 == r2, DECISION=14, byte-count identical — zero delta by design (no SNES prompt >17; no FP carries a strong cluster; tail gate dormant on replay via the schema caveat above). The bundle buys future-path (long modern prompt + real choice column mints) paid for by live strictness. Live gate with D1 pass 2.
8. **CAT reliability — translator crash class HARDENED 2026-10-23 (revert: `revert cat-reliability`; fix lives in the PARKED uncommitted files `SidecarProcessManager.cs` + `AppShell.cs` per W1b protocol — a checkout of those files drops it)**. The 09:18 live session posted only the title: the CAT translator sidecar segfaulted (0xC0000005, the 2026-10-14/10-17 "4-bit weight load" family in cross-process form — load window overlapping OCR model load + emulator) 4× inside a ~105s window, the 3×25s retry budget burned inside it, and exhaustion was silently log-only. Detection was healthy all session (9 `[sublines]` events incl. the gambler live split さあ、どうする？ | しょうぶする | 逃げだす — **first live D-SUB proof**). Fixes: `HF_HUB_DISABLE_MMAP=1` on the CAT spawn (the segfault mechanism, documented Windows mitigation), CAT restart backoff 10s/60s/240s, and the terminal panel notice now fires for CAT exhaustion too with role wording (the "fallback covers" assumption is false for the Offline provider). Gates: GREEN; mmap-off load ready (weights 14.5s); induced-death spacing measured ~12s/~62s. Live gate owed: next session translates normally; no silent dead sessions.
  - **2026-10-23 pm — crash-diag logger + Windows event-log forensics** (revert: `revert crash-diag-logger`; same parked/uncommitted files). Read the Application log for the first time: **56 python crash events in 7 days (10-16 x2, 10-17 x46 storm, 10-19 x2, 10-23 x6), 56 of 56 at a fixed instruction** — 55x `torch_cpu.dll+0x8e4a279` + 1x `c10.dll+0x90514`, all `0xC0000005` → the class is 8 days old, deterministic, and streaky (the 4 quiet days were state, not the 10-23 fixes; venv/driver/torch all unchanged). Death forensics now embed the fault event (module/offset), a system memory snapshot, and a 1s python RAM/commit/VRAM timeline — the next AV death answers "pressure or deterministic?" by itself. 6 minidumps preserved for exact-function analysis. Details + gates: BOOKS CRASH-DIAG entry.

### Campaign phases (after the immediate queue)
- **Z2 - Zelda wrap: inventory menu.** Parked until T3/C3 gates + D1 close (directive 2026-10-22: park until the other 3 tracks wrap). Menu routing has regressed before (B19 MENU attempt reverted) - evidence-first, small gated steps. **Design spec on file (2026-10-22; SVG: `D:\Wunderlanguage\zelda-inventory-mockup.svg`, original `Downloads\ui SVG\Zelda inventory screen.svg`):** the inventory translation feed = 5 fixed boxes — top-left empty, **top-right = item name, the ONLY text that changes with selection**, bottom-left = action words (static — "what you can do", not selectable), middle-right = static non-changing text, bottom-right = "EQUIPTMENT" (already English). Box art/sprites = nothing to translate, render blank. Engine implication: the inventory feed view needs exactly ONE extracted field (the selected item name); the other four boxes are fixed chrome.
- **F2x — FFVI wrap arc:** title, intro all blocks, first town (most/all text), battle UI (F1 exists — validate), shop UI (P6, blocked on shop-play signal), inventory UI.
- **PM1 - Paper Mario focused week:** intro stability, title posting, inventory, menus. Played live; no alt-tab; in-app Shot button; collects N1-N6 proofs.
- **R1–R4 — Resolution matrix, 2 games per generation:**
  - R1 240p: Zelda + FFVI (both onboarded ✓)
  - R2 480p: Paper Mario + **Okami** (PICKED 2026-10-29)
  - R3 720p: **NieR Replicant** + **Kingdom Hearts 3D: Dream Drop Distance** (PICKED 2026-10-29; TWEWY dropped — DS-native 256×192, not 3DS — KH3D chosen for the unique dual-screen capture geometry: 400×240 top + 320×240 bottom in one window = the matrix's only two-text-zone test; watch = bottom-screen UI text entering the capture rectangle, document as a find if it pollutes the feed)
  - R4 1080p: Persona 5 + **Yakuza 0** (PICKED 2026-10-29)
  Each generation trains resolution-specific capture/recognition until solid, then feeds gains back into shared systems.

### Feed categories (agreed 2026-10-20)
Five exclusive categories for the translation feed: **Title** (title screens only) · **In-game text** (NPC dialogue, signs — the default) · **Decisions** (option lists with selector arrow/finger) · **MENU** (inventory, save select, game over, map, pause, main menu) · **BATTLE** (battle screens, JRPG-flavored — command windows, status, FFVI the model case).
Current reality: Title works and is gated; In-game is the solid default; Battle is an FFVI-only bolt-on; Menu is fragile; Decisions does not exist. Untested games leak menu/battle junk through the default path — that is the mass-release hole C1/C2 target. Safety-valve design rule: **default to in-game; route away only on strong evidence; misclassification costs a label, never a suppressed line.** C1 measured (2026-10-22): the hole is NOT a flood (universal publish-truth junk = 0/15 stamped; attempt-level 2.7%) — the real class is menu garbage, rare (~0.6% content-verified) but hits EVERY game (Zelda/FFVI/PM), and battle-shape routing would over-suppress (14 correct publishes flagged). Decision: Option 1, C2 fixes the class — C2 coded + offline-gated 2026-10-22 (single-choke menu-row hold in PublishDecisionAsync; zero-delta A/B on replay + catstats). **2026-10-22 evening: C2 CLOSED** (live outcome met — zero menu-row dialogue bubbles across a multi-menu session; hold = unengaged backstop); **T3 (title fastpath) + C3 (overlay readback guard) coded + offline-gated** after a live run exposed the first-visit title failure and the overlay self-readback class (root causes in BOOKS T3+C3 entry); their live gates are owed; **D1 (decisions) = next build**.

## Complete

### S1. Survival & reliability
System stays alive and diagnosable. Job ownership with WER keys, 50% CPU cap with clamp, completion-port detect with 3-attempt auto-restart, low-memory listener, session power request, notifications. Done: all live-proven. Still needed: nothing (watch: sidecar death cause, see W1).

### S2. Performance program
14s startup, PERFORMANCE.md SLOs. Done and closed.

### S3. Vision probes 1–4
Plumbed. Done.

### F1. FFVI battle UI
Closed: splitter, slots, glossaries, action menu, item grids. Done.

### F2. Zelda repair
Battle gate, logo hold/window, title purity, fragment hold, turned-page rescue, flush-on-pageturn proven live. Done.

### B1–B9. Bottlenecks 1–9
Offline scope closed: stain/weak-hold, battle vocab, speaker fixups, scroll-glue, wall-clock pricing, loop metrics, recall proxies, cold-start zero + pack checklist, latency budget. Done as far as offline reaches.

### B10-T1. Fuzzy-window commit (revert: revert fuzzy-window)
Same-length OCR variants count as one lineage. Done: FFVI-old 115→117 fires with overlap/missed parity and one genuine clean-form recovery. Still needed: live observation.

### B10-T2. Anchored-singleton rescue (revert: revert tier2-anchored)
Button prompts and speaker-start lines fire on first sight under shape guards. Done: +2 distinct genuine FFVI-new, zero removed (worktree diff). Still needed: live observation.

### B11. Chart-gate punctuation exemption (revert: revert chart-punct)Kana-chart gate skips blocks carrying sentence punctuation (full/half-width); bare-kana blobs still suppress. Convicted: hiragana-heavy PM dialogue (zero kanji) ate whole screens with candidate reset. Done: PM 4→7 fires (+3 genuine: kekkou prefix+full, sonnakoto), missed 0; Zelda 16/1/4 identical; FFVI 3/0/14 identical with empty JA-set diffs. Old-corpora re-gate: FFVI-old 46/4, extra 58→60 (+1 genuine worldmap help, +1 suspect Wedge merge disclosed as next-batch residue); Zelda-old 50/60 missed 10 extra 21 matches ledger exactly. Bare-kana suppression intact (chart-suppressed still fires on bare grids); no name-entry rows exist in any corpus. Still needed: live observation + PM name-entry silence proof when encountered. Failed side-quest (reverted, no residue): ？-blend guard killed 3 legit ？-mid lines.

### B12. Scroll-line + static-page roads (revert: revert scroll-line)
Line-granular scroll tracker for typewriter-scroll phases (Zelda intro proven ground): growth-tolerant lineage, longest complete-ish form fires on scroll-out/page-turn, scroll-update revises variants in place, trailing dot-family fold for identity. Verbatim otherwise, game-blind, menus/battles excluded, static normal-road lines untouched. Commits 4f27def + be9aec4. Done: intro 2→8 (+6 reviewed), Zelda-new +1 genuine, PM/FFVI-new zero-delta empty diffs, FFVI-old -1 suspect merge +2 genuine, Zelda-old missed 10→8 overlap 50→52 (+17 reviewed: ~14 clean, 2 wobbles, 1 double-form disclosed). Scroll-verdict provenance: all merge/junk suspects (ケアルファイア, name-mash, blog-misread, map frags) are PRE-EXISTING normal-road fires, not scroll introductions; scroll adds genuine + variant doubles only (doubles = shared sentdedup follow-up, queued). Title-logo fired live once → split to B13. Still needed: live 11/11 + PM name-entry silence.

### B13. Pack-title guard (revert: revert title-guard)
Pack FullTitle matched by containment/truncation/0.85 parks scroll fires for the title path (game-blind mechanism, evidenced per-game data). Caught a real chain bug on the way in (park flag fell through to fire — fixed, verified). Done: live intro title gone, 6/6 genuine hold, missed +1 = the withheld title itself (correct miss). Follow-up B13b (revert: revert title-unvoice): voicing-tolerant match after live ドライフォース leak (Dry Force misteach) — unvoice both sides; verified title gone, empty diffs intro/pm/ffvi-old/zelda-old. Still needed: live title-silence confirmation.

### L1. Global fixup tier (revert: revert loop1-global)
Cross-game agreed pairs promote to shared file. Done: 70 pairs, proven live with exact-count match.

### L2. Confusion re-mine (revert: revert loop2-remine)
Script plus 29 reviewed pairs, zero-delta gated. Done; rerun per new game.

### L3. Memo persistence (reverts: revert loop3-memo, revert memo-stop)
Per-pack names survive sessions plus Stop closeout plus sim hermeticity. Done code-complete.

### L4. Vocab self-proposal (revert: revert loop4-vocab)
Write-only review queue, live-gated, zero behavior change. Done; reviewer promotes per game.

### L5. Per-game profiles (revert: revert loop5-profiles)
Four whitelisted knobs with validated ranges; probe proved both directions. Done; no profiles written (tuning needs measured spread).

### E1. Truth eval set v1 (revert: revert truth-set)
40 rows, dev/heldout split, data only. Done; expand with Paper Mario panels.

### P1. Paper Mario pack v1 (revert: revert pm-pack)
Checklist order, evidenced-only contents, zero-delta measured. Done code-complete.

### H1. ROM-ID identity (revert: revert rom-id)
Version-proof game identity plus paren fix; replica-proven. Done code-complete.

### T1. Title hardening (revert: revert title-harden)
Gameplay veto, TITLE-grace prompt, credits audit (zero Latin-only in ~1000 bubbles). Done code-complete.

## Needs play session

### N1. Memo live proof
Needs `[memo] saved/loaded` lines from a real session (single-game save now works via Stop).

### N2. Title live proofs
Mid-game TITLE silence (veto), TITLE-then-prompt on Paper Mario, PM-logo TITLE via new pack.

### N3. Engine notice & forensics paths
Need a real 4-death session (hopefully never) carrying the notice row and `[job] death forensics` line.

### N4. PM pack live behavior
Routing line, banking IDs, memo/vocab activation, battle/menu/decision coverage, intro latency numbers.

### N5. Paren-fix confirmation
Undoubled stable ROM ID plus normalized line on Dolphin.

### N6. Shot labels
Next bundles must carry pack prefixes and headers.

## In progress
### T2. Title-screen episode detector (revert: revert title-episode) — **DONE 2026-10-22 round-7**: three single posts via the episode detector (boot, post-intro, return-after-gameplay = visit semantics proven), intro 11/11, JA+EN title bubbles, all three title publishers on one shared 60-loop clock (a867a43). Shipped cdc7128..a867a43; scope items for F2x: matcher HasJapanese gate (JA-title assumption), tracker purity 4766 (pre-existing Zelda branch). Leftover from round-2 forensics (BOOKS T2.6): ghost-inset chain-wrap + A/B folds into G1. **Next: G1**, then C1.

### B19. Cross-fire variant consolidation (prerequisite for repetition-evidence) — code complete through 2f (HEAD 1000960), live 11/11 proof LANDED 2026-10-21 (see Z1 / CHECKPOINT-2026-10-21)
A (repetition-as-evidence) was built through 5 iterations and reverted: static-page recovery works (block 4 exact) but pulls misread twins with it (帰がけて/出かけて, きわって/日ボタン — incorrect lines = regressions by the book), and every guard tried (dominance, twin-veto, net-displacement) either missed the twins or ate clean lines. Conviction: variant consolidation must come FIRST (merge variant doubles into single bubbles), repetition-evidence second. Part 1 DONE (live 2026-10-16 Zelda specimens: 900/封印は both posted, そこで identical double-post, 164-full never fired while truncated did, イグニュー ghost flushed): (1) fire-history ring (revert: revert fire-hist) — identical/folded-identical/containment/near-dupe vs last-8-fires in a 10-loop window, recorded synchronously at the Publish choke (sim+live); (2) sweep twin-park (revert: revert twin-sweep) — evidence-ordered sweep (Sightings desc) keeps max-evidence twin (weighted-sim ≥0.55, strictly-less-than; measured P1 0.58 catch / N9 0.48 miss / distinct ≤0.45), plus deterministic matcher/sweep/trim ordering (hash-randomized Dictionary iteration convicted by sim: identical inputs gave 86 vs 91 fires run-to-run; sorted snapshots now hash-identical across repeats); (3) freshest-extension (revert: revert fresh-ext) — growth-fast fires the extraction when it folded-extends the candidate (gate moved onto the fired text; self-suppression convicted by sim); (4) blank-flush corroboration (revert: revert blank-corrob) — uncorroborated held lines don't publish on blank (omission over misteaching); (5) panel drop-eq (revert: revert drop-eq) — identical/folded-equal arrivals collapse. Deterministic ConFold closure included (multi-pair chars げ×3/封×7 folded by HashSet luck; convicted by sim). Sim-verified: target run 13→9 fires (dup/900/ささ-prefix/ghost-173 gone, ここに-full single post, ゼルダ-full fires); Zelda-old 91→86 deterministic with 3 genuine recoveries (full-ささば, イケニエを使い, あっ魔法) and no live-affecting miss (Presents = live-vision-only line via untouched weak-hold; 地図/カンテラ pre-existing sim gaps; マスターソード fires as read-variant); FFVI 121→115 with identical misses (6 dup-variants parked). Killed in measurement, not shipped: flush corroboration ≥2 (legit FFVI pageturn flushes score 1 — mass-omission) and naive twin-suppression (would have kept 900-garbage over correct). RESIDUAL for part 2: ghost-row wins (138 mega-box flushed as candidate — needs region/mined promotion design with the 3 banked specimens) + A-retry behind it. Part 2a DONE (live 2026-10-17: correct ここに revised to ぽっとリー by update-neardupe): revision twin-guard (revert: revert upd-twins) — updates revise only on small wobbles (plain-sim ≥0.85, edit-distance ≤3, affix/middle shape; growth-via-containment road untouched). Measured: motivating flicker (dist 1, 0.875) still revises; twin (dist 5, 0.881) parks; blocking a revision keeps the fired text (omission-safe direction — worst case is a stale-correct bubble, never a swap-in). Sim: target 9 fires steady + 0 updates (ぽっとリー-class neither revises nor refires); Zelda-old 86→89 fires with 11→5 updates; FFVI 115→120 fires (deterministic hash ×2), 7→2 updates, missed 4→1 (remaining miss pre-existing, zero new), extra 69→71 (variant doubles now visible instead of swapped in — dup preferred over misteach). Part 2b DONE (live 2026-10-17: ここに prefix + full posted as TWO bubbles; first block はるか昔 never posted in any run): (a) completion routing (revert: revert fresh-upd) — growth-fast completions (strict folded extensions of the shown, posted prefix) revise via the update road instead of firing fresh, so typewriter lines post ONE bubble (design matches the road's documented "completion via update"; panel already order-safe both directions, absorb-growth stays as backup); parked-prefix completions still fire fresh (dangling updates revise nothing). Sim: target 9+0 becomes 8+1 (ゼルダ-full via update), today's run 8 fires + 2 updates (ここに-full and ゼルダ-full both via update, prefixes single). (b) script-row promotion (revert: revert script-promo) — sidecar assembly prefers a row matching a pack script line (same 0.92 bar as script-match) over the area pick: the はるか昔 crawl read clean 7 loops but the dots box won primary by area every time. Game-blind mechanism over evidenced per-game data (title-pack discipline); never rewrites, only selects; dots/shorts can never match a full script line (ghost max 0.59 vs 1.00 measured). Live-proof owed for (b) (extraction-level, sim-invisible). Loop2 queue: (き,ま) evidenced games=2 (いまました misread ×68: zelda 55 + ffvi 13; EN already correct via script-match, JA wobble honest) — next mining batch with zero-delta discipline, not this commit. Part 2c DONE (live 2026-10-17: block 10 posted the ghost row ありがとう/先輩 instead of the strip): ghost-inset override (revert: revert ghost-inset) — when a full-bleed box contains a disagreeing inset row, the inset row feeds the pipeline (raw level, so the winner gets the full fixup/mined chain; corpus keeps sidecar original as raw). Gates, all measured: containment + weighted<0.65 (124-twin 0.583 and 900-twin 0.58 trigger, distinct lines top ~0.51) + long outer + clear inset margin + full-bleed outer (x≤10); no battle/menu/title gates (downstream suppression owns those frames). Mirror-verified on all banked ghost loops (122-partial silent, 123/124/127 strip wins, 138 strip wins; remnants/dots/disjoint rows silent; PM date-junk resolves to content). Sim-neutral by construction (runs before the banked-extracted feed point; identical hash). Part 2d DONE (live 2026-10-17: MENU chrome posted as dialogue; ここに full followed by a late wobble-fragment post; はるか昔 + そこで single-sighting losses): (a) late-prefix staleness (revert: revert prefix-stale) — incoming shorter than shown with folded common prefix ≥10 parks at the Publish choke (growth direction safe by construction; measured fragment 13 vs consecutive-distinct 0, bar 10). (b) script-anchored fire (revert: revert script-fire) + sidecar script_hit flag (revert: revert script-hit) — a clean read matching a pack script line (same 0.92 bar) posts on first sighting; skipped when the read continues the candidate lineage (growth owns); repeats die at the history choke (single post guaranteed); packs without script data unaffected. (c) MENU attempt and revert: menu-provenance parking mass-parked legitimate lines within one sim run (menu tracker false-positives on intro remnants) — reverted same session, zero residue; MENU chrome needs a shape-based answer (latin-majority measurement queued). Sim: target steady 8+1 (hash-identical); Zelda-old 89→88 fires with identical missed-4 (one fewer double, zero legit loss); FFVI hash-identical. む/せ: single specimen only — NOT added (Loop2 needs games≥2); the prefix rule covers the class inventory-free. Still needed: live proof (no MENU post, no late fragments, はるか昔 + そこで post on first clean read, block 10 strip), then A-retry design behind the finished B19. Part 2e DONE (live 2026-10-17: block 10 posted incomplete → complete → wobbled incomplete triple): wobble-publish (revert: revert wobble-publish) — when extracted is longer than candidate and highly similar (wobble variant, not clean extension), publish extracted — the newer reading is more complete. Live 2026-10-17: 貌蓮を...the full line posts instead of 貌違を... the wobble variant. Gates all measured: extracted longer than candidate + weighted≥0.85 (124-twin 0.881 triggers, distinct lines top ~0.51) + common prefix≥6. Measured: Zelda-old 86→88 fires with missed 4→2 (地図/カンテラ now post), FFVI 120→119 fires with missed 4→2 (2 previously-missed lines now post), target run block 10 posts incomplete → complete (no 貌違を... third post). Loop2 queue: (き,ま) evidenced games=2 (いまました misread ×68: zelda 55 + ffvi 13; EN already correct via script-match, JA wobble honest) — next mining batch with zero-delta discipline, not this commit. Still needed: live proof (no MENU post, no late fragments, はるか昔 + そこで post on first clean read, block 10 strip posts complete last), then A-retry design behind the finished B19. Part 2f DONE (live 2026-10-18: block 10 triple-post fixed; block 11 incomplete-prefix fixed): (3) incomplete-prefix suppression — growth-fast now requires `EndsCompleteSentence(extractedText)` so incomplete prefixes (ささげら, もうそこま) don't post; complete version replaces incomplete candidate when extracted is longer, sim≥0.85, common prefix≥6. Verified: block 10 posts `７賢者の血をひく娘達を、つぎつぎイケニエにささげていったのです。` once (complete), block 11 posts `もうそこまでせまっていました。` (complete), block 8 posts once (… vs ! twin deduped). Sim: target 11 fires all blocks correct; Zelda-old 82 fires identical hash (no regression); FFVI 108 fires identical hash (no regression).

### B18. Confusion-tolerant counting (revert: revert fuzzy-weighted)
Tier1 lineage counting accepts confusion-weighted similarity (inventoried pairs join instead of resetting). Counting only. Done: 48 genuine joins measured offline, empty JA diffs on all 6 corpora. Still needed: live observation (a churned line stabilizing faster).

### W1. Sidecar death root cause — CONVICTED + FIXED (revert: revert nocatfb-retry)
CAT 4-bit quantize load segfaults ~140s after start (3 live deaths + 2 idle repros, 0xC0000005 at shard 0-1, despite 4492MB free — threshold tuning cannot catch it). Explains PM 4-death cadence (restart+120s cycles) and both intro block-10 gaps. Fix, two layers: healthy-period death retries OCR-only (backstop); lazy CAT (revert: restore eager loader) never loads weights until first offline /translate need, so DeepL sessions never gamble — proven by 180s idle soak with zero load, then triggered load segfaulting on demand as convicted. Verified by kill-test (nocatfb → healthy, tail intact). Still needed: live soak (a full session with zero 503 gaps).

#### W1b. Crash-hunt night (2026-10-17, ~30 trials — read before touching)
Site: torch storage `__getitem__` during weight materialization (faulthandler-convicted, identical frame every time). CONVICTED INNOCENT (each by controlled trial): VRAM shortage (4.5GB free, unfragmented per allocator snapshot), OCR contention (idle/busy parity), double-quant, dispatch path, fp4, worker count (incl. serial), import order, safetensors backend (mmap+pread both fine standalone), safetensors file (reads clean repeatedly), torch CUDA copy/dtype/NF4 kernels (all survive standalone), safetensors version surface. INCONCLUSIVE (confounded by machine-state drift — same script flips survive→crash over hours): transformers 4.x-vs-5.x, hf-xet (test-venv flip correlated AND contradicted), uvicorn concurrency, import set. LEAD THEORY (unproven): commit-space exhaustion — C: critically full (3% free, 33GB TEMP), commit ~76%, small-model loads immune, 6.4GB CAT loads die; fits drift, live pattern, and same-frame (allocation failure surfacing as AV). PROVING IT needs: reboot + free C: + admin (Defender exclusions unsettable without). Also hot GPU (74°C near-idle) noted. SHIPPED (safe, committable): faulthandler + allocator snapshot (observability), transformers<5 pin (provisional — less fault surface, OCR-compat proven). PARKED (uncommitted working tree): cat_service.py + host two-process wiring + WUNDER_ROLE gates — built for the residency theory, held until the mechanism is proven. DO NOT commit the split blind. Morning protocol: reboot → free C: aggressively → rerun cleanload ×3 → intro run first thing (cleanest state = best 11/11 chance).

### W2. Paper Mario baseline play triage follow-ups
Intro measured (28/28 sim); battle/menu/decision coverage needs a focused session without focus loss.

### B16. One-bubble growth merge (revert: revert growth-merge)
ShowTranslation revises the shown bubble when a fire strictly extends it within 4s (display-only; capture/translation/sim untouched). Superseded by B16b below (arrival-order merging proven insufficient against async races — same revert covers both).

### B16b. Order-independent bubble absorption (revert: revert absorb-ring)
Ring of recent plain bubbles with element refs: extending fires revise, covered fires drop, updates fold other contained bubbles. Strict containment +6, 15s window (translation variance measured ~9s live), battle/menu excluded. Follow-ups: confold matching (revert: revert confold-match) folds Loop2 pairs for wobble tolerance; single-source inventory (revert: revert confold-src); folded stale-prefix suppression (revert: revert stale-fold) covers late wobble prefixes order-safely at translate time. Still needed: live screenshot proof (single bubble on typewriter, absorb-* lines in log).

## Planned

### P2. Dictionary experiment (Step 3) — EXECUTED 2026-10-18, concluded inconclusive-by-design — ✅ **CLOSED 2026-10-28**
The v2 dev-only beam-diversity rule yielded 0 corrections / 0 damages across the truth set — gate safe, nothing worth shipping. Residue: 6 untracked tools/p2_*.py scripts. Side-question RESOLVED (offline, per the standing recommendation): declare done, move to P3 — the re-OCR option is priced out by its own evidence (0 corrections/0 damages across the truth set = nothing convicts a build).

### P3. Missed-line sweep + LM-fluency gate (Steps 4–6) — ✅ **OFFLINE DONE 2026-10-29** (repriced against truth set, zero builds convicted; full entry at 2026-10-29 below; LM-fluency gate NOT convicted, phantom source SOLVED)
Re-price singleton residue with truth set; build only what evidence convicts.

### P4. Loop payoffs (Step 9) — ✅ **OFFLINE DONE 2026-10-29** (re-mine + promotion review complete; nothing qualifies for append — zero measured spread; entry at 2026-10-29 below)
Re-mine and promotion review with Paper Mario data; profile tuning only on measured spread.

### P5. Decision boxes + sprites (Step 10) — **OFFLINE PRICED 2026-10-29** (no build convicted: PM pointing-hand evidence still absent — 0/52 decHit in the now-in-hand PM data; zero covered misses; build stays queued for live, full numbers in the 2026-10-29 P5/P7/P8 entry below)
Positioned boxes, Zelda sprite map, PM pointing hand — queued behind Paper Mario data.

### P6. Shop UI
FFVI battle-polish order otherwise intact.

### P7. Zelda-long latency exceedance — **OFFLINE PRICED 2026-10-29** (Zelda frame p99 = 12.1s vs 3.4–9.9s peers measured; no Zelda-specific stage regression found — FFVI rec max exceeds Zelda's; tail = time outside det+rec; investigation stays with the profiling session, numbers in entry below)
Pre-existing (stash-proven, not a regression). Investigate when profiling time comes.

### P8. Display-behavior queue (from ChatGPT taxonomy review, 2026-10-14) — **ALL FOUR CLASSES PRICED 2026-10-29: no build** (transient-numeric miss set empty — junk gate owns it 105/105 dead, 0 published; fade class unpriceable offline from garble (B10-consistent singletons + salvage covers); per-line replacement = mostly normal dialogue advance; timed-vs-advance unmeasurable at ~1s OCR cadence. Entry below.)
Already covered: instant/typewriter/scroll/fixed/replacement/page/advance/menus/titles/name-entry/singleton-glimpse (B10-T2, B12, title/menu/battle paths). Queued, unmeasured: fading text, moving/animated text + transient notifications (damage numbers, pickups), per-line replacement, timed-vs-advance-controlled distinction. Each needs a priced miss set before any build.

## Open questions (restored 2026-10-15 - blocks FFVI names work until answered)
- Mash memo policy: **RESOLVED + EXECUTED 2026-10-29 = expired to empty** (decision: expire now; `expire_memo.py` wiped the 9 pairs → 0, provider field preserved for relearn). Evidence: memo/ffvi.json = 9 pairs / 538 B, written 2026-10-13 under provider "DeepL (unreliable-offline fallback)"; entries include garble-key junk ('ただから'→"Just Because" — a garble of たたかう=Fight, which the same memo also holds correctly) and dialogue leaking into battle-name rows ('またな。'→"See you later."). The provider guard keeps the file inert while the primary provider is active, but it would serve junk in any future fallback window; fast-track keeps the bad keys; expire = one 9-entry relearn against a permanent correctness risk, and matches the Loop-3 "deliberately unseeded memo" doctrine (relearns live, cap 200).
- Wedge/Biggs official English forms: **RESOLVED 2026-10-29 → "Biggs" / "Wedge"** (Matthew precedent: manual wins — the 2026-10-09 pin rule is literally "manual differs from Woolsey, manual wins", and Vicks IS the Woolsey form). Evidence: ウェッジ = "Wedge" in every official source; ビックス = "Vicks" only in the 1994 Woolsey NA localization (SNES script + official Player's Guide), "Biggs" in every official re-release since (incl. the official FFVI Advance EN manual) and in Japanese materials that use English text; series canon = Star Wars Biggs Darklighter/Wedge Antilles (FFVI is the sole game spelling it ビックス). Not yet pinned in code (BOOKS: "absent from manual pages — no source") — sources cited here; pin ready when FFVI names work resumes.
- PM battle glossary EN confirmations.
- Shop-play signal for FFVI (shop UI build waits on it).
- P2 side-question: true-beam re-OCR on ~36 crops vs declare P2 done — **RESOLVED 2026-10-28: declare done, moved to P3** (recommended option; 0/0 truth-set evidence never convicted the re-OCR build).
- Resolution matrix picks: **RESOLVED 2026-10-29 (agreed) → R2 Okami / R3 NieR Replicant + KH3D Dream Drop Distance / R4 Yakuza 0** — matrix complete, next-game onboarding unblocked.
- Category routing depth: **DECIDED 2026-10-22 on C1 numbers — Option 1 (display tagging)** universal; routing stays evidence-triggered (no new routing architecture). Rationale: content-verified junk ≈0.6% of live publishes (no case for a universal routing layer); the strongest route signal, battle shape, false-positives at scale on non-pack layouts — 14/16 publish-truth flags were CORRECT dialogue that routing would have suppressed; the one real class (menu garbage) is rare + universal → tier-1 engine fix (C2), not category routing.
- Watch items: two crash-truncated corpus gzips (Snes9x_1.60_20261019_035824 + one Zelda_no_Densetsu tail file; scanners skip them cleanly, not blocking); one orphaned PM miner file under pre-paren versioned id (negligible — but see P4(5): it can defeat the ≥2-file promotion gate if an identical pair lands in both PM files); exact-match metrics understate variant correctness; sim/replay never touch bitmaps, loop, or title path (live-provable only).

## SCALING DOCTRINE (standing policy, 2026-10-22)
Two tiers: (1) THE FLOOR - a universal game-blind core strong enough that
UNSEEN games work out of the box; this is where effort goes by default.
(2) THE SEAL - gradually certified playtested games (guaranteed list at
release), grown by choice, never by blocking the floor.
Fix ladder for every defect, in order: (1) ENGINE/CLASS fix - ask "does
this bug class exist for all games?" and fix the class once; (2) SELF-
LEARNING mechanism - prefer strengthening mined corrections / gates /
thresholds so the system learns per-game (ML direction) over hand rules;
(3) PACK DATA - static pairs only inside a pack, seal-tier games only,
never engine logic keyed on a game name; (4) ONE-OFF ENGINE BRANCH -
forbidden without explicit sign-off. Verification substitute for
"can't playtest every game": every engine fix runs the merged-corpus
replay A/B (all recorded games at once); unseen games have no corpora,
which is exactly why their fixes must be class-level. Zelda/FFVI fixup
accumulation = legacy bootstrapping; STOP adding new per-game fixups by
default (directive 2026-10-22).

CONTINUOUS STRENGTHENING (2026-10-22): knowledge COMPOUNDS - every
onboarded game is a training session for the WHOLE system; older games
must get stronger as new ones onboard (new words, error patterns,
structure). Reference implementation already live: Loop 1 compounding
(CaptureController LoadMinedCorrections/PromoteGlobalCorrections,
approved 2026-10-11) - per-game mined pairs (corrections/<game>.json)
that >=2 distinct games mine IDENTICALLY promote to
corrections/_global.json, loaded for EVERY game (89 global pairs as of
2026-10-22, with a non-seal Dolphin game already contributing);
conflicts hold per-game, never poisoning the shared store. The
translation model + engine machinery are inherently shared layers.
Policy: every new knowledge item must have a PATH to a shared store
(mined -> global promotion, engine, translation layer); per-game
staging is fine, knowledge with NO promotion path is tier-3 in
disguise. Per-game silos remain ONLY for narrative-specific data
(script matches) and seal-tier pairs.

## G1 PROGRESS - 2026-10-22 — ✅ **DONE (live gate passed same day)**
Audit complete + fix batch shipped (8b50b17, reverts: g1-timeouts /
g1-single-instance / g1-circuit-breaker): HttpClient 100s defaults were the
entire wedge window (OCR now 15s, translate 30s + gate-slot release);
single-instance named mutex at module init (round-3 = two hosts running
simultaneously - the "6x OCR collapse + sidecar refused" class); circuit
breaker (3+ nested timeouts -> /health probe, quiet hold). W1b
RECLASSIFIED: job ABNORMAL events = sidecar import probes + shutdown kills,
NEVER the emulator (emulator is not in the job); the parked crash hunt was
chasing that phantom - remaining unknown = round-4 launch-1 silent death
(instrumentation sufficient, await recurrence). T2.6 CLOSED (A/B zero
delta across 429 corpora; bare pair 消えていきました Zelda-pack-scoped =
no live cross-game exposure). Live gate PASSED 2026-10-22: title posted
before + after intro, all intro blocks posted, healthy-path behavior
byte-compatible with round-7. NEXT (2026-10-22 evening update): C2 live gate evaluated on the 20:25–20:35 run = **outcome MET** (zero menu-row dialogue bubbles; the hold was pre-empted by upstream guards every instance) → **C2 CLOSED**; same evening, a live run exposed two defects whose root causes are on file in the BOOKS T3+C3 entry — the first-visit title failure (**T3** fastpath, revert `revert title-fastpath`) and four overlay self-readback "hallucinations" (**C3** run-guard, revert `revert readback-run-guard`) — both coded + offline-gated (catstats A/B, 321 audited parks), commits this file. Live gates owed: (T3) cold boot — no input needed, the title→intro transition is the game's own timer — must post the title on the first visit; (C3) zero overlay re-reads with no real dialogue missing. Build work while awaiting that play session: **D1 decisions**.

- **2026-10-23 pm — error-code feed** (revert: `revert error-codes`; parked/uncommitted batch + new `Diagnostics/ErrorCodes.cs`): ONE central registry (code -> chip line + detailed What-happened/What-to-do popup) so chip and popup wording can never drift; ONE amber chip per session updated in place, clickable (popup, no focus steal). E1 = CAT load-window death (retrying burst now visible instead of silent), E2/E3 = budget exhaustion (EngineDown now carries the code, role resolves in the manager), E4 = window lost (chip REPLACES the blocking MessageBox - surfaced to the player), pending-signal replay closes the fired-before-Connect hole (one-shot errors used to be lost forever; healthy restart of the same role clears the pending code). E5/E6 registry-ready, unwired: need a VRAM reading (nvidia-smi pressure tick is the likely hook) and a provider-health probe. Gated: `--errortest` 8/8 incl. real-click popup + visual-tree PNG dumps (coordinate screenshots/clicks banned - the emulator was live behind the test panel), real-host E1 emission + pending clear. Details: BOOKS ERROR-CODE FEED entry.

2026-10-24 - every-block-post batch landed (code uncommitted, build
pending): dwell-sight1 + salvage-starved (starvation class dead:
95 starved replacements measured, [loss] ledger now logs every
candidate death), kana-bar10 (pure-kana speech no longer
second-class), decision-echo-split (gambler yes/no mints through
its echoed tail), dec-probe ([dec-probe] names every silent decision
gate - next live run pins decision 3's blocker), jp-first (JA posts
at fire with a pending marker, EN refreshes the same bubble in
place - kills the measured 8-18s empty-panel class). Reverts:
revert dwell-sight1, revert salvage-starved, revert kana-bar10,
revert dec-probe, revert decision-echo-split, revert jp-first.
Next: GREEN -> catstats A/B (salvage/kana/decision deltas expected
and explained) -> live run as the final gate - [loss]/[dec-probe]
lines plus JP-within-1s on every line as the acceptance test.

2026-10-24 - every-block-post batch GATED GREEN (catstats A/B pass):
simFires 2636->4149 (+1513, 291 corpora up / 149 flat / 1 dedup-down /
0 lost), all record-side counters identical, salvage 1774 publishes /
6494 reasoned discards (already-fired park = double-fire impossible),
1191 unique salvaged texts sampled clean, dec-probe histogram live
(18 gate tags counted). Uncommitted -> committed this date. Only the
live session remains as the final gate. Reverts: revert dwell-sight1,
revert salvage-starved, revert kana-bar10, revert dec-probe,
revert decision-echo-split, revert jp-first.

2026-10-26 - F1-F4 occlusion/miss/decision batch (committed this date).
Post-run forensics on the 2026-10-24 10:29-10:43 run closed all three
reported complaint classes with evidence: (a) phantom posts = CAPTURE
OCCLUSION - the hot path bitblts the SCREEN at the game rect, a
persistent foreign window (webpage/video, cross-game, recurring since
2026-10-07) covered it for ~45s while focus still read emulator; (b) the
'!' miss = the run's ONE silent death - chart-suppressed clears the held
candidate before the tail snapshot, returns, no [loss]; (c) 30
never-candidate texts = challenger class (no stableCount -> no road ->
no ledger); (d) gambler DECISION = weak-tier question-cap-20 on a 3-row
joined prompt (43 chars) with a single はい option (strong needs >=2).
Fixes: F1 occlusion-hold gate (Z-order walk, cloaked skipped, >=2%
overlap, occluder NAMED in log, refocus-style hash reset on clear);
F2 challenger salvage (every non-candidate death gets a [loss] verdict,
one attempt per distinct string); F3 chart-clear salvage (inline in the
chart branch); F4 question cap scales with joined prompt rows
(20/row, single-row shapes untouched). Gates: build both sides 0 err /
17 warn exact; catstats A/B 443 corpora record-side identical,
simFires 4196->4310 (+114), +125 SALVAGE-publish, +510 already-fired
labels, decHit 161->182 (+21), 103 gained salvage texts (102 clean),
8 re-routed verified still-publishing, zero corpora lost. Reverts:
revert occlusion-hold, revert challenger-salvage, revert chart-clear-
salvage, revert question-cap-scale.
Next: LIVE gate - reopen emulator/app: zero phantom posts while
roaming, punctuation lines post, gambler box = DECISION (4 rows incl
はい), and read the occlusion-hold log line to name any window sitting
over the game.

2026-10-27 - H1-H5 phantom-post batch (committed this date). Phantom
posts RECURRED (10-27: そう思うほうがなかったのか / そういえば、そう
ですね。; 10-24b: そういうことでしょうか / それでも、これからのサービス
では / ファンディングサービス) with F1 occlusion-hold SILENT - forensics
proved a SECOND cause: the rec-beam hallucinates fluent text from game
GRAPHICS (cls classical-proposer blocks rotating at fixed rect
(136,234,593,168) while real dialogue is on screen), not from a foreign
window. Five universal fixes, all CaptureController.cs: H1 frame-level
ja block-sanity veto (edge x<=0.02 / giant h>0.32 or n>=7 or area>0.40 /
lowconf<0.95; all-ja-blocks-fail -> clear extracted + Diag
"block-reject vetoed"; A-side skips mining under the same veto;
lineheight rule dropped in tuning), H2 sticky source confirmation
(birth-frame H1 pass sticky-OR'd -> Loss unconfirmed-source at salvage),
H3 position-based hold-decay (rect unobserved >=3 loops ->
STABLE-DECISION=hold-decayed, adopt fresh; string-staleness variant
rejected in tuning), H4 decode disagreement (sticky max MatchingDiv
> 0.25 -> Loss decode-disagree; falls open on the ~345/445 corpora
without the `d` sidecar), H5 Diag per-publish frame dumps (publish-dumps\
ring 80 + index.txt). Gates: build BOTH sides 0 err / 17 warn exact;
catstats A/B 445 corpora (applog_catstats_base/new.log) - ALL known
phantoms killed (そう思う 1->0, そういえば 3->0, そうで 2->0, web 1->0,
今後は、おまえ 56->1, 時時時間終わり 39->2, このメニュー 6->0), 166 REAL
salvage fires GAINED (朝までには戻ってくる +14, ブーメランだ！ +10,
魔法の力 +8, MAP +4, アグニム intro +3 ...), net -131 salvages = junk
killed with every real-looking loss attributed per [loss] line to
PRE-existing gates (menu-or-title/blend/continuation/already-fired) or
re-routed to another road (全知全能/７賢者/カンテラ/とにかく１Ｆ still
SALVAGE-publish); NEW-gate real cost all-history = 4 clean lines +
stutter variants at decode-disagree (oracle-flagged wrong reads) and 2
unconfirmed-source; weak-hold 108->20 mostly redirected to salvage
(same text, new road); hold-decayed 523; junk +1271 labels (first-gate
kills, zero publishes); vetoed=0 in replay = Diag off in catstats (H1
effect visible as phantom strings absent from OCR-result lines).
Reverts: revert block-sanity, revert source-confirm, revert hold-decay,
revert decode-agree, revert publish-dump.
Next: LIVE gate - zero phantom posts while roaming WITH occlusion-hold
silent (proves the graphics-hallucination path is dead); Diag on ->
"[blocks] block-reject vetoed" + unconfirmed-source/decode-disagree
loss lines present; publish-dumps\ files appear per publish; real
item-get lines still post.

2026-10-27 - Z2 inventory, offline portion (no live play needed).
Evidence mined (z2_mine/z2_mine2/z2_gate over all 63 Zelda corpora):
the 2026-10-08 inventory SVG geometry CONFIRMED against real frames
(unlock condition met - item-name/pendant/DO/EQUIPMENT zones all
aligned; item box holds カンテラ/ブーメラン/（１）カンテラ); the MENU
road already fires the screen correctly (menu-fire + menu-update on
selection change); the dialogue road leaked it LIVE 3x via
publish-weak-hold ('（１）カンテラ' x2, '… カンテラ カンテラ'). Fix =
C2 predicate rule 4, decorated-row containment (revert:
revert z2-menu-decor): candidate minus ALL occurrences of a menu row
(>=4ch) must leave DECORATIONS ONLY (enumerators/digits/punct/space) -
content remainders never hold. Gates: build 0/17 exact; 3/3 live leaks
held (constructive proof); 3016-fire worst-case sweep adds EXACTLY 4
holds = the leaks, zero real dialogue; catstats full re-run ZERO DELTA
on every counter (choke is !_simMode - sim unreachable, now verified
not assumed). REMAINS FOR THE BULK LIVE SESSION: the 5-box inventory
feed view (item name = only translated field) - display work that
cannot be verified offline, and shrinking the MENU bubble before the
view exists would omit content (label over omission).

2026-10-28 - F2x offline portion (FFVI wrap, no live play needed).
Measurement only, no code change. (1) Battle UI VALIDATED on the latest
full catstats sweep (445 corpora, BYGAME ffvi): recBattle=1251 battle
stamps vs junkBattle=1 = 0.08% junk, battleLoops=299, bothLoops=80 =
the known battle-command-window-mints-MENU overlap class (pre-existing,
label-only per C1 Option 1), bFfviProbe=1 flagged on 16 corpora =
observe-only Classify probe, matches the BOOKS probe notes (true battle
frames, zero engine actuation). (2) Title = NO OFFLINE EVIDENCE POSSIBLE:
tools/t2_title_mine.py over all corpora gives ffvi 0 title-frame matches
/ 126 corpora, and f2x_title.py proves why - FFVI sessions load
save-states (first frames = blank/in-game; the only "title-ish" hits are
Snes9x emulator-menu garble). Ungated false-positive side is clean
(section C = 0 across all 3,632 dialogue reads), so the HasJapanese
generalization has a safe basis but NO positive frames to gate on -
blind coding rejected. Needs ONE cold-boot FFVI capture in the bulk
session, then rerun the miner and decide. (3) Empty-TITLE anomaly
CLOSED as historical: 21 empty-ja menu-fire:TITLE records (ffvi 18, all
2026-09-18..10-09; zelda 3 = 10-10 + 10-22 x2) trace to the quiet-era
payload (0,"",false); fixed by 3f001e6 (2026-10-22 04:14, "TITLE posts
carry JA body"); post-fix corpora 10-23/10-24 = 8/8 non-empty, zero
recurrence, and the two 10-22 empties came from session 0405 started
nine minutes BEFORE the fix commit. The 7806 FullTitle fallback already
covers the EN side. No action. (4) FFVI menu/inventory census (69
menu-fire + 29 menu-update / 126 corpora): joins sane (gil/party/reward
rows), battle-window rows in MENU = bothLoops class again; a focused
FFVI inventory/menu session is still owed. REMAINS LIVE-GATED: cold-boot
title capture, intro on a fast first pass (READ-limited), first-town
coverage, shop (P6 signal), focused inventory/menu session.

2026-10-28 - PM1 offline portion (measurement only, no code change).
Corpora are THIN: 3 PM files / 987 lines fed (BYGAME papermario):
stableMenuLoops=5, transient=131, battle=1 loop, simFires=73,
recPub=58 (junkPct 1.7%), recMenuM=5, recDlgDec=58 / recDlgFire=55.
Intro stability stands on the prior measurement (W2: intro measured
28/28 sim). Title: t2_title_mine = 0 PM title frames (corpora start
post-title) - same cold-boot capture as FFVI in the bulk session. The 5
recorded PM menu fires: 2 real ('さて今日は．．．', GAME OVER + save
rows), 2 contaminated joins (web class: 生活保険/２０１８年１２月２０日
salad welded to real Rogueport text) = the capture-occlusion class F1
now names, not a menu defect. Battle/menu/decision coverage (W2) needs
the focused no-focus-loss session = bulk live. PM battle glossary EN
confirmations stay blocked on the same session.

2026-10-29 - P3 offline (missed-line sweep + LM-fluency gate repriced
against the truth set; measurement only, no code change). Method: all
40 eval/truth.jsonl rows replayed against the full-corpus catstats logs
(base = pre-H-batch, new = current binary, z2 identical to new; base ==
new on every truth row - the H-batch changed junk kills, not truth
outcomes). Fire attribution = SALVAGE-publish inline text +
candidateText -> STABLE-DECISION=publish-* adjacency, whitespace-
insensitive 10-char probes, plus a manual context pass for matcher
misses (probe limits noted in the verdict).

(1) Truth census: 18 miss rows (17 miss-complete + 1 miss-menu) = the
largest class at 45% of the set; 6 false-pub, 5 withheld-partial
(correct silence), 4 fidelity-error, 3 true-negative, 2 hit, 1 stale,
1 dup.

(2) Miss repricing: 15/18 covered by corpora (3 sessions absent from
the corpus dir = n/a: 174916_929, 175728_187, 184115_544). 12/15 NOW
FIRE under today's binary: the whole heldout Zelda opening-crawl
family (225839/225902/225914/225927/225944/225956), 230057 cutscene,
230036, decision box 174623, shop line 174402, 185246, and 175205_983
(manual context proves a growth-fast-fresh publish at loop 3046 of the
grown superset; the probe matcher failed on truth-vs-OCR punctuation
variance - matcher artifact, not an engine miss).

(3) The 3 covered residuals are ALL no-build classes:
  a. 183048_555 - two-box OCR blend: engine holds by design
     (STABLE-DECISION=hold-blend) and salvage posts the fragment
     '「俺がやる。' (label over omission honored); recovery needs
     sub-line geometry that does not exist (D1: OCR flattens
     multi-line boxes, unsplittable today).
  b. 182352_886 - OCR fidelity blend (デート/チルシェ/一般的に misreads)
     -> suspect-hold -> continuation death. The truth note itself
     classes it dictionary/beam - P2 already priced that surface at 0
     corrections. Vision class, no engine build.
  c. 184850_950 - FFVI intro-crawl never OCRs (fast auto-advance):
     the known READ-limited class (reading speed, not engine); live gate.

(4) False-pub rows = phantom family, SOURCE SOLVED. The phantom
('外国よりうつわをみつけたパートのレベルが１つあがった トのうつわをみつけた' and
siblings) exists in corpora as the RAW field of live OCR records over
textless map/overworld frames - blocks beneath are dot-garbage
('．．．' at conf 1.0): the live rec-beam hallucinated fluent text from
game graphics (the H-batch root cause, now corpus-proven: 10-07_204617
loop 1664, 10-08_203005 loops 1782-1798, 10-10_171811 loops
1924-1940). Measurement bug found and corrected during the hunt:
corpus JSON escapes Japanese as \uXXXX, so raw-line substring scans
always miss (two early 'absent from every corpus' readings were void;
recovered with decoded walks). Replay today: exactly ONE fire - corpus
10-07_204617 whose blocks carry d=0/4167 (H4 structurally open, the
documented no-sidecar fallback); every other family appearance is
KILLED (menu-or-title-owns x3, blend x2, already-fired x1), including
the modern 10-23 instance (d-bearing). All captures 10-23/24/27 carry
d on 100% of blocks -> H4 armed going forward. Phantom recurrence
strings ('外国よりうつわ…', '外側に何かようかのう…') join the live-gate
probe list. No build.

(5) Fidelity row 184638_835 re-checked manually: session covered; the
panel-text family fires in replay (a variant died already-fired = its
twin already published) while the truth-correct 'ケフカ「魔導…' NEVER
OCRs in any corpus (魔導/ヒッヒッ never read) = the beam/dictionary
fidelity surface P2 already priced to zero. No build.

(6) LM-fluency gate: NOT CONVICTED. [loss] reason census over 445
corpora: base 10,672 lines vs new 11,988 - junk 1479->2750 (H-batch
kills), already-fired 2299->2097, anchorless 1878->1883,
menu-or-title-owns 1578->1558, continuation 901->734, blend 426->415,
decode-disagree 0->534 (new gate), unconfirmed-source 0->2, too-short
48->42, non-ja 24->61, already-shown 106->110 - plus 1,801 starved
lines that SALVAGE-POSTED (published, not lost). The salvage/
every-block/H roads now post what the B10-era 3/41/46 singleton census
counted as residue; a fluency scorer priced ~40% garbage cannot beat
that funnel. Revisit only on live-gate miss evidence.

P3 verdict: zero builds - every residue class is already shipped
(H-batch), design-held with label (blend), vision/read-limited
(fidelity, crawl speed), or gated live. Truth-set replay caution for
future runs: probes are whitespace-insensitive but NOT OCR-fidelity-
insensitive; a manual context pass is required before claiming
'still-miss' (1 of the 4 claimed miss rows plus 1 fidelity row were
matcher artifacts; 3/3 survive as genuine residue).

2026-10-29 - P4 offline (loop payoffs: re-mine + promotion review with
Paper Mario data; measurement only, no code change).
(1) Re-mine (tools/mine-confusion.py --min-count 2 over all 5 miner
files): 3,365 substitutions / 394 distinct / 144 candidates not already
in ConfusionPairs. PM contributed 3 pairs total, all one family
('おじようさん．．．…' normalization), split across the two PM files
(Dolphin_2609a-embedded id + ペーパーマリオRPG_(G8MJ01 = the known
orphan-file split from the ResolveGameId id bug).
(2) Reviewer verdict on appends: NOTHING QUALIFIES. The only
multi-game, non-artifact class is punctuation: (…,．) 232 x3 files and
(…,・) 216 x3 files. Everything below is either single-game heavy-garble
ALIGNMENT artifacts (the たたかうひっさつわざ family - う/ら, た/だ,
ね/わ, れ/わ = the diff aligner distributing mismatch across garble;
appending would corrupt real text) or unsafe global swaps (ホ/ポ 92x1,
ビ/ピ 19x1 would wreck legitimate ホ/ピ words in unseen strings).
(3) Measured-spread gate (P4's own rule) = 0. C#-parity fold
(union-find min-char over EXISTING ∪ proposed) applied to all 3,374
fired texts from the full 445-corpus replay: ZERO twin pairs merge
under the proposed folds that do not already merge under the existing
set - no dup/escape today is attributable to ．/…/・ variance, so no
append. (Scope note: equality-level merges within corpus; the cost-0.4
alignment path at CaptureController:4907 was not separately priced -
latent, unmeasured, unneeded.)
(4) Promotion review: _global.json = 88 pairs, fully spot-checked =
100% sane text corrections (ハイラル/人口→入口, 賢者, 封印, セーブ,
ボタン, ゼルダ, ルピー…), ZERO hallucinated/phantom values → the
>=2-file agreement gate is not poisoned; PM's 3 pairs correctly NOT
promoted (keys differ across its two files).
(5) Gate-risk found, no action taken (folds into the ResolveGameId
normalize-id fix already awaiting go in BOOKS): PromoteGlobalCorrections
counts one FILE = one vote, so the orphaned PM duplicate file means an
identical pair landing in BOTH PM files would self-promote to _global
from a single real game. Not triggered today. When the id fix goes:
merge/delete the orphan.
(6) Profile tuning: none - PM corpora too thin (3 files / 987 lines) to
measure a spread; tune only when a measured spread exists.

2026-10-29 - P5/P7/P8 offline pricing (measurement only, no code; miss
sets priced over all 445 corpora + the full new-log replay).

P5 - decision boxes + sprites. Corpus census: DECISION fires = 17
(FFVI 11, Zelda 6, PM 0) + 2 FFVI DECISION updates; MENU fires = 111
(FFVI 40, other 33, Zelda 33, PM 5). dec-probe over the full replay:
191 decHit=True / 2,233 decHit=False = 7.9% overall - per game: Zelda
21.9% (106/484), other 6.2%, FFVI 4.0% (60/1485), PM 0.0% (0/52).
Top fail gates: blocks<3 923, content 727, options-count-0 660,
prompt-bracket 545, option-nature 462, prompt-space 317. Verdict: NO
BUILD.
  - PM pointing hand: the PM data this was queued behind is now in
    hand and still shows 0/52 decision hits (thin corpora, 987 lines)
    - the convicting evidence does not exist yet; needs the focused
    no-focus-loss PM session (bulk live).
  - Zelda sprite map / positioned boxes: the truth set's 2 decision
    rows are 174623 (FIRE-NOW under current binary) + 175728 (session
    absent from corpus dir) = zero covered misses attributable to
    missing positioning/sprites; display geometry is live-verifiable
    only anyway.
  - The decision road itself is healthy: fast-fail gates, 7.9% hit
    rate, PM's 5 menu fires remain the PM1 real+contaminated picture.

P7 - Zelda-long latency. Offline profile (det/rec exist only on newer
records; frame interval = wt delta between OCR records, <60s):
  game    det p50/p99/max      rec p50/p99/max     frame p50/p99/max
  ffvi     152/1273/1678        328/5131/17176      1070/9880/23402
  zelda    112/521/14770        328/1543/15585       918/12056/57295
  pm        77/160/758          222/1493/8636        781/3365/39485
  other    114/1242/9211        165/4291/6172        775/3614/41603
Reading: p50 frame ~= det+rec+capture ~= 900-1100ms in every game.
The Zelda long tail is real as measured (frame p99 12.1s vs PM 3.4s /
other 3.6s / FFVI 9.9s), but p99 frame >> p99 det+rec (2.1s) and FFVI
rec max (17.2s) exceeds Zelda's (15.6s) = the tail is time OUTSIDE the
det/rec stages (dwell/skip/stage work), not a Zelda-specific stage
regression. Verdict: numbers recorded, NO BUILD - stays queued for the
profiling session; the ~1s loop cadence cannot split the tail any
further offline.

P8 - display-behavior queue, all four classes priced:
(a) transient notifications (damage numbers/pickups) = numeric-only
    class: tiny - FFVI 3 distinct/3 occ, Zelda 20/110, other 24/156
    (mostly title dates/labels), PM 0. Fate over the full replay:
    103 dead:junk + 2 dead:menu-or-title-owns, ZERO published.
    Miss set = empty; the junk gate already owns the class. NO BUILD.
(b) fading / single-glimpse: distinct-text singleton fraction = 61-69%
    (len>=4) and 59-71% (len>=10) per game - consistent with B10's
    64-71% census; garble inflates it and cannot be separated from
    real fades offline, and the salvage road posts starved singletons
    (1,801 in this replay) so single-glimpse text does publish.
    NO BUILD - any real-fade subset must be priced live.
(c) per-line replacement: same-rect, no-containment text swaps between
    adjacent records = 1,393 total (zelda 813, ffvi 279, other 223,
    pm 78). Samples show exactly two sources: OCR block jitter on a
    fixed rect, and full-screen dialog rect (0,105,667,384) line
    advance (PM 'そういえば、' -> 'そういうことはないですが、' -> '数多く、')
    = normal dialogue advance, not a defect. Advance vs forced-
    replacement cannot be split offline (needs live timing). NO BUILD.
(d) timed-vs-advance: NOT MEASURABLE from corpus clocks - prefix-growth
    events n=290 p50 1051ms vs jump-in n=6000 p50 979ms: the ~1s OCR
    loop cadence swamps typewriter reveal speed (~30-80ms/char), so
    the two arrival modes are indistinguishable in wt deltas. Queued
    with empty miss set until video-level/live evidence exists.

Data note: 2 corpus gzips are crash-truncated (Snes9x_1.60_20261019_
035824, a Zelda_no_Densetsu tail file) - scanners skip them cleanly;
watch item updated from one to two files.

2026-10-30 - bulk live session EXECUTED (5 legs, played live; keep-log.armed
validated across all 5 launches; R2-R4 matrix locked: Okami / NieR+KH3D /
Yakuza 0; mash memo expired 9->0). Full evidence + three silent-death root
causes in BOOKS tail "2026-10-30 BULK LIVE SESSION". Outcomes: B1/B2 titles
NOT MET (latin-logo titles cannot fire CheckTitleEpisode - HasJapanese gate;
spec: title text only, flashing start line excluded), B3 FFVI wrap died
mid-leg on sidecar accept-loop death (OCR connection-refused x120, no chip),
B4 PM froze on translation circuit-open loop-hold (83/115 holds, pending EN
never retried, no chip), B5 Zelda = TITLE + DECISION posted, phantom probes
dead, B16b unmet, B6 Z2 5-box PASS + v2 spec. NEXT BUILD QUEUE (offline-
verifiable, evidence-first): (1) Z2 v2 - bottle tall-box merge + EN mirror
grid (corpus rows already harvested); (2) transport-death E-code surface +
sidecar restart watchdog (silent class 1); (3) circuit-open chip +
capture-continue + pending-EN late retry (directive, silent class 2);
(4) latin-title path PRICED over harvested title frames before any build
(silent class 3 - HasJapanese gate is deliberate, price first). Watch item:
THIRD crash-truncated corpus gzip (Zelda_20261030_142705, taskkill /F
finalizes nothing) - harvest/validate corpus BEFORE any forced kill.

2026-11-12 - LEDGER CATCH-UP: UX wave batches 1-7 + cat_service tracking +
commit-precheck all live-confirmed (live session 15:25:48-15:27:37 +
explicit confirmation of the b4/b5/b6 checks; root-cause chain in BOOKS
tail). Nine commits, gates green each:
- `edffd6d` batch 1 pop-out feed (revert: revert popout-feed) - detached
  topmost-band overlay + Ctrl+Shift+D hotkey + persisted position;
  live-used throughout the session (popout restored position, DETACHED
  flips correct).
- `913d474` batch 2 HDR detection (revert: revert hdr-detect) -
  advanced-color query + sustained-blank probe -> E9 chip; OFFLINE-GATED,
  live proof still owed (no HDR on this machine; deferred to an HDR-capable display).
- `80543fa` batch 3 capture-swap (revert: revert capture-swap) -
  PrintWindow adopt when BitBlt returns a degenerate frame; engage/miss
  arm live-owed with the same HDR proof.
- `3726c68` batch 4 calib-cover (revert: revert calib-cover) - raw PMv2
  virtual screen + <=3-pass measure-and-correct overlay; LIVE PASS:
  calibration window was re-dragged to the true screen bottom and the overlay lands right
  (pass=2 converged scale=1.250x1.250 - fixed the stale 1.50 ctor scale).
- `9f6a1bf` batch 5 feed-defer (revert: revert feed-defer) - 1s
  post-movement Settings save debounce + 4Hz opacity; LIVE PASS: no
  mid-drag freeze, Settings saved only after movement stops.
- `b6aa8a1` batch 6 feed-resize (revert: revert feed-resize) - CanResize
  while detached + FeedWidth/FeedHeight persist; LIVE PASS: drag-resize
  reflows and persists across pop-out/dock.
- `13a6da2` batch 7 dock-hide (revert: revert dock-hide) -
  GameCoversMonitor hides the Dock affordance while covered; LIVE PASS:
  dockvis flips correct all session (True fullscreen / False windowed /
  False after game close).
- `2001f2e` track ocr-service/cat_service.py as-is (tracking only, no
  logic change - it ran production untracked since the 2026-10-17 split).
- `25904c6` commit-precheck (revert: revert commit-precheck) - free-commit
  >=11500MB gate right before from_pretrained, mirrors the existing VRAM
  pre-check (fail-open, env override WUNDER_CAT_MIN_FREE_COMMIT_MB); ROOT
  CAUSE PROVEN (BOOKS tail): deterministic AV 0xC0000005 in
  torch_cpu.dll+0x8e4a279 when system free commit <~11GB at the instant
  the 4-bit weight load spikes ~6.7GB; 26-boot dataset zero-overlap split
  (crashes <=10675MB, clean >=12215MB); 3 repros incl. separate-process
  purity run; availPhys + fullscreen-timing theories exonerated; tests
  A/C/B pass + catstats A/B per-corpus IDENTICAL (466 common, 0 changed);
  LIVE PASS: `host-commit check: 12495MB free, proceeding`, no chip,
  session translated normally.
Remaining live-owed: batches 2/3 HDR proofs only (no HDR on this box).
NEXT (approved 2026-11-12, both root-caused in code): batch 8
taskbar-in-popout (revert: revert taskbar-feed), batch 9
auto-popout-on-fullscreen-coverage (revert: revert auto-popout) -
taskbar first; then Phase 0 onboarding for the five R2-R4 picks (Okami /
NieR Replicant / KH3D / Persona 5 / Yakuza 0), then the beta/alpha
release build.

2026-11-13 - UX wave batches 8, 9, 10 committed; 8+9 live-confirmed,
10 offline-complete (live gate owed with the next session). Three commits,
gates green each, catstats A/B PER-CORPUS IDENTICAL each (466 common,
0 changed -> zero dialogue losses):
- `0da1cf1` batch 8 taskbar-feed (revert: revert taskbar-feed) - the
  popped-out feed carries the app's taskbar button (Title first, then
  ShowInTaskbar; docked restores byte-identical so the window picker
  contract is unchanged) + CloseShouldExit so closing the popped-out
  feed exits the app (mirrors BottomBarWindow). Gate --taskbarfeed
  includes a ResizeMode provenance control: the first run failed
  onlyBitDiffers, stage-traced BEFORE touching the assertion - the flip
  adds EXACTLY WS_EX_APPWINDOW (0x8080000 -> 0x80C0000), the stray
  0x100 is batch-6's ResizeMode.CanResize WS_EX_WINDOWEDGE (corroborated
  by production 0x8080108 redock read predating this batch). Live-confirmed
  2026-11-13 (taskbar button + close-exits behavior as designed).
- `db2ddef` batch 9 auto-popout (revert: revert auto-popout) -
  coverage edge + docked -> auto-pop via the existing SetDetached (auto
  flag, persist:false so transients never write settings); edge cleared
  + auto-popped -> auto-redock; manual pop never yanked, manual dock
  holds until the next edge (edges only, never mid-state). Pure policy
  AutoDetachForCoverage + flag NextAutoFlag; gate --autopopout truth
  table 6/6, flag table 4/4, sequences S1-S4. Live-confirmed 2026-11-13:
  windowed->fullscreen auto-popped, back to windowed auto-redocked.
- `154245b` batch 10 popout-anchor (revert: revert popout-anchor) -
  launch-straight-into-fullscreen fix. Root cause proven from the
  mirrored session log (app2.log 12:35:44, full chain in BOOKS
  tail): OnBoundsChanged anchored the DOCKED panel to the fullscreen
  rect (sidePanel L2557 T-77 = seam + off-screen top), the batch-9
  auto-pop 66ms later INHERITED that anchor -> feed DETACHED at
  (2045.6,-61.6) = the reported "invisible somewhere". Fix =
  ClampFeedRect (pure full-containment, coverage-aware generalization
  of b4's restore-path ClampToVirtualScreen) + AnchorFeedOnPop (raw
  USER32 space, game monitor when covered else virtual screen,
  CONVERGENCE LOOP max 3 passes). The PMv2 DPI-seam hazard was proven
  by the gate's first failure: raw write -> move across the 125%/100%
  seam -> WPF re-applies its DIP cache -> size changes after the clamp
  (304px clamped, 380px settled); position survives, size does not, so
  re-clamp until stable. Gate --popanchor: real-HWND contract
  assertions (repro lands 2180,0 fully inside + top-0; uncovered ->
  virtual screen; windowed (2050,59) position-identity; covered
  identity; oversized pin; pull-in) - all ok, exit 0.
Live gates owed: batch 10 relaunch-into-fullscreen check (start game
fullscreen first, then WunderLanguage, attach, set region -> transparent
popout appears on the game screen) + batches 2/3 HDR proofs (no HDR on
this machine). Next: Phase 0 onboarding for the five R2-R4 picks (Okami /
NieR Replicant / KH3D / Persona 5 / Yakuza 0; logger armed), then the
beta/alpha release build. Open items: P6 shop-play signal +
PM glossary EN confirmations.

2026-11-15 - fullscreen wave (Okami legs 3-4) + feed-selfmask
LIVE-CONFIRMED; three leg-4 UX batches committed offline-green (live
gates ride NieR leg 1). Nine commits since the 11-13 tail; every gate
green on the final build:
- `3120ac1` fullscreen-fg (revert: revert fullscreen-fg) - the focus
  gate held 4145 loops / 0 publishes through 28 min of Okami play:
  PCSX2 F11 hides the attached MAIN window and renders in a NEW
  top-level HWND of the same process, so fg==attached never matched.
  FocusAllowed passes on the fullscreen signature (attached NOT
  visible + foreground in the attached process); the visible-attached
  check stays first so a windowed same-process dialog still holds.
  Gate --fullscreenfg 6/6.
- `0e894cc` fs-capture (revert: revert fs-capture) - leg-2 cause A
  proven: capture anchored to the HIDDEN main window's stale windowed
  rect (slice (529,320)-(1831,1066) vs dialogue ink at y=1152-1200,
  0/13 rows inside). FullscreenCaptureTarget re-resolves from live
  USER32 state every loop - largest visible top-level window of the
  attached process (process-derived, so a dead/recreated HWND reacquires
  next loop) - and CaptureWindowFull reads that window's OWN rect whole
  in physical pixels. Gate --fscapture 7 cases incl. foreign-occluder
  naming.
- `b21db62` fs-coverage (revert: revert fs-coverage) - leg-2 cause C
  proven: RefreshDockAffordance fed GameCoversMonitor the HIDDEN main
  window's stale rect -> coverage edge never fired all leg (docked bars
  z-paired above the hidden main = BEHIND the fullscreen game, z probe
  bars 36 / game 30). CoverageWindowFor resolves the surface SHOWING
  the game right now; dockvis, the pop anchor and (from today)
  RefreshFeedOpacity all derive from it. Gate --dockvis EXTENDED (not
  a parallel gate). Live-confirmed leg 3.
- `560e8e4` case7-env-tolerant (revert: revert case7-env-tolerant) -
  gate-only env fix, no product code: fscapture case-7 tray pick made
  Z-order-tolerant for a fullscreen occluder above the taskbar.
- `96596ac` captureswap-topmost (revert: revert captureswap-topmost) -
  gate-only env fix, no product code: captureswap's raw-BitBlt test
  window enforces its own on-screen precondition (Topmost=true).
- `2be72c8` feed-selfmask (revert: revert feed-selfmask) - leg-3 root
  cause PROVEN: whole-frame capture saw our own popped-out feed and OCR
  read its history UI as dialogue (fullwidth suspect-holds, stableCount
  starved 1-2, rec p50 394ms -> 4877ms by 13 crops/frame). MaskOwnUi
  black-fills same-process windows out of the frame before any metric
  or OCR - pixel masking is the root fix because the sidecar merges
  rawText from the same pixels (a block filter would leave candidates
  dirty). Gate: fscapture case 8. Live-confirmed leg 4: 370 mask lines,
  ZERO Macebook garbage, stableCount reaches 27 (leg 3 capped at 2),
  loop p50 ~5000ms -> 45-48ms (~10x), suspect-hold holds real JP
  (頭突き/ジャンプ), 367 OCR results / 39 publishes (13 new / 12 salvage /
  4 scroll / 5 menu / 3 weakhold / 2 growth), autonomous pop->dock
  cycle, 0 exceptions.
- `ecc4d8f` opacity-live (revert: revert opacity-live) - leg-4 report
  item 1: RefreshFeedOpacity intersected the RAW attached handle,
  hidden/stale in fullscreen -> "no overlap" -> feed stuck solid over
  the game (proven: `opacity -> 1 (overlaps game=False)` at 16:30:28
  while the game stayed fullscreen). Reads CoverageWindowFor now;
  windowed path byte-identical (resolver returns the handle unchanged).
- `15fa964` opacity-release (revert: revert opacity-release) - leg-4
  report item 1b: the 250ms LocationChanged throttle can miss the
  final drop position (user report: the popout "gets stuck in solid
  mode" after a drag). DragEnded fires once per finished DragMove;
  AppShell refreshes opacity UNSthrottled at the true drop - feed fades
  back to its resting 0.75 over the game.
- `525c991` edge-resize (revert: revert edge-resize) - leg-4 report
  item 2: zero hit-test machinery existed anywhere in the codebase.
  Root cause PROVEN empirically (controlled A/B, identical
  preconditions): a REAL injected edge drag pre-fix ours=True
  thick=True layered=True dwmInset=0 yet dw=0 - WindowStyle=None WPF
  answers WM_NCHITTEST itself (HTCLIENT), so the native WS_THICKFRAME
  sizing border can never engage; post-fix dw=-40 exact. The hook
  answers the native HT codes via pure EdgeHitTest (corners uniform on
  both axes, sides single-axis, interior pass-through, 7px band at
  window DPI) while DETACHED only - docked size stays owned by
  OnBoundsChanged. --feedresize extended: EdgeHitTest 12-point table +
  three REAL injected drags (bottom-right corner dW=dH=-30 uniform,
  top-right corner uniform, side clamp to MinWidth 340 with dH=0) +
  second-monitor park + HWND_TOP push + WindowFromPoint proof AT THE
  POINT. Two environment findings, log-only never asserted: (a) this
  box intermittently refuses WS_EX_TOPMOST (SetWindowPos ok=True
  lastErr=0, bit unset) so the gate owns z-order by geometry + point
  proof instead of a style bit; (b) AllowsTransparency + CornerRadius=8
  leave the outer arc pixels fully transparent and clicks there pass
  through to the window below (alpha beats z-order) - corner test
  points sit 3px inside: opaque, in-band, exactly where a cursor
  lands. Revert-phrase compile check run (revert -> 0 errors ->
  restore).
Final-build gate sweep: --feedresize exit 0 twice consecutive;
  --feeddefer --dockvis --taskbarfeed --autopopout --popanchor
  --calibcover --fullscreenfg --captureswap --fscapture --simcheck all
  exit 0; --errortest sequence complete + all PNGs dumped (harness
  note: that gate NEVER exits by design - taskkill ~20s after start;
  its earlier exit-1 was a single-instance lock conflict from two
  parallel gate runs, which also raised the "already running" popup -
  dismissing it only makes the extra copy back off; gates run
  strictly one at a time).
Next: NieR Replicant leg 1 (attach windowed first - the picker lists
only visible windows - then F11, 10-15 min, quit normally) with three
live checks riding it (fade to 0.75 after drag release; edge + corner
resize; [feed-selfmask] lines present), then KH3D -> Persona 5 ->
Yakuza 0, then the beta/alpha release build (flywheel logging +
one-command log collection).
