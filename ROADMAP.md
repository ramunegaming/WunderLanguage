# WunderLanguage — Development Roadmap & Milestone Tracker

Statuses: **Complete** · **Needs play session** (shipped, live proof owed) · **In progress** · **Planned**.
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
### B19. Cross-fire variant consolidation (prerequisite for repetition-evidence)
A (repetition-as-evidence) was built through 5 iterations and reverted: static-page recovery works (block 4 exact) but pulls misread twins with it (帰がけて/出かけて, きわって/日ボタン — incorrect lines = regressions by the book), and every guard tried (dominance, twin-veto, net-displacement) either missed the twins or ate clean lines. Conviction: variant consolidation must come FIRST (merge variant doubles into single bubbles), repetition-evidence second. Part 1 DONE (live 2026-10-16 Zelda specimens: 900/封印は both posted, そこで identical double-post, 164-full never fired while truncated did, イグニュー ghost flushed): (1) fire-history ring (revert: revert fire-hist) — identical/folded-identical/containment/near-dupe vs last-8-fires in a 10-loop window, recorded synchronously at the Publish choke (sim+live); (2) sweep twin-park (revert: revert twin-sweep) — evidence-ordered sweep (Sightings desc) keeps max-evidence twin (weighted-sim ≥0.55, strictly-less-than; measured P1 0.58 catch / N9 0.48 miss / distinct ≤0.45), plus deterministic matcher/sweep/trim ordering (hash-randomized Dictionary iteration convicted by sim: identical inputs gave 86 vs 91 fires run-to-run; sorted snapshots now hash-identical across repeats); (3) freshest-extension (revert: revert fresh-ext) — growth-fast fires the extraction when it folded-extends the candidate (gate moved onto the fired text; self-suppression convicted by sim); (4) blank-flush corroboration (revert: revert blank-corrob) — uncorroborated held lines don't publish on blank (omission over misteaching); (5) panel drop-eq (revert: revert drop-eq) — identical/folded-equal arrivals collapse. Deterministic ConFold closure included (multi-pair chars げ×3/封×7 folded by HashSet luck; convicted by sim). Sim-verified: target run 13→9 fires (dup/900/ささ-prefix/ghost-173 gone, ここに-full single post, ゼルダ-full fires); Zelda-old 91→86 deterministic with 3 genuine recoveries (full-ささば, イケニエを使い, あっ魔法) and no live-affecting miss (Presents = live-vision-only line via untouched weak-hold; 地図/カンテラ pre-existing sim gaps; マスターソード fires as read-variant); FFVI 121→115 with identical misses (6 dup-variants parked). Killed in measurement, not shipped: flush corroboration ≥2 (legit FFVI pageturn flushes score 1 — mass-omission) and naive twin-suppression (would have kept 900-garbage over correct). RESIDUAL for part 2: ghost-row wins (138 mega-box flushed as candidate — needs region/mined promotion design with the 3 banked specimens) + A-retry behind it. Part 2a DONE (live 2026-10-17: correct ここに revised to ぽっとリー by update-neardupe): revision twin-guard (revert: revert upd-twins) — updates revise only on small wobbles (plain-sim ≥0.85, edit-distance ≤3, affix/middle shape; growth-via-containment road untouched). Measured: motivating flicker (dist 1, 0.875) still revises; twin (dist 5, 0.881) parks; blocking a revision keeps the fired text (omission-safe direction — worst case is a stale-correct bubble, never a swap-in). Sim: target 9 fires steady + 0 updates (ぽっとリー-class neither revises nor refires); Zelda-old 86→89 fires with 11→5 updates; FFVI 115→120 fires (deterministic hash ×2), 7→2 updates, missed 4→1 (remaining miss pre-existing, zero new), extra 69→71 (variant doubles now visible instead of swapped in — dup preferred over misteach). Part 2b DONE (live 2026-10-17: ここに prefix + full posted as TWO bubbles; first block はるか昔 never posted in any run): (a) completion routing (revert: revert fresh-upd) — growth-fast completions (strict folded extensions of the shown, posted prefix) revise via the update road instead of firing fresh, so typewriter lines post ONE bubble (design matches the road's documented "completion via update"; panel already order-safe both directions, absorb-growth stays as backup); parked-prefix completions still fire fresh (dangling updates revise nothing). Sim: target 9+0 becomes 8+1 (ゼルダ-full via update), today's run 8 fires + 2 updates (ここに-full and ゼルダ-full both via update, prefixes single). (b) script-row promotion (revert: revert script-promo) — sidecar assembly prefers a row matching a pack script line (same 0.92 bar as script-match) over the area pick: the はるか昔 crawl read clean 7 loops but the dots box won primary by area every time. Game-blind mechanism over evidenced per-game data (title-pack discipline); never rewrites, only selects; dots/shorts can never match a full script line (ghost max 0.59 vs 1.00 measured). Live-proof owed for (b) (extraction-level, sim-invisible). Loop2 queue: (き,ま) evidenced games=2 (いまました misread ×68: zelda 55 + ffvi 13; EN already correct via script-match, JA wobble honest) — next mining batch with zero-delta discipline, not this commit. Part 2c DONE (live 2026-10-17: block 10 posted the ghost row ありがとう/先輩 instead of the strip): ghost-inset override (revert: revert ghost-inset) — when a full-bleed box contains a disagreeing inset row, the inset row feeds the pipeline (raw level, so the winner gets the full fixup/mined chain; corpus keeps sidecar original as raw). Gates, all measured: containment + weighted<0.65 (124-twin 0.583 and 900-twin 0.58 trigger, distinct lines top ~0.51) + long outer + clear inset margin + full-bleed outer (x≤10); no battle/menu/title gates (downstream suppression owns those frames). Mirror-verified on all banked ghost loops (122-partial silent, 123/124/127 strip wins, 138 strip wins; remnants/dots/disjoint rows silent; PM date-junk resolves to content). Sim-neutral by construction (runs before the banked-extracted feed point; identical hash). Live-proof owed (the actual 11/11 run). Still needed: live proof (block 10 posts the strip), then A-retry design behind the finished B19.

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

### P2. Dictionary experiment (Step 3)
Design accepted; needs truth (banked) plus beams (banked). Pass bar: corrections ≥3× regressions, zero name damage, ≤50ms/line. Integration only on pass, separate revert.

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
- Watch items: one crash-truncated tail gzip in corpus dir (not blocking); one orphaned PM miner file under pre-paren versioned id (negligible); exact-match metrics understate variant correctness; sim/replay never touch bitmaps, loop, or title path (live-provable only).
