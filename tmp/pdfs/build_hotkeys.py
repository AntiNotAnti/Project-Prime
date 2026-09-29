from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.platypus import SimpleDocTemplate, Paragraph, Table, TableStyle, Spacer, PageBreak
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from pypdf import PdfReader
out=Path('output/pdf/project-prime-map-studio-shortcuts.pdf').resolve()
navy=colors.HexColor('#142B40'); teal=colors.HexColor('#087E8B'); ink=colors.HexColor('#203447'); muted=colors.HexColor('#586C7B'); pale=colors.HexColor('#EDF5F6')
styles={
 'title':ParagraphStyle('title',fontName='Helvetica-Bold',fontSize=25,leading=28,textColor=navy,spaceAfter=8),
 'sub':ParagraphStyle('sub',fontSize=10,leading=14,textColor=muted,spaceAfter=14),
 'h':ParagraphStyle('h',fontName='Helvetica-Bold',fontSize=12,leading=15,textColor=teal,spaceBefore=10,spaceAfter=6),
 'body':ParagraphStyle('body',fontSize=9,leading=12,textColor=ink,spaceAfter=7),
 'key':ParagraphStyle('key',fontName='Helvetica-Bold',fontSize=8.8,leading=11,textColor=navy),
 'cell':ParagraphStyle('cell',fontSize=8.8,leading=11,textColor=ink),
 'small':ParagraphStyle('small',fontSize=8,leading=11,textColor=muted,spaceAfter=5)
}
story=[]
def p(s,style='body'):return Paragraph(s,styles[style])
def title(n,t,desc):
 story.extend([p('MAP STUDIO  /  SHORTCUT REFERENCE','small'),p(t,'title'),p(desc,'sub')])
