# WunderLanguage — What The Books Taught Us

> DEV-ONLY: exclude from the release/packaging manifest. Internal notes,
> not user documentation.

Reading log + applied lessons from every volume studied in this project,
kept so future sessions (or future contributors) don't re-derive them.
Rule governing all of it: books are read for applicable concepts only;
nothing is implemented without a cited failure or a measured win.

Project-wide disciplines that came out of the reading:
- Panel proof over compile proof; new drift needs cited truth (log/panel/screenshot).
- Corpora under `%AppData%\WunderLanguage\corpus\` are never rewritten.
- One thing at a time; every batch ships a revert phrase.
- Governing principles (user, 2026-10-08 — gate every change):
  1. Scalability: untested future games playable with zero personal
     intervention. Pack-scoped or game-blind work only.
  2. Reliability: accurate reads, accurate translations, posts landing
     with the on-screen text (latency is correctness), every screen read
     as what it is (menus, battle UI, decisions, titles). Omission over
     misteaching, always.
  3. Adaptability: survives setup variance — machines, ROMs, games,
     programs. Degrades gracefully, never assumes the dev machine.
- No finetuning on live outputs ever (circularity + bias feedback);
  adaptation lives in the symbolic layer (packs, memos, caches, thresholds).

---

## 1. Systems Performance (Gregg) — 12 chapters

The observability book. Nearly everything in `PERFORMANCE.md` traces to it.

**Applied:**
- Baseline statistics + latency breakdown per stage: `[loop-cost]`
  (cap/det/rec/pub), `[startup]` staged markers, per-minute pressure line.
- Observer-effect audit: one `FileLogger.Log` costs ~0.2 ms here
  (`[log-cost]` probe at startup) — observability must not be the tax.
- Bottleneck-first discipline: Defender exclusions for venv + HF cache
  (45.8 s → ~14 s startup, the single biggest win ever), CAT concurrency
  cap 4 (cold-row publish p99 8 s → 1.2 s), VRAM pre-check with `nocatfb`
  fallback instead of the bitsandbytes 4-bit GPU segfault, BelowNormal
  sidecar (A/B: no harm), staged readiness (usable at OCR-ready, CAT warms
  behind with a log-only watcher).
- Caches made visible: translation cache + sticky/prefetch/sim hit lines,
  prefetch key alignment, mined corrections, corpus `.jsonl.gz` (15×
  smaller, never delete history), diagnostics mode gating dev artifacts.
- SLOs from measured instruments (capture p99 < 50 ms, OCR round-trip
  p99 < 2 s, publish p99 < 5 s, startup < 90 s, 100% crash-free launches).
- Refuted with data, not opinion: upscale (no win), Debug≈Release
  (1.1 vs 1.2 s, identical replay counts — Debug stays daily driver until
  packaging), FileLogger acquitted (0.188 ms), resolution both directions
  (0.43×–2.16× size-proof), PNG-split timing (25 ms), reboot-cold split
  (exclusions did ~everything), micro-hygiene (127.0.0.1, shared HTTP
  clients, TEMP-DIAG gating).

**Parked:** nothing — this book is fully spent. Revisit only if an SLO
breaches (a breach is a bug report with a number attached).

## 2. Vision volume — 16 chapters

The architecture book for the overlay pipeline.

**Applied:**
- Box confidence v0 (geometric prior + multiplicity plumbed, never read
  until distributions validate it).
- Classical box proposer as fallback, learning-mode union only (proven on
  synthetic + Zelda-live frames).
- Calibration fields on `IPack` + wired threshold gate (first nonzero
  `BoxScoreThreshold` proves the gate).
- Temporal diff signal on settled frames (classify-only; distributions
  print during gameplay, nothing reads them yet).
- Score-weighted NMS, highest-confidence wins overlaps (a refactor bug in
  it was caught by the synth test — the harness earning its keep).
- Augmentation spec for the synthetic regression frames
  (photometric-only, deterministic).

**Standing rule from this book:** ambiguous shapes stay blank; single
sightings held, never shipped. Covariate shift is the default suspicion
for every accuracy failure (see §4 collage verdict).

## 3. ML Systems (Huyen-style, 7 chapters)

The data-discipline book.

**Applied:**
- Session input fingerprint at Stop (shift-detection input across
  sessions/games) — saved by the teardown fix (Stop now runs on close;
  previously session `.gz` files were silently truncated).
- Per-game tags on loop-cost and cache-hit lines; fixup audit (4 duplicate
  rows pruned, replay parity held).
- Corpus discipline: compress, never delete; translation cache +
  mined corrections always on; adaptation in the symbolic layer.

## 4. PyTorch / CNN-in-practice volume — 8 chapters

Consumed via frameworks, not written by us — correctly so.

**Applied, once:** the crop-batching experiment (2.7× faster on paper,
accuracy pending real frames) → graduated into the collage test below.
Everything else (training loops, optimizers, architectures) is N/A:
we ship no trained weights, only quantized downloads.

## 5. PMPP (GPU parallel computing) — 8 chapters

The understand-the-GPU book. Usable content was small but load-bearing.

**Applied:**
- Heterogeneous host/device model → our host/sidecar split mirrors it.
- PCIe/transfer math → closed the transport question (0.2 ms measured,
  not our 50 ms).
- Scheduling/occupancy/bottleneck-first → motivated the collage test.
- Streams explain GPU time-slicing (affirms the CAT concurrency gate).
- Quantization discipline via degenerate guards (the `nocatfb` reflex).
- Device-name logging covers the device-query lesson.

**Rejected with data — crop collaging (`25c7b64`):** batching N crops
into one recognition call is 2–7× faster (launch/transfer overhead
dominates small calls) but destroys accuracy on real frames — stacked
layout + gutter breaks the manga-page-trained model (merged lines,
altered dates, total collapse to `．．．` on one frame). Speed at the
cost of readings violates project rules. Closed unless a
layout-preserving batching design appears (none on the table).
No code changed anywhere in the experiment — pure measurement.

**Genuinely N/A:** kernel-authoring chapters (grids, convolution, stencil,
histogram, reduction, scan, sort, sparse — PyTorch + cuDNN own that
layer, we write no kernels), MPI/cluster chapters (single laptop),
unified memory (no play at our level), streams-as-API (no custom streams).

**Net of the book:** understand the GPU (ramp, gate, collage verdict all
rest on it), measure the bottleneck first (standing discipline), one
experiment (run, rejected).

## 6. Windows Internals Parts 1+2 — the survival program

Goal: survive the insane variety of Windows machines/users. Six items,
each implemented as one small batch, each live-proven, each with a
revert phrase. Commits `9bd5c5d` through `0e3fe9f` (+`15d70c1`).

| # | Item (source) | Shipped | Proof |
|---|---|---|---|
| 1 | Job-object sidecar group, kill-on-close (WI1 Ch3 Jobs) | `SidecarJob.cs` | Dirty-kill leaves zero pythons, repeatedly |
| 1b | Completion-port death notifications (WI1 Ch3, `TpAllocJobNotification` pattern) | Watcher thread, `[job] event=` lines | Force-kill → `ABNORMAL-exit pid=N`; full death → `active-process-zero` |
| 1c | Auto-restart on `active-process-zero` only | 3-attempt budget, log-only, no popups | Kill server → attempt=1 → healthy in 12 s → serving |
| 2 | CPU hard cap 50% (WI1 Ch3/Ch4 rate control) | `SetRateCap(50)` | `cpucap=50%`, warmup 13.2 s unclamped → 13.1 s clamped |
| 2b | Priority-class clamp BelowNormal ceiling (WI1 Ch3 limits) | Merged with kill-on-close | `limits ... ok=True`, no healthy-path change |
| 3 | Low-memory listener + tier-1 shed (WI1 Ch5) | `MemoryPressure.cs`, rides the 60 s pressure line | `watcher armed`, `mem=high` steady-state; trim/pri APIs probe-proven |
| 4 | Session-scoped System power request (WI1 Ch5 + WI2 Ch8) | `PowerAvailability.cs`, Hold on capture Start, Release on Stop, Saver opts out | User log: `held (ac=dc)` → `released` |
| 5 | WER LocalDumps keys (WI2 Ch10) | `CrashDumps.cs`, HKCU (no admin) + HKLM attempt | Registry verified, dumps dir created |
| 6 | FS/AV/security discipline (WI2 Ch11/Ch12, WI1 Ch1/Ch2) | Already practice, verified | Narrow exclusions, flush-on-teardown, per-user layout, no HKLM writes |

**Discoveries made during implementation (not in the plan):**
- venv `python.exe` is a launcher stub: it re-execs the base interpreter
  as a *child* (uvicorn "server process" PID ≠ spawned PID). The job —
  not the PID — is therefore the correct unit for cap/kill/notify;
  `HasExited` watches the stub (documented at the call site); server
  liveness is decided by `/health`. The stub supervises: killing it reaps
  the tree.
- `mem=high` is the healthy steady-state of the resource notification,
  not `ok` — the live log corrected the comment before commit.
- Healthy boxes sit under the clamp/cap with zero behavior change;
  ceilings only bite escape attempts.

**Deliberately parked (needs cited failure or measured win — the books
rule). Revisit only on evidence:**
- Tier-2 shed: restart sidecar `WUNDER_NO_CAT` on sustained Low (needs a
  real Low event; untestable on 32 GB).
- ETW TraceLogging provider (log cost is 0.2 ms — no problem to solve).
- WMI field-survey script, SCM failure actions (restart now covered by 1c).
- Timer-coalescing split for the poll loop (needs jitter evidence).
- Sequential-scan hints on corpus bulk load (needs I/O profile evidence).
- ARM64 wheels, MSIX/packaging questions (packaging time).

**Final-sweep finds (2026-10-06, not yet implemented, smallest next):**
- `RegisterApplicationRestart` + recovery callback (WI2 Ch10): survive
  Windows-Update kills mid-session. Tiny win, real.
- `ShutdownBlockReasonCreate` + shutdown level for corpus flush
  (WI2 Ch12): bounded, user-visible flush instead of silent truncation.
- Timer-resolution audit (WI1 Ch4): grep for `timeBeginPeriod(1)` in us
  and libs; never raise the system tick for OCR polling.
- Session disconnect handling (WI1): pause capture on
  fast-user-switch/RDP disconnect, resume on reconnect.
- UIPI note: overlay can't drive elevated windows — degrade to read-only
  with a hint, document it.
- OneDrive vs `%AppData%\corpus`: cloud sync can stomp `jsonl.gz` —
  verify exclusion, atomic tmp+rename already the pattern.
- Stay unpackaged (WI2 Ch8): MSIX/UWP would bring loopback exemption,
  freeze/throttle and 30 s background limits — architecture guardrail.
- Sysinternals runbook for field diagnosis (ProcExp handles/GDI columns,
  ProcMon filters, `procdump -mp` then `-ma`) — docs only.
- Parked: MMCSS expectations, processor-group pinning (>64-LP machines),
  MMF-vs-socket transport, USN journal, GFlags/shims (lab only),
  hardlink model dedup (multi-user installs).

## Rejections ledger (data, not taste)

- Upscale for OCR speed: refuted by measurement.
- Debug→Release switch for speed: parity proven, parked to ship day.
- Crop-collage batching: 2–7× faster, readings destroyed. Rejected.
- Pad-to-square vs stretch (paper #5 MSR audit): manga-ocr's preprocessor
  stretches every crop to 224×224, but the model is stretch-trained —
  bake-off on 19 hand-transcribed lines: stretch exact 9/19 CER 0.357,
  black-pad exact 5/19 CER 0.497, same speed. Hands off the preprocessor.
- `ホーション`→H-row mapping: reverted (inventory-ambiguous, proven).
- Single-sighting vocab rows (よJ, メダル…, glove variants, みざて,
  ひだソて): held, never shipped.
- Finetuning on live outputs: banned (circularity + feedback).
- CAT 4-bit on weak VRAM: pre-check + skip, never crash.
- ETW/WMI/tier-2/coalescing/sequential-scan: parked, not rejected -
  each names its unlocking evidence above.
- A1 settled-frame averaging PILOT (HDIP Ch 25): 6 same-text pairs from
  the live ring, 4 averaged (2 skipped on 1px box drift — production must
  align before mean). 1 real fix (single-read tail error corrected by the
  mean), 0 regressions on clean reads, identical ms. Positive-but-thin:
  justifies a production trial reusing dwell crops (no extra waits/calls).
- A1 production TRIAL (live, 2026-10-07): host means previous + current
  full capture and OCRs the mean in a single pass — no extra waits, no
  extra OCR calls (the pass it replaces). Gate frac<0.002 (static) keeps
  drifted pairs out; every activation logs [avg]. Replay parity
  structural (replay refeeds text, never bitmaps) + verified: 0 drift /
  263-identical-to-baseline. Live proof = [avg] lines + panel truth.
- A1 trial LIVE (Zelda 30-min session, 2026-10-07): 23 [avg] activations,
  0 errors. Attribution join lost (replay wiped app.log before backup —
  procedure fix: backup-before-replay unconditional). Fires: 54, of which
  ~40 score exact-or-trivially-off vs the script; systematic リンク→
  あいうく-class misreads noted, NO script-sourced string fixes per user
  directive (script is verification oracle only).
- Turned-page rescue PROVEN live: 23 flush-on-pageturn publishes, incl.
  Zelda's dungeon page and uncle page 1. Fragment-hold: 4 kills, no
  escapes. Title: 58 quiet watches, 0 publishes — still starving.
- Sentence-double collapse (live 4/54 doubled fires: uncle XYXY, Zelda
  demon XX, soldiers XX, sage XYXY + seen-never-fired candidates of the
  same shape): adjacent-identical + exact-half collapse at normalize,
  keeps last terminator, len>=2 guard. FFVI predicted drift 0, verified
  0 / 263-identical. Unblocks the noisy seen-never-fired pages.
- Sentence-double LIVE PROOF (Zelda session 2026-10-08, keeper-preserved):
  5/5 [sentdedup] collapses correct (demon XX→X keeping ！, uncle XYXY,
  old-woman XX), zero exact-double fires in 37. Residual: variance-doubles
  escape (loop-731 old-woman fire: halves differ by OCR variance) — queued
  as fuzzy-halves, needs more instances before scoping.
- Keeper system PROVEN: play survived a mid-session restart (2 banners,
  one continuous record), 5504 play lines, 37 fires, 0 errors.
- REJECTED guard (measured): pageturn-flush stableCount>=2 would kill 5/7
  legit count-1 flushes to stop 1 junk fire (「今日は日本人です。」,
  loop-458: 1-sample transition read evicted a count-6 held sign, then got
  flushed by the next change). Flush-hardening needs a tighter distinguisher
  (length/provenance, not age) + more instances. Queued, not scoped.
- Mistitle instance (queued): [menu] publish TITLE fired on scroll-mid
  fragment 「というのは、」 (loop 25, avg'd read — attribution unclear).
  Needs code-path read before scoping.
- A1 trial: 10 activations, all on boot/menu screens (copyright, PLAYER
  SELECT, scroll-mid); dialogue attribution still open. Trial stays in.
- B3 calibration CURVE (offline, 2026-10-08): feed confirmed live (1259/1259
  blocks carry numeric conf, src=detector/classical). 63 fires labeled vs
  the script oracle (fuzzy halves-aware match; unverifiable NOA-only lines
  excluded): 48 correct, 15 incorrect. 34 conf-joined pairs show NO
  separation — incorrects at 0.946/0.959/1.0, corrects spanning 0.692–1.0.
  Box-conf is a presence signal, not a correctness signal (detector is
  confidently wrong on transition misreads). Per the rule thresholds stay
  0: B3 CLOSED negative on box-conf; rescorer/combiner weights needing conf
  stay blocked. Recognition-level confidence (CTC probs via sidecar) is a
  new measurement, not assumed — queued.
- Mistitle repair (offline batch, 2026-10-08): loop-25 TITLE published
  mid-intro-scroll (copyright-year hint + 10 quiet loops; blocks were tall
  inset scroll text). Hinted non-FFVI publish now requires logo geometry
  (all JA blocks full-bleed short bands, same test as logo hold); FFVI
  byte-identical, bare-quiet/static paths unchanged. Replay 0 /
  263-identical. Geometry-block observability added ([menu] quiet-title
  blocked) after a no-publish session proved indistinguishable from an
  untested one. Live proof still pending.
- Village session verdicts (2026-10-08, keeper-preserved, 13 fires):
  sentdedup 7/7 correct incl. 2 new shapes (shop rupee, flute-father);
  new areas read clean (hideout man, fortune teller, Sahasrahla search,
  pub べっぴん改善); 4/13 junk fires (wanted-sign garble, サプリ garble,
  自分 fragment, アプリセンター) queued for next scoping; 2 more
  variance-double fires (thieves/かなぁ, fortune-teller/ない) — fuzzy-halves
  escapes now 3, still parked; avg 26 activations all menu/boot (A1 dialogue
  verdict still open); startup sidecar-refused ×10 loops (transient,
  self-recovered — startup guard queued); titlegeo untested (no hint at
  boot, bar never reached).
- Full-sweep verdicts (2026-10-08, 66 fires, 25 shot bundles + keeper):  TITLE logic VERIFIED working — loop-213 TITLE minted on the real title
  screen (attract-loop return, marker row 神々のトライフォース, purity
  stripped to marker); 4/4 geometry blocks correct (inset fragments).
  NO title regression: the missed first showing is the startup race (27
  errors, ALL loops 1–37, zero after) eating the boot window while the
  sidecar loads. Startup gate upgraded from queued to next-batch:
  gate the OCR loop on sidecar health.
  Intro scroll pages 1–2 outrun stability (page seen once at loop 55,
  never fires; shot confirms panel empty mid-scroll) — needs own scoping.
  A1: 3/43 avg reads on dialogue (uncle + sage, misreads intact) —
  engages safely, win unproven, stays in. sentdedup 12/12, 0 exact
  doubles escaped; fuzzy-halves escapes now ~7 (revisit threshold).
  KILL×10 + 1995×10 suppressions correct. longbox 0 (ceiling clear).
  rec-gen line live (4/2.0/3). New areas: big key, Kakariko signposts
  (SIGN DIALOGUE source confirmed), flute father, pub, yard man cleaner.
- Boot-window outage ROOT-CAUSED (2026-10-08, same session): NOT a title
  regression. The tree died 35s into warmup (ABNORMAL + active-process-zero
  20:29:42) during CAT 4-bit weight load; auto-restart correctly recovered
  (healthy 20:30:04, first OCR 20:30:05). Killer unconvicted — prime suspect
  is the combined load spike on 6GB VRAM; a process-memory-limit job event
  with no configured limit is on record but unexplained (possible PID-reuse
  artifact; observability retained, no fix built on it). The restart was
  correct (alternative was a dead engine); a warmup grace period was
  considered and REJECTED (would only delay recovery).
  Shipped startup-survival batch: (1) host quiet-hold on OCR transport
  failure (first + every 20th logged, state untouched); (2) /health
  defaults honest ("warming") — DOWNGRADED on re-verification 2026-10-08:
  READY initializes to warming and keys are never deleted, so the live
  path already reported correctly; the change is defense-in-depth only,
  no live behavior change; (3) 503 bodies attached to host errors;
  (4) CAT load deferred 120s post-OCR-ready (boot window serves on a
  quiet machine, translations fall back meanwhile); (5) CAT watcher cap
  90→150 polls so the deferred CAT-ready line can't expire unseen.
  Revert phrases: revert quiet-hold,
  revert health-defaults, revert ocr-503-detail, revert cat-defer,
  revert catwatch-cap.
- Veto first light (offline, 122 crops): mystery bright-box fires read
  bright=0.87/edge~4 vs dialogue boxes (bright 0.05–0.2, edge 1–4) and
  copyright (edge~14, bright 0.28). Signal-shaped but thin (2 instances).
  CORRECTION 2026-10-08: ZELDA's overworld map carries NO text (user) —
  Zelda map negatives can only ever be image-level (frames of the map
  screen for a no-text-screen veto), never text. This is game-specific,
  not a general claim: other games' maps may carry text, and the veto
  framework stays game-blind. Veto text negatives re-targeted to
  text-bearing non-dialogue: item menu, PLAYER SELECT, logo/copyright.
  Vetoes stay queued.
- Zelda textbox sprite map (user spec 2026-10-08, from chat images —
  sprites, NOT font glyphs; handling needs sprite detection, queued as
  sidecar+host feature, NOT implemented): heart pickup → emit "<3"
  inline; sign left-arrow → "<-"; sign down-arrow → "↓" (U+2193);
  Link-face sprite (wanted poster) → ignore, not text; big decision
  arrow → selection marker (left of chosen option in decision menus);
  「/」 brackets present, handling TBD on cited misread. WORKING
  HYPOTHESIS: unexplained leading-glyph junk (口力, ●, 、) may be
  sprite misreads — check future junk against this map before scoping.
- Fuzzy-halves PARKED with data: 14 near-doubles / 18,607 corpus texts,
  ~1 panel escape (loop-731 old woman). Prize too thin for new machinery;
  revisit on recurrences. Exact doubles stay owned by sentdedup (5/5 live).
- Manga-ocr doc batch verdicts (offline, 2026-10-08, all pages + installed
  source + local HF cache; zero implementation):
  - Generation config CORRECTED (2026-10-08, same day): the greedy verdict
    drafted below was wrong — it probed a reconstructed path
    (manual from_model_config on the wrapped object), not the deployed one.
    Loading via from_pretrained, exactly as the sidecar does, yields
    beams=4, lenpen=2.0, norepeat=3, maxlen=300. The raw-JSON keys DO
    transfer at load; only post-hoc introspection hides them. Lesson
    recorded: probe the deployed path, never a reconstruction. Glossary's
    "greedy" is wrong for our runtime. Reinstated: length-bonus
    hallucination pressure IS live; no-repeat-3 IS live, so a single read
    cannot repeat a 3-gram — exact doubles are box-gluing, certain. The
    shipped [rec-gen] line printed the truth on first contact (4/2.0/3,
    greedy=False); the offline probe was the liar, not the log line.
  - Superseded greedy draft (kept for the record, DO NOT USE): deployed
    config.json carries beams=4/lenpen=2.0/norepeat=3, but manual
    from_model_config on the wrapped object returned all-None, wrongly
    suggesting greedy/lenpen-1.0/norepeat-0. The error was the probe path,
    not the config.
  - Vocab verdict CLOSED (6144 chars): …/！/？/space/fullwidth-latin OOV,
    halfwidth ASCII + kana/kanji IN. Pipeline already routes around every
    OOV (post_process/jaconv/host-normalize); リンク IN → noise class kept.
    No structural gaps for our domain.
  - Package 0.1.16 installed (docs describe 0.1.14; claims re-verified
    against installed source — no drift found).
  - Crop margins: sidecar cuts EXACTLY at box bounds (main.py crop call,
    zero margin) vs 10px-margin training convention. Edge-cut misreads
    plausibly related. Offline shave test: 4/14 crops robust to 8px shave,
    rest degrade — but re-OCRed saved crops already differ from live reads,
    so this measures fragility generally, not our tightness specifically.
    METHODOLOGY NOTE: saved-crop re-OCR is not live-equivalent; the real
    margin experiment (expand live boxes into full frames) needs
    troubleshoot _all.png + corpus from one run — scoped, awaiting play.
    Back up _all.png before replay/wipe from now on.
  - Observability shipped: sidecar [rec-gen] startup line (effective
    beams/lenpen/norepeat/greedy — every future log self-identifies
    inference behavior), host [longbox] watch (>150 chars vs 300-token
    ceiling). Both observe-only. Next run troubleshoots gen-config,
    ceiling approach, and (with _all.png) margins independently.
- EasyOCR doc batch verdicts (offline bake-off, 2026-10-08, single saved
  app-window frame, production scaling replicated):
  - Detection params CLOSED negative: stock settings find most boxes (66
    vs 56–62 for mag2/lower-thresholds/combo); variants only merge.
    Nothing to tune; detector exonerated on this frame.
  - Margin bake-off CLOSED negative for blanket +10px, twice
    independently: (a) 16 same / 48 differ on 64 boxes (single frame);
    (b) 2026-10-08, 25 saved frames, same=62 diff=212 judged — fixes some
    edge glyphs (みっかり→みつかり, ささけ→ささげ, leading しかし
    recovered) but breaks more (賢者→貿者, 封印→射印 regressions on reads
    that were right; neighbor-line soup). Mechanism: dialogue line spacing
    ≈ margin size, so +10px eats the next line. Edge-cuts stand; next
    shape (if ever): background-only expansion, unscoped.
- Zelda inventory geometry (user SVG 2026-10-08, zelda-inventory.svg;
  normalized to game frame): ITEM (0.037,0.070,0.573,0.485), item-name
  (0.666,0.070,0.295,0.187) — the ONLY changing text (カンテラ/
  ブーメラン/… by selected item), pendant (0.666,0.274,0.295,0.284),
  DO commands (0.037,0.612,0.576,0.284), EQUIPMENT
  (0.666,0.612,0.295,0.284). Everything else sprites. Unlocks (when
  cited, not before): item-name closed vocab, menu geometry gating.
  No inventory-screen failure cited to date — recorded, unimplemented.
- Confusion set v1 (2026-10-08, confusion-set.txt): 2741 mined [ocr-fixup]
  pairs from live keeper logs aligned to single-char substitutions, ranked.
  Top: 入→人 269, 賢→腎 269, む→も 206, 賢→貿 151; voicing cluster
  (ボ→ポ, ハ→バ, グ→ゲ, ブ→プ, ゾ→ジ, す→ず, ご→こ); 封 magnet (射/針/
  樹/掛/批); Latin-vs-kanji (Ｂ→日/ロ/目/自, Ａ→日/自). Our data, not
  script-sourced. STT weighted-CE discipline transfer: known pairs cost
  0.4 in fuzzy paths. Measured on 10 fire escapes: plain halves-ratios
  already 0.05–0.17 (weights change 2/10 mildly) — fuzzy-halves needs no
  weights, just a threshold from the full-distribution measurement;
  weights apply to C3 vocab matching. Implementation proposed, awaiting go.
- B5 positional-instability (HDIP): growth excluded, equal-length settled
  runs read near-perfect (max 2 disagreements at any position over 52
  runs) — no drift-shaped positional effect, no position-weighted trust
  needed. D1 consensus labels implied trivial by the same result.
- B4 combiner rank-fusion (HDIP Ch 11): closed UNIMPLEMENTED with data —
  3 real battle frames, 13 classical boxes, zero IoU>0.5 contests with
  detector boxes. The proposers cover different regions by design; there is
  no cross-proposer contest to fuse. Revisit only if the classical-vs-detector
  contest ever fires live (observable in [blocks] cls markers).
- Papers #13–16 (bottleneck-2 batch, quickest-change detection; concepts
  only, no models imported): #13 Xie et al survey (2104.04186) + #15
  Veeravalli & Banerjee chapter (1210.5552) — transient-change framing is
  the diagnosis (our 3-count assumes persistent pages; scroll pages are
  transient, gone before 3 reads); Shewhart (fire-now, detect-at-onset)
  vs CUSUM (accumulate, delay-averaged) mismatch named; thresholds come
  off a measured delay/false-alarm curve, not fixed counts. #14 Adams &
  MacKay BOCD — run-length posterior discipline (score over same-vs-new
  page, not a resetting count); their likelihoods NOT transferred (we
  have no text-read likelihood model). #16 Ren obs-scheduling PARKED
  (needs a cost model unmeasured). Nothing to copy; framing + method
  transferred, math left behind.
- Bottleneck-2 dwell + single-read measurements (offline, 2026-10-09, 16
  Zelda corpora + script oracle): 1672 sighting runs — span 1: 1250,
  span 2: 181, span ≥3: 241 (86% dwell ≤2 loops, unfireable by the
  3-count; loop-55 typical, not outlier). 759 substantial low-dwell
  runs: full-page match 133 correct (18%) vs fragment-aware match 434
  correct fragments (57%), 325 non-fragments (43%). Verdict: single
  sights are mostly truth-fragments (scroll windows), not pages and not
  junk — firing raw posts fragments (loop-25 mistitle precedent). Fix
  shape is assemble-then-fire, not lower-the-count. 43% non-fragment
  rate sets the false-alarm bar for any fast path.
- Scroll-window assembly SCOPED offline (awaiting measurement): stitch
  consecutive span-1 fragments in loop order, fire when the stitched
  page matches script shape; scored on the same 759 with zero junk-fire
  regression required. No production change until the offline number
  lands.
- Blind overlap tiling MEASURED (offline, 2026-10-09, same 659 pairs,
  exact suffix/prefix overlap ≥6 or containment — no oracle in the
  tiling, oracle only for scoring): 147 blind tiles → 129 script-shaped
  (88%), 18 junk-tiles (12%). Shaped tiles are full intro pages at
  0.00–0.16. Junk tiles are mostly already-owned shapes (exact-doubles
  = sentdedup territory, copyright/date fragments, dialogue repeats).
  Verdict: tiling signal is real and game-blind; the 12% needs a gate
  (length/provenance, dedup-first ordering) before any production fast
  path. Implementation proposed, awaiting go.
- Gate search CLOSED negative (offline, 2026-10-09, 147 tiles): length
  fails (shaped 16–57 vs junk 15–78, full overlap); dedup-first removes
  1/18; sentdedup-shaped internal-repeat catches 0/18 (stutter isn't
  clean XX/XYXY); B-novelty inverted (junk median 0.61 > shaped 0.33 —
  drifted restatements contribute long tails); symmetric-tiling weak and
  wrong-direction (shaped 50% vs junk 28%). No cheap textual separator
  exists. Rate context: 18 junk tiles / 16 corpora ≈ 1 per session —
  unshippable under omission-over-misteaching. Remaining shape (if
  ever): provenance gating (fast path engages only where stability
  starves), unmeasured. Assembly parked with data, same precedent as
  fuzzy-halves.
- Provenance gating MEASURED positive (offline, 2026-10-09): turnover
  as run-count fails (scroll chain-merges, everything reads 2), but
  neighbor-stability separates — junk lives in flicker (med 0.27),
  shaped tiles in smooth glide (med 0.54). Engage-only-if-stability≥0.4
  keeps 102/129 shaped (79%), suppresses 15/18 junk (83%); ≥0.3 keeps
  113/129 (88%) / suppresses 11/18 (61%). Coherent: stability machine
  owns stable-identical, flicker stays off, glide is the fast path's
  territory. Caveat: n=18 junk is thin. Metric is live-computable and
  game-blind (rolling adjacent-similarity). Scopes (not implements) the
  assembly fast path: tile + stability≥0.4 + fire mechanics TBD.
- Bottleneck-3 batch (recognition confidence; concepts only, no
  training): Paper A (Wang et al 2020 NMT inference calibration) —
  ECE + reliability diagrams with TER/oracle correctness labels is the
  validation method to steal; softmax miscalibration gap (train 1.39 vs
  inf 15.83) predicts our pixel-shifted OCR needs the same distrust;
  their fixes are training (banned). Paper B (Lu et al 2022 ConNet) —
  CLOSED (joint-trained confidence net = training, banned); survives
  only as caution (MSP fails silently on 35.8% of BAD outputs, same
  death as box-conf). Paper C (Flores et al 2025 beam-shape) — the
  transfer: ratio p1/pK + tail-thinness over live beams, no fine-tuning;
  maps to our confusion-mass-split pathology.
- rec-beam probe SHIPPED (sidecar, observe-only): scored generate
  (all 4 beam strings) replaces bare mocr() call; parity 39/39
  byte-identical on banked crops, rec cost neutral (4726 vs 4718ms);
  any mismatch parks the probe live; per-frame [rec-beam] line
  (divmax + suspect count at div>0.20), no host contract change.
  output_scores=False (strings only, no logits transient).
  Revert: revert rec-beam.
- Beam-SCORE metrics CLOSED negative (offline, 2026-10-09, 182
  dialogue-band crops, script oracle): tail 0.250–0.251 on ALL crops,
  ratio 0.04–0.09 (inverted) — sequences_scores equalize across beams,
  carry zero signal. Paper C's score-shape does not transfer as stated.
- Beam-STRING diversity MEASURED positive (same 182): all crops have
  diverse beams (0 all-agree), splitting exactly on confusion pairs
  (賢/貿/腎, ハ/バ/パ, みっかり/みつかり). Accuracy falls with
  diversity: 0.47/0.78/0.69/0.33/0.18 across quintiles; mean div
  correct 0.149 vs incorrect 0.235. Suspect-flag div>0.20: precision
  0.82, recall 0.49 for wrong reads. Game-blind, no training — the
  rec-confidence signal bottleneck 3 needed. Rescorer/combiner roadmap
  UNBLOCKED to scoping (lower beams sometimes right, e.g. みつかり).
  Proposed: log div + flag in [rec-beam] (observe-only), stability use
  after. Awaiting go.
- FFVI run verdicts (live, 2026-10-09, Narshe, loops 20–3296, 2127 ocr /
  238 bubbles, 15 shot bundles banked): rec-beam parity=ok live;
  suspect-hold 31 holds, 0 confirmed starvation — battle/UI junk held
  right (ウェッジビックス??? div 0.92–1.00), held fragments' pages
  posted complete (ここは→老人 line, お願いしませんよ×2). 3 opens
  need panel truth: いたぞ×6 holds/0 fires (stable-but-suspect, the
  exact trade — garbled echo or omitted truth, frames decide),
  気がついたのか×2 (garbled ログ/ロッフ variants, never 3× either
  way — hold changed nothing), Kefka帝国 line (garbled, clean form
  never read). TITLE live on logo (quiet-title publish). sentdedup 167,
  pageturn 21, publish-new 37. C3/confw ZERO live engagement
  ([fuzzy]/[fuzzy-w] absent) — vocab paths untested live; A3 stays
  offline (15 FFVI frames banked for beam-choice bake-off).
- A3 bake-off CLOSED on this material (offline, 2026-10-09, 15 FFVI
  frames, 654 boxes, full 34-word vocab): top exact=3, fuzzy=0,
  abstain=651; rescue opportunities 0, conflicts 0; even weakest
  oracle (any beam within d≤2 of vocab) finds 4. Vocab reads are
  exact or nowhere-near — no beam-choice prize exists outside
  shop/inventory screens, which this run never opened. A3 parks
  behind shop screens (deferred with shop UI work), not engineering.
- A1 coverage boundary PROVEN live (FFVI corpus div audit): garbles
  that stabilize read at div 0.07–0.14 (サコ×24 loops, 鹿野アニマー/
  サルシェ×3 — beams AGREE on the wrong reading). Suspect-hold is
  blind to confidently-wrong reads by construction (Paper C's own
  Table-6 failure class; Lu 35.8% silent failures). That class needs
  symbolic knowledge (fixup pairs), not confidence — queued.
- Shot triage (15 FFVI shots, panel truth): (a) CAT profanity
  hallucination — ウェッジビックス??? → "What is the Japanese word
  for 'wet dick'?" — NEW, SEVERE, translation-side, trust violation;
  (b) title quiet + intro crawl unposted (bottleneck-2 FFVI evidence,
  no action); (c) BATTLE wireframes empty while menus/names visible
  (FFVI battle-polish proof); (d) no-box dialogue fire
  (シルバリオカートリー on battle screen — junk-fire instance with
  frame+blocks); (e) translation glossary gaps (gilded, High Vor ion,
  Bicks — translation-quality queue). Proposed order: profanity guard
  (omit+log, game-blind) → A3 bake-off → battle polish scoping.
- Profanity-hallucination guard SHIPPED: EN-trigger list (6 unambiguous
  stems) + JA-alibi list (12 markers); withholds EN only (posts "------"
  like untranslatable, JA read kept, no silent vanish); provider-blind
  (fire + update paths). Predicate verified on 5 cases (cited hold,
  Yakuza passthrough, 2 clean passes, embroidery hold). Build +0 new
  warnings / 0 errors; replay 0 / 263-identical. Live proof (a
  [profanity-hold] line) needs real CAT output — pending play.
  Revert: revert profanity-guard.
- FFVI run fixups SHIPPED (7 pairs, shot-cited 2026-10-09): いて突っこむ/
  突っこむサコ/かまっな/鹿野アニマー/アニマー/サルシェ/どうとうこの.
  Both cited lines repair exactly to on-screen truth, idempotent, no
  cross-fire. Build +0/0; replay 0 / 263-identical. Revert: drop the 7
  pairs. Name pins (Biggs/Wedge/Terra…) NOT touched — EN forms need
  user say (Matthew precedent). Glossary gaps (gilded, High Vor ion)
  stay in translation-quality queue (exact-match-only policy intact).
- Title weak-hold guard SHIPPED: publish-weak-hold now requires
  !_frameIsTitle (pageturn-flush precedent; loop-64 title-screen publish
  of churn-held line is the cited instance). Replay 0 / 263-identical
  (no old-corpus weak-hold fired on title). Known limit: FFVI logo
  reads EMPTY (total=0) so the flag was false at the cited loops — the
  empty-logo-title gap stays queued (quiet-watch interaction unscoped).
  Revert: delete the conjunct.
- Manual name pins SHIPPED (12 entries, user-cited official manual
  literal EN): Tina/Lock/Mog/Edgar/Mash/Cayenne/Gau/Celes/Setzer/
  Shadow/Stragus/Relm. Mash REPLACES the earlier Matthew pin per the
  same user decision. Mechanism confirmed (TryBattleGlossary exact
  match on menu/battle rows); merged rows stay live (known boundary).
  Wedge/Biggs NOT pinned (absent from manual pages — no source, no
  pin). Replay 0 / 263-identical. Revert: restore Matthew pin, drop
  the rest.
- Unknown-name mask SHIPPED (`67066d0`): runs of [?？]{2,} masked
  with private-use tokens pre-translate, restored after (fire + update
  paths, provider-blind); single ? stays punctuation; CAT-eaten tokens
  keep EN as-is + [qmask-lost] log. Round-trip verified (multi-run,
  fullwidth, drop fallback). Build +0/0; replay 0 / 263-identical.
  Live proof (masked CAT output) pending play. Revert: revert qmask.
- Battle BATTLE-panel gaps CLOSED as designed (FFVI run corpus: 92
  battle-frame:fire, event rows post correctly; static menus don't
  repost by design — no spam). No action.
- No-box fire MECHANISM FOUND (shot-15 シルバリオカートリー):
  loop-3059 count-1 text flushed as "turned page" when loop-3060 read
  dissimilar (pageturn path has no stability minimum). 2nd instance
  for queued flush-hardening (loop-458 precedent); needs tighter
  distinguisher (length/provenance — bottleneck-2 window-stability
  metric is the candidate) + more instances. Still queued, not scoped.
- Suspect-hold A1 SHIPPED (pending replay parity): sidecar div rides
  per-block (d, default 0.0) → OcrBlock.Div (lenient parse) → corpus d
  (additive, replay-lenient) → stability match-branch holds count AND
  text on div≥0.20 ([suspect-hold] log), flush paths untouched.
  Old corpora lack d → Div 0 → never holds → parity by construction.
  Revert: revert suspect-hold.
- Combiner A2 CLOSED negative (offline bake-off, 2026-10-09, 182
  banked crops, script oracle): top 0.489, oracle ceiling 0.533 (+8
  crops — thin prize). Majority vote: 0 gain / 0 flips (beams never
  agree exactly). Confusion-medoid: net -1 (1 harm: イケニエ→イケ三エ
  — truth isn't the centroid when beams share error direction).
  Suspect-gated medoid: 0/0, identical to top. No blind rule reaches
  the ceiling; beams differ in punctuation/trivia, not rescue material.
  Parked with data. A3 dissolves: confw second pass already IS the
  shipped vocab-rescorer; beam-choice-for-vocab needs FFVI crops
  (unbanked) → parked behind material.
- Provenance observability SHIPPED (pending replay parity): trailing
  10-loop adjacent-similarity window + per-loop [prov-stab] line,
  observe-only (banks live distributions for flush-hardening, which
  owns the シルバリオカートリー instance). Revert: revert prov-stab.
- M7c slice v1 SHIPPED (parity proven): injectable clock (TimeProvider
  field, default System; loop pacing + cost-flush read it, perf
  Stopwatches stay real) + wall-ms `wt` banked per corpus ocr line
  (additive, replay ignores) + `--simcheck` PASS (clock-injection,
  cost-predicate, delay-overload). Step extraction (ticktape shape)
  stays next slice. Revert: revert m7c-slice1.
- M7c slice v2 SHIPPED (pending proof runs): decision seam —
  611-line stability machine moved verbatim into
  ProcessOcrObservationAsync (only exits adapted: chart-hold
  continue→false, scroll-composite loop-exit→true + caller re-exit;
  chart delay skipped in sim), all 11 fire + 3 update sites route via
  PublishDecision/UpdateAsync (live forwards identically, sim captures
  JA), `--simreplay` feeds corpus (loop, extracted) through the REAL
  step and reports fire overlap vs recorded decision bubbles. JA-side
  only: dwell/bitmap, TITLE/menu, suspect-div(0), density, vocab paths
  stay live-only by construction. Revert: revert m7c-slice2.

## Session state — 2026-10-10 pre-compaction resume ledger
Shipped (all replay-paritied 0/263-identical unless noted, all revertible):
- sentdedup (exact doubles) → revert sentdedup
- fuzzy-halves ratio 0.40 vocab-guarded → revert fuzzy-halves
- confw second pass (NormItemKey, 40 mined pairs @0.4) → revert confw
- A1 averaging trial (live, verdict-open) → revert a1-trial
- rec-beam probe: beam strings + div + suspect flag, parity 39/39 →
  revert rec-beam
- suspect-hold A1 (div≥0.20 holds count+text) → revert suspect-hold
- profanity guard (EN stems + JA alibi, ------ pattern) →
  revert profanity-guard
- qmask (?-runs through CAT) → revert qmask
- name placeholders (E010) + 12 manual pins (Mash replaces Matthew) →
  revert namemask
- FFVI run fixups (7 pairs), title weak-hold guard (!_frameIsTitle),
  startup-survival batch, titlegeo, keeplog, rec-gen line, longbox watch,
  prov-stab observability (trailing window, log-only),
  M7c slice v1 (injectable clock, corpus wt, simcheck PASS),
  M7c slice v2 (decision seam + simreplay: 81/83 + 99/100 overlap).
Pending live proof (need play with real CAT output): profanity-hold
line, qmask round-trip, namemask round-trip, suspect-hold on misread,
A1 averaging dialogue verdict, fuzzy-halves/[fuzzy-w] in the wild,
titlegeo Zelda, sentdedup Zelda shapes, intro scroll (Zelda + FFVI).
Parked with measured reasons: combiner (thin ceiling + 1 harm),
assembly (no gate; provenance stability≥0.4 keeps 79%/suppresses 83%
is the scoped-but-unimplemented shape), blanket margin (rejected 2×),
ConNet/training (banned), beam scores (zero signal), Ren scheduling
(no cost model), A3 beam-choice (needs shop screens), len-ratio/repeat
guards (n=7), meta-refusal (n=1), live consistency checks (too costly),
empty-logo-title gap, confidently-wrong class (needs symbolic pairs).
Queues: bottlenecks 5–7 UNDEFINED (user to name); shop UI deferred
until books done; game #3 after FFVI polish + shop; Zelda confirmatory
run; FFVI shop/inventory screens (A3 material); Wedge/Biggs EN (no
manual source — user call); いたぞ (no panel truth); glossary gaps
(gilded, High Vor ion — exact-match policy intact).
Protocols: taskkill-first rebuilds, GREEN = +0 warnings/0 errors
(2 pre-existing CS8604 at TranslateAndPublishAsync warmup logs),
replay = the two pinned FFVI corpora, git checkout translation_cache
after replays, corpora never rewritten, keeper armed via keep-log.armed,
troubleshoot/ wipes per launch (pull before relaunch).
- Bottleneck-4 papers graded (P1 WMT19 QE, P2 unsupervised QE, P3
  robustness): usable = referenceless framing, severity tiers, MCC,
  dispersion-over-mean (black-box), confusion-mapped perturbations;
  closed = supervised models, glass-box internals, live consistency
  checks, all retraining. Architecture verdict: discrete guards only.
- B4 audit done offline (2026-10-10, 1289 banked dialogue-fire
  bubbles): ja-leak 0, copy-through 0, empty-en 0, repeat 3, len-ratio
  28. Zelda-oracle calibration: len-ratio 5/5 bad (base 0.29), repeat
  2/2 bad — perfect precision, thin n. FFVI exhibits: Ke-fugu,
  tina-foreigner (CAT mangles unpinned dialogue names), one CAT
  meta-refusal ("already in English"). Proposed: name-placeholders via
  qmask machinery (manual names, dialogue path) — awaiting go;
  len-ratio/repeat stay observed (thin); meta-refusal guard queued
  (n=1).
- Name-placeholders SHIPPED (pending replay parity): pack NameGlossary
  (FFVI 12 manual names; others empty) masked through live CAT as
  E010 tokens, restored after (fire + update paths, provider-blind);
  katakana-boundary longest-first (モグリン untouched, ティナ masked —
  verified); CAT-eaten tokens keep EN + [namemask-lost]. Wedge/Biggs
  still unpinned (no manual source). Revert: revert namemask.
