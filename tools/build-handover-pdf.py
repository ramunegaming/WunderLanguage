import sys
sys.path.insert(0, r'D:\pylibs')
sys.path.insert(0, r'D:\Wunderlanguage\tools')
from fpdf import FPDF
from fpdf.enums import XPos, YPos
MC = dict(new_x=XPos.LMARGIN, new_y=YPos.NEXT)
from handover_part1 import SECTIONS as P1
from handover_part2 import SECTIONS as P2
from handover_part3 import SECTIONS as P3
from handover_part4 import SECTIONS as P4

class Doc(FPDF):
    def footer(self):
        self.set_y(-12)
        self.set_font('Noto', '', 8)
        self.set_text_color(120, 120, 120)
        self.cell(0, 8, 'WunderLanguage design handover — page %d' % self.page_no(), align='C',
                  new_x=XPos.LMARGIN, new_y=YPos.NEXT)

pdf = Doc()
pdf.set_auto_page_break(True, margin=18)
pdf.add_font('Noto', '', r'D:\Wunderlanguage\tools\fonts\NotoSansJP-Regular.ttf')
pdf.add_page()
pdf.set_font('Noto', '', 22)
pdf.set_text_color(20, 20, 20)
pdf.multi_cell(0, 10, 'WunderLanguage — Design Handover', **MC)
pdf.set_font('Noto', '', 11)
pdf.set_text_color(80, 80, 80)
pdf.multi_cell(0, 6, 'Live Japanese game-text capture that compounds with every onboarded game.\nFull project transfer: mission, rules, architecture, measured history, loops, state, plans.\nDated 2026-10-13. Supersedes chat history; BOOKS.md remains the commit-level ledger.', **MC)
pdf.ln(4)

for title, blocks in (P1 + P2 + P3 + P4):
    pdf.set_font('Noto', '', 15)
    pdf.set_text_color(140, 30, 30)
    pdf.multi_cell(0, 8, title, **MC)
    pdf.set_draw_color(140, 30, 30)
    pdf.line(10, pdf.get_y(), 200, pdf.get_y())
    pdf.ln(2)
    for b in blocks:
        if b.startswith('h:'):
            pdf.set_font('Noto', '', 11)
            pdf.set_text_color(40, 40, 40)
            pdf.multi_cell(0, 6, b[2:], **MC)
            pdf.ln(1)
        elif b.startswith('b:'):
            pdf.set_font('Noto', '', 9.5)
            pdf.set_text_color(20, 20, 20)
            pdf.multi_cell(0, 5, '      ' + chr(8226) + '  ' + b[2:], **MC)
            pdf.ln(0.5)
        else:
            t = b[2:] if b.startswith('p:') else b
            pdf.set_font('Noto', '', 9.5)
            pdf.set_text_color(20, 20, 20)
            pdf.multi_cell(0, 5, t, **MC)
            pdf.ln(1)
    pdf.ln(2)

out = r'D:\Wunderlanguage\WunderLanguage-Design-Handover.pdf'
pdf.output(out)
print('wrote', out, 'pages:', pdf.page_no())
