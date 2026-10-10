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
  stay live-only by construction. Gap analysis (polish, proven):
  81/83 + 99/100 overlap; extras (24+32) = dwell-fired dialogue
  (live dwell fires carry no decision-bubble record — sim
  stability-fires the same JA, benign) + battle/menu rows (live
  FFVI-pack battle tracker owns them; sim runs replay-default pack —
  known caveat); misses (2+1) = cascade from extras shifting
  _lastTranslatedText + missing real-block context. Seam faithful;
  gaps are documented live-only contexts, not move corruption.
  Slice v3 scoped (needs go): feed recorded blocks (corpora carry
  c/n/s/d) + select FFVI pack in sim; dwell/bitmap stays live-only.
- M7c slice v4 SHIPPED (proof runs): frame-dims + det/rec banking
  live (fw/fh/det/rec on every corpus ocr line, additive, replay
  ignores) + SetFrameDimsForSim (banked-only). Estimator tried and
  CONVICTED: block coords live in detector space (~1522px vs
  SNES-size bitmap), max(x+w) ≈ 2x true crop, ratios halve, overlap
  regressed 82→74 / 97→95 — reverted same batch, back to 82/83 +
  97/100 with dims unset (v3 behavior measures best on old corpora;
  true dims arrive banked on future recordings). Extras hold at
  11/17. Replay parity 0/263 throughout. Bottleneck 5 closed:
  clock ✓ step ✓ block+pack feed ✓ context banking ✓; permanently
  live-only: bitmaps/OCR, dwell path, CAT, transport.
  Revert: revert m7c-slice4 (slice2/3 seam stays).

## Bottleneck 6 — papers graded + measured (no behavior change)
Sources (all read, none reupload): K8s probes (startup/liveness/
readiness) — HIGH confirmatory; re-translation paper (erasure/lag,
mask-k, bias) — MEDIUM metrics-only (mechanisms need model
internals/streaming CAT: banned/absent); Google blog — LOW
(same paper, product story); SimulST survey (110) — LOW-MEDIUM
(cascade+re-translation validates us 9/13; buffer-trim = our
candidate state; aware/unaware latency).
Shipped measurement: bubble loop stamps (additive; decision +
update sites pass iteration; menu/title omit) + [b6metrics]:
corpus1 simNE=0.35 simLag=30.6loops liveNE=0.39; corpus2
simNE=0.18 simLag=15.9loops liveNE=0.19 (overlap 82/97).
Sim reproduces live revision behavior quantitatively; lag scale
(6–12s) matches the paper's seconds-scale TL. Live lag n/a on
old corpora (no stamps — banked going forward).
Probe audit (verified in code): startup = warmup+min-spec+CAT
defer120s+catwatch (gates liveness/readiness ✓); liveness = job
TreeDied→restart budget 3/5s-then-down (bounded, no cascade;
no slow-kill probe — passes the K8s caution ✓); readiness =
honest health+503 detail+quiet hold+fallback chain+lowmem/CPU
(degrades, never kills ✓); consecutive thresholds where they
matter (window-death 5). Killer still unconvicted, no recurrence
→ NO behavior change. Revert: revert b6metrics.

