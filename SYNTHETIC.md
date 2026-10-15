# Synthetic Regression Frames — Specification

Purpose: offline test frames for rare text styles (glow, outlines,
pixel fonts, compressed streams) to evaluate detection/OCR/panel
behavior. Evaluation data ONLY — never for weight updates, never
fed back as training (circularity + bias feedback).

## Source material

Start from REAL captures (troubleshoot PNGs, live frames), never
purely invented layouts. Real frames carry true geometry, fonts,
and UI context; synthesis adds style variance on top.

## Allowed transforms (photometric only)

All preserve glyph identity and reading order:

- Brightness/contrast jitter (bounded, e.g. ±15%)
- Gaussian blur, small radius only (1–2 px at 900p — models
  defocus, not destruction)
- Gaussian/monochrome noise (bounded SNR — models sensor noise)
- JPEG recompression, quality floor 70 (models stream compression)
- Gamma tweaks (mild — models display variance)

Deterministic: seeded RNG, parameters logged per frame (data
lineage — transform chain must be reproducible from the log).

## Forbidden transforms

- Flips and rotations (break reading order; kana/voicing pairs
  like バ/パ and b/d-class shapes become different characters)
- Color inversion (text polarity matters to detectors)
- Crops that cut glyphs, elastic warps, perspective tilts
- Downscales below legibility (that's aliasing testing, a
  different experiment — see resolution variance)

## Equivariance rule

Any geometric op ever allowed must transform boxes identically
(crop/shift the image ⇒ shift the boxes by the same vector), or
replay/detection keys corrupt. Prefer photometric-only so this
rule stays vacuous.

## Acceptance for the future generator

1. Byte-identical output for a fixed seed (rerun twice, compare).
2. Each frame ships expected text; pass = OCR output matches
   directly or after the current fixup chain (no new rows to
   satisfy synthetic frames — real citations only).
3. Volume discipline: a handful of variants per style, not an
   explosion. Rare styles are edge cases; the suite stays small
   and fast enough to run per batch.
