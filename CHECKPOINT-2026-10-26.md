# WunderLanguage — Handover Checkpoint (2026-10-26, refreshed 2026-10-29)

## Status (2026-10-29): ✅ **OFFLINE QUEUE DRAINED** — every roadmap item answerable without live play is closed. Remaining work = ONE bulk live session (checklist below) + 3 user calls.

Closed this batch (**no code changes** — build baseline 0 err / 17 warn exact stands; binary = Z2 code; app closed):
- **Z2** ✅ C2 decorated-row containment, live-evaluated (`7116265`).
- **F2x offline** ✅ — battle UI validated on the 445-corpus sweep (`recBattle=1251` / `junkBattle=1` = 0.08%); HasJapanese title generalization waits on ONE cold-boot FFVI title frame (0 in 126 corpora).
- **PM1 offline** ✅ — 5 menu fires = 2 real + 2 contaminated joins (capture-occlusion class, F1 names it); `t2_title_mine` = 0 PM title frames → same cold-boot capture; W2 battle/menu/decision coverage needs the focused no-focus-loss session.
- **P2** ✅ closed (`78a7620`) — 0/0 truth-set evidence never convicted re-OCR.
- **P3** ✅ closed no-build (`98d1eb2`) — 12/15 covered misses now fire (whole heldout crawl family); 3 residuals all no-build classes (two-box blend held-by-design with fragment posted; OCR fidelity = P2's priced-out surface; FFVI intro READ-limited); **phantom SOURCE SOLVED** (rec-beam graphics hallucination — lives in corpora as `raw` over textless map frames, `d=0/4167`; 100% d-coverage on 10-23/24/27 captures → H4 armed); LM-fluency gate **NOT** convicted; measurement bug found+fixed (corpus JSON `\uXXXX` escapes defeat raw-line scans).
- **P4** ✅ re-mine + promotion review — 144 candidates (only (…,．) 232×3 files and (…,・) 216×3 files are multi-game/non-artifact); `_global.json` 88 pairs all sane, zero phantom poisoning, PM correctly unpromoted; **measured spread = 0** (3,374 fired texts, C#-parity fold) → tuning stays ungated; gate-risk documented = orphan PM file can self-promote a single-game pair (one FILE = one vote) → folds into the awaiting-go ResolveGameId id fix.
- **P5/P7/P8 priced** — P5 no build (PM decision **0/52 decHit**; zero covered misses); P7 numbers recorded (Zelda frame p99 = 12.1s vs 3.4–9.9s peers, but tail = time outside det+rec, FFVI rec max exceeds Zelda's → no Zelda-specific stage regression; stays with the profiling session); P8 all four classes = empty or unmeasurable miss sets (numeric-only transient class: 105/105 dead, **0 published**; fade/glimpse unpriceable offline from garble; per-line replacement ≈ normal dialogue advance; timed-vs-advance unmeasurable at ~1s OCR cadence).
- **Open questions** — Wedge/Biggs **RESOLVED → "Biggs"/"Wedge"** (2026-10-09 precedent: manual differs from Woolsey, manual wins — Vicks IS Woolsey); mash-memo recommendation firmed = **expire to empty** (9 junk-prone pairs from the DeepL-fallback window; user go pending); P2 side-question closed; category routing DECIDED (C1 numbers); R2/R3/R4 picks + shop-play signal still user-side.

---

## Status (2026-10-27): ✅ **H1–H5 PHANTOM BATCH CODED + OFFLINE-GATED** — phantom posts recurred with F1 occlusion-hold *silent*; forensics proved a **second cause**: the rec-beam hallucinates fluent text from **game graphics** (cls classical-proposer blocks rotating at fixed rect (136,234,593,168) while real dialogue is on screen). Five universal fixes shipped + gated: H1 frame-level ja block-sanity veto, H2 sticky source confirmation, H3 position-based hold-decay, H4 decode disagreement (div>0.25), H5 per-publish frame dumps. Gates: build both sides 0 err/17 warn exact; catstats A/B 445 corpora — **all known phantoms killed** (そう思う 1→0, そういえば 3→0, そうで 2→0, web 1→0, 今後は、おまえ 56→1, 時時時間終わり 39→2, このメニュー 6→0), **+166 real salvage fires gained** (朝までには戻ってくる +14, ブーメラン +10, 魔法の力 +8, MAP +4 …), net −131 salvages = junk killed with every real-looking loss attributed to pre-existing gates / re-routing; new-gate real cost all-history = 4 clean lines + stutter variants at decode-disagree, 2 unconfirmed-source. Full numbers: BOOKS tail "H-PHANTOM ROOT CAUSE (2026-10-27)" + ROADMAP tail entry. **Remaining: LIVE gate only.**

---

## Status: ✅ **F1–F4 BATCH CODED + OFFLINE-GATED (2026-10-26)** — post-run forensics on the 2026-10-24 10:29–10:43 live run (55 fires, "best run yet" per user) closed all three complaint classes **with proof**, then fixed each:

1. **Phantom posts = capture occlusion, PROVEN.** `WindowCapture.CaptureWindowRegion` hot path BitBlts the *screen* DC (`GetDC(NULL)` SRCCOPY) at the game rect — a persistent **foreign window** (webpage/video; cross-game Zelda+FFVI, recurring since ≥2026-10-07: `Ｗｅｂサイト`, `ファンディングサービス`, `１日２０１８年１１月１１日`) covered it for ~45 s at 10:39–10:40 while the focus gate held emulator. Refuted: OCR hallucination (phantoms = `raw==extracted`, `mined=false`, mined fired once = legit fixup), panel echo (`_history` in-memory only, first_ocr always first), rec-beam (real text has identical suspect/divmax). Frame census: 405 `[blocks]` frames, 0 mixed real/phantom. **Fix F1** = `WindowCapture.IsCapturedRegionOccluded` (EnumWindows top-down Z-order, stop at target; skip invisible/iconic/DWM-cloaked/`same-PID`; overlap ≥ max(4096, area/50) of the exact capture rect; false if target unseen) + controller gate after focus-release mirroring the focus hold (occluder handle+**title named in log**, %20 throttle, refocus-style hash reset on clear). → `revert occlusion-hold`
2. **'!' miss = the run's ONLY silent death.** `chart-suppressed (kana input grid)` at loop 717 cleared the held wanted-poster line `おたずお者…。！` (sc=1) before the tail's salvage snapshot and returned → no `[loss]`, no salvage. **Fix F3** = capture `chartPrior` inside the chart branch, judge via `TrySalvageStarved` right after the clear. → `revert chart-clear-salvage`
3. **Challenger class** = 30 OCR texts never became candidateText (23 non-junk) → no road, no ledger. **Fix F2** = tail tracks the latest qualifying non-candidate read (`_challText/_challSc/_challTried`); death judged through `TrySalvageStarved` once per distinct string (skip dead==salvPrior/candidate; menu/title excluded at seed). → `revert challenger-salvage`
4. **Gambler DECISION** died at weak `question-cap-20`: 3 substantial prompt rows joined = 43 chars, single `はい` option (strong needs ≥2; `いいえ` row never OCR'd); `blocks=2` was NOT the blocker (5285 bypasses on decHit). **Fix F4** = `promptRows = qparts.Count`; multiPrompt (≥2 rows + ≥1 judgeable option) → `qCap = max(tier, 20+20*(rows-1))` = 60; single-row shapes keep exact 20/12; oCap only moves with strongCluster. → `revert question-cap-scale`

**Gates passed (2026-10-26):** build **both sides 0 err / 17 warn exact** (base = HEAD `ccc5291` stashed, new restored hash-verified); **catstats A/B 443 corpora both sides** (record-side identical; `simFires 4196→4310` +114; `simUpdates` 489=489; `[loss]` +125 SALVAGE-publish, +510 already-fired labels (parked dupes, double-fire impossible), +33 already-shown; `decHit 161→182` +21; 103 gained salvage texts = 102 clean/1 junk-like; the 8 "lost-from-SALVAGE" verified re-routed — 3 of 4 sampled show `already-fired` in the new log = still publish; ingame−4/transient+4 = F4 classifying frames as menu/decision; zero corpora lost). Artifacts: `Temp\opencode\catstats_base.log` (192,183 ln) / `catstats_new.log` (194,422 ln) / `catstats_base_prevbatch.log`; forensic scripts `Temp\opencode\phantom_*.py`, `ab_compare.py`; source backup `Temp\opencode\fixbatch_f1f4\`.

> **Fresh chat?** Read ROADMAP `## HANDOVER STATE (2026-10-26)` (pointer at top), then ROADMAP tail entry "2026-10-26 - F1-F4 occlusion/miss/decision batch", then BOOKS tail entry (full root-cause chains + A/B numbers). Also read ROADMAP "SCALING DOCTRINE" (standing policy: universal fixes, BOOKS path for every knowledge item, revert phrases, label over omission, every block posts).

---

## BULK LIVE SESSION CHECKLIST (consolidated 2026-10-29 — the ONE remaining gate)

### A. User calls (can precede the session)
1. **R2/R3/R4 resolution-matrix picks**: second 480p game; both 720p games; second 1080p game (Persona 5 = slot 1) → unblocks next-game onboarding.
2. **Mash memo go**: recommended = **expire to empty** (wipe the 9 pairs in `%APPDATA%\WunderLanguage\memo\ffvi.json`; pure cache, relearns live, cap 200; keeping them risks serving DeepL-fallback junk in a future offline window).
3. ~~**Z2 5-box build call**~~ → **DONE 2026-10-29, commit `efb70e8`** (revert: `revert z2-5box-view`): 5-box inventory view built inside the standard outline — pack-constant chrome, item box live-refreshes, EN + residual strips below the grid; build 0 err/17 warn exact, catstats A/B zero content delta. **Live verify = B6.**

### B. What to play (capture requirements)
1. **Cold-boot FFVI, no input**: title must post on the FIRST visit (T3 fastpath — title→intro is the game's own auto-advance timer, not user-driven); return visits = exactly one post per visit. **Shoot the title frame before it auto-advances** = the one missing FFVI title capture (F2x HasJapanese waits on it: 0 FFVI title frames in 126 corpora).
2. **Cold-boot PM**: PM-logo TITLE post (N2) + a title frame for `t2_title_mine` (0 PM title frames so far) — same capture serves both.
3. **FFVI wrap**: intro all blocks, first town, battle UI (offline-validated), **inventory/menu session (still owed)**, and **enter a shop** (P6 shop-play signal = the shop-UI build gate).
4. **PM focused session** — no alt-tab, no focus loss, user plays, our Shot button: N1–N6 proofs = `[memo] saved/loaded` lines (N1), mid-game TITLE silence + TITLE-then-prompt on PM (N2), PM routing line + banking IDs + memo/vocab activation + intro latency numbers (N4), undoubled Dolphin ROM ID + normalized line (N5), shot labels with pack prefixes (N6); plus **W2 battle/menu/decision coverage** (decision re-probe: PM currently 0/52 decHit) and **PM battle glossary EN confirmations** (user reads the EN names off screen).
5. **Zelda**: B16b screenshot proof = single bubble on typewriter + `absorb-*` lines in the log.
6. **Z2 5-box live verify**: open the inventory in-game — outline present, 5 boxes in mockup geometry, item box updates on switch (ブーメラン → レンタン etc.), chrome stays put, TRANSLATION strip shows the item EN; ordinary menus (shop/dialogue) must NOT route to the 5-box layout. Screenshot for the record.

### C. Log / acceptance checks (copy `app.log` FIRST — it wipes per launch)
1. **H1–H5 phantom batch**: zero phantom posts while roaming (foreign/web text never in the feed — this is also the graphics-hallucination proof); any covering window gets named: `[loop N] occlusion-hold: 0x… "<window title>" composited over the game, holding`. Artifacts must appear: `[blocks] block-reject vetoed` on graphics frames, `[loss] starved … discard reason=unconfirmed-source` / `reason=decode-disagree`, `STABLE-DECISION=hold-decayed`, `publish-dumps\pub_*.png` + `index.txt` entries (ring 80).
2. **Phantom recurrence probes (P3 strings)**: `外国よりうつわ…`, `外側に何かようかのう…` on textless map/overworld frames must stay **dead** — 100% d-coverage since 10-23 means H4 is armed; a fire carrying `d=0/4167` = structurally-open recurrence (report it, do not tune).
3. **C3**: zero overlay self-re-reads (`[publish] history-parked` lines carry the class) **with no real dialogue missing**.
4. **F1–F4**: punctuation lines post (`SALVAGE-publish "…"` instead of silence at chart moments); gambler box = DECISION (4 rows incl はい, no `question-cap-20`), no double-post; every never-candidate text death leaves a `[loss]` verdict (challenger visibility); occlusion-hold line names any window sitting over the game.
5. **CAT reliability + W1 soak**: normal translation for the whole session, no silent dead sessions (restart backoff lines + terminal notice if CAT exhausts), zero 503 gaps.
6. **B18**: a churned line stabilizes faster (confusion-weighted joins observed live).
7. **N2 title silence + JP-first**: mid-game titles never post; JA within ~1 s of every line, EN refreshes the same bubble in place.

### D. Post-session
1. Copy `%APPDATA%\WunderLanguage\app.log` immediately; harvest the session's corpus (validate before any re-tuning — never harvest an unvalidated corpus).
2. Harvest proofs → close N1–N6, W2, B18, B16b, T3, C3, F1–F4, H1–H5, W1 acceptance; fold R2/R3/R4 picks + P6 shop signal into the next batch; then next-game onboarding per the scaling doctrine (floor first).

Everything else is closed offline — do not reopen a closed item without new evidence.

## Standing mechanics (do not regress)

- **Taskkill-first rebuilds**: user closes app first, confirm via `tasklist` before build.
- **catstats**: `WunderLanguage.Host.exe --catstats "<%APPDATA% corpus dir>"` via `Start-Process -PassThru; $p.WaitForExit()`; app.log wipes per launch — **copy immediately** to `Temp\opencode\catstats_<side>.log`.
- **Console mangles Japanese**: Python via script files, never `python -c` inline; PowerShell forbids `&` chaining; coordinate clicks/screenshots on the live desktop are BANNED.
- **Ledgers append-only**: BOOKS.md + ROADMAP.md; commits carry every revert phrase.
- **Revert phrases for this batch**: `revert z2-5box-view`, `revert block-sanity`, `revert source-confirm`, `revert hold-decay`, `revert decode-agree`, `revert publish-dump` (F1-F4: `revert occlusion-hold`, `revert challenger-salvage`, `revert chart-clear-salvage`, `revert question-cap-scale`; prior batches: `revert salvage-starved`, `revert dwell-sight1`, `revert kana-bar10`, `revert dec-probe`, `revert decision-echo-split`, `revert jp-first`, `revert d1-decisions`, `revert decision-tiering`, `revert decision-sublines`, `revert error-codes`, … full list in BOOKS).

## Key files

- `src/WunderLanguage.Host/Capture/WindowCapture.cs` — F1 `IsCapturedRegionOccluded` + `GetWindowThreadProcessId` + `DWMWA_CLOAKED` P/Invokes
- `src/WunderLanguage.Host/Capture/CaptureController.cs` — F1 loop gate (after focus-release), F2 fields (~line 235) + tail challenger block, F3 chart-branch salvage, F4 `promptRows`/`multiPrompt` in `TryBuildDecisionRows`
- `C:\Users\RamuneGaming\AppData\Local\Temp\opencode\app_live_2026-10-24b.log` — primary forensic artifact (7,032 lines, 55 fires)
- Corpus of the run: `%APPDATA%\WunderLanguage\corpus\Zelda_no_Densetsu_(V1.0)_(J)_-_Snes9x_1.60_20261024_102851.jsonl.gz` (483 records)
