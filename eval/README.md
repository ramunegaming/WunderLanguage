# Truth eval set v1 (Step 1, 2026-10-11)

40 rows from 36 banked troubleshoot shots (`*_all.png` + game pane read
directly — never OCR output as ground truth). Fields: shot, game,
session, split, context, visible_ja (text actually on screen),
complete (fully displayed vs partial reveal), panel_ja (what the
program published), verdict, notes.

- `split`: dev = 17/18h sessions (tuning allowed), heldout = 22/23h
  (NEVER tune on it; verification only).
- Partial typewriter stages labeled by visible text (complete=false);
  correct silence is a valid expectation (withheld-partial, true-negative).
- Covers: clean dialogue, typewriter partials, crawl pages, signs,
  decision boxes, game-over/menu screens, titles (JA + Latin),
  known misses, false pubs (menu garbage, prompt leak, ?-junk),
  blends, dups, stale bubbles, fidelity errors (single-char).
- Initial set, NOT cross-game proof (no Paper Mario yet).
- Usage: CER/exact-line on (visible_ja vs panel_ja) for hit/fidelity
  rows; completeness on miss rows; false-pub rate on false-pub rows.
- Revert: `git revert` (data only, no code).
