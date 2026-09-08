import json, urllib.request
ENDPOINT = "http://127.0.0.1:8000/translate"
def tr(t):
    p = json.dumps({"text": t, "source_lang": "ja", "target_lang": "en"}).encode()
    r = urllib.request.Request(ENDPOINT, data=p, headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(r, timeout=300) as resp:
        return json.loads(resp.read().decode()).get("translated_text", "(none)")
variants = {
 "V0 original": "はるか昔　山と森にかこまれた美しき　ハイラルの大地…",
 "V1 live-ocr shape": "はるか昔、山と森にかこまれた美しき、ハイラルの大地．．．",
 "V2 stripped": "はるか昔、山と森にかこまれた美しき、ハイラルの大地",
 "V3 completed": "はるか昔、山と森にかこまれた美しき、ハイラルの大地。",
 "V4 modern+completed": "はるか昔、山と森にかこまれた美しい、ハイラルの大地。",
}
for name, t in variants.items():
    try:
        print(name, "=>", tr(t)[:120].replace("\n", " "))
    except Exception as ex:
        print(name, "=> FAILED:", ex)