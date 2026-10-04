# WunderLanguage — Performance Notes

Measured numbers from real sessions, not estimates. Every figure below
is reproducible from `app.log` markers — no profiler required.

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
`ocr-service\venv` + the HuggingFace cache cut the import phase ~4×
(27 s → 7 s). Keep them on release machines.

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
- Pressure is watched per-minute (host MB, GPU util/VRAM) — note:
  per-PID VRAM attribution rarely populates on Windows (WDDM driver
  model); system-wide GPU util/memory is the usable signal there.
- Every batch ends with a revert phrase; `pre-perf-baseline` tag
  (+ full file backup) rewinds the whole performance arc.
