# Truth eval set (Step 1, 2026-10-11; PM expansion 2026-11-10)

48 rows from 44 banked troubleshoot shots (`*_all.png` + game pane read
directly — never OCR output as ground truth). Fields: shot, game,
session, split, context, visible_ja (text actually on screen),
complete (fully displayed vs partial reveal), panel_ja (what the
program published), verdict, notes.

- `split`: dev = 17/18h sessions (tuning allowed), heldout = 22/23h
  (NEVER tune on it; verification only).
- Paper Mario (8 rows, 2026-11-10): sessions 13/14/21h. All dev —
  PM corpora already informed builds (G-D A/B, pack), so heldout
  would be dishonest. Covers: title (false-pub + blink-off
  true-negative), save decision-box misses (2, one with pointing-hand
  P5 evidence), dialogue hits (2), unpublished-line misses (2, corpus
  loop order verified 210008). Cold-boot title capture still owed
  (0 PM title frames in corpus).
- Partial typewriter stages labeled by visible text (complete=false);
  correct silence is a valid expectation (withheld-partial, true-negative).
- Covers: clean dialogue, typewriter partials, crawl pages, signs,
  decision boxes, game-over/menu screens, titles (JA + Latin),
  known misses, false pubs (menu garbage, prompt leak, ?-junk),
  blends, dups, stale bubbles, fidelity errors (single-char).
- Cross-game: zelda 26 / ffvi 14 / papermario 8. PM adds a third
  game but is not yet cross-game PROOF (its rows are dev-only; heldout
  verification still zelda+ffvi 22/23h).
- Usage: CER/exact-line on (visible_ja vs panel_ja) for hit/fidelity
  rows; completeness on miss rows; false-pub rate on false-pub rows.
- Revert: `git revert` (data only, no code).
