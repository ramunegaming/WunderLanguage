# WunderLanguage

Live Japanese game-text capture and translation overlay — built for learners playing Japanese games in English.

Attach it to an emulator window, set a game region, and WunderLanguage captures Japanese text as it appears in-game, publishing faithful Japanese alongside an English translation in a live side panel. Japanese first, always: the published source must match what's on screen before anything gets translated.

- Website: https://www.ramunesoft.com
- WunderLanguage page: https://www.ramunesoft.com/software/wunderlanguage

## What it does

- **Window capture loop** (~1s): focus-gated capture with static-frame skip, so it only works while you play.
- **OCR sidecar (Python):** EasyOCR detection + manga-ocr recognition with beam candidates, served locally.
- **Stability machine (C#):** candidate formation, growth/typewriter tracking, pageturn and scroll roads, duplicate suppression — decides what is genuine, complete, and worth publishing.
- **Per-game packs:** signatures, title handling, vocabularies, glossaries, and calibration scoped per title — on top of shared, game-blind systems that improve with every onboarded game.
- **Translation downstream:** DeepL primary with an offline fallback, translation cache, and name masking.

## What makes it interesting

- **Compounding loops:** banked correction pairs, confusion re-mining, battle-name memory, vocabulary self-proposal, per-game stability profiles — symbolic learning (no model training) where each onboarded game strengthens the shared system for all games.
- **Evidence discipline:** append-only capture corpora, replay parity checks, simulated replays with fire-overlap metrics, and a hand-read truth eval set. Behavior changes are measured before they ship.
- **Two runtimes, one budget:** a C# host plus a Python OCR sidecar cooperating under a 6GB VRAM budget with process survival (job ownership, auto-restart, death forensics).

## Status

Active development. See [ROADMAP.md](ROADMAP.md) for the milestone tracker, [BOOKS.md](BOOKS.md) for the engineering log, and [troubleshoot-sample/](troubleshoot-sample/) for real capture samples. `eval/truth.jsonl` documents the hand-verified evaluation set.

## Running it

This repository is a development snapshot for review, not a release — there are no packaged builds or run scripts included. Running it from source needs Windows, .NET 8, a Python OCR environment, emulators with Japanese titles, and your own DeepL key.
