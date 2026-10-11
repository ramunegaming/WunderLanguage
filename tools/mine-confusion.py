"""Mine char-confusion candidates from corrections/*.json (Loop 2, compounding).

Usage: python mine-confusion.py [--min-count N]
Reads per-game miner files (skips _global.json + .bak), aligns each
raw->fixed pair, extracts single-char substitutions with counts and
source games. Reviewer appends qualifying pairs to ConfusionPairs
in CaptureController.cs. Rerun per new corpus/game.
"""
import json, glob, sys
from collections import Counter, defaultdict
from difflib import SequenceMatcher

# Already in ConfusionPairs (CaptureController.cs) — never re-propose.
EXISTING = {tuple(sorted(p)) for p in [
    ('入','人'), ('賢','腎'), ('む','も'), ('賢','貿'), ('王','エ'),
    ('ニ','ー'), ('運','選'), ('封','射'), ('げ','け'), ('祭','宗'),
    ('ご','こ'), ('Ｂ','日'), ('Ｂ','ロ'), ('ボ','ポ'), ('ハ','バ'),
    ('Ａ','日'), ('封','針'), ('エ','ュ'), ('封','樹'), ('７','『'),
    ('あ','お'), ('Ｂ','目'), ('グ','ゲ'), ('封','掛'), ('娘','塊'),
    ('開','聞'), ('賢','覧'), ('す','ず'), ('さ','き'), ('城','姉'),
    ('む','な'), ('ゾ','ジ'), ('司','同'), ('封','批'), ('７','Ｐ'),
    ('Ａ','自'), ('ブ','プ'), ('カ','ガ'), ('Ｂ','自'),
]}

def pairs_for(raw, fixed):
    out = []
    if len(raw) == len(fixed):
        for a, b in zip(raw, fixed):
            if a != b:
                out.append(tuple(sorted((a, b))))
    else:
        for tag, i1, i2, j1, j2 in SequenceMatcher(None, raw, fixed).get_opcodes():
            if tag == 'replace' and (i2 - i1) == (j2 - j1):
                for a, b in zip(raw[i1:i2], fixed[j1:j2]):
                    if a != b:
                        out.append(tuple(sorted((a, b))))
    return out

def main():
    min_count = int(sys.argv[sys.argv.index('--min-count') + 1]) if '--min-count' in sys.argv else 2
    counts = Counter()
    games = defaultdict(set)
    examples = {}
    files = [p for p in glob.glob(r'C:\Users\RamuneGaming\AppData\Roaming\WunderLanguage\corrections\*.json')
             if not p.split('\\')[-1].startswith('_global') and not p.endswith('.bak')]
    for p in files:
        game = p.split('\\')[-1]
        try:
            m = json.load(open(p, encoding='utf-8'))
        except Exception as e:
            print('SKIP', game, e)
            continue
        for raw, fixed in m.items():
            for pr in pairs_for(raw, fixed):
                counts[pr] += 1
                games[pr].add(game)
                examples.setdefault(pr, (raw[:50], fixed[:50]))
    print('files=%d total-subs=%d distinct=%d' % (len(files), sum(counts.values()), len(counts)))
    print('--- candidates (count>=%d, not already hardcoded) ---' % min_count)
    n = 0
    for pr, c in counts.most_common():
        if c < min_count or pr in EXISTING:
            continue
        n += 1
        a, b = pr
        r, f = examples[pr]
        print('%3d (%s,%s) games=%d e.g. %r -> %r' % (c, a, b, len(games[pr]), r, f))
    print('proposed=%d' % n)

main()