def section(t,rows):
 story.append(p(t,'h'))
 data=[[p(a,'key'),p(b,'cell')] for a,b in rows]
 tab=Table(data,colWidths=[150,378],hAlign='LEFT')
 tab.setStyle(TableStyle([('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),9),('RIGHTPADDING',(0,0),(-1,-1),9),('TOPPADDING',(0,0),(-1,-1),4),('BOTTOMPADDING',(0,0),(-1,-1),4),('ROWBACKGROUNDS',(0,0),(-1,-1),[pale,colors.white]),('LINEBELOW',(0,0),(-1,-1),.35,colors.HexColor('#DCE7EB'))]))
 story.append(tab)
def note(s):story.extend([Spacer(1,9),p(s,'small')])
def page():story.append(PageBreak())
title(1,'Everyday controls','Project Prime Map Studio - current desktop bindings, checked against the local source on September 28, 2026.')
story.append(p('<b>Notation:</b> Ctrl/Cmd means Ctrl on Windows/Linux or Command on macOS. Alt is Option on macOS. Mouse gestures labeled Ctrl require the actual Control key, including on macOS.'))
story.append(p('<b>Focus:</b> Click the viewport before using modeling shortcuts. Text fields retain their normal editing behavior. Finish or cancel an active transform before using other commands.'))
section('Project and history',[
('Ctrl/Cmd + S','Save the map project.'),('Ctrl/Cmd + Enter','Playtest the map.'),('Ctrl/Cmd + Shift + P','Open the command palette.'),('Ctrl/Cmd + Z','Undo.'),('Ctrl/Cmd + Shift + Z','Redo.'),('Ctrl/Cmd + Y','Redo (alternate binding).')])
section('Camera and navigation',[
('Right-button drag','Orbit around the camera target.'),('Middle-button drag','Pan the camera.'),('Mouse wheel','Zoom toward or away from the target.'),('W / A / D','Move the camera forward / left / right.'),('Q / E','Move the camera down / up. E is Extrude in mesh element modes.'),('F','Frame the selection in Object mode. In a read-only preview, frame all.')])
note('<b>Current binding detail:</b> S selects Scale, so it does not move the camera backward even though the status bar still advertises WASD navigation. F is Fill when editing an active mesh in Face, Edge or Vertex mode.')
section('Escape behavior',[
('Esc during interaction','Cancel a transform or selection drag; stop orbit/pan. A canceled sub-element transform adds no undo entry.'),('Esc after interaction','Exit measure/paint mode when active. Otherwise dismiss an open dialog, cancel an active job, or close Map Studio according to the current screen state.')])
page()
title(2,'Modes and selection','Select an editable mesh object first, then choose Face, Edge or Vertex mode.')
section('Selection modes',[
('1','Object mode.'),('2','Face mode.'),('3','Edge mode.'),('4','Vertex mode.')])
note('Use the top-row number keys. Changing modes clears the current sub-element selection.')
section('Object selection and editing',[
('Left click','Select an object; clicking an already selected object keeps the current group.'),('Shift + left click','Add an object to the selection.'),('Drag from empty space','Box-select objects. Hold Shift to add to the selection.'),('Ctrl/Cmd + A','Select all objects.'),('Ctrl/Cmd + C / V','Copy selected objects / paste objects.'),('Ctrl/Cmd + D','Duplicate selected objects.'),('Delete','Delete selected objects.'),('H','Hide selected objects.'),('Shift + H','Isolate the selection.'),('Alt + H','Show all geometry.')])
section('Face, Edge and Vertex selection',[
('Left click','Replace the element selection.'),('Shift + left click','Add elements.'),('Ctrl + left click','Toggle elements on or off.'),('Drag from empty space','Box-select elements; Shift adds and Ctrl toggles. Edges/faces must have all their vertices inside the box.'),('Double-click face','Select its connected surface/island.'),('Double-click edge','Select an edge loop where regular quad topology permits.')])
note('Object-mode mouse selection has no Ctrl-toggle binding. Copy/paste/duplicate remain object operations even when the viewport is in an element mode.')
page()
title(3,'Selection and transforms','Object tools are selected with G/R/S; mesh elements also support keyboard-started modal transforms.')
section('Expand or clear a mesh selection',[
('Ctrl/Cmd + A','Select every element of the current type in the active mesh.'),('Alt + A','Clear the sub-element selection.'),('L','Select linked geometry.'),('Ctrl/Cmd + plus / equals','Grow the selection. The main plus/equals key or keypad Add is accepted.'),('Ctrl/Cmd + minus','Shrink the selection. The main minus key or keypad Subtract is accepted.')])
section('Start and manipulate a transform',[
('G','Move. In an element mode, starts a live transform of the selected elements.'),('R','Rotate. In an element mode, starts a live transform.'),('S','Scale. In an element mode, starts a live transform.'),('T','Select the Scale tool (alternate tool-selection key; does not start a keyboard transform).'),('Left drag a gizmo','Drag an axis tip to constrain that axis. For sub-elements, drag the center for movement using the current axis settings.'),('Release the left button','Commit a gizmo/object drag as one history operation.'),('Ctrl during element move','Snap the moving selection pivot to a nearby surface on another object.')])
section('During a G / R / S element transform',[
('X / Y / Z','Constrain to that axis.'),('Shift + X / Y / Z','Constrain to the other two axes: YZ / XZ / XY.'),('0-9, minus, period','Enter an exact amount using the top-row digits. Move uses map units, rotation uses degrees, scale uses a factor.'),('Backspace','Remove the last character of the numeric entry.'),('Enter or a viewport click','Commit the keyboard-started transform.'),('Esc','Cancel and restore the starting geometry with no new history entry.')])
note('<b>Settings:</b> World/local orientation, pivot, grid spacing, angle snap and scale snap are toolbar/inspector controls, not additional key bindings. Mouse transforms use those snap settings; typed amounts set an exact value. Axis constraints remain in effect until changed.')
page()
title(4,'Modeling, paint and measure','These modeling keys operate on the active editable mesh in Face, Edge or Vertex mode.')
section('Fast modeling commands',[
('E','Extrude the selected face region.'),('I','Inset the selected face region.'),('B','Bevel: one selected edge in Edge mode, or selected faces otherwise.'),('M','Merge selected vertices at their center.'),('X or Delete','Delete selected faces; in Edge/Vertex mode, delete faces using the selected edges/vertices.'),('F','Fill a complete selected boundary. Select boundary edges or their vertices.'),('Shift + right click','Open the contextual modeling menu.')])
note('E/I/B/M/X/F execute immediately. Keyboard modeling uses the command defaults: amount 0.25 and one bevel segment. Use the numeric inspector for precise amounts and the additional topology tools. Invalid selections produce a status message.')
section('Materials and measuring',[
('P','Toggle material-paint mode.'),('Left click / drag in Paint','Apply the active material to editable mesh faces. A continuous stroke is one undo operation.'),('Ctrl + click in Paint','Sample the material under the pointer.'),('Shift + paint','Paint the connected surface.'),('Alt + paint','Restore the mesh base material. Combine with Shift for the connected surface.'),('M in Object mode','Toggle Measure mode; click two surface points to measure their distance.'),('Esc','Exit Paint or Measure mode when no transform/drag is active.')])
section('Useful sequences',[
('4, select vertices,<br/>S, Z, 0, Enter','Flatten selected vertices along Z at the current pivot. Use Shift-click or a box to select the vertices.'),('2, select faces,<br/>G, Y, 2, Enter','Move selected faces 2 map units on Y.'),('3, select an edge,<br/>B','Bevel that edge using the default width and segment count.')])
note('<b>Menu/inspector tools:</b> Split, dissolve, collapse, slide, merge first/last/by distance, connect, rip, duplicate/separate/join, boundary selection, triangulate, winding repair, CSG and source reimport have no dedicated keyboard bindings in this build.')
note('<b>Source of truth:</b> MapViewport.cs (pointer and key handlers), MapViewportModeling.cs (element selection and transform keys), and MapStudioScreen.cs (project shortcuts), under src/MphRead/Mods/Launcher/Gui. This reference covers application-specific bindings, not standard text-field or operating-system shortcuts.')
def footer(c,doc):
 c.setStrokeColor(teal);c.setLineWidth(2);c.line(42,752,570,752)
 c.setFont('Helvetica',8);c.setFillColor(muted);c.drawString(42,27,'PROJECT PRIME  |  Map Studio  |  September 2026');c.drawRightString(570,27,f'{doc.page} / 4')
doc=SimpleDocTemplate(str(out),pagesize=(612,792),rightMargin=42,leftMargin=42,topMargin=52,bottomMargin=44,title='Project Prime Map Studio - Hotkeys and Shortcuts',author='Project Prime',subject='Current keyboard bindings, mouse gestures, and contextual modeling controls')
doc.build(story,onFirstPage=footer,onLaterPages=footer)
r=PdfReader(str(out));print(str(out));print('Pages:',len(r.pages));assert len(r.pages)==4
for n,pg in enumerate(r.pages,1): print(n,len(pg.extract_text()))
