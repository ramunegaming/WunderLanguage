# CAT translation service (W1 split, 2026-10-17).
#
# WHY A SECOND PROCESS (evidence, not architecture astronautics):
# convicted by bisection 2026-10-17 that manga-OCR residency poisons
# safetensors weight reads (plain slice reads segfault with MangaOCR
# resident, survive without; full CAT loads crash 17/17 with OCR
# resident in either transformers 4.x or 5.x). One process can never
# hold both models safely on this machine, so the translator lives
# alone: this process imports main.py for the ENTIRE tested translate
# stack (fixups, title/script matches, fallback chain — zero drift)
# but never loads any OCR model, keeping a clean address space in
# which CAT loads reliably (4.x clean loads 2/2; pinned in
# requirements.txt). The host spawns this as `uvicorn
# cat_service:app --port 8001` with WUNDER_ROLE=cat and routes
# /translate here; OCR stays on :8000. A CAT death now kills only
# translations (host restarts just this process, OCR never blinks).
# Revert: delete this file + host CAT wiring (single-process legacy).
import os as _os

_role = _os.environ.get("WUNDER_ROLE", "")
if _role != "cat":
    print("[WunderLanguage] cat_service started without WUNDER_ROLE=cat; refusing (would duplicate the OCR service).", flush=True)
    raise SystemExit(1)

# --- crash-diagnostics (2026-10-23; revert: revert crash-diag-logger) -----
# Evidence first: 56 Windows Event 1000 records over 8 days
# (2026-10-16..23) all show torch_cpu.dll+0x8e4a279 / 0xC0000005 during
# weight reads, but NO log ever held system memory state at load time
# ("was the machine under pressure?" was unanswerable), and the host's
# stderr ring truncated faulthandler stacks to 3KB (the torch frames
# were lost on 2026-10-23). So: (1) full all-threads faulthandler
# dumps -> their own file, never truncated; (2) a 1s sampler of
# available RAM / commit / own RSS / VRAM-free -> CSV, giving the
# seconds BEFORE a death. Everything guarded: diagnostics must never be
# able to take this service down. Runs before `import main` so the
# whole import+load timeline is captured.
import threading as _thr
import ctypes as _ct
import time as _time
import sys as _sys
import faulthandler as _fh

_DIAG_LOGDIR = _os.environ.get("WUNDER_LOGDIR") or _os.path.join(
    _os.environ.get("APPDATA", ""), "WunderLanguage")
_diag_crash_file = None
_diag_csv = None


class _MSEX(_ct.Structure):
    _fields_ = [("dwLength", _ct.c_ulong), ("dwMemoryLoad", _ct.c_ulong),
                ("ullTotalPhys", _ct.c_ulonglong), ("ullAvailPhys", _ct.c_ulonglong),
                ("ullTotalPageFile", _ct.c_ulonglong), ("ullAvailPageFile", _ct.c_ulonglong),
                ("ullTotalVirtual", _ct.c_ulonglong), ("ullAvailVirtual", _ct.c_ulonglong),
                ("ullAvailExtendedVirtual", _ct.c_ulonglong)]


