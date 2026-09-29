from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, KeepTogether
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase import pdfmetrics
from pathlib import Path

OUT = Path('output/pdf/replay-hotkeys-and-shortcuts.pdf')
NAVY = colors.HexColor('#14253B')
TEAL = colors.HexColor('#087E8B')
GOLD = colors.HexColor('#D59B35')
INK = colors.HexColor('#243447')
MUTED = colors.HexColor('#66778A')
PALE = colors.HexColor('#EEF3F7')
PALEBLUE = colors.HexColor('#E6F3F4')
WHITE = colors.white

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name='TitleX', parent=styles['Title'], fontName='Helvetica-Bold', fontSize=27, leading=31, textColor=WHITE, alignment=TA_LEFT, spaceAfter=4))
styles.add(ParagraphStyle(name='SubX', parent=styles['Normal'], fontName='Helvetica', fontSize=10, leading=14, textColor=colors.HexColor('#D9E4EE')))
styles.add(ParagraphStyle(name='SectionX', parent=styles['Heading2'], fontName='Helvetica-Bold', fontSize=14, leading=17, textColor=NAVY, spaceBefore=7, spaceAfter=4))
styles.add(ParagraphStyle(name='BodyX', parent=styles['BodyText'], fontName='Helvetica', fontSize=9.2, leading=12.5, textColor=INK))
styles.add(ParagraphStyle(name='SmallX', parent=styles['BodyText'], fontName='Helvetica', fontSize=8, leading=10.5, textColor=MUTED))
styles.add(ParagraphStyle(name='HeadCell', parent=styles['BodyText'], fontName='Helvetica-Bold', fontSize=8.3, leading=10, textColor=WHITE))
styles.add(ParagraphStyle(name='KeyCell', parent=styles['BodyText'], fontName='Helvetica-Bold', fontSize=8.7, leading=11, textColor=NAVY))
styles.add(ParagraphStyle(name='Cell', parent=styles['BodyText'], fontName='Helvetica', fontSize=8.7, leading=11, textColor=INK))

def P(text, style='Cell'):
    return Paragraph(text, styles[style])

def table(rows, widths, header=True):
    t = Table(rows, colWidths=widths, repeatRows=1 if header else 0, hAlign='LEFT')
    cmds = [
        ('VALIGN',(0,0),(-1,-1),'TOP'),
        ('LEFTPADDING',(0,0),(-1,-1),8),('RIGHTPADDING',(0,0),(-1,-1),8),
        ('TOPPADDING',(0,0),(-1,-1),4),('BOTTOMPADDING',(0,0),(-1,-1),4),
        ('LINEBELOW',(0,0),(-1,-1),0.35,colors.HexColor('#D6E0E8')),
        ('BACKGROUND',(0,0),(-1,0),TEAL if header else WHITE),
    ]
    if header:
        for i in range(1,len(rows)):
            cmds.append(('BACKGROUND',(0,i),(-1,i),WHITE if i%2 else PALE))
    t.setStyle(TableStyle(cmds))
    return t

def banner(title, subtitle):
    box = Table([[Paragraph(title, styles['TitleX'])],[Paragraph(subtitle, styles['SubX'])]], colWidths=[7.25*inch])
    box.setStyle(TableStyle([
        ('BACKGROUND',(0,0),(-1,-1),NAVY),('LEFTPADDING',(0,0),(-1,-1),18),('RIGHTPADDING',(0,0),(-1,-1),18),
        ('TOPPADDING',(0,0),(-1,0),12),('BOTTOMPADDING',(0,0),(-1,0),1),('TOPPADDING',(0,1),(-1,1),0),('BOTTOMPADDING',(0,1),(-1,1),11),
    ]))
    return box

def footer(canvas, doc):
    canvas.saveState()
    w,h=letter
    canvas.setStrokeColor(colors.HexColor('#D6E0E8')); canvas.setLineWidth(.5)
    canvas.line(.68*inch,.52*inch,w-.68*inch,.52*inch)
    canvas.setFont('Helvetica',7.5); canvas.setFillColor(MUTED)
    canvas.drawString(.7*inch,.34*inch,'PROJECT PRIME  /  REPLAY STUDIO QUICK REFERENCE')
    canvas.drawRightString(w-.7*inch,.34*inch,f'{doc.page}')
    canvas.restoreState()

