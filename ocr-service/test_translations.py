"""
Standalone accuracy test harness for the WunderLanguage sidecar's
/translate endpoint. Run this with the sidecar already up (the app must
be running, or at minimum SidecarProcessManager's python process must be
alive on port 8000).

Uses only the Python standard library (urllib) - no new dependency to
install just to run a test script.

Usage:
    python test_translations.py
"""

import json
import urllib.request

ENDPOINT = "http://127.0.0.1:8000/translate"

TEST_CASES = [
    (
        "はるか昔　山と森にかこまれた美しき　ハイラルの大地…",
        "Long ago, surrounded by mountains and forests, the beauteous land of Hyrule...",
    ),
    (
        "全知全能の力を持つ黄金がこの地方のどこかにあると伝えられていました",
        "It was said that somewhere in this region was gold possessing the powers of omnipotence and omniscience.",
    ),
    (
        "そのハイラルの王国で黄金がかくされた聖地への入口がみつかり",
        "The entrance to the sacred land where the gold was hidden was discovered in that Hyrule Kingdom, and",
    ),
    (
        "多くの人々があらそって聖地へと消えていました",
        "Many people fought, and vanished into the sacred land.",
    ),
    (
        "しかし戻ってきた者はなくまた、悪しき力がそこからわきでてきたと言うことです",
        "However, there were none who returned. Worse, evil power came gushing forth from there.",
    ),
    (
        "そこで国の王は７人の賢者に命じ聖地への入口を封印させたそうです",
        "Thereupon, the country's king commanded Seven Sages and had the entrance to the sacred land sealed.",
    ),
    (
        "封印は２度と解かれる事はないはずでした",
        "The seal should never again have been undone.",
    ),
    (
        "……しかし、それらのでき事が伝説となったころ…",
        "....however, when those events had become legend...",
    ),
    (
        "ここに、封印をとこうとたくらむ、ナゾの司祭アグニムがいました。彼は国王をほうむり…。",
        "Here, there was the mysterious priest Aghanim, who plotted to dissolve the seal. He got the king out of his way, and...",
    ),
    (
        "城の兵達をあやつって７賢者の血をひく娘達を、つぎつぎイケニエにささげていったのです。",
        "Manipulating the castle's soldiers, he offered as sacrifices maidens descended from the Seven Sages, one after another.",
    ),
    (
        "そして、王女ゼルダがイケニエにささげられる運命の時も、もうそこまでせまっていました",
        "Then, the fateful time when Princess Zelda would be offered as a sacrifice, too, had already drawn that near.",
    ),
]


def translate(text: str) -> str:
    payload = json.dumps({"text": text, "source_lang": "ja", "target_lang": "en"}).encode("utf-8")
    request = urllib.request.Request(
        ENDPOINT,
        data=payload,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        result = json.loads(response.read().decode("utf-8"))
    return result.get("translated_text", "(no translated_text field in response)")


def main():
    print(f"Testing {len(TEST_CASES)} known reference-translated sentences against {ENDPOINT}\n")
    for i, (japanese, reference) in enumerate(TEST_CASES, start=1):
        try:
            actual = translate(japanese)
        except Exception as ex:
            actual = f"(request failed: {ex})"

        print(f"--- Block {i} ---")
        print(f"JA:        {japanese}")
        print(f"Reference: {reference}")
        print(f"Actual:    {actual}")
        print()


if __name__ == "__main__":
    main()