## Bottleneck 7 — papers graded + recall proxies measured
Sources (all read, none reupload): TESTR — LOW (training-based,
curved-text machinery n/a to pixel fonts; ablations confirm our
detect→recognize shape + upscale/cap operating point); Union14M
revisit — MEDIUM (method + challenge taxonomy; artistic/
contextless/salient/multi-words/incomplete map to our logo-
fonts/item-numbers/neighbor-ingestion/merged-rows/fragments;
vocab-reliance supports vocab-guarded fuzzy); DSText V2 —
LOW-MEDIUM (small/dense is the hard regime — our regime;
no implementation on 6GB VRAM).
Shipped measurement (offline, both pinned FFVI corpora,
corpora untouched): zero-blocks == extracted-empty exactly
(52.6% / 31.0% — detector and extractor never disagree; empties
are scene-empty, not missed reads); blocks/line mean 1.5/1.9,
max 22/24 (dense battle screens box fine); fragments 6.6%/3.4%
(fragment-hold territory); merged-proxy (1 block ≥15ch) 187/324
(splitter territory, expected); contextless 109/70; blank-gaps
48/21 (bounded miss-window candidates). Caveats: these two
corpora predate c/n/s/d banking (conf=1.0 default, src/div
absent — per-block quality analysis needs newer corpora);
TRUE recall (text on screen, zero boxes) needs frames — queued
as the banked-frame audit protocol for next play (settle-eval
persists; sample offline, user confirms misses).
Verdict: detection looks healthy on every text-present read;
no recall failure cited → NO behavior change. Bottleneck 7
closed pending the play-time frame audit. Revert: revert b7
(docs-only).

## Bottleneck 8 — cold-start priced + authoring checklist
Cold-start delta SHIPPED (proof runs): `--simreplay <corpus>
universal` forces UniversalPackInstance (ForcePackArg, restored
after). Result on both FFVI corpora: IDENTICAL numbers both
packs (93/33/82 + NE0.35/lag30.6; 114/20/97 + NE0.18/lag15.9;
same missed/extra sets). JA-fire cold-start price = ZERO.
Honest decomposition: the v3 extras win (24→11/32→17) came
from BLOCKS (geometry-aware menu tracking/div), not pack —
pack select isolated to nil on dialogue decisions. The real
cold-start price lives where sim can't score (no live CAT):
glossary instant-path dead (per-line CAT burn + JA flicker),
name masking dead (tina-foreigner class returns), fuzzy/confw
dead (misread fixups lost), splitters passthrough, weapon box
off. Pack-authoring checklist (fill order by value): 1.
Signatures (2–7 proper nouns, unlocks routing) 2. Title
triple 3. Ocr/DisplayFixups (miner-as-pack-data) 4. Battle/
monster/item vocabs (splitters + fuzzy) 5. BattleGlossary
(no-burn chrome) 6. NameGlossary (official EN, user-sourced)
7. WeaponLabels 8. Vision calibration last (learning estimates,
publish requires). Replay parity 0/263 (override is sim-only).
Revert: revert b8-coldstart.

## Hygiene logging (bottleneck-8 follow-up, SHIPPED)
DeepL spend counters (calls/chars on API attempts, interval
snapshot) + cache size (entries/bytes point-in-time) on the 60s
cost-flush as [spend]. Log-only, zero behavior. First [spend]
sighting waits for a live loop (sim/replay never run the flush).
Replay parity 0/263. Revert: revert spend-log.

