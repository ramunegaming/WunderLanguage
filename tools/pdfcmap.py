import re, zlib, sys
path = sys.argv[1] if len(sys.argv) > 1 else r'D:\Wunderlanguage\WunderLanguage-Design-Handover.pdf'
d = open(path, 'rb').read()
found = False
for m in re.finditer(rb'stream\r?\n', d):
    s = m.end()
    e = d.find(b'endstream', s)
    try:
        dec = zlib.decompress(d[s:e])
    except Exception:
        continue
    if b'beginbfchar' in dec or b'beginbfrange' in dec:
        print('CMAP found len', len(dec))
        for code in [b'30BC', b'4F1D', b'30EB', b'4E00', b'300C', b'FF62']:
            print(' ', code.decode(), dec.count(b'<' + code + b'>'))
        found = True
        break
if not found:
    print('NO CMAP STREAM FOUND')
