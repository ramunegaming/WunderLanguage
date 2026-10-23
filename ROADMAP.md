# WunderLanguage — Development Roadmap & Milestone Tracker

Statuses: **Complete** · **Needs play session** (shipped, live proof owed) · **In progress** · **Planned**.
Note: pre-bottleneck milestone definitions (M1–M7) predate this ledger — only M7b/M7c slices are recorded. This tracker supersedes them with the bottleneck/loop/step structure. Revert phrases with each shipped batch.

## HANDOVER STATE (2026-10-21 — read this first)

> Checkpoint of record: **`CHECKPOINT-2026-10-21.md`** (full session detail). Older `CHECKPOINT-2026-10-20.md` is superseded.

### ✅ Z1 WRAPPED (live run 2026-10-21 08:27)
- **11/11 blocks, one bubble each**, byte-identical vs user's official script
  (char-for-char: gap 　, … tails, only アグニム + ささげていった blocks keep 。,
  ささげられる, 消えていました, no fabricated 。 anywhere). Title posted once.
- Both defects closed live: two-anchor (`1f2f66a`) + intro-punct (`5cafd30`).
- Evidence: `app-log-intro-live-pass-2026-10-21.log` (474KB, copied out of the
  wipe-per-launch app.log).

### Committed (git log)
- `6a819cb` しかし-line tail strip pair — gated (revert: revert intro-punct)
- `1f2f66a` Z1 two-anchor + quote/bracket strip (revert: revert two-anchor)
- `5cafd30` Zelda intro 1:1 fixup pairs + ころ… anchor (revert: revert intro-punct)
- `44e5691` BOOKS log entries for both

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
- しかし-line pair (`6a819cb`): A/B ReplayChecker 52-corpus sweep — baseline 402 vs
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
1. ~~Decide しかし-line pair~~ done → `6a819cb`
2. ~~Live intro run~~ done → PASS 11/11 (log: `app-log-intro-live-pass-2026-10-21.log`)
3. Campaign continues with **G1** (debugging/hardware audit — audit first, no rewrites)

## Campaign plan (2026-10-20 — session restart, the wrap order)

Strategy agreed with the user: wrap Zelda, wrap FFVI, focused Paper Mario week, then extend the resolution matrix. Every onboard must strengthen the shared game-blind systems so untested future games inherit the gains (Scalability — never a collection of unrelated hand-tuned implementations).

