"""Rigid alternate companion maps without changing native effect bindings."""
import json,shutil
from pathlib import Path
from glb import load

def copies(cfg,root):
    for target,origin in cfg.get('materialCopies',{}).items():
        for channel in ['albedo','normal','emissive','material']:
            source=Path(root)/'textures'/(origin+'-'+channel+'.png')
            if source.exists():shutil.copy2(source,Path(root)/'textures'/(target+'-'+channel+'.png'))

def companions(cfg,root,source):
    from material_maps import add_material_maps
    from recolors import add_recolors
    root=Path(root);path=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb';copies(cfg,root);add_material_maps(root,path);add_recolors(cfg,root)
    doc,_=load(source);selected=json.loads((root/'atlas-layout.json').read_text())['materials'];assert all(m.get('alphaMode','OPAQUE')=='OPAQUE' for m in doc['materials'] if m['name'] in selected),'Add explicit Source alpha partition before shipping transparent rigid alternates'
    (root/'source-alpha.json').write_text(json.dumps({'pass':True,'scope':'All selected Source materials explicitly OPAQUE; native effect/attack visibility remains native alpha/node state','materials':{m['name']:'OPAQUE' for m in doc['materials'] if m['name'] in selected}},indent=2)+'\n')
