"""Reuse hunter team recoloring for first-person shells, preserving neutral details."""
import json,hashlib,sys,argparse
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from recolors import add_recolors
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);a=p.parse_args();cfg=json.loads(Path(a.config).read_text());root=Path(a.output).resolve()
# The common recolor writer supports an explicit asset path for each part.
config=dict(cfg,teamRecolorMaterials=cfg.get('teamRecolorMaterials',[cfg['mainMaterial']]),recolorModelRelative=cfg['hunter'].lower()+'/viewmodel.glb',recolorPackDirectory='starter')
add_recolors(config,root)
audit=json.loads((root/'audit.json').read_text());audit['teamRecolorsRequired']=True;audit['viewmodelSha256']=hashlib.sha256((root/'starter'/cfg['hunter'].lower()/'viewmodel.glb').read_bytes()).hexdigest();(root/'audit.json').write_text(json.dumps(audit,indent=2)+'\n')
