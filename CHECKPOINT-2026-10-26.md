# WunderLanguage — Handover Checkpoint (2026-10-26)

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

## NEXT: LIVE GATE (the only remaining gate — needs the user)

User reopens the **emulator + host app** (both closed, PC restarted), plays, and checks:

1. **No phantom posts while roaming** (H-batch: this is now the proof that the **graphics-hallucination** path is dead — the 10-27/10-24b phantoms posted *without* any occlusion). Foreign/webpage text must never appear in the feed. If a window covers the game, the log still names it: `[loop N] occlusion-hold: 0x… "<window title>" composited over the game, holding` (then `occlusion-cleared …`) — an occlusion-hold line means F1 caught it; **silence + zero phantoms means H1–H4 did**.
1b. **H1–H5 artifacts present (Diag on)** — `[blocks] block-reject vetoed` lines on graphics frames; `[loss] starved … discard reason=unconfirmed-source` / `reason=decode-disagree` when a hallucination tries to publish; `STABLE-DECISION=hold-decayed` on stale rects; `publish-dumps\pub_*.png` + `index.txt` entries appear per publish (ring 80).
2. **Punctuation lines post** — '!' / '?' / '…' / digit lines that went missing; chart-gate moments should show `[loss] starved sc=… SALVAGE-publish "…"` instead of silence.
3. **Gambler box = DECISION** — the 3-row prompt + はい should tag DECISION (dec-probe no longer `question-cap-20` on it), not a merged IN-GAME TEXT; no double-post (menuSignature suppresses the dwell road).
4. **Challenger visibility** — every never-candidate text death now leaves a `[loss]` verdict line (already-fired/already-shown/anchorless/continuation/… or SALVAGE-publish).
5. **JP-first unchanged** — JA within ~1 s of every line, EN refreshes the same bubble in place.

## Standing mechanics (do not regress)

- **Taskkill-first rebuilds**: user closes app first, confirm via `tasklist` before build.
- **catstats**: `WunderLanguage.Host.exe --catstats "<%APPDATA% corpus dir>"` via `Start-Process -PassThru; $p.WaitForExit()`; app.log wipes per launch — **copy immediately** to `Temp\opencode\catstats_<side>.log`.
- **Console mangles Japanese**: Python via script files, never `python -c` inline; PowerShell forbids `&` chaining; coordinate clicks/screenshots on the live desktop are BANNED.
- **Ledgers append-only**: BOOKS.md + ROADMAP.md; commits carry every revert phrase.
- **Revert phrases for this batch**: `revert block-sanity`, `revert source-confirm`, `revert hold-decay`, `revert decode-agree`, `revert publish-dump` (F1-F4: `revert occlusion-hold`, `revert challenger-salvage`, `revert chart-clear-salvage`, `revert question-cap-scale`; prior batches: `revert salvage-starved`, `revert dwell-sight1`, `revert kana-bar10`, `revert dec-probe`, `revert decision-echo-split`, `revert jp-first`, `revert d1-decisions`, `revert decision-tiering`, `revert decision-sublines`, `revert error-codes`, … full list in BOOKS).

## Key files

- `src/WunderLanguage.Host/Capture/WindowCapture.cs` — F1 `IsCapturedRegionOccluded` + `GetWindowThreadProcessId` + `DWMWA_CLOAKED` P/Invokes
- `src/WunderLanguage.Host/Capture/CaptureController.cs` — F1 loop gate (after focus-release), F2 fields (~line 235) + tail challenger block, F3 chart-branch salvage, F4 `promptRows`/`multiPrompt` in `TryBuildDecisionRows`
- `C:\Users\RamuneGaming\AppData\Local\Temp\opencode\app_live_2026-10-24b.log` — primary forensic artifact (7,032 lines, 55 fires)
- Corpus of the run: `%APPDATA%\WunderLanguage\corpus\Zelda_no_Densetsu_(V1.0)_(J)_-_Snes9x_1.60_20261024_102851.jsonl.gz` (483 records)
