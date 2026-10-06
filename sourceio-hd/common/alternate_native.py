"""Generated native alternate reference import and exact full rest matrices.

Reuse the established native importer by presenting a private, temporary
reference descriptor with the chosen alternate contract. Production reference
files and the generated exporters are never rewritten.
"""
import json, tempfile, shutil
from pathlib import Path
from native import load_native, reference_bind

def contract_for(kit, part='alternateForm'):
    return next(m for m in json.loads((Path(kit)/'native-reference.json').read_text())['models'] if m['part']==part)

def import_alternate(kit, hunter, common_module, part='alternateForm'):
    kit=Path(kit);contract=contract_for(kit,part)
    with tempfile.TemporaryDirectory(prefix='prime-native-alternate-') as tmp:
        proxy=Path(tmp)
        descriptor=json.loads((kit/'native-reference.json').read_text())
        selected=dict(contract,part='biped',lod=0)
        descriptor['models']=[selected]
        (proxy/'native-reference.json').write_text(json.dumps(descriptor))
        (proxy/'reference').mkdir()
        # The generated script can load relative image paths while importing;
        # symlinking preserves the authoritative source directory intact.
        (proxy/'reference'/contract['model']).symlink_to((kit/'reference'/contract['model']).resolve(),target_is_directory=True)
        rig,meshes=load_native(proxy,hunter,common_module)
        authoritative=reference_bind(proxy)
    return contract,rig,meshes,authoritative