## Bottleneck 9 — panel latency budgeted + measured (SHIPPED)
Sources (all read, none reupload): stream-latency paper —
MEDIUM-HIGH (AL/DAL pair, global-delay/stream-level; our
simLag already stream-fed ✓); re-translation paper (dup of B6 —
benchmark only: stabilized TL~4.1s/NE~0.1; ours NE already
there); interactive-latency paper — MEDIUM (throughput≠latency,
think-vs-wait, 0.1s/2–4s anchors, distributions); SRE
Monitoring — discipline (percentiles, golden signals,
test-your-monitoring).
Shipped: decision-kind capture (12 fire + 3 update kinds,
additive params) + [panellat] (lag p50/p95/max, DAL-like
catch-up debt in fire order, per-kind counts@means) + panel
budget (p50≤10, p95≤30, max≤60, NE≤0.5 — regression
tripwires w/ headroom, exit stays 0) + CAT-gate saturation
(waits/maxdepth into [spend]; live-visible later, sim always
0/0 — no CAT in sim).
Measured (both FFVI corpora): p50=2, p95=8–9, max=18–21,
NE 0.35/0.18, BUDGET-OK. Per-kind: publishnew @1, pageturn
@2–3, flushes @1–6; weak-hold NEVER fired in either corpus
(the feared 12–18 path didn't trigger once). CORRECTION to
the B6 entry: simLag means 30.6/15.9 were first-seen artifacts
(repeated JA across scenes, max=704); run-based definition
gives ~2 loops. B6 NE numbers stand.
Mash verdict (user's fear): typical dialogue posts ~1s after
appearing (p50=2 loops); worst measured ~10s (max 21);
catch-up flushes (dwell/flash/pageturn) exist for exactly the
autoscroll case. New batches added ~zero (parity-proven).
Remaining latency work is live-only: wall-clock conversion
(stamped corpora banking), CAT-gate sighting, weak-machine
scaling (no hardware). Replay parity 0/263 throughout.
Revert: revert b9-panellat.

## keep-shots sentinel (user-requested, SHIPPED)
`keep-shots.armed` next to `keep-log.armed`: present →
troubleshoot/ shots accumulate across launches (newest 200
kept, oldest pruned, one log line — bounded storage); absent
→ last-run-only wipe as before. Startup file handling only,
zero pipeline touch. Armed-path live proof waits for an armed
run (arming is the user's call — sentinel must be created in
%AppData%/WunderLanguage). Replay parity 0/263.
Revert: revert keep-shots.

## B1 suspect×weak-hold guard (SHIPPED, live-triage batch 1)
Cited: （１）カンテラ suspect-held 14× then weak-published
(loop 889, lag 26). Fix: lineage stain — suspect-hold sets
_suspectStainedText; cleared on all 11 adoption/clear points;
publish-weak-hold requires unstained; hold-stained observability
line. Principle preserved: suspicion closes the weak-hold
shortcut, never the breakthrough road (stable clean reads still
publish). Omission-only if wrong.
Proof: build +0/0; replay 0/263; old corpora numbers IDENTICAL
pre/post (div=0 → guard dormant — zero regression); new
stamped corpora WITH guard: Zelda 54/60 (hold-stained ×34,
カンテラ now held — the designed trade), FFVI 50/50 missed-0;
banked dims used live first time (896×788 / 897×788 — matches
live frames, re-convicts the ~1522 estimator); budget OK FFVI,
trip on Zelda via by-design weak-hold (documented, not loosened).
いたぞ ('「いたぞ」ということで、「いたぞ！' suspect-held 9×,
0 fires) recorded as flush-hardening-related instance #3
(mechanism differs: suspect-starved vs pageturn — noted).
Protocol note: rapid-sequential CLI launches lose some log
lines (3 done-lines vanished mid-chain, others survived); solo
runs are reliable — read simreplay results solo.
Revert: revert b1-stain.

## B2 まどう action-menu variance (SHIPPED, live-triage batch 2)
Cited (user + live 2026-10-10): slot 1 is not always たたかう —
Magitek intro rows show まどう; panel posted まどう as "Wow"
(CAT) and spell names as romaji garbage ("Kearu Faia").
Fix (pack-scoped): まどう appended to FFVI BattleCommands
(existing priority preserved); SplitActionRows indices floated
(fight first, magic/item last-two — identical for all existing
5-entry packs, new path only for FFVI's 6); glossary +Magitek/
Cure/Fire (official EN, user corrects); ("まどう ?"→"まほう?")
fixup kept (doubt-suffixed only, clean rows unaffected);
substring ContainsBattleCommand keeps glued rows action-side
(remainder dropped once まどう matches). Magic menu verdict:
publishes already, names were the gap. Action-fuzzy live proof
recorded ("ただか"→"たたかう" d=2). Slot-vs-shot correctness
needs next play. Build +0/0; replay 0/263-identical.
Revert: revert b2-madou.

## B3 fuzzy-name handling (SHIPPED, live-triage batch 3)
Cited (live 2026-10-10, loops 2393/2151): ログ「×3 + ＤＶＤの「×1
in Locke speaker slots — ログ/ＤＶＤ never legit in FFVI
dialogue. Fix (FFVI OcrFixups, speaker-anchored, idempotent):
ログ「→ロック「, ＤＶＤの「→ロック「. MaskNames stays
exact-only (fuzzy-name masking deferred: short-name collision
risk unmeasured; cited instances covered by fixups).
qmask LIVE PROOF (first, from new FFVI corpus): Ｐ？？？２？？
がしベルアップ → "P???2?? Level Up" ([3,2]→[3,2]) and
うけ「？？？ → EN keeps ？？？ ([3]→[3], speaker+quotes
intact). Old corpora lack both targets → replay 0/263 holds.
Live Locke-slot proof needs next play. Build +0/0.
Revert: revert b3-lockefix.

## B4 scroll-glue guard (SHIPPED, live-triage batch 4)
Cited: coal line (loop 1161 read ITSELF doubled mid-autoscroll,
stabilized via prefix-match, fired). Mechanism redirect: NOT
update policy (flash-post total = 0 all sessions; update chains
are battle-list churn + benign near-variants) — read-level
self-duplication at the fire funnel.
Predicate (calibrated offline on 406 recorded fires with the REAL
ComputeSimilarity): halves-sim ≥0.65, min total 16 (halves ≥8).
True dups ≥0.67 (coal 0.80, sign-glue 0.67–0.78, stutter 0.69),
legit multi-utterance ≤0.60, normal ≤0.22. same-start-8 rule
tried and dropped (offset-brittle). Withhold + [selfdup-hold]
log in TranslateAndPublishAsync (all fire paths; short/menu rows
skip by length); clean full read supersedes next loop.
Sim teeth: [selfdup] would-block count — 23/85 on FFVI-new,
8/8 sampled all true dups (level-up/reward doubles incl.
counter-tick variants like ６０ギル→ギル; variant-chain coal
shape). Overlaps the sim-extra class live never posted —
convergent. Updates untouched (variant chains score low).
Build +0/0; replay 0/263-identical; live proof needs next play.
Revert: revert b4-selfdup.

## B5 wall-clock latency (SHIPPED, measurement-only — live-triage batch 5)
First wall-clock numbers (stamped corpora: wt + loop stamps;
loop budget ≈ seconds budget at ~1s/loop p50 here):
loop period 0.94/1.14s p50 (p95 4–7s tails); fire wall-lag FFVI
p50 2.4s/p95 9.4/max 17.3, Zelda p50 9.1/p95 23.4/max 35.1;
per-kind: publish-new 0.9–2.4s, pageturn 2.9–9.7s, weak-hold
16.1s p50 / 35.1 max (Zelda only — the user's complaint,
quantified); scroll ~1s. Translation per line (all sessions):
CAT p50 3.0s/p95 8.6s/max 46s; DeepL-fallback p50 5.2s/p95
28.5s/max 62s. End-to-end typical ≈ 5–12s (stability +
translation); worst stacks to ~35+46s rare. CAT p95 is the
biggest typical chunk (hardware/model-bound — noted, not
touched). Weak machines stretch loop period (unmeasured —
no hardware). TITLE-5-min stays game-context-open. No product
change (docs only). Revert: revert b5 (docs-only).

## TITLE mistitle CONFIRMED (user 2026-10-10, game-context)
Zelda 17:27:35 TITLE fired mid-gameplay, right after receiving
the sword from the dying uncle — first-of-kind (user: never
seen before). Quiet-title logo detector mistook gameplay for
logo screen (10 quiet loops, hint=True). Instance queued for
title-path hardening (with loop-25 scroll-mid + 20:32 no-corpus
instances). Needs shot/log context before scoping.

## Strategic pivot (user 2026-10-10): game #3 = Paper Mario (480p)

## Zelda title+intro shot verdicts (2026-10-10, 6 bundles — keeper+corpus)
Session: 166 ocr / 4 bubbles (2 decisions + 2 fires) = ONE post
for ~6 pages. Shots 1–4 (intro movie + throne): panel EMPTY
("Waiting for dialogue...") — intro-scroll gap CONFIRMED live
(pages outrun stability; user memory of "working perfectly"
was recognition fixups, a different layer). Shots 5–6: panel
shows the Agahnim page accurately but STALE across 2+ game
pages (same bubble, game moved on) — mash failure mode.
Mechanism (not translation backlog: [spend] FIRST SIGHTING —
deepl 0/0, tcache 40→5 pruned, catgate waits0/maxdepth0):
scrolling reads never yield 2 similar loops → no candidate →
all flush paths (need _candidateText) dead; the one post used
publish-new, then nothing dislodged it. No TITLE in window
(title skipped fast or quiet loops unmet — noted).
Policy question for user (no build until answered): mashed
content — (a) keep stale bubble (current), (b) fast-track
current page (misteaching risk), (c) expire to empty on
turn-evidence-without-fire (honest emptiness)? Recommending
(c) under omission-over-misteaching. Revert: n/a (docs).
Runs tapped dry on FFVI/Zelda data; new game for new data.
Zelda/FFVI screen fixes (signs, battle/shop UI) deferred to the
return — the point is a stronger core first. Governing trio
restated: scalability, reliability, adaptability.
Onboarding plan (checklist-driven, bottleneck-8 order):
pack detection (window title) → title triple → fixups (mined)
→ vocabs → glossaries → calibration last. Higher-res note:
480p frames vs SNES crops — detection width-cap 900 +
upscale behavior re-verified on first corpus, perf watched
(OCR tails already the open tail).

## Zelda live run notes (2026-10-10, user — NO ACTION until FFVI run done)
Three big issues observed:
a) LATENCY — posts sometimes arrive ~5s+ late (open box, wait,
   shot, close, then it posts); one TITLE posted ~5 min late,
   minutes into gameplay.
b) BLOCK SPLITTING — blocks cut off first/last words, or post
   part then repeat it (copy). User hypothesis: Japanese-language
   parameters so the pipeline better understands Japanese.
c) HALLUCINATIONS — random dates, "this is japanese how should
   I translate"-class meta text.
