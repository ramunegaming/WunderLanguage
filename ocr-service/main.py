import time as _ptime
_SSTART = _ptime.perf_counter()
def _slog(stage):
    # Perf baseline batch 2 (observe-only): startup stage timestamps so
    # the 22s spawn-to-healthy window splits into import/model stages.
    # Lines flow to app.log via the host's sidecar-stdout relay.
    # Revert: delete this helper + all _slog calls.
    try:
        print(f"[sstartup] stage={stage} ms={int((_ptime.perf_counter() - _SSTART) * 1000)}", flush=True)
    except Exception:
        pass
from fastapi import FastAPI, UploadFile, File, HTTPException
from pydantic import BaseModel
from PIL import Image
import numpy as np
import io
import os
import threading as _threading
import time
import sys
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass
from packs import zelda_pack as _zelda_pack
import hashlib as _hashlib
import pathlib as _pathlib
_slog("imports-done")
try:
    CODE_VERSION = "py-" + _hashlib.sha256(_pathlib.Path(__file__).read_bytes()).hexdigest()[:8]
except Exception:
    CODE_VERSION = "py-dev"
app = FastAPI()
try:
    import onnxruntime as ort
    print(f"[WunderLanguage] onnxruntime available providers: {ort.get_available_providers()}")
except ImportError:
    print("[WunderLanguage] onnxruntime not installed yet (expected until the manga-ocr-torchless swap is done).")
def resolve_device() -> str:
    print(f"[WunderLanguage] torch.__version__={torch.__version__}, "
          f"torch.version.cuda={torch.version.cuda}, "
          f"torch.cuda.is_available()={torch.cuda.is_available()}")
    if torch.version.cuda is None:
        print("[WunderLanguage] This torch build has NO CUDA support compiled in at all.")
    try:
        if torch.cuda.is_available():
            name = torch.cuda.get_device_name(0)
            print(f"[WunderLanguage] CUDA GPU detected: {name} - using GPU acceleration.")
            return "cuda"
    except Exception as ex:
        print(f"[WunderLanguage] CUDA device query failed ({ex}) - falling back to CPU.")
        return "cpu"
    print("[WunderLanguage] No CUDA-capable GPU detected - using CPU.")
    return "cpu"
# Staged readiness (perf batch: serve the API first, warm models
# behind). Uvicorn binds in seconds; the loader thread started below
# brings OCR then CAT online in order. Endpoints 503 with the stage
# reason while their stage warms; /health reports per-stage state.
# torch/easyocr/MangaOcr import inside the OCR stage (they pull ~24s
# cold: torch CUDA wheels + torchvision + sympy/scipy). Globals below
# are rebound by the loader thread before first use.
DEVICE = "warming"
USE_GPU = False
mocr = None
reader = None
READY = {"ocr": "warming", "cat": "warming"}
MIN_BOX_WIDTH = 10
MIN_BOX_HEIGHT = 10
DETECTION_UPSCALE_FACTOR = 1
RECOGNITION_UPSCALE_FACTOR = 2
def merge_nearby_boxes(boxes, x_gap=20, y_gap=15):
    # Perf/vision batch (box confidence v0): also returns detector
    # multiplicity per merged box — a region where the detector fired
    # 5 overlapping boxes is stronger text evidence than a singleton.
    # Returns [(box, nmerged)]. Single caller below.
    merged = []
    used = [False] * len(boxes)
    if not boxes:
        return []
    # M6.2 (steadiness): horizontal gap scales with the frame's tallest
    # block (split word-groups rejoin on tall dialogue); vertical gap
    # stays 15 so menu rows, HUD stacking, and ASCII grouping never move.
    try:
        _max_h = max(b[3] - b[1] for b in boxes)
    except Exception:
        _max_h = 0
    x_gap = max(20, min(40, int(_max_h * 0.2)))
    y_gap = 15
    boxes = sorted(boxes, key=lambda b: (b[1], b[0]))
    used = [False] * len(boxes)
    merged = []
    for i, box in enumerate(boxes):
        if used[i]:
            continue
        x_min, y_min, x_max, y_max = box
        used[i] = True
        _nmerged = 1
        changed = True
        while changed:
            changed = False
            for j, other in enumerate(boxes):
                if used[j]:
                    continue
                ox_min, oy_min, ox_max, oy_max = other
                horizontally_close = ox_min < x_max + x_gap and ox_max > x_min - x_gap
                vertically_close = oy_min < y_max + y_gap and oy_max > y_min - y_gap
                if horizontally_close and vertically_close:
                    x_min, y_min = min(x_min, ox_min), min(y_min, oy_min)
                    x_max, y_max = max(x_max, ox_max), max(y_max, oy_max)
                    used[j] = True
                    changed = True
                    _nmerged += 1
        merged.append(((x_min, y_min, x_max, y_max), _nmerged))
    return merged
