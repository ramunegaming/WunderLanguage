# WunderLanguage — Performance Notes

Measured numbers from real sessions, not estimates. Every figure below
is reproducible from `app.log` markers — no profiler required.

## Service-level objectives

"Reliable" made measurable. Checked per session from existing
instruments; a breach is a bug report with a number attached.

| Objective | Target | Source line |
|---|---|---|
| Capture p99 | < 50 ms | `[loop-cost] cap` |
| OCR round-trip p99 | < 2 s | `[loop-cost] ocr-rt` |
| Cold-row publish p99 | < 5 s | `[loop-cost] pub` (CAT gate bounds worst case ≈ 4 s) |
| Startup to usable | < 90 s | `[startup] stage=connect-shown` |
| Crash-free launches | 100% | any `Sidecar startup failed` / unhandled exception with a working sidecar |

Current standing: capture ~17–38 ms, OCR tails to ~4 s on GPU-contention outliers (above target — the open tail), publish 1.2 s, startup 14–46 s. Objectives are targets, not boasts; misses drive the work list.

Measured on: 12 CPU, 32 GB RAM, RTX 3060 Laptop GPU, Windows 11,
Debug build. Lighter machines will see longer OCR/startup; the
`[min-spec]` line auto-selects the reduced matching profile when
warmup means exceed det > 250 ms or rec > 800 ms.

## Startup (cold, models cached)

| Stage | Time | Log marker |
|---|---|---|
| App start → loading window | ~0.5 s | `[startup] stage=loading-shown` |
| Sidecar spawn → API bound | ~0.4 s | `[sstartup] stage=sidecar-imports-done` |
| OCR models ready | ~11–12 s | `[startup] stage=sidecar-healthy` |
| Connect window usable | ~14–15 s | `[startup] stage=connect-shown` |
| Translator (CAT) ready | +6–9 s behind | `[startup] stage=cat-ready` |

First-ever launch downloads ~6–7 GB of models once (up to minutes
on slow connections); every launch after that loads from disk.
Translations requested while CAT warms fall back silently
(glossary → DeepL path); OCR works the moment the app is usable.

Biggest historical win: Windows Defender exclusions for
`ocr-service\venv` + the HuggingFace cache cut the import phase ~25×
(27 s → under 1 s even reboot-cold). Model weight reads stay
cache-sensitive (MangaOCR load 16 s cold vs 9 s warm), so total
cold startup is ~23 s vs ~14 s warm. Keep exclusions on release
machines.

## Steady-state loop (per settled frame)

| Stage | p50 | p99 | Log marker |
|---|---|---|---|
| Capture (BitBlt) | 12–14 ms | 17–38 ms | `[loop-cost] cap` |
| OCR round-trip | ~450–550 ms | 1–4 s | `[loop-cost] ocr-rt` |
| — detection | ~120–140 ms | 0.3–0.6 s | `det` |
| — recognition | ~250–360 ms | 1–2 s | `rec` |
| Publish (fire → panel) | ~2 ms warm / ~1.2 s per cold row | up to ~8 s | `[loop-cost] pub` |

Loop cadence ≈ 70 iterations/min, roughly half skipped as
unchanged frames. Larger capture windows double capture cost
(~25 ms) but detect/recognize *faster* (bigger glyphs); small
windows (408×313) run the fastest profile yet (OCR ~220 ms p50).
Tight capture regions remain the best loop-speed lever (4–7× observed).
Geometry verified size-proof in both directions (0.43×–2.16×):
same rows post, no drops or merges from the absolute thresholds.

Warm refires publish in ~2 ms (persistent translation cache +
prefetch + last-published reuse + flicker-tolerant name memo —
see `[cache-hit]` rates). Cold rows burn one full CAT translate
each; a frame costs its slowest row (fan-out, capped at 4 concurrent
offline translates after ramp-proving GPU serialization — wide cold
fires dropped from ~8 s to ~1.2 s p99).

## Design rules that produced these numbers

- Serve the API first, warm models behind (staged readiness).
- Never assume exclusive hardware: no thread/CPU pinning, shed
  work (skip frames, slow the loop) instead of queueing it.
- Caches are measured (`[cache-hit]` interval rates), never assumed.
- One log line per minute per instrument — observability must not
  be the tax (audited: ~0.2 ms per log call).
- Recognition batching via crop collage: rejected with data.
  Collaging N crops into one call is ~2–7× faster (launch/transfer
  overhead dominates small calls) but destroys accuracy — stacked
  layout + gutter breaks the manga-page-trained model (merged,
  confused, or dropped lines vs clean separate reads). Speed at
  the cost of readings violates the project rules; do not revisit
  without a layout-preserving batching design.
- Pressure is watched per-minute (host MB, GPU util/VRAM) — note:
  per-PID VRAM attribution rarely populates on Windows (WDDM driver
  model); system-wide GPU util/memory is the usable signal there.
- Every batch ends with a revert phrase; `pre-perf-baseline` tag
  (+ full file backup) rewinds the whole performance arc.

## Known upstream issues (release-relevant)

These come from our dependencies, not our code. Recorded so
release triage starts here instead of from zero:

- **bitsandbytes 4-bit load fragility (Windows).** The CAT weight
  quantize has segfaulted at shard 1 under load (exit
  -1073741819). Two guards, in order: a VRAM pre-check skips the
  load when free GPU memory is short (`disabled-low-vram`,
  fail-open), and a one-shot host retry falls back to OCR-only
  mode (`WUNDER_NO_CAT`) if the process still dies. OCR is
  unaffected in all cases; translation degrades to the
  glossary → DeepL chain.
- **HuggingFace rate limits on first run.** Without `HF_TOKEN`,
  hub requests are unauthenticated and can be rate-limited —
  which matters exactly once per machine, during the ~7 GB
  first-run model download (slow or failing download reads as
  "translation engine won't start"). `HF_TOKEN`, when set on the
  machine, passes through to the sidecar automatically (process
  environment inheritance — nothing to configure in-app).
  Release notes should say so.
- **Transformers tokenizer warnings** (`generation_config` /
  `max_new_tokens`, BPE cleanup notices) are upstream noise.
  Harmless; filtered from the log, left alone in code — do not
  "fix" them with settings changes that alter generation.