W=7.25*inch
story=[]
story += [banner('Replay hotkeys', 'Keyboard defaults and camera editing controls  ·  Desktop reference'), Spacer(1,12)]
story += [Paragraph('Playback and navigation',styles['SectionX'])]
rows=[[P('KEY','HeadCell'),P('ACTION','HeadCell'),P('DETAIL','HeadCell')]]
for key,action,detail in [
    ('Space','Play / pause','Toggles playback; restarts when playback has reached the end.'),
    ('Comma / Period','Step one frame','Move one recorded frame backward / forward.'),
    ('Left / Right','Seek','Jump 5 seconds backward / forward.'),
    ('[ / ]','Playback speed','Reduce / increase playback speed.'),
    ('Home','Restart','Return to the beginning of the replay.'),
    ('1–8','Watch player','Switch the viewed player to that player slot.'),
]: rows.append([P(key,'KeyCell'),P(action),P(detail)])
story += [table(rows,[1.35*inch,1.35*inch,4.55*inch]),Spacer(1,5),Paragraph('Playback bindings can be changed in Settings. The guide inside Replay Studio reflects your current bindings.',styles['SmallX'])]
story += [Paragraph('Camera controls',styles['SectionX'])]
rows=[[P('KEY','HeadCell'),P('ACTION','HeadCell'),P('DETAIL','HeadCell')]]
for key,action,detail in [
    ('F','Toggle free camera','Switch between free camera and first person.'),
    ('C','Chase / first person','Toggle between chase and first person.'),
    ('O','Orbit camera','Use the orbit camera.'),
    ('B','Add keyframe','Save the current camera pose at the current replay frame.'),
    ('N','Next keyframe','Select and jump to the next saved camera keyframe.'),
    ('Delete','Remove selected key','Deletes the selected keyframe. Select a key on the timeline first.'),
    ('- / =','FOV','Decrease / increase field of view by 5 degrees.'),
    ('; / \'','Roll','Tilt the camera by 5 degrees left / right.'),
    ('W / A / S / D','Move camera','Move forward, left, backward, right in free camera.'),
    ('E / V','Move vertically','Move up / down.'),
    ('Shift','Move faster','Hold while moving the free camera.'),
    ('Arrow keys','Look around','Rotate the free camera while gameplay has focus.'),
]: rows.append([P(key,'KeyCell'),P(action),P(detail)])
story += [table(rows,[1.35*inch,1.35*inch,4.55*inch]),Spacer(1,7)]
call = Table([[P('<b>Editing tip</b>  Click a camera marker on the timeline, or use PREVIOUS KEY / NEXT KEY. Adjust the camera and lens, then choose UPDATE SELECTED. This edits the chosen key without lining up the playhead.','BodyX')]],colWidths=[W])
call.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),PALEBLUE),('BOX',(0,0),(-1,-1),.6,TEAL),('LEFTPADDING',(0,0),(-1,-1),10),('RIGHTPADDING',(0,0),(-1,-1),10),('TOPPADDING',(0,0),(-1,-1),8),('BOTTOMPADDING',(0,0),(-1,-1),8)]))
story += [call,PageBreak(),banner('Preview and controller', 'Focus, timeline and gamepad shortcuts for Replay Studio'),Spacer(1,12)]
story += [Paragraph('Embedded camera preview',styles['SectionX'])]
rows=[[P('INPUT','HeadCell'),P('ACTION','HeadCell')]]
for key,action in [
    ('Click preview','Focus camera controls and enter free camera.'),
    ('W / A / S / D','Move forward / left / backward / right.'),
    ('E / V','Move up / down.'),
    ('Shift + movement','Move faster.'),
    ('Drag with left mouse','Look around.'),
    ('F, B, N, Delete, -, =, ;, \'','Use the camera shortcuts above while the preview has focus.'),
    ('Esc','Release preview focus.'),
]: rows.append([P(key,'KeyCell'),P(action)])
story += [table(rows,[1.7*inch,5.55*inch])]
story += [Paragraph('Timeline focus',styles['SectionX'])]
rows=[[P('KEY','HeadCell'),P('ACTION','HeadCell')]]
for key,action in [
    ('Left / Right','Nudge playhead by one second.'),
    ('Home / End','Jump to replay start / end.'),
    ('Delete / Backspace','Delete the selected camera keyframe.'),
    ('Mouse wheel','Scrub the timeline.'),
    ('Ctrl + wheel','Zoom the timeline.'),
    ('Shift + drag','Move the selected range when the drag begins inside it.'),
    ('Drag gold In / Out handles','Trim the clip selection.'),
    ('Drag a camera marker','Move that keyframe to another frame.'),
]: rows.append([P(key,'KeyCell'),P(action)])
story += [table(rows,[2.25*inch,5*inch])]
story += [Paragraph('Default gamepad bindings',styles['SectionX'])]
rows=[[P('BUTTON','HeadCell'),P('ACTION','HeadCell')]]
for key,action in [
    ('A','Play / pause'),('X','Step forward one frame'),('D-pad Left / Right','Seek backward / forward'),
    ('D-pad Down / Up','Slower / faster playback'),('Left / Right bumper','Previous / next player'),('Y','Toggle camera mode'),
    ('Left stick','Move free camera'),('Right stick','Look around'),
]: rows.append([P(key,'KeyCell'),P(action)])
story += [table(rows,[2.25*inch,5*inch]),Spacer(1,7),Paragraph('Gamepad actions can be rebound in Settings. Camera keyframe editing uses the Replay Studio buttons and keyboard shortcuts.',styles['SmallX'])]

doc=SimpleDocTemplate(str(OUT),pagesize=letter,rightMargin=.62*inch,leftMargin=.62*inch,topMargin=.42*inch,bottomMargin=.62*inch,title='Replay Hotkeys and Shortcuts',author='Project Prime')
doc.build(story,onFirstPage=footer,onLaterPages=footer)
print(OUT.resolve())
