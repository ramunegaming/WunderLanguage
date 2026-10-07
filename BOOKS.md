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
- B5 positional-instability (HDIP): growth excluded, equal-length settled
  runs read near-perfect (max 2 disagreements at any position over 52
  runs) — no drift-shaped positional effect, no position-weighted trust
  needed. D1 consensus labels implied trivial by the same result.
- B4 combiner rank-fusion (HDIP Ch 11): closed UNIMPLEMENTED with data —
  3 real battle frames, 13 classical boxes, zero IoU>0.5 contests with
  detector boxes. The proposers cover different regions by design; there is
  no cross-proposer contest to fuse. Revisit only if the classical-vs-detector
  contest ever fires live (observable in [blocks] cls markers).
