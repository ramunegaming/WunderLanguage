# WunderLanguage — Development Roadmap & Milestone Tracker

Statuses: **Complete** · **Needs play session** (shipped, live proof owed) · **In progress** · **Planned** · **Blocked on user**.
Note: pre-bottleneck milestone definitions (M1–M7) predate this ledger — only M7b/M7c slices are recorded. This tracker supersedes them with the bottleneck/loop/step structure. Revert phrases with each shipped batch.

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
Pack FullTitle matched by containment/truncation/0.85 parks scroll fires for the title path (game-blind mechanism, evidenced per-game data). Caught a real chain bug on the way in (park flag fell through to fire — fixed, verified). Done: live intro title gone, 6/6 genuine hold, missed +1 = the withheld title itself (correct miss). Still needed: live title-silence confirmation.

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

### B18. Confusion-tolerant counting (revert: revert fuzzy-weighted)
Tier1 lineage counting accepts confusion-weighted similarity (inventoried pairs join instead of resetting). Counting only. Done: 48 genuine joins measured offline, empty JA diffs on all 6 corpora. Still needed: live observation (a churned line stabilizing faster).

### W1. Sidecar death root cause — CONVICTED + FIXED (revert: revert nocatfb-retry)
CAT 4-bit quantize load segfaults ~140s after start (2 live deaths + 1 idle repro, 0xC0000005 at shard 0-1, despite 4492MB free — threshold tuning cannot catch it). Explains PM 4-death cadence (restart+120s cycles) and both intro block-10 gaps. Fix: healthy-period death retries OCR-only; startup deaths unchanged; fresh launches always try GPU. Verified by kill-test (nocatfb → healthy, tail intact). Still needed: live soak (a full session with zero 503 gaps).

### W2. Paper Mario baseline play triage follow-ups
Intro measured (28/28 sim); battle/menu/decision coverage needs a focused session without focus loss.

### B16. One-bubble growth merge (revert: revert growth-merge)
ShowTranslation revises the shown bubble when a fire strictly extends it within 4s (display-only; capture/translation/sim untouched). Wobbly completions arrive via the exact-match update path. Still needed: live screenshot proof (single revised bubble on typewriter + [panel] growth-merge line).

## Planned

### P2. Dictionary experiment (Step 3)
Design accepted; needs truth (banked) plus beams (banked). Pass bar: corrections ≥3× regressions, zero name damage, ≤50ms/line. Integration only on pass, separate revert.

### P3. Missed-line sweep + LM-fluency gate (Steps 4–6)
Re-price singleton residue with truth set; build only what evidence convicts.

### P4. Loop payoffs (Step 9)
Re-mine and promotion review with Paper Mario data; profile tuning only on measured spread.

### P5. Decision boxes + sprites (Step 10)
Positioned boxes, Zelda sprite map, PM pointing hand — queued behind Paper Mario data.

### P6. Shop UI
Waits on user shop-play signal. FFVI battle-polish order otherwise intact.

### P7. Zelda-long latency exceedance
Pre-existing (stash-proven, not a regression). Investigate when profiling time comes.

### P8. Display-behavior queue (from ChatGPT taxonomy review, 2026-10-14)
Already covered: instant/typewriter/scroll/fixed/replacement/page/advance/menus/titles/name-entry/singleton-glimpse (B10-T2, B12, title/menu/battle paths). Queued, unmeasured: fading text, moving/animated text + transient notifications (damage numbers, pickups), per-line replacement, timed-vs-advance-controlled distinction. Each needs a priced miss set before any build.

## Blocked on user

- Mash policy a/b/c (keep stale / fast-track / expire).
- Shop-play signal. Wedge/Biggs English. PM glossary EN confirmations.
- Next-build pick after this handover.

## Watch items (not batches)

- One crash-truncated tail gzip in corpus dir (not blocking).
- One orphaned PM miner file under pre-paren versioned id (negligible).
- Exact-match metrics understate variant correctness (accounted in verdicts).
- Sim/replay never touch bitmaps, loop, or title path (live-provable only).