class _PMC(_ct.Structure):
    _fields_ = [("cb", _ct.c_uint32), ("PageFaultCount", _ct.c_uint32),
                ("PeakWorkingSetSize", _ct.c_size_t), ("WorkingSetSize", _ct.c_size_t),
                ("QuotaPeakPagedPoolUsage", _ct.c_size_t), ("QuotaPagedPoolUsage", _ct.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", _ct.c_size_t), ("QuotaNonPagedPoolUsage", _ct.c_size_t),
                ("PagefileUsage", _ct.c_size_t), ("PeakPagefileUsage", _ct.c_size_t)]


def _diag_sample():
    mb = lambda b: int(b // (1024 * 1024))
    avail = total = free_c = lim_c = load = -1
    m = _MSEX()
    m.dwLength = _ct.sizeof(_MSEX)
    if _ct.windll.kernel32.GlobalMemoryStatusEx(_ct.byref(m)):
        avail, total = mb(m.ullAvailPhys), mb(m.ullTotalPhys)
        free_c, lim_c, load = mb(m.ullAvailPageFile), mb(m.ullTotalPageFile), int(m.dwMemoryLoad)
    rss = -1
    try:
        _ct.windll.psapi.GetProcessMemoryInfo.argtypes = [_ct.c_void_p, _ct.c_void_p, _ct.c_uint32]
        _ct.windll.kernel32.GetCurrentProcess.restype = _ct.c_void_p
        p = _PMC()
        p.cb = _ct.sizeof(_PMC)
        if _ct.windll.psapi.GetProcessMemoryInfo(
                _ct.windll.kernel32.GetCurrentProcess(), _ct.byref(p), _ct.sizeof(_PMC)):
            rss = mb(p.WorkingSetSize)
    except Exception:
        pass
    vram = -1
    try:
        t = _sys.modules.get("torch")  # only if already imported - never import from here
        if t is not None and t.cuda.is_available():
            vram = mb(t.cuda.mem_get_info()[0])
    except Exception:
        vram = -2
    return (f"{_time.strftime('%H:%M:%S')}|availPhys={avail}|totalPhys={total}"
            f"|freeCommit={free_c}|commitLimit={lim_c}|load%={load}|rss={rss}|vramFree={vram}")


def _diag_sampler():
    try:
        while True:
            try:
                if _diag_csv is not None:
                    _diag_csv.write(_diag_sample() + "\n")
                    _diag_csv.flush()
            except Exception:
                pass
            _time.sleep(1.0)
    except Exception:
        pass


def _diag_boot():
    global _diag_crash_file, _diag_csv
    try:
        if not _os.path.isdir(_DIAG_LOGDIR):
            print(f"[diag] logdir missing: {_DIAG_LOGDIR}", flush=True)
            return
        # Append (not truncate): a restart must not erase the previous
        # death's dump. faulthandler writes directly to the fd; keep the
        # handle alive for the process lifetime.
        _diag_crash_file = open(_os.path.join(_DIAG_LOGDIR, "cat_crash_full.log"), "a", buffering=1)
        _fh.enable(file=_diag_crash_file, all_threads=True)
        _diag_csv = open(_os.path.join(_DIAG_LOGDIR, "diag_cat_load.csv"), "a", buffering=1)
        _diag_csv.write(f"# boot pid={_os.getpid()} role=cat time={_time.strftime('%Y-%m-%dT%H:%M:%S')}\n")
        _diag_csv.flush()
        _thr.Thread(target=_diag_sampler, daemon=True, name="diag-mem").start()
        print("[diag] crash-diagnostics armed (full faulthandler + 1s mem sampler)", flush=True)
    except Exception as _ex:
        print(f"[diag] arm failed (continuing): {_ex}", flush=True)


_diag_boot()

import asyncio as _aio
from fastapi import FastAPI as _FastAPI

import main as _main

# Torch + device globals live in main's namespace (set by its OCR
# stage, which never runs here). Wire them explicitly before loading.
try:
    import torch as _torch
    _main.torch = _torch
    _main.DEVICE = _main.resolve_device()
    _main.USE_GPU = (_main.DEVICE == "cuda")
except Exception as _ex:
    print(f"[WunderLanguage] cat_service torch setup failed ({_ex}).", flush=True)

app = _FastAPI()


@app.get("/health")
async def health():
    # Cat-only view (main's /health reports both stages; the OCR stage
    # never exists in this process).
    try:
        cat = _main.READY.get("cat", "warming")
    except Exception:
        cat = "warming"
    try:
        code = _main.CODE_VERSION
    except Exception:
        code = "py-dev"
    ok_states = ("ready", "disabled", "disabled-low-vram")
    return {"status": "ok" if cat in ok_states else "starting",
            "cat": cat, "code_version": code}


@app.post("/translate")
async def translate_text(request: _main.TranslateRequest):
    translated, unreliable, reason = await _aio.to_thread(
        _main.cat_translate, request.text, request.source_lang, request.target_lang)
    try:
        code = _main.CODE_VERSION
    except Exception:
        code = "py-dev"
    return {"translated_text": translated, "unreliable": unreliable,
            "reason": reason, "code_version": code}


def _boot():
    # Load SYNCHRONOUSLY before serving (convicted 2026-10-17: any
    # concurrent server activity — uvicorn event loop, health polls —
    # during the weight load segfaults; quiet loads always survive).
    # Initialization-before-traffic is textbook server design; the
    # 30-60s startup cost lands only on this process (OCR serves
    # immediately, translations fall back meanwhile per the host
    # chain). Daemon-thread background loading is what kills us.
    try:
        print(f"[WunderLanguage] cat_service starting (code {_main.CODE_VERSION}), loading CAT synchronously before serving.", flush=True)
        _main._load_cat_stage()
    except Exception as _ex:
        print(f"[WunderLanguage] cat_service boot load failed ({_ex}).", flush=True)


_boot()
