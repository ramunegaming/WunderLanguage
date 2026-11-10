import json

rows = [
    {"shot": "135503_983", "game": "papermario", "session": "13h", "split": "dev",
     "context": "title-ja",
     "visible_ja": "ペーパーマリオ / スタートボタンをおしてください (logo + start prompt)",
     "complete": False, "panel_ja": "さて今日は… | やれやアメリカです。",
     "verdict": "false-pub",
     "notes": "Title-screen misreads published: stylized start-prompt read as さて今日は… and logo fragments; title class (0 PM title frames in corpus, capture owed to live)"},
    {"shot": "135505_510", "game": "papermario", "session": "13h", "split": "dev",
     "context": "title-ja-blink-off",
     "visible_ja": "ペーパーマリオ (logo only, prompt blink off)",
     "complete": False, "panel_ja": "", "verdict": "true-negative",
     "notes": "Blink-off moment correctly silent; positive control paired with 135503_983"},
    {"shot": "142225_584", "game": "papermario", "session": "14h", "split": "dev",
     "context": "decision-box",
     "visible_ja": "これまでの ゲームの ないようを セーブしますか？ / は い / いいえ",
     "complete": True,
     "panel_ja": "アプリスタンドについて | さて今日は… | スターストーンと魔法の地図のお話をしましょう",
     "verdict": "miss-complete",
     "notes": "Save dialog never published; panel carries web-occluder junk posts instead (date-salad class now choke-blocked by G-D; アプリスタンド/さて今日は = starved-salvage web text). Pointing-hand P5 evidence: index finger on は い"},
    {"shot": "210133_403", "game": "papermario", "session": "21h", "split": "dev",
     "context": "decision-box",
     "visible_ja": "これまでの ゲームの ないようを セーブしますか？ / は い / いいえ",
     "complete": True, "panel_ja": "ゴロッキタウンのクリボーよ…",
     "verdict": "miss-complete",
     "notes": "Same save dialog (repeats 210155/210213 not rowed); panel latest is stale earlier dialogue"},
    {"shot": "210230_024", "game": "papermario", "session": "21h", "split": "dev",
     "context": "dialogue-complete",
     "visible_ja": "この人には マジメに はたらこうという キモチが ぜんぜん ないのかしら？",
     "complete": True,
     "panel_ja": "この人にはマジメにはたらこうというキモチがぜんぜんないのかしら？",
     "verdict": "hit", "notes": "Full line published via update; positive control"},
    {"shot": "210243_532", "game": "papermario", "session": "21h", "split": "dev",
     "context": "dialogue-complete",
     "visible_ja": "チューさんの マルチュロよ いつも もうけ話のニオイを かぎつけようとしているわ",
     "complete": True,
     "panel_ja": "チューさんのマルチュロよいつももうけ話のニオイをかぎつけようとしているわ",
     "verdict": "hit", "notes": "Full line published via update; positive control"},
    {"shot": "210307_227", "game": "papermario", "session": "21h", "split": "dev",
     "context": "dialogue-complete",
     "visible_ja": "そんなことに いっしょうけんめいに なるくらいなら マジメに はたらけばいいのに…",
     "complete": True,
     "panel_ja": "チューさんのマルチュロよいつももうけ話のニオイをかぎつけようとしているわ",
     "verdict": "miss-complete",
     "notes": "Line fully OCR'd (corpus loop175-191) but never published; panel carries prior line"},
    {"shot": "210316_640", "game": "papermario", "session": "21h", "split": "dev",
     "context": "dialogue-complete",
     "visible_ja": "そんなことに いっしょうけんめいに なるくらいなら マジメに はたらけばいいのに…",
     "complete": True,
     "panel_ja": "それともこの街の人ってこんな人ばかりなのかしら？",
     "verdict": "miss-complete",
     "notes": "Same unpublished line still on screen; panel latest = それとも line (published 21:03:11, not visible at shot) = stale offset; そんなことに never fired"},
]

p = r"D:\Wunderlanguage\eval\truth.jsonl"
with open(p, "a", encoding="utf-8", newline="\n") as f:
    for r in rows:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")

all_rows = [json.loads(l) for l in open(p, encoding="utf-8") if l.strip()]
import collections
print("rows now", len(all_rows))
print(collections.Counter(r["verdict"] for r in all_rows))
print(collections.Counter(r["split"] for r in all_rows))
print(collections.Counter(r["game"] for r in all_rows))
