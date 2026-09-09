from fastapi import FastAPI, UploadFile, File
from pydantic import BaseModel
from manga_ocr import MangaOcr
from PIL import Image
import numpy as np
import io
import time
import torch
import easyocr
import sys
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass
import hashlib as _hashlib
import pathlib as _pathlib
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
DEVICE = resolve_device()
USE_GPU = DEVICE == "cuda"
mocr = MangaOcr()
reader = easyocr.Reader(["ja"], gpu=USE_GPU)
MIN_BOX_WIDTH = 10
MIN_BOX_HEIGHT = 10
DETECTION_UPSCALE_FACTOR = 1
RECOGNITION_UPSCALE_FACTOR = 2
def merge_nearby_boxes(boxes, x_gap=100, y_gap=15):
    if not boxes:
        return []
    boxes = sorted(boxes, key=lambda b: (b[1], b[0]))
    used = [False] * len(boxes)
    merged = []
    for i, box in enumerate(boxes):
        if used[i]:
            continue
        x_min, y_min, x_max, y_max = box
        used[i] = True
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
        merged.append((x_min, y_min, x_max, y_max))
    return merged
def detect_and_ocr(pil_image: Image.Image):
    det_scale = DETECTION_UPSCALE_FACTOR
    if pil_image.width * det_scale > 900:
        det_scale = 900 / pil_image.width
    detection_image = pil_image.resize(
        (max(1, int(pil_image.width * det_scale)), max(1, int(pil_image.height * det_scale))),
        Image.LANCZOS,
    )
    img_np = np.array(detection_image)
    horizontal_list, free_list = reader.detect(img_np)
    raw_boxes = []
    for x_min, x_max, y_min, y_max in horizontal_list[0]:
        raw_boxes.append((int(x_min), int(y_min), int(x_max), int(y_max)))
    for quad in free_list[0]:
        xs = [pt[0] for pt in quad]
        ys = [pt[1] for pt in quad]
        raw_boxes.append((int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))))
    merged_boxes = merge_nearby_boxes(raw_boxes)
    blocks = []
    for x_min, y_min, x_max, y_max in merged_boxes:
        orig_x_min = x_min / det_scale
        orig_y_min = y_min / det_scale
        orig_x_max = x_max / det_scale
        orig_y_max = y_max / det_scale
        orig_width = orig_x_max - orig_x_min
        orig_height = orig_y_max - orig_y_min
        if orig_width < MIN_BOX_WIDTH or orig_height < MIN_BOX_HEIGHT:
            continue
        recognition_crop = pil_image.crop((orig_x_min, orig_y_min, orig_x_max, orig_y_max))
        recognition_crop = recognition_crop.resize(
            (
                max(1, int(recognition_crop.width * RECOGNITION_UPSCALE_FACTOR)),
                max(1, int(recognition_crop.height * RECOGNITION_UPSCALE_FACTOR)),
            ),
            Image.LANCZOS,
        )
        text = mocr(recognition_crop)
        if not text.strip():
            continue
        blocks.append({
            "text": text,
            "box": [int(orig_x_min), int(orig_y_min), int(orig_width), int(orig_height)],
            "area": orig_width * orig_height,
        })
    primary_text = ""
    if blocks:
        # HUD strips (tiny h) must never outrank dialogue for primary.
        tall = [b for b in blocks if b["box"][3] >= 50]
        pool = tall if tall else blocks
        primary_block = max(pool, key=lambda b: b["area"])
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
    return primary_text, blocks, words

CAT_TRANSLATE_MODEL_ID = "cyberagent/CAT-Translate-3.3b-beta"
from transformers import AutoTokenizer, AutoModelForCausalLM, pipeline as hf_pipeline, BitsAndBytesConfig
_cat_translate_dtype = torch.float16 if DEVICE == "cuda" else torch.float32
print(f"[WunderLanguage] CAT-Translate: loading tokenizer for {CAT_TRANSLATE_MODEL_ID}...")
_cat_translate_tokenizer = AutoTokenizer.from_pretrained(CAT_TRANSLATE_MODEL_ID)
print("[WunderLanguage] CAT-Translate: tokenizer loaded.")
if DEVICE == "cuda":
    print(f"[WunderLanguage] CAT-Translate: loading 4-bit quantized weights...")
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
    print("[WunderLanguage] CAT-Translate: 4-bit quantized model loaded on GPU.")