Zelda sprite/text spec (user, from chat screenshots):
- 「 left bracket and 」 right bracket must post 1:1.
- Decision arrow (white ▶): always marks a player-choice menu =
  a "decision" category box (alongside menu/title/in-game text);
  post question + options in EXACT in-game positions; do NOT
  post the arrow itself (doesn't refresh live).
- Heart container pickup → post as <3 inline, e.g.
  ハートのうつわを見つけた ！ / <3 / ハートのレベルが１つあがった.
- Sign left-arrow → <- inline: この先 <- / カカリコ村.
- Sign down-arrow → ↓ inline: この先 ↓ / カカリコ村.
- Link-face sprite (wanted poster etc.) → ignore, not text, no symbol.
Status: recorded only. Triage + scoping after FFVI run.

## FFVI live run notes (2026-10-10, user — triage now, both runs done)
Same 3 issues as Zelda: (a) latency, (b) half-block-then-repeat,
(c) hallucinations.
Battle finding (user-diagnosed): action-menu slot 1 is NOT always
たたかう — intro shows まどう (Magitek). Fixed-4-slot assumption
broken; BattleCommands/splitter/glossary impact. Magic menu
(ケアル/ファイア + MP panel) shot banked as battle-UI reference.
Party info: ?????? for unnamed Tina (also reward bar + level-up
banner ??????がレベルアップ); named rows ウェッジ/ビックス live
(qmask territory; Wedge/Biggs EN still user call).
Half-block-then-repeat PROOF banked (panel shot: coal-mine line
posts first half twice instead of completing).
FFVI brackets: closing 」 has a curled variant (OCR confusion
candidate); ロック「 speaker-script form re-confirmed.
Decision screen (save-point はい/いいえ + arrow): same rule as
Zelda — positioned decision box, question + options in EXACT
in-game positions, arrow itself not posted.

## Live triage verdicts (2026-10-10, both runs — keeper 9.8MB/78k lines)
New stamped corpora: Zelda 1395 ocr/135 bubbles (wt 100%,
blocks 4926, conf<1 61%); FFVI 1524 ocr/222 bubbles (battle-
frame:fire 110, dialogue-fire 58). Loop stamps land (66/52).
Zelda DECISION fired live (delete-data menu, correct box);
FFVI DECISION fired on garbled read (needs the positioned-box
rule). Zelda sign reads post WITHOUT arrows; heart line
garbled by the sprite (ハート→外国/パート/ト) — sprite-spec
instances confirmed.
LATENCY: Zelda weak-hold fires live with lags 12–45 loops
(12 fires); FFVI has ZERO weak-hold fires (publish-new/
pageturn only, lags ≤38, mostly ≤7). "Open-5s-then-posts-on-
close" = flush-on-pageturn on close — policy, not loop speed.
TITLE: 4 publishes total, all logo-screen quiet-loop; 17:27
Zelda instance needs game-context check; 20:32 session has
no corpus (diag off?) — open.
SUSPECT-HOLD: 118 live holds (div up to 1.00) — first live
proof it engages. Starvation smell: top held lines (カンテラ
×14, 図書館 ×11, いたぞ-line ×9, legit dialogue ×9/×6/×4)
show ZERO later translate lines. INTERACTION BUG: （１）
カンテラ suspect-held 14× then WEAK-HOLD-published at loop
889 (lag 26) — weak-hold path doesn't check div.
NAMES: ログ/ロック/ＤＶＤ instability (pins bypassed by
misreads); ウェッジ/ビックス + ?????? live (qmask territory).
DUPLICATION: coal-mine half-posted-twice (panel proof) +
ログ/ロック update chains — update policy posts misread
variants, no clause awareness.
BATTLE: まどう rows publish (act=4, 218 lines) — slot
correctness vs shots still to verify; action-fuzzy live
("ただか"→"たたかう" d=2). Boot ERRORs + auto-restart
re-proven twice (attempt=1 → healthy). min-spec REDUCED both
runs (rec 846–1469ms) — loop pace itself is a latency input.
No profanity-hold; no meta-refusal text found in keeper
(Zelda meta-hallucination needs EN-side hunt).

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
  M7c slice v2 (decision seam + simreplay: 81/83 + 99/100 overlap),
   M7c slice v3 (block+pack feed: extras halved, 82/83 + 97/100),
   M7c slice v4 (banking kept, estimator convicted+reverted),
   Bottleneck 6 measured (erasure/lag agree sim/live; probe audit PASS; no behavior change),
   Bottleneck 7 measured (recall proxies healthy; frame audit queued for play; no behavior change),
   Bottleneck 8 priced (JA-fire delta zero; price is menu/CAT-side; checklist shipped),
   Hygiene logging shipped (spend/cache lines, first sighting needs live loop),
   Bottleneck 9 shipped (panel budget BUDGET-OK both corpora; p50=2 loops; weak-hold never fired),
   keep-shots sentinel shipped (unarmed default; arming is user call),
   B1 shipped (suspect stain; old-corpora identical; Zelda 54/60 FFVI 50/50),
   B2 shipped (madou commands/splitter/glossary; magic-menu verdict),
   B3 shipped (locke fixups; qmask live proof),
   B4 shipped (scroll-glue guard; update chains classified),
   B5 measured (wall-clock: FFVI 2.4s / Zelda 9.1s p50; CAT p50 3.0s; weak-hold 16s),
   TITLE mistitle confirmed (Zelda sword scene; queued),
   Game #3 = Paper Mario 480p (onboarding next).
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