def propose_classical_boxes(img_np):
    # Vision batch item 2 (classical box proposer, learning-mode
    # fallback): dialogue boxes ARE blocks world — axis-aligned
    # rectangles on flat fills. Gradient/contour proposal with zero
    # training, for games whose styling the frozen detector misses.
    # Runs for every frame but tagged src=classical; the HOST decides
    # (learning mode unions, vocab mode drops — tuned path untouched).
    # Returns [(x0,y0,x1,y1, rect_score)] in DETECTION coords.
    out = []
    try:
        import cv2 as _cv2
    except Exception:
        return out
    try:
        h, w = img_np.shape[0], img_np.shape[1]
        if h < 40 or w < 40:
            return out
        gray = _cv2.cvtColor(img_np, _cv2.COLOR_RGB2GRAY) if len(img_np.shape) == 3 else img_np
        frame_area = float(w * h)
        seen = []
        for thresh, _ in ((_cv2.THRESH_BINARY + _cv2.THRESH_OTSU, False), (_cv2.THRESH_BINARY_INV + _cv2.THRESH_OTSU, True)):
            _, bw = _cv2.threshold(gray, 0, 255, thresh)
            contours, _ = _cv2.findContours(bw, _cv2.RETR_EXTERNAL, _cv2.CHAIN_APPROX_SIMPLE)
            for c in contours:
                area = float(_cv2.contourArea(c))
                if area < 800.0 or area > 0.70 * frame_area:
                    continue
                peri = _cv2.arcLength(c, True)
                approx = _cv2.approxPolyDP(c, 0.02 * peri, True)
                if len(approx) != 4 or not _cv2.isContourConvex(approx):
                    continue
                x, y, bw_, bh_ = _cv2.boundingRect(approx)
                if bw_ < 1 or bh_ < 1:
                    continue
                if float(bw_) / float(bh_) < 1.2:
                    continue
                rect_score = area / float(bw_ * bh_)
                if rect_score < 0.75:
                    continue
                # Dedupe across polarities (same box found twice).
                dup = False
                for (sx0, sy0, sx1, sy1, _) in seen:
                    ix0, iy0 = max(x, sx0), max(y, sy0)
                    ix1, iy1 = min(x + bw_, sx1), min(y + bh_, sy1)
                    iw, ih = max(0, ix1 - ix0), max(0, iy1 - iy0)
                    union = bw_ * bh_ + (sx1 - sx0) * (sy1 - sy0) - iw * ih
                    if union > 0 and (iw * ih) / union > 0.5:
                        dup = True
                        break
                if dup:
                    continue
                seen.append((x, y, x + bw_, y + bh_, round(min(1.0, rect_score), 3)))
        out = seen
    except Exception:
        out = []
    return out