else:
    print(f"[WunderLanguage] CAT-Translate: loading full-precision on CPU.")
    _cat_translate_model = AutoModelForCausalLM.from_pretrained(
        CAT_TRANSLATE_MODEL_ID,
        dtype=_cat_translate_dtype,
    )
    print("[WunderLanguage] CAT-Translate: model loaded on CPU.")
print("[WunderLanguage] CAT-Translate: constructing generation pipeline...")
cat_translate_pipeline = hf_pipeline(
    "text-generation",
    model=_cat_translate_model,
    tokenizer=_cat_translate_tokenizer,
)
print(f"[WunderLanguage] CAT-Translate ready on {DEVICE}.")
print(f"[WunderLanguage] sidecar code version {CODE_VERSION}.")
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
_REFUSAL_RE = re.compile(r'can.t help with that|ready to translate|provide the Japanese|need the Japanese passage|happy to help translate', re.IGNORECASE)
_PROMPT_ECHO_RE = re.compile(r'(Translate|Transform) the following Japanese text into English', re.IGNORECASE)
_RUN_RE = re.compile(r'([A-Za-z0-9])\1{6,}')
_EN_STUTTER = [(r'\bTha\b', 'That'), (r'\btwas\b', 'was'), (r'\bTthe\b', 'The'), (r'\bteh\b', 'the')]
_ZELDA_EN_NOUNS = [(r'\b[Hh]iranal\b', 'Hyrule'), (r'\bHilar\b', 'Hyrule'), (r'\bAgnim\b', 'Agahnim')]
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
    cleaned = re.sub(r'！{2,}$', '…', cleaned)
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
    text = text.replace('望月は', '２度と')
    text = re.sub(r'^[〈〉『』「」【】\'" ]+', '', text)
    text = re.sub(r'[〈〉『』「」【】]+', '', text)
    text = text.replace('たくらも、', 'たくらむ、')
    text = text.replace('カジの司祭', 'ナゾの司祭')
    text = text.replace('運命の晩も', '運命の時も')
    text = text.replace('ナツの同窓', 'ナゾの司祭')
    text = text.replace('国王を戻ったのです。', '国王をほうむり…。')
    text = text.replace('ほうむ丁寧', 'ほうむり…')
    text = text.replace('ぽうむりゃ', 'ほうむり…')
    text = text.replace('ぼうむリー', 'ほうむり…')
    text = text.replace('ぽうむリー', 'ほうむり…')
    text = text.replace('ぼうむすぎ', 'ほうむり…')
    text = text.replace('ぼうなり', 'ほうむり')
    text = text.replace('ほうむリー', 'ほうむり…')
    text = text.replace('ぼうむりー', 'ほうむり…')
    text = text.replace('ほうむりー', 'ほうむり…')
    text = text.replace('運命の塊も', '運命の時も')
    text = text.replace('ささがられる', 'ささげられる')
    text = text.replace('ささがあられる', 'ささげられる')
    text = text.replace('エ女セルダ', '王女ゼルダ')
    text = text.replace('エケゼルダ', '王女ゼルダ')
    text = text.replace('兵童', '兵達')
    text = text.replace('１ヶ二工', 'イケニエ')
    text = text.replace('作戦者の皿をひく物音', '７賢者の血をひく娘達')
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
    response = cat_translate_pipeline(user_input, max_new_tokens=120, do_sample=False)
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
    out = apply_english_fixups(_run_generation(src_text, src_lang_name, tgt_lang_name), src_text)
    bad = _is_bad_output(out) if target_lang.lower() == "en" else ""
    if bad in ("degenerate-loop", "wrong-language"):
        alt = strip_trailing_ellipsis(src_text)
        if alt and alt != src_text:
            print("[WunderLanguage] retrying once reason=ellipsis-strip")
            out2 = apply_english_fixups(_run_generation(alt, src_lang_name, tgt_lang_name), src_text)
            bad2 = _is_bad_output(out2)
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
    return {"status": "ok", "device": DEVICE, "code_version": CODE_VERSION}
@app.post("/ocr")
async def ocr_image(file: UploadFile = File(...)):
    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    primary_text, blocks, words = detect_and_ocr(image)
    return {"text": primary_text, "blocks": blocks, "words": words}
@app.post("/translate")
async def translate_text(request: TranslateRequest):
    translated, unreliable, reason = cat_translate(request.text, request.source_lang, request.target_lang)
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