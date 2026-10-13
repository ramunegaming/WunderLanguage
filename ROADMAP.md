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

### B11. Chart-gate punctuation exemption (revert: revert chart-punct)
Kana-chart gate skips blocks carrying sentence punctuation (full/half-width); bare-kana blobs still suppress. Convicted: hiragana-heavy PM dialogue (zero kanji) ate whole screens with candidate reset. Done: PM 4→7 fires (+3 genuine: kekkou prefix+full, sonnakoto), missed 0; Zelda 16/1/4 identical; FFVI 3/0/14 identical with empty JA-set diffs. Old-corpora re-gate: FFVI-old 46/4, extra 58→60 (+1 genuine worldmap help, +1 suspect Wedge merge disclosed as next-batch residue); Zelda-old 50/60 missed 10 extra 21 matches ledger exactly. Bare-kana suppression intact (chart-suppressed still fires on bare grids); no name-entry rows exist in any corpus. Still needed: live observation + PM name-entry silence proof when encountered. Failed side-quest (reverted, no residue): ？-blend guard killed 3 legit ？-mid lines.

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

### W1. Sidecar death root cause
4 mid-session deaths, cause unlogged; forensics now dump uptime, exit code, ring tail. Still needed: a death WITH forensics, then the actual fix. Revert: revert death-forensics.

### W2. Paper Mario baseline play triage follow-ups
Intro measured (28/28 sim); battle/menu/decision coverage needs a focused session without focus loss.

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

## Blocked on user

- Mash policy a/b/c (keep stale / fast-track / expire).
- Shop-play signal. Wedge/Biggs English. PM glossary EN confirmations.
- Next-build pick after this handover.

## Watch items (not batches)

- One crash-truncated tail gzip in corpus dir (not blocking).
- One orphaned PM miner file under pre-paren versioned id (negligible).
- Exact-match metrics understate variant correctness (accounted in verdicts).
- Sim/replay never touch bitmaps, loop, or title path (live-provable only).