def detect_and_ocr(pil_image: Image.Image):
    det_scale = DETECTION_UPSCALE_FACTOR
    if pil_image.width * det_scale > 900:
        det_scale = 900 / pil_image.width
    detection_image = pil_image.resize(
        (max(1, int(pil_image.width * det_scale)), max(1, int(pil_image.height * det_scale))),
        Image.LANCZOS,
    )
    img_np = np.array(detection_image)
    _t_det0 = time.time()
    horizontal_list, free_list = reader.detect(img_np)
    _det_ms = (time.time() - _t_det0) * 1000.0
    raw_boxes = []
    for x_min, x_max, y_min, y_max in horizontal_list[0]:
        raw_boxes.append((int(x_min), int(y_min), int(x_max), int(y_max)))
    for quad in free_list[0]:
        xs = [pt[0] for pt in quad]
        ys = [pt[1] for pt in quad]
        raw_boxes.append((int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))))
    merged_boxes = merge_nearby_boxes(raw_boxes)
    # M6 (steadiness): frame-dominant area. Dialogue scenes have one
    # dominant block; HUD riders are short and tiny beside it. Menu
    # scenes have no dominant block, so every row survives this.
    _dom_area = 0.0
    for (_qx0, _qy0, _qx1, _qy1), _qn in merged_boxes:
        _qa = float((_qx1 - _qx0) / det_scale) * float((_qy1 - _qy0) / det_scale)
        if _qa > _dom_area:
            _dom_area = _qa
    _raw_n = len(raw_boxes)
    _skipped = 0
    # Classical fallback runs every frame (cheap contours) but is TAGGED;
    # the host unions it only in learning mode. Deduped here so the
    # detector always wins overlaps (tuned path byte-identical).
    _work = [(_b, _n, None) for (_b, _n) in merged_boxes]
    try:
        for (_px0, _py0, _px1, _py1, _ps) in propose_classical_boxes(img_np):
            _dup = False
            for (_qb, _qn) in merged_boxes:
                _qx0, _qy0, _qx1, _qy1 = _qb
                _ix0, _iy0 = max(_px0, _qx0), max(_py0, _qy0)
                _ix1, _iy1 = min(_px1, _qx1), min(_py1, _qy1)
                _iw, _ih = max(0, _ix1 - _ix0), max(0, _iy1 - _iy0)
                _un = (_px1 - _px0) * (_py1 - _py0) + (_qx1 - _qx0) * (_qy1 - _qy0) - _iw * _ih
                if _un > 0 and (_iw * _ih) / _un > 0.5:
                    _dup = True
                    break
            if not _dup:
                _work.append((((_px0, _py0, _px1, _py1), 0, _ps)))
    except Exception:
        pass
    _t_rec0 = time.time()
    _crop_px = 0
    blocks = []
    for (_wb, _nmerge, _cls) in _work:
        (x_min, y_min, x_max, y_max) = _wb
        orig_x_min = x_min / det_scale
        orig_y_min = y_min / det_scale
        orig_x_max = x_max / det_scale
        orig_y_max = y_max / det_scale
        orig_width = orig_x_max - orig_x_min
        orig_height = orig_y_max - orig_y_min
        if orig_width < MIN_BOX_WIDTH or orig_height < MIN_BOX_HEIGHT:
            continue
        if orig_height < 50 and (orig_width * orig_height) < 0.05 * _dom_area:
            _skipped += 1
            continue
        recognition_crop = pil_image.crop((orig_x_min, orig_y_min, orig_x_max, orig_y_max))
        recognition_crop = recognition_crop.resize(
            (
                max(1, int(recognition_crop.width * RECOGNITION_UPSCALE_FACTOR)),
                max(1, int(recognition_crop.height * RECOGNITION_UPSCALE_FACTOR)),
            ),
            Image.LANCZOS,
        )
        _crop_px += recognition_crop.width * recognition_crop.height
        text = mocr(recognition_crop)
        if not text.strip():
            continue
        # Box confidence v0 (geometric prior ONLY — not a calibrated
        # probability; fusion with text/temporal cues is the roadmap).
        # Classical boxes carry their rectangularity score instead.
        if _cls is not None:
            _conf = round(max(0.0, min(1.0, float(_cls))), 3)
            _src = "classical"
        else:
            _src = "detector"
            try:
                # size_m: margin above the MIN noise floors, saturating.
                # dom: share of the frame-dominant area (dialogue ~1, riders ~0).
                _size_m = ((orig_width - MIN_BOX_WIDTH) / 60.0 + (orig_height - MIN_BOX_HEIGHT) / 48.0) / 2.0
                _size_m = max(0.0, min(1.0, _size_m))
                _dom = (orig_width * orig_height) / max(1.0, 0.05 * _dom_area)
                _dom = max(0.0, min(1.0, _dom))
                _conf = round(0.5 * _size_m + 0.5 * _dom, 3)
            except Exception:
                _conf = 0.0
        blocks.append({
            "text": text,
            "box": [int(orig_x_min), int(orig_y_min), int(orig_width), int(orig_height)],
            "area": orig_width * orig_height,
            "conf": _conf,
            "nmerge": int(_nmerge),
            "src": _src,
        })
    _rec_ms = (time.time() - _t_rec0) * 1000.0
    try:
        print(f"[ocr-timing] det={_det_ms:.0f}ms rec={_rec_ms:.0f}ms raw={_raw_n} merged={len(merged_boxes)} kept={len(blocks)} skip={_skipped} scal={det_scale:.2f} px={_crop_px}")
    except Exception:
        pass
    primary_text = ""
    if blocks:
        # HUD strips (tiny h) must never outrank dialogue for primary.
        tall = [b for b in blocks if b["box"][3] >= 50]
        pool = tall if tall else blocks
        primary_block = max(pool, key=lambda b: b["area"])
        # Same-row band join: small trailing blocks on the anchor's row
        # (sign attributions like 国王) belong to the line. X-ordered.
        # Tracker reads raw blocks, so menus/titles are untouched.
        try:
            _ax, _ay, _aw, _ah = primary_block["box"]
            _y_tol = min(_ah, 60) / 2 + 8
            _row = [primary_block]
            for b in blocks:
                if b is primary_block:
                    continue
                _bx, _by, _bw, _bh = b["box"]
                if abs(_by - _ay) >= _y_tol:
                    continue
                _t = (b["text"] or "").strip()
                if not _t:
                    continue
                if _bh >= 50:
                    _row.append(b)  # tall: existing behavior, unchanged
                elif re.fullmatch(r'[．.…・… \u3000]+', _t):
                    _row.append(b)  # dot rows: leading/trailing … (sleep/cell)
                elif _bx < _ax + _aw + 150 and _bx + _bw > _ax - 150:
                    _row.append(b)  # small but x-near: sign attributions (国王)
            if len(_row) > 1:
                _row = sorted(_row, key=lambda b: (b["box"][1] // 40, b["box"][0]))
                primary_text = " ".join(b["text"] for b in _row)
            else:
                primary_text = primary_block["text"]
        except Exception:
            primary_text = primary_block["text"]
    words = {"content": 0, "total": 0}
    try:
        if primary_text and _fugashi_tagger is not None:
            tot = 0
            con = 0
            for _w in _fugashi_tagger(primary_text.strip()):
                try:
                    _p = _w.feature.pos1 if hasattr(_w.feature, 'pos1') else ""
                except Exception:
                    continue
                _s = _w.surface
                if not _s or not _s.strip():
                    continue
                tot += 1
                if _p in ("名詞", "動詞", "形容詞", "副詞"):
                    con += 1
            words = {"content": con, "total": tot}
    except Exception:
        pass
    _timing = {"det_ms": round(_det_ms, 1), "rec_ms": round(_rec_ms, 1), "raw": _raw_n, "merged": len(merged_boxes), "kept": len(blocks), "skip": _skipped, "scale": round(det_scale, 3), "px": _crop_px}
    return primary_text, blocks, words, _timing

CAT_TRANSLATE_MODEL_ID = "cyberagent/CAT-Translate-3.3b-beta"
# No-CAT fallback (cited 2026-10-01: CAT 4-bit GPU weight load segfaults
# (-1073741819) at shard 1 under load — bitsandbytes-on-Windows fragility,
# not our code; host retries with WUNDER_NO_CAT=1 after a load crash).
# OCR stays GPU-fast; /translate degrades to the host fallback chain
# (glossary pins + DeepL path keep battle UI intact). Title/script matches
# below are pure-python and keep working in this mode.
_NO_CAT = os.environ.get("WUNDER_NO_CAT", "").strip() == "1"
_CAT_SKIP_REASON = ""
cat_translate_pipeline = None
def _load_ocr_stage():
    # Stage 1: torch + OCR models (~24s cold, ~4s warm). Sequential on
    # one daemon thread; the API is already serving /health by now.
    global torch, easyocr, MangaOcr, DEVICE, USE_GPU, mocr, reader
    import torch
    import easyocr
    from manga_ocr import MangaOcr
    DEVICE = resolve_device()
    USE_GPU = DEVICE == "cuda"
    mocr = MangaOcr()
    _slog("mangaocr-loaded")
    reader = easyocr.Reader(["ja"], gpu=USE_GPU)
    _slog("easyocr-loaded")
    READY["ocr"] = "ready"
    print("[WunderLanguage] OCR stage ready.", flush=True)
def _load_cat_stage():
    # Stage 2: translator. Runs after OCR (GPU contention + the quantize
    # segfault must never take OCR down with it). Exceptions mark the
    # stage failed (reported via /health; /translate keeps fallback
    # blank) instead of killing the process blind. A segfault still
    # kills the process — the host's nocatfb retry owns that path.
    global cat_translate_pipeline
    if _NO_CAT:
        READY["cat"] = "disabled"
        print("[WunderLanguage] CAT-Translate disabled by WUNDER_NO_CAT=1 (OCR-only mode).", flush=True)
        return
    # VRAM pre-check (perf batch, 2nd book pass: check the limit before
    # hitting it — the quantize segfaults under memory pressure and the
    # nocatfb retry only rescues afterwards). Fail-OPEN: a broken query
    # must never disable translation by itself. CPU path skips (host-RAM
    # pressure is a different question). Threshold override for tests:
    # WUNDER_CAT_MIN_FREE_MB (default 3072).
    global _CAT_SKIP_REASON
    if DEVICE == "cuda":
        try:
            _min_free_mb = int(os.environ.get("WUNDER_CAT_MIN_FREE_MB", "3072"))
        except Exception:
            _min_free_mb = 3072
        try:
            _free_b, _ = torch.cuda.mem_get_info()
            _free_mb = _free_b // (1024 * 1024)
            if _free_mb < _min_free_mb:
                _CAT_SKIP_REASON = "low-vram"
                READY["cat"] = "disabled-low-vram"
                print(f"[WunderLanguage] CAT-Translate skipped: only {_free_mb}MB VRAM free (need {_min_free_mb}MB) - /translate keeps fallback-blank.", flush=True)
                return
            print(f"[WunderLanguage] CAT-Translate VRAM check: {_free_mb}MB free, proceeding.", flush=True)
        except Exception as _ex:
            print(f"[WunderLanguage] CAT-Translate VRAM check inconclusive ({_ex}) - proceeding.", flush=True)
    try:
        from transformers import AutoTokenizer, AutoModelForCausalLM, pipeline as hf_pipeline, BitsAndBytesConfig
        _slog("transformers-imported")
        _cat_translate_dtype = torch.float16 if DEVICE == "cuda" else torch.float32
        print(f"[WunderLanguage] CAT-Translate: loading tokenizer for {CAT_TRANSLATE_MODEL_ID}...", flush=True)
        _cat_translate_tokenizer = AutoTokenizer.from_pretrained(CAT_TRANSLATE_MODEL_ID)
        print("[WunderLanguage] CAT-Translate: tokenizer loaded.", flush=True)
        _slog("cat-tokenizer")
        if DEVICE == "cuda":
            print(f"[WunderLanguage] CAT-Translate: loading 4-bit quantized weights...", flush=True)
            _quant_config = BitsAndBytesConfig(
                load_in_4bit=True,
                bnb_4bit_quant_type="nf4",
                bnb_4bit_compute_dtype=torch.float16,
                bnb_4bit_use_double_quant=True,
            )
            _cat_translate_model = AutoModelForCausalLM.from_pretrained(
                CAT_TRANSLATE_MODEL_ID,
                quantization_config=_quant_config,
                device_map="auto",
            )
            print("[WunderLanguage] CAT-Translate: 4-bit quantized model loaded on GPU.", flush=True)
            _slog("cat-weights-gpu")
        else:
            print(f"[WunderLanguage] CAT-Translate: loading full-precision on CPU.", flush=True)
            _cat_translate_model = AutoModelForCausalLM.from_pretrained(
                CAT_TRANSLATE_MODEL_ID,
                dtype=_cat_translate_dtype,
            )
            print("[WunderLanguage] CAT-Translate: model loaded on CPU.", flush=True)
            _slog("cat-weights-cpu")
        print("[WunderLanguage] CAT-Translate: constructing generation pipeline...", flush=True)
        cat_translate_pipeline = hf_pipeline(
            "text-generation",
            model=_cat_translate_model,
            tokenizer=_cat_translate_tokenizer,
        )
        print(f"[WunderLanguage] CAT-Translate ready on {DEVICE}.", flush=True)
        _slog("cat-pipeline-ready")
        READY["cat"] = "ready"
    except Exception as ex:
        READY["cat"] = f"failed: {ex}"
        print(f"[WunderLanguage] CAT stage failed ({ex}) - /translate keeps fallback-blank.", flush=True)
def _load_all_stages():
    try:
        _load_ocr_stage()
    except Exception as ex:
        READY["ocr"] = f"failed: {ex}"
        print(f"[WunderLanguage] OCR stage failed ({ex}).", flush=True)
        return
    _load_cat_stage()
_threading.Thread(target=_load_all_stages, daemon=True).start()
print(f"[WunderLanguage] sidecar code version {CODE_VERSION}.")
_slog("sidecar-imports-done")
LANGUAGE_CODE_NAMES = {"ja": "Japanese", "en": "English"}
import re
import difflib
_KATAKANA_RE = re.compile(r'^[\u30A1-\u30FA\u30FC]+$')
_KANJI_RE = re.compile(r'[\u4E00-\u9FAF]')
_JA_CHAR_RE = re.compile(r'[\u3040-\u30ff\u4e00-\u9faf]')
_WS_RE = re.compile(r'\s+')
_TRAIL_ELLIPSIS_RE = re.compile(r'[…\.。\s]+$')
_JA_NEG_RE = re.compile(r'(なく|ない|ません|なかった|ではない|ぬ|(?<!は)ずに|(?<!は)ず)')
_EN_NEG_RE = re.compile(r'\b(no|not|never|none|nothing|neither|nor|without|isn\'t|aren\'t|wasn\'t|weren\'t|don\'t|doesn\'t|didn\'t|haven\'t|hasn\'t|hadn\'t|won\'t|wouldn\'t|couldn\'t|shouldn\'t|can\'t)\b', re.IGNORECASE)
_REFUSAL_RE = re.compile(r'can.t help with that|ready to translate|provide the Japanese|need the Japanese passage|happy to help translate|not sure if you.*familiar|do my best to translate it into English', re.IGNORECASE)
_PROMPT_ECHO_RE = re.compile(r'(translat\w*|transform\w*).{0,25}following Japanese text.{0,30}english|following Japanese text.{0,40}translat\w+ into english|ready to translate|provide the Japanese (passage|text)', re.IGNORECASE)
_RUN_RE = re.compile(r'([A-Za-z0-9])\1{6,}')
_EN_STUTTER = [(r'\bTha\b', 'That'), (r'\btwas\b', 'was'), (r'\bTthe\b', 'The'), (r'\bteh\b', 'the')]
_ZELDA_EN_NOUNS = [(r'\b[Hh]iranal\b', 'Hyrule'), (r'\bHilar\b', 'Hyrule'), (r'\bAgnim\b', 'Agahnim')]
# Item-coverage family: JA katakana item present but EN lacks it = invention.
# Shape, not instance: add rows, never new logic. Blank over misteach.
_JA_EN_COVERAGE = [(r'ブーメラン', r'boomerang', 'boomerang-missing')]
def _coverage_fail(src: str, out: str) -> str:
    try:
        for ja_pat, en_pat, reason in _JA_EN_COVERAGE:
            if re.search(ja_pat, src) and not re.search(en_pat, out, re.IGNORECASE):
                return reason
    except Exception:
        pass
    return ""
# Zelda intro script pack: fixed lines get reference translations instantly
# (no GPU, deterministic). Template for per-game script packs later.
_ZELDA_SCRIPT = [
    ("はるか昔　山と森にかこまれた美しき　ハイラルの大地…",
     "Long ago, surrounded by mountains and forests, the beauteous land of Hyrule..."),
    ("全知全能の力を持つ黄金がこの地方のどこかにあると伝えられていました",
     "It was said that somewhere in this region was gold possessing the powers of omnipotence and omniscience."),
    ("そのハイラルの王国で黄金がかくされた聖地への入口がみつかり",
     "The entrance to the sacred land where the gold was hidden was discovered in that Hyrule Kingdom, and"),
    ("多くの人々があらそって聖地へと消えていました",
     "Many people fought, and vanished into the sacred land."),
    ("しかし戻ってきた者はなくまた、悪しき力がそこからわきでてきたと言うことです",
     "However, there were none who returned. Worse, evil power came gushing forth from there."),
    ("そこで国の王は７人の賢者に命じ聖地への入口を封印させたそうです",
     "Thereupon, the country's king commanded Seven Sages and had the entrance to the sacred land sealed."),
    ("封印は２度と解かれる事はないはずでした",
     "The seal should never again have been undone."),
    ("……しかし、それらのでき事が伝説となったころ…",
     "....however, when those events had become legend..."),
    ("ここに、封印をとこうとたくらむ、ナゾの司祭アグニムがいました。彼は国王をほうむり…。",
     "Here, there was the mysterious priest Aghanim, who plotted to dissolve the seal. He got the king out of his way, and..."),
    ("城の兵達をあやつって７賢者の血をひく娘達を、つぎつぎイケニエにささげていったのです。",
     "Manipulating the castle's soldiers, he offered as sacrifices maidens descended from the Seven Sages, one after another."),
    ("そして、王女ゼルダがイケニエにささげられる運命の時も、もうそこまでせまっていました",
     "Then, the fateful time when Princess Zelda would be offered as a sacrifice, too, had already drawn that near."),
]
def _script_key(s: str) -> str:
    s = s.replace("\u3000", " ")
    s = _WS_RE.sub("", s)
    s = re.sub(r'[、。．\.…・！？＂"「」『』…]+', '', s)
    return s
_SCRIPT_KEYS = [(_script_key(ja), en) for ja, en in _ZELDA_SCRIPT]
# Per-game title pack (mirrors DeepL ZeldaTitleMap): exact JA row -> pinned EN.
_ZELDA_TITLES = {
    "ゼルダの伝説 神々のトライフォース": "The Legend of Zelda: Triforce of the Gods",
    "ゼルダの伝説": "The Legend of Zelda",
}
def _title_match(text: str):
    return _ZELDA_TITLES.get(text.strip())

# Phase B shadow-only: confusion observer. Log-only, never alters output.
_CONFUSION_SEED = ["封印", "賢者", "ハイラル", "ゼルダ", "アグニム", "聖地", "入口", "黄金", "司祭", "娘達", "運命"]

def _confusion_lev_le2(a: str, b: str) -> int:
    if a == b:
        return 0
    la, lb = len(a), len(b)
    if abs(la - lb) > 2:
        return 3
    prev = list(range(lb + 1))
    for i in range(1, la + 1):
        cur = [i] + [0] * lb
        row_min = cur[0]
        ai = a[i - 1]
        for j in range(1, lb + 1):
            cost = 0 if ai == b[j - 1] else 1
            cur[j] = min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost)
            if cur[j] < row_min:
                row_min = cur[j]
        if row_min > 2:
            return 3
        prev = cur
    return prev[lb]

def _confusion_is_content(sub: str) -> bool:
    for c in sub:
        if ('\u4e00' <= c <= '\u9faf' or '\u3400' <= c <= '\u4dbf'):
            return True
        if ('\u30a1' <= c <= '\u30fa' or c == '\u30fc'):
            return True
    return False

def _confusion_esc(s: str) -> str:
    return s.encode('unicode_escape').decode('ascii')

def _log_confusion_matches(text: str):
    if not text or len(text) > 200:
        return
    t = text.strip()
    if not t:
        return
    logged = 0
    for seed in _CONFUSION_SEED:
        L = len(seed)
        if L == 0:
            continue
        max_dist = 1 if L <= 2 else 2
        for win_len in (L,):
            if win_len < 2 or win_len > len(t):
                continue
            for i in range(len(t) - win_len + 1):
                sub = t[i:i + win_len]
                if sub == seed:
                    continue
                if not _JA_CHAR_RE.search(sub):
                    continue
                if not _confusion_is_content(sub):
                    continue
                d = _confusion_lev_le2(sub, seed)
                if 1 <= d <= max_dist:
                    print(f"[confusion] \"{_confusion_esc(sub)}\" ~ \"{_confusion_esc(seed)}\" in \"{_confusion_esc(t[:60])}\"")
                    logged += 1
                    if logged >= 5:
                        return
                    break
            if logged >= 5:
                return

def _script_match(text: str):
    key = _script_key(text)
    if not key:
        return None
    best = None
    best_ratio = 0.0
    for skey, en in _SCRIPT_KEYS:
        r = difflib.SequenceMatcher(None, key, skey).ratio()
        if r > best_ratio:
            best_ratio = r
            best = en
    if best is not None and best_ratio >= 0.92:
        return best
    return None
try:
    from fugashi import Tagger
    _fugashi_tagger = Tagger()
    print("[WunderLanguage] fugashi available - auto-normalization enabled.")
except Exception:
    _fugashi_tagger = None
    print("[WunderLanguage] fugashi not available - normalization disabled.")
def clean_for_translation(text: str) -> str:
    cleaned = text.replace("\u3000", " ")
    cleaned = _WS_RE.sub(" ", cleaned).strip()
    cleaned = re.sub(r'[・．\.＂"]+$', '…', cleaned)
    cleaned = re.sub(r'！{2,}$', '！', cleaned)
    cleaned = re.sub(r'[…．\.]{2,}', '…', cleaned)
    return cleaned
def normalize_for_translation(text: str) -> str:
    original = text
    try:
        if _fugashi_tagger is not None:
            replacements = {}
            for word in _fugashi_tagger(text):
                try:
                    pos = word.feature.pos1 if hasattr(word.feature, 'pos1') else str(word.feature).split(',')[0]
                    lemma = word.feature.lemma if hasattr(word.feature, 'lemma') else None
                except Exception:
                    continue
                surf = word.surface
                if (pos == "名詞" and _KATAKANA_RE.match(surf) and len(surf) >= 2
                    and lemma and lemma != surf and _KANJI_RE.search(lemma)):
                    replacements[surf] = lemma
                elif (pos == "形容詞" and len(surf) >= 2
                    and lemma and lemma != surf and _KANJI_RE.search(lemma)):
                    replacements[surf] = lemma
            for surf, lemma in replacements.items():
                if surf in text:
                    text = text.replace(surf, lemma)
            if replacements:
                print(f"[WunderLanguage] auto-normalize applied count={len(replacements)}")
    except Exception:
        print("[WunderLanguage] auto-normalize failed, using original.")
        text = original
    text = re.sub(r'.{1,3}をとこう', '封印をとこう', text)
    text = re.sub(r'[MＭｍｍ][AＡａａ][PＰｐｐ]', 'ＭＡＰ', text)
    text = text.replace('昨年ＰＣ', 'ＭＡＰ')
    # L1 mirror (universal, same as host): SEE and TRANSLATE must match.
    # Button-token: <kanji/kana>ボタン → Ｂボタン. Latin A/B/X/Y/START never match here.
    # Visit/seal: seals open, visits don't. True 訪問 without 開 passes through.
    # Idempotent: outputs contain no match-input, so no rematch.
    text = re.sub(r'[一-鿿ァ-ヶ]{1,3}ボタン', 'Ｂボタン', text)
    text = re.sub(r'[一-鿿ァ-ヶ]{1,3}(?:オ)?(?<![ボポ])タン(?=.*押す)', 'Ｂボタン', text)
    text = re.sub(r'[昨映][年午日]?ＰＣ?を手に入れた', 'ＭＡＰを手に入れた', text)
    text = text.replace('訪問を再び開', '封印を再び開')
    text = text.replace('訪問を開', '封印を開')
    text = re.sub(r'^[〈〉『』「」【】\'" ]+', '', text)
    text = re.sub(r'[〈〉『』「」【】]+', '', text)
    # M8 moved: instances live in packs/zelda_pack.py. Same order (no bracket interaction).
    text = _zelda_pack.apply_instance_fixups(text)
    text = re.sub(r'イケ[ァ-ヶー]+', '生け贄', text)
    if "イケニエ" in text:
        text = text.replace("イケニエ", "生け贄")
        print("[WunderLanguage] safety-net ikenie katakana applied.")
    if "いけにえ" in text:
        text = text.replace("いけにえ", "生け贄")
        print("[WunderLanguage] safety-net ikenie hiragana applied.")
    return text
def apply_english_fixups(out: str, src_text: str) -> str:
    for pat, rep in _EN_STUTTER:
        out = re.sub(pat, rep, out)
    if ("ハイラル" in src_text or "アグニム" in src_text or "ゼルダ" in src_text):
        for pat, rep in _ZELDA_EN_NOUNS:
            out = re.sub(pat, rep, out)
    return out
def is_degenerate_english(text: str) -> bool:
    if not text:
        return False
    if _RUN_RE.search(text):
        return True
    if len(text) >= 30 and len(set(text.lower())) <= 12:
        return True
    toks = text.lower().split()
    if len(toks) >= 12:
        from collections import Counter
        top = Counter(zip(toks, toks[1:])).most_common(1)[0][1]
        if top >= max(4, len(toks) // 3):
            return True
    if len(text) < 20:
        return False
    toks = text.split()
    if len(toks) >= 20:
        uniq = len(set(toks))
        if uniq <= max(3, len(toks) // 4):
            return True
    if re.search(r'(?:\b\w\b\s*){8,}', text):
        return True
    return False
def _is_bad_output(out: str) -> str:
    if not out:
        return "empty"
    if _REFUSAL_RE.search(out):
        return "refusal"
    if _PROMPT_ECHO_RE.search(out):
        return "prompt-echo"
    if _JA_CHAR_RE.search(out):
        return "wrong-language"
    if is_degenerate_english(out):
        return "degenerate-loop"
    return ""
# Romaji tripwire (generic, all games): pure-Latin output that merely
# transliterates the kana source is not English. Short names (1-2 tokens)
# stay exempt so proper nouns keep posting verbatim.
_ROMAJI_MAP = {}
for _k, _r in [
    ("ア","a"),("イ","i"),("ウ","u"),("エ","e"),("オ","o"),
    ("カ","ka"),("キ","ki"),("ク","ku"),("ケ","ke"),("コ","ko"),
    ("サ","sa"),("シ","shi"),("ス","su"),("セ","se"),("ソ","so"),
    ("タ","ta"),("チ","chi"),("ツ","tsu"),("テ","te"),("ト","to"),
    ("ナ","na"),("ニ","ni"),("ヌ","nu"),("ネ","ne"),("ノ","no"),
    ("ハ","ha"),("ヒ","hi"),("フ","fu"),("ヘ","he"),("ホ","ho"),
    ("マ","ma"),("ミ","mi"),("ム","mu"),("メ","me"),("モ","mo"),
    ("ヤ","ya"),("ユ","yu"),("ヨ","yo"),
    ("ラ","ra"),("リ","ri"),("ル","ru"),("レ","re"),("ロ","ro"),
    ("ワ","wa"),("ヲ","wo"),("ン","n"),
    ("ガ","ga"),("ギ","gi"),("グ","gu"),("ゲ","ge"),("ゴ","go"),
    ("ザ","za"),("ジ","ji"),("ズ","zu"),("ゼ","ze"),("ゾ","zo"),
    ("ダ","da"),("ヂ","ji"),("ヅ","zu"),("デ","de"),("ド","do"),
    ("バ","ba"),("ビ","bi"),("ブ","bu"),("ベ","be"),("ボ","bo"),
    ("パ","pa"),("ピ","pi"),("プ","pu"),("ペ","pe"),("ポ","po"),
]:
    _ROMAJI_MAP[_k] = _r
    try:
        _ROMAJI_MAP[chr(ord(_k) - 0x60)] = _r  # hiragana counterparts
    except Exception:
        pass
_SMALL_KANA_VOWEL = {"ャ":"a","ュ":"u","ョ":"o","ゃ":"a","ゅ":"u","ょ":"o","ァ":"a","ィ":"i","ゥ":"u","ェ":"e","ォ":"o","ぁ":"a","ぃ":"i","ぅ":"u","ぇ":"e","ぉ":"o"}
def _romaji_fold(s: str) -> str:
    try:
        pieces = []
        for ch in (s or ""):
            if ch in ("ッ", "っ"):
                pieces.append("\x01")  # sokuon: double next consonant
                continue
            if ch == "ー":
                for c in reversed(pieces):
                    if c in "aeiou":
                        pieces.append(c)
                        break
                continue
            if ch in _SMALL_KANA_VOWEL:
                if pieces and pieces[-1] not in ("", "\x01"):
                    prev = pieces[-1]
                    if prev.endswith("i") and len(prev) >= 2:
                        pieces[-1] = prev[:-1] + _SMALL_KANA_VOWEL[ch]
                        continue
                pieces.append(_SMALL_KANA_VOWEL[ch])
                continue
            r = _ROMAJI_MAP.get(ch)
            if r is None:
                continue
            pieces.append(r)
        flat = "".join(pieces)
        flat = re.sub("\x01([bcdfghjklmnpqrstvwxyz])", r"\1\1", flat)
        return flat.replace("\x01", "")
    except Exception:
        return ""
def _romaji_output(src: str, out: str) -> bool:
    try:
        words = re.findall(r"[a-z]{2,}", (out or "").lower())
        if len(words) < 3:
            return False
        folded = _romaji_fold(src or "")
        if len(folded) < 6:
            return False
        hits = 0
        for w in words:
            if w in folded:
                hits += 1
                continue
            L = len(w)
            for ln in (L - 1, L, L + 1):
                if ln < 2 or ln > len(folded):
                    continue
                for i in range(len(folded) - ln + 1):
                    if _confusion_lev_le2(w, folded[i:i + ln]) <= 1:
                        hits += 1
                        break
                else:
                    continue
                break
        return hits / len(words) >= 0.6
    except Exception:
        return False
def _output_bad(src_text: str, out: str, target_lang: str) -> str:
    bad = _is_bad_output(out) if target_lang.lower() == "en" else ""
    if not bad and target_lang.lower() == "en":
        bad = _coverage_fail(src_text, out)
    if not bad and target_lang.lower() == "en" and _romaji_output(src_text, out):
        print(f"[WunderLanguage] flagged unreliable reason=romaji for \"{_confusion_esc(out[:60])}\"")
        bad = "wrong-language"
    return bad
def is_negation_mismatch(ja: str, en: str) -> bool:
    if not ja or not en:
        return False
    if _JA_NEG_RE.search(ja) and not _EN_NEG_RE.search(en):
        return True
    return False
def strip_trailing_ellipsis(text: str) -> str:
    stripped = _TRAIL_ELLIPSIS_RE.sub("", text).strip()
    return stripped if stripped else text
def _parse_out(response):
    try:
        return response[0]["generated_text"][-1]["content"].strip()
    except (KeyError, IndexError, TypeError) as ex:
        print(f"[WunderLanguage] CAT-Translate response shape unexpected: {type(response)} ({ex})")
        return ""
def _run_generation(src_text: str, src_lang_name: str, tgt_lang_name: str):
    prompt = CAT_TRANSLATE_PROMPT.format(src_lang=src_lang_name, tgt_lang=tgt_lang_name, src_text=src_text)
    user_input = [{"role": "user", "content": prompt}]
    _t0 = time.time()
    # Short battle rows must not pay a 120-token generation: cap output tokens
    # by input length (long dialogue still gets the full 120).
    _max_tokens = max(40, min(120, len(src_text) * 3))
    response = cat_translate_pipeline(user_input, max_new_tokens=_max_tokens, do_sample=False)
    dt = time.time() - _t0
    print(f"[WunderLanguage] CAT-Translate generation took {dt:.2f}s for {len(src_text)} input characters.")
    return _parse_out(response)
CAT_TRANSLATE_PROMPT = "Translate the following {src_lang} text into {tgt_lang}.\n\n{src_text}"
def _log_word_stats(text: str):
    if not text or len(text) > 200:
        return
    try:
        if _fugashi_tagger is None:
            return
        total = 0
        content = 0
        nouns = 0
        verbs = 0
        for word in _fugashi_tagger(text.strip()):
            try:
                pos = word.feature.pos1 if hasattr(word.feature, 'pos1') else str(word.feature).split(',')[0]
            except Exception:
                continue
            surf = word.surface
            if not surf or not surf.strip():
                continue
            total += 1
            if pos in ("名詞", "動詞", "形容詞", "副詞"):
                content += 1
                if pos == "名詞":
                    nouns += 1
                elif pos == "動詞":
                    verbs += 1
        print(f"[words] content={content}/{total} nouns={nouns} verbs={verbs} in \"{_confusion_esc(text.strip()[:40])}\"")
    except Exception:
        pass

def cat_translate(text: str, source_lang: str, target_lang: str):
    try:
        _log_confusion_matches(text)
    except Exception:
        pass
    try:
        _log_word_stats(text)
    except Exception:
        pass
    if target_lang.lower() == "en":
        title_hit = _title_match(text)
        if title_hit is not None:
            print("[WunderLanguage] title-match hit, no GPU used")
            return title_hit, False, ""
        script_hit = _script_match(text)
        if script_hit is not None:
            print("[WunderLanguage] script-match hit, no GPU used")
            return script_hit, False, ""
    src_text = normalize_for_translation(clean_for_translation(text))
    src_lang_name = LANGUAGE_CODE_NAMES.get(source_lang.lower(), source_lang)
    tgt_lang_name = LANGUAGE_CODE_NAMES.get(target_lang.lower(), target_lang)
    if cat_translate_pipeline is None:
        print("[WunderLanguage] CAT unavailable (OCR-only mode), leaving blank for fallback chain")
        if _CAT_SKIP_REASON == "low-vram":
            reason = "cat-low-vram"
        else:
            reason = "cat-warming" if READY.get("cat") == "warming" else "cat-unavailable"
        return "", True, reason
    out = apply_english_fixups(_run_generation(src_text, src_lang_name, tgt_lang_name), src_text)
    bad = _output_bad(src_text, out, target_lang)
    if bad in ("degenerate-loop", "wrong-language"):
        alt = strip_trailing_ellipsis(src_text)
        if alt and alt != src_text:
            print("[WunderLanguage] retrying once reason=ellipsis-strip")
            out2 = apply_english_fixups(_run_generation(alt, src_lang_name, tgt_lang_name), src_text)
            bad2 = _output_bad(src_text, out2, target_lang)
            if not bad2:
                print("[WunderLanguage] retry kept")
                return out2, False, ""
            out, bad = out2, bad2
    if bad and target_lang.lower() == "en":
        print(f"[WunderLanguage] failed fast reason={bad}, leaving blank for DeepL")
        return "", True, "untranslatable-offline"
    unreliable = False
    reason = ""
    if bad:
        unreliable = True
        reason = bad
    elif target_lang.lower() == "en" and is_negation_mismatch(src_text, out):
        unreliable = True
        reason = "negation-mismatch"
        print("[WunderLanguage] flagging reason=negation-mismatch")
    if unreliable:
        print(f"[WunderLanguage] flagged unreliable reason={reason}")
    return out, unreliable, reason
class TranslateRequest(BaseModel):
    text: str
    source_lang: str = "ja"
    target_lang: str = "en"
@app.get("/health")
async def health():
    ocr = READY.get("ocr", "ready")
    cat = READY.get("cat", "ready")
    if _NO_CAT:
        cat = "disabled"
    return {"status": "ok" if ocr == "ready" else "starting", "ocr": ocr, "device": DEVICE, "code_version": CODE_VERSION, "cat": cat}
@app.post("/ocr")
async def ocr_image(file: UploadFile = File(...)):
    if READY.get("ocr") != "ready" or mocr is None or reader is None:
        raise HTTPException(status_code=503, detail=f"ocr-{READY.get('ocr', 'warming')}")
    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    primary_text, blocks, words, timing = detect_and_ocr(image)
    return {"text": primary_text, "blocks": blocks, "words": words, "timing": timing}
@app.post("/translate")
async def translate_text(request: TranslateRequest):
    import asyncio as _aio
    translated, unreliable, reason = await _aio.to_thread(cat_translate, request.text, request.source_lang, request.target_lang)
    return {"translated_text": translated, "unreliable": unreliable, "reason": reason, "code_version": CODE_VERSION}

class ObserveRequest(BaseModel):
    text: str

@app.post("/observe")
async def observe_text(request: ObserveRequest):
    try:
        _log_confusion_matches(request.text)
    except Exception:
        pass
    try:
        _log_word_stats(request.text)
    except Exception:
        pass
    return {"ok": True}

_jam = None

def _jamdict():
    global _jam
    if _jam is None:
        try:
            from jamdict import Jamdict
            _jam = Jamdict()
        except Exception as ex:
            print(f"[WunderLanguage] jamdict unavailable ({ex})")
            _jam = False
    return _jam if _jam is not False else None

class DefineRequest(BaseModel):
    text: str

@app.post("/define")
async def define_text(request: DefineRequest):
    words = []
    try:
        t = (request.text or "").strip()[:120]
        if t and _fugashi_tagger is not None:
            seen = set()
            for w in _fugashi_tagger(t):
                try:
                    surf = w.surface
                except Exception:
                    continue
                if not surf or len(surf) < 2 or surf in seen:
                    continue
                try:
                    pos = w.feature.pos1 if hasattr(w.feature, 'pos1') else ""
                except Exception:
                    pos = ""
                if pos not in ("名詞", "動詞", "形容詞"):
                    continue
                seen.add(surf)
                if len(seen) > 8:
                    break
                reading, gloss = "", ""
                try:
                    jam = _jamdict()
                    if jam is not None:
                        r = jam.lookup(surf)
                        entries = getattr(r, "entries", []) or []
                        if entries:
                            ent = entries[0]
                            kana = getattr(ent, "kana_forms", []) or []
                            if kana:
                                reading = getattr(kana[0], "text", "") or ""
                            senses = getattr(ent, "senses", []) or []
                            if senses:
                                g = getattr(senses[0], "gloss", []) or []
                                if g:
                                    first = g[0]
                                    gloss = first if isinstance(first, str) else (getattr(first, "text", "") or "")
                except Exception:
                    pass
                words.append({"surface": surf, "reading": reading or "", "gloss": gloss or ""})
    except Exception:
        pass
    return {"words": words}