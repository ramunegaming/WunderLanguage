# Paper Mario — latest sessions handover (2026-10-12/13)

Read with ROADMAP.md (statuses) and the design PDF (full transfer).

## Game
Paper Mario: The Thousand-Year Door, GameCube, Dolphin (defaults per policy), ROM id G8MJ01. Pack `papermario` exists (v1, evidenced-only). Hiragana-heavy text, small menu fonts, decision hand-cursor sprite, stylized logo/curved text.

## Session 1 (10/12, first contact)
Intro movie x2, town, crash at first battle. 1528 loops / 343 reads / 26 fires (ALL intro, growth-fast carried it). Gameplay silence = focus holds (screenshotting) + static skips + crash — not policy. Sim reproduces 26/26. Loop 1 proven live (`[miner] global promoted 70/44`).

## Session 2 clean (10/13, the diagnosis session)
485 reads, 28 fires, sim 28/28 zero extras. Peach letter posted (loop 678), then sidecar died 4x (00:24/26/29/~00:31), restart budget spent, 30 silent minutes. holds=0 (clean discipline). Memory flat (not OOM); cause unlogged. Dims 788x583. pack=universal. Beams banked 314/343. ROM-ID live in doubled form (paren fix shipped after). No TITLE/memo/vocab activity (correct under universal).

## What shipped since (all gated, committed)
Paren fix, engine-death panel notice, death forensics (uptime/exitcode/ring tail), PM pack v1, shot session labels, memo save-on-Stop + sim hermeticity.

## Owed live proofs (next session)
`[memo] saved`, undoubled ROM-ID + normalized line, TITLE-then-prompt, engine notice/forensics paths, PM pack routing + banking + logo TITLE.

## Attached screenshots (in-chat, 13)
Title logo + press-start + credits; はい/いいえ hand cursor; プロローグ stylized + circular medallion text; ゴロツキタウン banner; menu/stats/item/map screens; ジャンプ battle bubble.

## Next
Focused battle/menu/decision session (no alt-tab; our Shot button; pause to note). Then: dictionary experiment (truth + beams banked) vs death-cause findings vs pack cross-game check.