### Immediate queue (work in this order)
1. **✅ Z1 — Zelda intro 11/11 — DONE (live pass 2026-10-21).** Standing user demand: nothing else until it works. Two defects: **A** block 8 double-post (similarity 0.89 sits inside the 0.85–0.90 gap, both fire via script-anchored); **B** block 11 wobble twin (ささげられる vs ささばられる posted as separate lines). Fix by extending the B19 twin/wobble machinery, not by bolting on a threshold. Proof: fresh live run + screenshot; commit with revert phrase. Lesson on file: proper edit tools only — no PowerShell line-splicing on CaptureController.cs (213 build errors, reverted). **Closed: 11/11 blocks byte-identical vs official script, one bubble each, 1:1 punctuation — see HANDOVER STATE (2026-10-21).**
2. **G1 — Debugging-workflow & hardware audit** (user-approved brief; audit first — no logger/architecture rewrites). Deliverable: prioritized improvement plan with affected files. Known breakpoint spots: CaptureController.cs 1202–1219 (scriptFresh), 1349–1392 (growth-fast/wobble-publish); main.py 1108–1130 (/ocr), 767–780 (_script_match).
3. **C1 — Category measurement pass** — ✅ **DONE 2026-10-22** (observe-only `CatStats` harness, revert: `revert catstats`; artifact `gate\catstats_c1_v3.log`): 431 corpora / 45,369 lines through the REAL pipeline offline, two truth levels (sim attempts = pre-guard; publish-truth = stamped live bubbles joined to sampled frame state). Frame census: 88.9% in-game, 7.0% transient menu-shaped (crawls), 3.1% stable menu, 0.86% battle-shaped. Attempts: 8.1% of dialogue fires land on non-ingame frames; the live funnel absorbs nearly all where a bolt-on exists (FFVI 220→1) but has none elsewhere (Zelda 18→14). Publish truth: 16/563 flagged (2.8%), ALL flagged corpora content-spot-checked → **real junk = menu garbage, 3–4 instances, a UNIVERSAL class** (Zelda save-menu rows, FFVI magic-menu spell lists, PM date-screen garbage ≈0.6% of stamped publishes) + **14 battle flags were FALSE POSITIVES** (correct dialogue on date-stamped text screens — BattleFrame.Classify misreads non-FFVI layouts). **DECISION: Option 1 (display tagging)** + evidence-triggered guards stay; no universal routing layer (see watch item). C2 = the menu-garbage class fix.
4. **C2 — Menu-garbage class fix** — ✅ **DONE 2026-10-22** (live gate evaluated on the 20:25–20:35 run — verdict at end of item; revert: `revert c2-menu-hold`): single-choke hold inside `PublishDecisionAsync` (post sim-record, pre-RecordFire, live-only) withholds dialogue fires whose candidate matches the STABLE menu's rows (`HeldByStableMenu`: same-frame OCR identity via equality/fragment/merged-rows containment, length floors ≥4/≥8); omission-only, candidate stays (fires later or dies by staleness = delayed label, never a suppressed line); transient menu-shaped text (crawls) and updates untouched. Gates: replay A/B ZERO DELTA (stash/pop, identical extraction) + catstats full-sweep A/B ZERO DELTA on all 431 corpora + SUMMARY. **Part-2 RE-SCOPED** (evidence-based): loosening BattleFrame would ship C1's convicted false-positive class into a routing path — instead an observe-only probe (`bFfviProbe` = Classify with FFVI labels on non-FFVI corpora): 102 loops total, all in real-battle Zelda sessions (59/23), universal ≈ boundary frames — label-free shape detection stays future work, must pass the Zelda date-screen test. Named residual: cursor-moving menus stay transient (unheld) → F2x/tier-2. **Live gate verdict (2026-10-22 run): outcome criteria MET** — zero dialogue bubbles of stable-menu rows (GAME OVER/save, inventory, PLAYER SELECT), MENU mints unchanged, dialogue fired after menus; the specific `[publish] menu-hold` line never appeared because upstream guards pre-empted every instance (subpart-suppressed on the save rows) = hold is an unengaged backstop, first-fire evidence still unobserved. C2 = CLOSED with that note.
5. **T3 — Boot-title fastpath** — ✅ **CODED + OFFLINE-GATED 2026-10-22** (live gate owed; revert: `revert title-fastpath`): first-visit title post failed because the episode detector needs 3 evidence loops and the boot logo window offered ≤2 settled reads before the game's own **auto-advance timer** moved on (user correction 2026-10-22: title→intro is automatic, not user-driven — the window is game-determined and identical for every player; full root-cause chain in BOOKS T3+C3 entry; NOT pack lag — the window-title heal ran 1s after the logo appeared)). One settled block containing the full TitleNorm'd pack title (bn ⊇ pn, or the unvoiced variant) now accelerates `_titleEpisodeHits` straight to the publish threshold; the veto block is unchanged; Latin titles stay inert behind HasJapanese; T2-mined justification (0 false matches in 3,632 dialogue reads). Offline-inert by construction (title path does not run in replay/catstats — measured) → the live gate is the only gate: cold boot must post the title on the FIRST visit even at the game's own auto-advance speed (no input — the title→intro transition is a timer); return visits still exactly one post per visit.
6. **C3 — Overlay readback run-guard** — ✅ **CODED + OFFLINE-GATED 2026-10-22** (live gate owed; revert: `revert readback-run-guard`): 4-of-4 flagged "hallucination" posts were the overlay re-reading its own recent posts with mutated edges (the feed renders inside the capture region; every pre-existing dupe rule is head-anchored and the fire history is capped at 8 fires/10 loops — a middle-run mutation older than 10 loops leaked everything). Fix = `_postedTexts` long-horizon store (RecordFire, 400-loop horizon/cap 200, B19's cap-8/window-10 untouched) + one rule at the `FireHistoryCovers` tail: contiguous shared run ≥8 chars at ANY position parks pre-translate; strict growth (incoming containing shown) and exact/superset re-shows exempt. Gate: catstats full-sweep A/B base 108 → 615 parks — **all 321 unique new-parked texts audited line-by-line = duplicate/wobble/merge/battle-command/party-row/item-menu re-reads of content already shown, zero clean first-time dialogue lines**. Replay delta=0 proves fixup-path neutrality only (the replay harness runs extraction, measured caveat on file). Live gate: a replayed session must show zero overlay re-reads ([publish] history-parked lines carry the class) with no real dialogue missing.
7. **D1 — DECISIONS category (user item 2)** — **CODED + OFFLINE-GATED 2026-10-23; first live session evaluated + D-SUB sub-line fix + D2 era-tiering in (revert: `revert d1-decisions`, `revert decision-sublines`, `revert decision-tiering`)**. C1 Option-1 display tagging applies (tag at display, no routing/suppression change). Root causes from the 6 troubleshoot fixtures (now in `Roaming\WunderLanguage\troubleshoot\`, forensics in BOOKS D1 entry): the `<3 blocks` gate dropped 2-block decision frames; tall cls-mirror blocks arrive BOTH Y-grouped and re-attached, so ？-bearing merged text landed in the options bucket and the question check rejected the box (killed the shop); the builder hard-required ？-plus-options shape; OCR flattens multi-line boxes into one block (unsplittable without sub-line geometry). Final design — all universal structure, no per-game strings: tall-block contract (tall JA blocks appear exactly once as their own row); decision check runs FIRST (`decHit` gates the two fragment gates); ？-echo pieces SKIPped + `options.Distinct()`; options-only branch for no-？pickers (`texts.Count` 3–4, length-spread <2x, sentence punctuation rejected); question gates = length ≤20, no speaker bracket 「 (prompt uses 『 in the one real quoted prompt), no space (a prompt is one unbroken phrase — also catches the detached-？battle rows); option-nature gate (must be hiragana-bearing, pure digit, or bracket-led — else it is a name cell like ウェッジビックス). Sweep ladder on the 433-corpus `--catstats` A/B: 186 → 120 → 41 → 23 → **13 mints = 9/9 real fixtures** (raft, crossroads, status, party, save-point ×2, explain, PLAYER SELECT, gambler) **+ 4 named FPs** (Zelda hermit Q + hiragana callout; label-only under the safety valve). Base A/B (base = `5c95072`): all 8 base DECISION mints identified via publish-context as battle/name-pair FPs (4 of them NRE immediately after — the pre-existing `[menu] ERROR` class, 391 in both C3 logs); zero real decisions lost; MENU 301→300 = two FFVI MENU(3)→DECISION swaps with content intact + Zelda hermit re-shaping; TITLE 16=16, gameover 17=17. Named residuals: shop `かう` sits on a chest sprite so the detector never fires a clean box (mints Q + `いらない` until the detector keeps the line); hermit 4 FP; flattened no-？pickers stay MENU by design (2-row no-question). **First live session 2026-10-23 03:36–03:48 (cold boot + decisions route): D1 minted live (DECISION tag + rows at 03:46:54) but short — the merged box dropped `逃げだす`, and the picker posted IN-GAME TEXT → root cause = chain-merged multi-line block + single-line recognizer; D-SUB (revert `revert decision-sublines`) recs detector sub-boxes for question-bearing n≥2 h≥40 blocks and ships per-line `lines`; gambler frame test recovered all 3 rows; sweep A/B: 433 pre-session corpora byte-identical (12 distinct row-sets = v5), delta = the 2 new corpora from the session itself; corpora schema strips `lines` → sub-line path is live-only verified (caveat on file).** T3 **CLOSED** (title posted on first visit 03:37:34.997, exactly one post per visit ×3 visits) + C3 **CLOSED** (4 history-parked intro re-reads, zero re-posts, no dialogue missing, 0 errors) from the same session's log. D1 live gate remaining: gambler DECISION with BOTH options + shop DECISION tag, dialogue path byte-identical. **D2 decision tiering (2026-10-23, user-raised scaling hole: the absolute caps are SNES-era-calibrated and would degrade verbose modern prompts; brainstormed constraint = no new detector, structure-only):** ？-tail prompt gate on sub-line-sourced question rows (must END with ？ — mid-？ dialogue rows die at any length; merged/replay rows keep the lenient first-？ split) + question/option caps tiered on option-cluster strength (strong = ≥2 judgeable options each ≤ prompt length → 40/16; weak → exact 20/12). Sweep A/B on all 435 corpora: base == r1 == r2, DECISION=14, byte-count identical — zero delta by design (no SNES prompt >17; no FP carries a strong cluster; tail gate dormant on replay via the schema caveat above). The bundle buys future-path (long modern prompt + real choice column mints) paid for by live strictness. Live gate with D1 pass 2.
8. **CAT reliability — translator crash class HARDENED 2026-10-23 (revert: `revert cat-reliability`; fix lives in the PARKED uncommitted files `SidecarProcessManager.cs` + `AppShell.cs` per W1b protocol — a checkout of those files drops it)**. The 09:18 live session posted only the title: the CAT translator sidecar segfaulted (0xC0000005, the 2026-10-14/10-17 "4-bit weight load" family in cross-process form — load window overlapping OCR model load + emulator) 4× inside a ~105s window, the 3×25s retry budget burned inside it, and exhaustion was silently log-only. Detection was healthy all session (9 `[sublines]` events incl. the gambler live split さあ、どうする？ | しょうぶする | 逃げだす — **first live D-SUB proof**). Fixes: `HF_HUB_DISABLE_MMAP=1` on the CAT spawn (the segfault mechanism, documented Windows mitigation), CAT restart backoff 10s/60s/240s, and the terminal panel notice now fires for CAT exhaustion too with role wording (the "fallback covers" assumption is false for the Offline provider). Gates: GREEN; mmap-off load ready (weights 14.5s); induced-death spacing measured ~12s/~62s. Live gate owed: next session translates normally; no silent dead sessions.

### Campaign phases (after the immediate queue)
- **Z2 — Zelda wrap: inventory menu.** Parked until T3/C3 gates + D1 close (user directive 2026-10-22: "park this until after we wrap up the other 3 things"). Menu routing has burned us before (B19 MENU attempt reverted) — evidence-first, small gated steps. **Design spec on file (user, 2026-10-22; SVG: `D:\Wunderlanguage\zelda-inventory-mockup.svg`, original `Downloads\ui SVG\Zelda inventory screen.svg`):** the inventory translation feed = 5 fixed boxes — top-left empty, **top-right = item name, the ONLY text that changes with selection**, bottom-left = action words (static — "what you can do", not selectable), middle-right = static non-changing text, bottom-right = "EQUIPTMENT" (already English). Box art/sprites = nothing to translate, render blank. Engine implication: the inventory feed view needs exactly ONE extracted field (the selected item name); the other four boxes are fixed chrome.
- **F2x — FFVI wrap arc:** title, intro all blocks, first town (most/all text), battle UI (F1 exists — validate), shop UI (P6, blocked on shop-play signal), inventory UI.
- **PM1 — Paper Mario focused week:** intro stability, title posting, inventory, menus. User plays; no alt-tab; our Shot button; collects N1–N6 proofs.
- **R1–R4 — Resolution matrix, 2 games per generation:**
  - R1 240p: Zelda + FFVI (both onboarded ✓)
  - R2 480p: Paper Mario + second game (**TBD — user to pick**)
  - R3 720p: **both TBD — user to pick**
  - R4 1080p: Persona 5 + second game (**TBD — user to pick**)
  Each generation trains resolution-specific capture/recognition until solid, then feeds gains back into shared systems.

### Feed categories (agreed 2026-10-20)
Five exclusive categories for the translation feed: **Title** (title screens only) · **In-game text** (NPC dialogue, signs — the default) · **Decisions** (option lists with selector arrow/finger) · **MENU** (inventory, save select, game over, map, pause, main menu) · **BATTLE** (battle screens, JRPG-flavored — command windows, status, FFVI the model case).
Current reality: Title works and is gated; In-game is the solid default; Battle is an FFVI-only bolt-on; Menu is fragile; Decisions does not exist. Untested games leak menu/battle junk through the default path — that is the mass-release hole C1/C2 target. Safety-valve design rule: **default to in-game; route away only on strong evidence; misclassification costs a label, never a suppressed line.** C1 measured (2026-10-22): the hole is NOT a flood (universal publish-truth junk = 0/15 stamped; attempt-level 2.7%) — the real class is menu garbage, rare (~0.6% content-verified) but hits EVERY game (Zelda/FFVI/PM), and battle-shape routing would over-suppress (14 correct publishes flagged). Decision: Option 1, C2 fixes the class — C2 coded + offline-gated 2026-10-22 (single-choke menu-row hold in PublishDecisionAsync; zero-delta A/B on replay + catstats). **2026-10-22 evening: C2 CLOSED** (live outcome met — zero menu-row dialogue bubbles across a multi-menu session; hold = unengaged backstop); **T3 (title fastpath) + C3 (overlay readback guard) coded + offline-gated** after the user's live run exposed the first-visit title failure and the overlay self-readback class (root causes in BOOKS T3+C3 entry); their live gates are owed; **D1 (decisions) = next build**.

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
Line-granular scroll tracker for typewriter-scroll phases (Zelda intro proven ground): growth-tolerant lineage, longest complete-ish form fires on scroll-out/page-turn, scroll-update revises variants in place, trailing dot-family fold for identity. Verbatim otherwise, game-blind, menus/battles excluded, static normal-road lines untouched. Commits 3581ccd + 5d882f5. Done: intro 2→8 (+6 reviewed), Zelda-new +1 genuine, PM/FFVI-new zero-delta empty diffs, FFVI-old -1 suspect merge +2 genuine, Zelda-old missed 10→8 overlap 50→52 (+17 reviewed: ~14 clean, 2 wobbles, 1 double-form disclosed). Scroll-verdict provenance: all merge/junk suspects (ケアルファイア, name-mash, blog-misread, map frags) are PRE-EXISTING normal-road fires, not scroll introductions; scroll adds genuine + variant doubles only (doubles = shared sentdedup follow-up, queued). Title-logo fired live once → split to B13. Still needed: live 11/11 + PM name-entry silence.

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
### T2. Title-screen episode detector (revert: revert title-episode) — **DONE 2026-10-22 round-7**: three single posts via the episode detector (boot, post-intro, return-after-gameplay = visit semantics proven), intro 11/11, JA+EN title bubbles, all three title publishers on one shared 60-loop clock (8b4c6ec). Shipped 3558867..8b4c6ec; scope items for F2x: matcher HasJapanese gate (JA-title assumption), tracker purity 4766 (pre-existing Zelda branch). Leftover from round-2 forensics (BOOKS T2.6): ghost-inset chain-wrap + A/B folds into G1. **Next: G1**, then C1.

### B19. Cross-fire variant consolidation (prerequisite for repetition-evidence) — code complete through 2f (HEAD c082fdc), live 11/11 proof LANDED 2026-10-21 (see Z1 / CHECKPOINT-2026-10-21)
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

### P2. Dictionary experiment (Step 3) — EXECUTED 2026-10-18, concluded inconclusive-by-design
The v2 dev-only beam-diversity rule yielded 0 corrections / 0 damages across the truth set — gate safe, nothing worth shipping. Residue: 6 untracked tools/p2_*.py scripts. Side-question open: true-beam re-OCR on the ~36 mapped crops, or declare done and move to P3.

### P3. Missed-line sweep + LM-fluency gate (Steps 4–6)
Re-price singleton residue with truth set; build only what evidence convicts.

### P4. Loop payoffs (Step 9)
Re-mine and promotion review with Paper Mario data; profile tuning only on measured spread.

### P5. Decision boxes + sprites (Step 10)
Positioned boxes, Zelda sprite map, PM pointing hand — queued behind Paper Mario data.

### P6. Shop UI
FFVI battle-polish order otherwise intact.

### P7. Zelda-long latency exceedance
Pre-existing (stash-proven, not a regression). Investigate when profiling time comes.

### P8. Display-behavior queue (from ChatGPT taxonomy review, 2026-10-14)
Already covered: instant/typewriter/scroll/fixed/replacement/page/advance/menus/titles/name-entry/singleton-glimpse (B10-T2, B12, title/menu/battle paths). Queued, unmeasured: fading text, moving/animated text + transient notifications (damage numbers, pickups), per-line replacement, timed-vs-advance-controlled distinction. Each needs a priced miss set before any build.

## Open questions (restored 2026-10-15 - blocks FFVI names work until answered)
- Mash memo policy: keep stale / fast-track update / expire to empty (recommended: expire).
- Wedge/Biggs official English forms (Matthew precedent: manual wins).
- PM battle glossary EN confirmations.
- Shop-play signal for FFVI (shop UI build waits on it).
- P2 side-question: true-beam re-OCR on ~36 crops vs declare P2 done (recommended: declare done, move to P3).
- Resolution matrix picks: second 480p game; both 720p games; second 1080p game (Persona 5 is slot 1).
- Category routing depth: **DECIDED 2026-10-22 on C1 numbers — Option 1 (display tagging)** universal; routing stays evidence-triggered (no new routing architecture). Rationale: content-verified junk ≈0.6% of live publishes (no case for a universal routing layer); the strongest route signal, battle shape, false-positives at scale on non-pack layouts — 14/16 publish-truth flags were CORRECT dialogue that routing would have suppressed; the one real class (menu garbage) is rare + universal → tier-1 engine fix (C2), not category routing.
- Watch items: one crash-truncated tail gzip in corpus dir (not blocking); one orphaned PM miner file under pre-paren versioned id (negligible); exact-match metrics understate variant correctness; sim/replay never touch bitmaps, loop, or title path (live-provable only).

## SCALING DOCTRINE (standing policy, 2026-10-22, user-set)
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
forbidden without explicit user sign-off. Verification substitute for
"can't playtest every game": every engine fix runs the merged-corpus
replay A/B (all recorded games at once); unseen games have no corpora,
which is exactly why their fixes must be class-level. Zelda/FFVI fixup
accumulation = legacy bootstrapping; STOP adding new per-game fixups by
default (user directive 2026-10-22).

CONTINUOUS STRENGTHENING (user, 2026-10-22): knowledge COMPOUNDS - every
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
Audit complete + fix batch shipped (013c5bc, reverts: g1-timeouts /
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
byte-compatible with round-7. NEXT (2026-10-22 evening update): C2 live gate evaluated on the 20:25–20:35 run = **outcome MET** (zero menu-row dialogue bubbles; the hold was pre-empted by upstream guards every instance) → **C2 CLOSED**; same evening, the user's run exposed two defects whose root causes are on file in the BOOKS T3+C3 entry — the first-visit title failure (**T3** fastpath, revert `revert title-fastpath`) and four overlay self-readback "hallucinations" (**C3** run-guard, revert `revert readback-run-guard`) — both coded + offline-gated (catstats A/B, 321 audited parks), commits this file. Live gates owed: (T3) cold boot — no input needed, the title→intro transition is the game's own timer — must post the title on the first visit; (C3) zero overlay re-reads with no real dialogue missing. Build work while awaiting that play session: **D1 decisions**.
