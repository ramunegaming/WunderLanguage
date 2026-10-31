namespace WunderLanguage.Host.Diagnostics
{
    // Error-code feed system (2026-10-23; revert: revert error-codes).
    // WHY: two consecutive sessions died silently at the translation
    // step - a spent restart budget was log-only and the only visible
    // sign was "no bubbles". The panel now carries ONE compact amber
    // chip (SidePanelWindow.ShowError) whose text updates in place as
    // state worsens (E1 retrying -> E2 spent); clicking the chip opens
    // a popup with the detailed cause + fix. All wording lives HERE so
    // the chip line and the popup can never drift apart, and so the
    // next code is one registry entry + one event call.
    // WIRING (all through two SidecarProcessManager events):
    //   E1 = first CAT death inside the load window
    //        (ErrorCodeNotice) - makes a retrying burst visible instead
    //        of silent;
    //   E2/E3 = restart budget exhausted, cat/ocr role (EngineDown,
    //        which now carries the CODE instead of free text);
    //   E4 = capture lost the game window (AppShell.OnCaptureWindowLost).
    // E5/E6 are registry-complete but NOT wired: their triggers need a
    // signal the host does not have yet (a VRAM reading surfaced to the
    // host, a provider-health probe). Wire them through the same two
    // events when the signal exists - do not re-word them here.
    // Chrome, never translation data: chips stay out of the side
    // panel's _history, so copy-all and the corpus miner never see
    // them (same rule as ShowNotice).
    // Revert: revert error-codes (delete this file + ShowError/popup +
    // the three subscriptions; EngineDown goes back to carrying text).
    public sealed class ErrorCodeInfo
    {
        public string Code { get; init; } = "";
        // Compact feed row - one glanceable line, no paragraphs.
        public string Chip { get; init; } = "";
        // Popup header.
        public string Title { get; init; } = "";
        // What happened: mechanism, blast radius, what keeps working.
        public string Detail { get; init; } = "";
        // What to do: concrete steps + where evidence lives for reports.
        public string Fix { get; init; } = "";
    }

    public static class ErrorCodes
    {
        public static ErrorCodeInfo? Get(string code)
        {
            switch (code)
            {
                case "E1": return E1;
                case "E2": return E2;
                case "E3": return E3;
                case "E4": return E4;
                case "E5": return E5;
                case "E6": return E6;
                case "E7": return E7;
                case "E8": return E8;
                default: return null;
            }
        }

        // Translator crashed during its model load; retries are running.
        public static readonly ErrorCodeInfo E1 = new()
        {
            Code = "E1",
            Chip = "Error E1 \u00B7 Translation engine failed to start \u2014 retrying",
            Title = "E1 \u2014 Translation engine failed to start",
            Detail = "The translator process crashed while loading its AI model \u2014 inside its 15\u201325 second " +
                "weight-load window \u2014 before it could serve a single line. The program is already retrying with " +
                "increasing delays (10 seconds, then 1 minute, then 4 minutes), and dialogue posts again the moment " +
                "one attempt gets through. Detection stays healthy throughout: titles and menus still post from the " +
                "disk translation cache, but every live dialogue line needs this translator and stays hidden until a " +
                "retry succeeds. This machine has a known streaky crash with a fixed fault in its translation library " +
                "(torch) that comes and goes with machine state \u2014 each death is fully recorded in app.log with the " +
                "fault module, offset, and the RAM/VRAM numbers from right before it.",
            Fix = "Wait \u2014 the retries usually clear it within a minute or two, and this chip updates in place if " +
                "attempts keep failing (it only turns into E2 if the whole restart budget runs out). If you'd rather " +
                "not wait: relaunch WunderLanguage, rebooting first if this is the second streak today \u2014 a reboot " +
                "clears streak state most reliably. For a bug report, copy the \"[job:cat] death forensics\" blocks " +
                "from app.log; they contain everything needed.",
        };

        // Translator budget spent; engine stays down until relaunch.
        public static readonly ErrorCodeInfo E2 = new()
        {
            Code = "E2",
            Chip = "Error E2 \u00B7 Translation engine stopped \u2014 relaunch to resume",
            Title = "E2 \u2014 Translation engine stopped",
            Detail = "The translator crashed four times in a row and its restart budget is spent, so it will not try " +
                "again until you relaunch WunderLanguage. Translation is the only casualty: detection keeps running, " +
                "old titles and menus still post from the disk cache, and every new dialogue line is dropped at the " +
                "translation step \u2014 no bubble appears. Your game is completely unaffected; no input is sent to it " +
                "and nothing can touch your saves or files. This is a known streak on this machine (an 8-day-old " +
                "fixed-offset crash in the translation library) \u2014 the program itself is not damaged.",
            Fix = "Relaunch WunderLanguage to resume. If it happens again immediately, reboot first \u2014 the streak " +
                "follows machine state, not your data. When reporting it, attach app.log: every \"[job:cat] death " +
                "forensics\" block carries the fault module + offset and the memory timeline for that death, and " +
                "diag_cat_load.csv holds per-second RAM/VRAM around each crash.",
        };

        // OCR budget spent; no new text can be read at all.
        public static readonly ErrorCodeInfo E3 = new()
        {
            Code = "E3",
            Chip = "Error E3 \u00B7 Text reader stopped \u2014 relaunch to resume",
            Title = "E3 \u2014 Text reader (OCR) stopped",
            Detail = "The OCR process \u2014 the part that reads text off the game screen \u2014 crashed four times in a " +
                "row and its restart budget is spent. Nothing new can be detected until you relaunch, so the feed " +
                "stays silent even where the game is visibly showing dialogue; cached titles and menus may still " +
                "appear, but no fresh text exists at all. Your game is unaffected and no input is touched.",
            Fix = "Relaunch WunderLanguage. If it repeats, the \"[job:ocr] death forensics\" blocks in app.log hold " +
                "each crash's fault module, offset and memory numbers \u2014 those lines are what a bug report needs.",
        };

        // Capture lost the game/emulator window handle.
        public static readonly ErrorCodeInfo E4 = new()
        {
            Code = "E4",
            Chip = "Error E4 \u00B7 Game window lost \u2014 reconnect to resume",
            Title = "E4 \u2014 Game window lost",
            Detail = "Capture lost the connection to the emulator/game window \u2014 it most likely closed, crashed, " +
                "or restarted, changing its window handle. Detection has stopped, so no new text will be posted. " +
                "The translation engine is still running; only the window attachment is gone.",
            Fix = "Open the Connect window and reconnect to the emulator \u2014 reconnect even if the window looks " +
                "unchanged, since that re-establishes the handle. Posting resumes as soon as the reconnect succeeds.",
        };

        // Registry-only until the host has a VRAM reading to fire it from.
        public static readonly ErrorCodeInfo E5 = new()
        {
            Code = "E5",
            Chip = "Error E5 \u00B7 GPU low on video memory",
            Title = "E5 \u2014 GPU low on video memory",
            Detail = "Your GPU has less free video memory than the translator needs to load its model safely (about " +
                "4 GB). While VRAM is this tight the translator can crash partway through its load \u2014 or the " +
                "program falls back to much slower CPU processing \u2014 which shows up as missing or badly delayed " +
                "dialogue.",
            Fix = "Close other GPU-heavy apps (browser video, games, streaming tools) and relaunch WunderLanguage; " +
                "a reboot resets video memory completely. Evidence for reports: the memory-snapshot line inside " +
                "app.log's \"[job:cat] death forensics\" blocks, plus diag_cat_load.csv, which records free VRAM " +
                "every second during loads.",
        };

        // Registry-only until a provider-health probe exists to fire it.
        public static readonly ErrorCodeInfo E6 = new()
        {
            Code = "E6",
            Chip = "Error E6 \u00B7 No translation source available",
            Title = "E6 \u2014 No translation source available",
            Detail = "No translation source is answering: the offline translator is the selected provider but its " +
                "process is not responding, and no DeepL API key is configured as a fallback. Detection keeps " +
                "running, but bubbles cannot be produced from any path.",
            Fix = "Relaunch WunderLanguage to restart the translator (errors E1/E2 usually come first and explain " +
                "why it stopped). For a durable fix, add a DeepL API key in Settings \u2014 that gives every failed " +
                "line a second translation source instead of a single point of failure.",
        };

        // Transport death (item 2, 2026-10-30; revert: revert
        // transport-watchdog). The class that killed the FFVI leg
        // silently that day: the OCR sidecar's HTTP listener broke
        // (accept-coroutine died, WinError 64) while its PROCESS stayed
        // alive, so no crash/restart signal ever fired — the hold
        // counter ran 1->120 over five minutes with zero visible sign.
        // Fired from the capture loop's transport catch at the watchdog
        // threshold, and again on recovery; the LIVE line (captured at
        // the call site, appended to the chip + popup) carries the two
        // facts the user requires: WHEN it died and WHAT caused it
        // (first-failure time, exact connection error, consecutive
        // count, sidecar PID, restart status). Wording lives here.
        public static readonly ErrorCodeInfo E7 = new()
        {
            Code = "E7",
            Chip = "Error E7 \u00B7 Text reader connection lost",
            Title = "E7 \u2014 Text reader (OCR) connection lost",
            Detail = "The text reader stopped answering mid-session: its process is still running, but the connection " +
                "to it was refused \u2014 its request listener broke without the process ever exiting, so no crash " +
                "signal fired. Detection pauses from that moment (the line above shows when it died, the exact " +
                "connection error, how many attempts in a row failed, and the reader's process ID). This is the " +
                "silent-death class first seen 2026-10-30: five minutes of live frames with nothing posted and " +
                "nothing on screen explaining why.",
            Fix = "Nothing to do by hand \u2014 the program now kills the wedged reader and restarts it the moment the " +
                "failure threshold is reached (the line above says whether that restart was triggered); posting " +
                "resumes when the reader is warm again, usually well under a minute, and the chip's line updates " +
                "to 'recovered' when it does. If the reader cannot come back three times in a row the terminal E3 " +
                "chip takes over instead. For a bug report, copy the '[job:ocr]' and 'OCR transport hold' blocks " +
                "from app.log around the time on the chip \u2014 they hold the listener error verbatim.",
        };

        // Capture degraded (item 3, 2026-10-30; revert:
        // revert circuit-capture-continue). The G1 circuit-open chip:
        // before this entry the hold was invisible — 2026-10-30 PM leg
        // held 83 loops under GPU contention with nothing on screen
        // saying why posts stopped arriving. The live line (fired from
        // the loop at the threshold) carries when the slowdown started,
        // how many loops held, and the exact probe error; a second fire
        // refreshes it to 'recovered'. Capture-continue changed the
        // semantics underneath: while open the loop still captures and
        // reads every 4th cycle, so this is a slow-down chip, never a
        // stopped-camera chip. Wording lives here.
        public static readonly ErrorCodeInfo E8 = new()
        {
            Code = "E8",
            Chip = "Error E8 \u00B7 Text reader overloaded \u2014 reduced capture rate",
            Title = "E8 \u2014 Text reader overloaded (reduced capture rate)",
            Detail = "The text reader did not answer its health check inside the time budget \u2014 usually because the " +
                "translation model has the graphics card busy (both share the GPU, saturating around it). Capture " +
                "is NOT stopped: the program keeps capturing and reads every 4th cycle instead of every cycle, so " +
                "dialogue still posts while the reader catches up \u2014 it just arrives slower than normal. The line " +
                "above shows when the slowdown started, how many cycles were held, and the reader's last probe error.",
            Fix = "Nothing to do \u2014 it clears by itself the moment the reader answers again, and the line above " +
                "updates to 'recovered' when it does. If it never clears, close other GPU-heavy apps or restart the " +
                "session; the '[g1] circuit open' lines in app.log hold the exact timing for a bug report.",
        };
    }
}
