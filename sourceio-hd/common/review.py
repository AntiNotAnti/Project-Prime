"""Create a contact sheet from recorded acceptance screenshots for visual review."""
import argparse
from pathlib import Path
from PIL import Image,ImageDraw
p=argparse.ArgumentParser();p.add_argument('--output',required=True);a=p.parse_args();root=Path(a.output).resolve();capture=root/'acceptance'
names=['launcher-before','same-pose-native-biped','same-pose-source-biped','run-040','strafe-040','jump-fall-land-040','aim-turn-040','fire-040','damage-040','freeze-040','morph-move-119','unmorph-040','bright-skin-040','team-orange-040','team-green-040','double-damage-040','death-respawn-040','launcher-after']
width,height=480,380;sheet=Image.new('RGB',(width*3,height*6),'#111318');draw=ImageDraw.Draw(sheet)
for i,name in enumerate(names):
 path=capture/(name+'.png')
 if not path.exists():continue
 image=Image.open(path).convert('RGB');image.thumbnail((width,height-24));x=i%3*width;y=i//3*height
 sheet.paste(image,(x+(width-image.width)//2,y+24));draw.text((x+8,y+6),name,fill='white')
sheet.save(root/'acceptance-review.jpg',quality=93)
print(root/'acceptance-review.jpg')
