# CHECKPOINT 2026-11-13 — UX wave batches 8/9/10 (8+9 LIVE-CONFIRMED, 10 live-owed)

Status: the live-session UX bug wave is fully committed through batch 10.
Batches 8 and 9 confirmed in live gameplay (user 2026-11-13). Batch 10 is
offline-complete — its live check rides the next session. Ledgers
(ROADMAP.md / BOOKS.md) updated accordingly in this session's commit.
NEXT = Phase 0 onboarding for the five R2-R4 games, then the
beta/alpha release build.

## Shipped this session (three commits)

### 3c3401d — batch 8 taskbar-feed (revert: `revert taskbar-feed`) — LIVE PASS
- `SidePanelWindow.xaml.cs`: `CloseShouldExit => _detached` (:169),
  `SetDetached` flips `Title="WunderLanguage"` FIRST then
  `ShowInTaskbar=true` (redock restores byte-identical: flag false +
  empty title keeps the window-picker empty-title skip intact); gate
  `RunTaskbarFeedOfflineTest` (:456).
- `AppShell.cs`: `_sidePanel.Closed` handler →
  `Application.Current.Shutdown()` when `CloseShouldExit` (:220).
- `App.xaml.cs`: `--taskbarfeed` block (:157-178).
- Root-cause chain when the first gate run failed `onlyBitDiffers`:
  stage trace proved the flip adds EXACTLY `WS_EX_APPWINDOW`
  (`0x8080000→0x80C0000`); stray `0x100` = batch-6's
  `ResizeMode.CanResize` `WS_EX_WINDOWEDGE` (production `0x8080108`
  redock read predates this batch). Gate now asserts exact state
  `pre|APPWINDOW|WINDOWEDGE` + in-gate ResizeMode provenance control.
- LIVE: user confirmed the whole combined checklist ("seems to work
  fine great").

### 066cf98 — batch 9 auto-popout (revert: `revert auto-popout`) — LIVE PASS
- `AppShell.cs`: policy `AutoDetachForCoverage` (:976, pure), flag
  `NextAutoFlag` (:987, pure), `_autoPopped` (:929), coverage-edge block
  in `RefreshDockAffordance` (:954), `SetDetached(+auto)` (:1101), gate
  `RunAutoPopoutOfflineTest` (:1005: truth table 6/6, flag table 4/4,
  sequences S1–S4). Auto moves `persist:false`.
- `App.xaml.cs`: `--autopopout` block (:194).
- LIVE: windowed→fullscreen auto-popped within a tick, back to
  windowed auto-redocked — user explicit.

### 0a4808a — batch 10 popout-anchor (revert: `revert popout-anchor`) — LIVE OWED
- BUG: game already fullscreen → start WunderLanguage → attach → set
  region → feed invisible (players launch straight into fullscreen).
- ROOT CAUSE proven from the user's mirrored log `app2.log` 12:35:44:
  1. `OnBoundsChanged` anchored the DOCKED panel to the fullscreen
     rect: `actual sidePanel rect: (L2557,T-77,...)` — DELL/ASUS seam +
     off-screen top (`0 − 80 + 3`).
  2. 66 ms later the batch-9 coverage edge auto-popped INHERITING that
     anchor: `feed DETACHED at (2045.6,-61.6)` = phys (2557,−77).
  3. The working cycle (12:36:11) popped from a windowed anchor
     (2050,59) — why windowed-first worked.
- Fix: `ClampFeedRect` (pure full-containment clamp; coverage-aware
  generalization of batch-4's `ClampToVirtualScreen`, which stays the
  restore-path guard) + `AnchorFeedOnPop` called from
  `SetDetached(true)` after `ApplyStoredFeedSize()` — covered → fully
  inside the game monitor, else → raw `GetVirtualScreenRect()`; raw
  USER32 space throughout (`TryGetUserRect` = same coordinates as
  `SetWindowPos`). `SidePanelWindow.Pump` widened `private` →
  `internal`.
- PMv2 DPI-SEAM proof (from the gate's first failure, not assumed): a
  single-pass clamp moved the window across this box's 125%/100% seam
  and WPF re-applied its DIP cache → SIZE changed after the clamp
  (304 px → 380 px). Position survives; size does not → `AnchorFeedOnPop`
  is a convergence loop (clamp → move → `Pump(60)` → re-read, max 3,
  identity breaks, non-convergence logged).
- Gate `--popanchor` (real HWND, contract assertions — final rect ⊆
  target, top-0 for the repro, position-identity for the validated
  windowed spot): all 6 rows ok, exit 0.

## Evidence, all three batches
- Build 0 err / 17 warn = baseline.
- Gates exit 0: popanchor, autopopout, taskbarfeed, dockvis, feedresize,
  feeddefer, captureswap, calibcover, hdrtest; errortest PASS 7/7
  (taskkill harness — `--errortest` never exits by design,
  App.xaml.cs:553).
- catstats A/B vs `i5_catstats_H`: PER-CORPUS IDENTICAL each batch
  (466 common, 0 changed → zero dialogue losses; +3 Big_Walk corpora =
  the user's own live sessions, pre-existing on disk).

## Live-owed
1. Batch 10: launch game fullscreen FIRST → start WunderLanguage →
   attach → set region → transparent popout appears on the game screen
   (`popout-anchor:` + `auto-popout:` lines in the log).
2. Batches 2/3 HDR proofs (no HDR on this box — trust until a friend
   tests).
3. CHECKPOINT-2026-11-09.md issue-1 live gate (unchanged, still owed).

## NEXT (user's stated order)
1. **Phase 0 onboarding, 5 games**: Okami / NieR Replicant / KH3D /
   Persona 5 / Yakuza 0 — 10–15 min capture each, logger armed
   (`keep-log.armed` ✓), Phase 1 corpus+catstats+fire audit → Phase 2
   pack checklist → Phase 3 truth rows+replay A/B → Phase 4 live proof →
   ledger. Needs the user at the controls, ideally one sitting.
2. **Beta/alpha release build.**
3. GitHub backup pushed this session (repo
   https://github.com/ramunegaming/WunderLanguage).

## Standing rules (unchanged)
- Evidence first; root causes proven, not assumed; one change per batch
  with a revert phrase; extend existing machinery, never a parallel
  gate; report after each step; no model training; ledgers only after
  live confirmation.
- NOT NOW: fine-tuning, dictionary rescoring, crop-collage batching.
- Phantom d=0 fires (issue 1) = report only, never tune.
- Logger keeper armed at `%APPDATA%\WunderLanguage\keep-log.armed`.
- Offline work may proceed without a live-gate ping (standing
  permission).
