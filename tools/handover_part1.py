# Part 1: mission, principles, architecture, environment.
SECTIONS = [
("1. What this document is", [
"p:This is the full handover for WunderLanguage, a live Japanese-to-English game-translation overlay. It exists so a new chat session can operate the project at full understanding without re-learning months of measured decisions from scratch.",
"p:How to use it: treat sections 2-3 as binding operating rules, sections 4-6 as verified architecture and history, sections 7-9 as the compounding core of the project, sections 10-14 as current state and plans, section 15 as the questions only the user can answer. Do not re-litigate shipped verdicts without new evidence. Every behavior batch in this project ships with a revert phrase; they are listed with the batches.",
"p:Standing fact about evidence: panel and log proof outrank compile proof. Corpora under %AppData%\\WunderLanguage\\corpus\\ are append-only evidence and are never rewritten. Replay parity and simreplay decide whether behavior changed.",
]),
("2. Mission and product objective", [
"p:The mission is a reusable, adaptable system that captures Japanese text displayed in games and publishes faithful Japanese text alongside an English translation. The long-term goal is a defined collection of onboarded games, with the system becoming more capable as each game is added. Shared improvements must strengthen existing games as well as future ones — never a collection of unrelated hand-tuned game implementations.",
"p:Three stages, kept distinct. Stage 1 — capture Japanese: detect genuine in-game text, recognize the characters, reconstruct across frames where useful, publish. The objective is every genuine line including brief or one-time appearances; repeated visibility must not be a requirement. Stage 2 — preserve fidelity: published Japanese matches what the game displayed, character for character wherever visual evidence permits. Never substitute plausible Japanese for unreadable characters. Stage 3 — translate: the captured Japanese goes to the translation model; translation quality is evaluated independently and never repairs the source.",
"p:Completeness and fidelity are both primary. Missing real dialogue is a failure; publishing unsupported Japanese is also a failure. The system must achieve both rather than optimizing one by sacrificing the other. For an educational tool the release bar is on the order of one misteaching per two hours of play.",
]),
("3. Governing principles and operating rules", [
"h:3.1 The trio",
"b:Reliability — capture genuine in-game Japanese as completely and faithfully as possible; keep published text aligned with on-screen text; omission is preferred over misteaching, but omission of difficult text is itself a failure to fix, not a success to keep.",
"b:Scalability — prefer shared, reusable, game-blind capabilities when they solve the underlying problem; new game onboarding should improve or validate shared systems; game-specific rules only on evidence; never improve one game by silently degrading another.",
"b:Adaptability — survive resolution, font, UI, text-speed and setup variance; support transient dialogue, typewriter text, menus, titles and other contexts without forcing all through one rule; degrade gracefully and keep seeking recovery for ambiguous text.",
"h:3.2 Evidence discipline",
"b:Measures first, then implementation. One focused change at a time. Every batch ships a revert phrase. Replay parity plus simreplay decide behavior preservation. Panel and log proof, never compile proof alone. Fires alone are never a success criterion — newly recovered Japanese is reviewed against recorded evidence, and merged or incorrect lines count as regressions.",
"b:Corpora never rewritten; add evidence or labels, never rewrite history. Exact-match-only glossary and fixup rules unless a change is supported by evidence. Docs and papers are reference-only: relevance listed before acting, no action until the user approves, no implementation without a cited failure or measured win, no model training or finetuning, no model internals.",
"h:3.3 Run protocol",
"b:The user plays; the user never runs commands. Every run request ships exact instructions plus keeper state. Minimize runs — single confirmatory runs. Read simreplay and replay results solo (rapid-sequential launches lose log lines). Red means an old exe is still running: taskkill /F /IM WunderLanguage.Host.exe first, no asking needed.",
"h:3.4 Content and roadmap rules",
"b:Profanity blocked only when hallucinated (English profane plus no Japanese alibi — legitimate adult-game content stays). Names use official manual English forms (Mash per manual, reverses Matthew; Wedge/Biggs English still an open user call). Unknown names masked through translation and restored after. Unknown JRPG names (?????? runs) masked through. Shop UI waits on the user's shop-play signal. Text-only communication except geometry or truth shots.",
]),
("4. System architecture", [
"h:4.1 Pipeline",
"b:Capture loop (~1s p50): window capture with focus gate (unfocused emulator holds, no OCR) and static-frame skip; OCR via sidecar (EasyOCR detection stock params, manga-ocr recognition, beamed 4 live, 2x upscale, 900px detection cap); stability machine (candidate formation, growth/typewriter tracking, pageturn/flush roads, suspect/stain guards, duplicate and subpart suppression); publish decision kinds (publish-new, growth, flash, pageturn, flush variants, singleton, weak-hold); translation downstream (DeepL primary, CAT-Translate offline fallback, translation cache global and shared).",
"b:Title path: quiet-loop logo detector with hint gating (caps/year/copyright), logo-geometry gate, gameplay veto (no TITLE within 60 loops of dialogue), TITLE-grace prompt window, pack FullTitle triple. Battle path (FFVI): geometry splitter, slots, glossaries, name memo. Menu path: geometry-aware tracking with divergence guards.",
"h:4.2 Packs",
"b:Pack = game-scoped data and behavior: signatures (routing by proper-noun votes), title triple, OCR/display fixups, battle/monster/item/weapon vocabs, battle and name glossaries, vision calibration (neutral until cited). Unknown games run universal (degradation baseline: never bank guesses, never post TITLE). Registry routes by ROM-normalized id, window title, and content votes; ties keep current.",
"h:4.3 Sidecar and services",
"b:Python sidecar on 127.0.0.1:8000 (leave alone): OCR + CAT translation. 6GB VRAM budget (RTX 3060 laptop class). Survival: job-object ownership, CPU cap with clamp, completion-port detect with 3-attempt auto-restart, low-memory listener, power request, WER keys. Restart budget exhaustion now raises an in-panel engine-dead notice (no popup, no focus steal). Deaths dump uptime, exit code, and ring tail for forensics.",
"h:4.4 Evidence and metrics",
"b:Corpus: append-only JSONL (ocr reads with blocks/conf/geometry/beams, decision bubbles with kinds and loop stamps, translation fires). Replay: fixup/normalize path parity (drift must be 0). Simreplay: stability/commit simulation with fire overlap vs recorded, missed/extra sets, [b6metrics] (NE updates-per-fire, lag in loops), [panellat] budgets (p50<=10, p95<=30, max<=60, NE<=0.5), [selfdup] would-block, [spend] translation economics. Keepers: keep-log.armed and keep-shots.armed sentinels (newest 200 kept).",
]),
("5. Environment and constraints", [
"b:Windows, Debug as daily driver, 12 CPU / 32GB reference machine, second own Python server on :8000 left alone, Defender exclusions applied. No model training or finetuning, no model internals, no architectures busting 6GB VRAM. No shop UI until the user signals shop play. Books program completed; papers read for applicable concepts only.",
]),
]
