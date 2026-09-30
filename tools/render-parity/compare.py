#!/usr/bin/env python3
"""Compare identically named captures. Requires Pillow and NumPy.
Reports color error and coarse structural error; never silently resizes images.
"""
import argparse, json
from pathlib import Path
import numpy as np
from PIL import Image
p=argparse.ArgumentParser()
p.add_argument('reference',type=Path); p.add_argument('candidate',type=Path)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--mean-limit',type=float,default=0.06)
p.add_argument('--structural-limit',type=float,default=0.08)
p.add_argument('--changed-limit',type=float,default=0.10)
a=p.parse_args(); results=[]
for ref in sorted(a.reference.glob('*.png')):
    other=a.candidate/ref.name
    if not other.exists():
        results.append(dict(image=ref.name,passed=False,error='missing candidate'));continue
    x=Image.open(ref).convert('RGB'); y=Image.open(other).convert('RGB')
    if x.size!=y.size:
        results.append(dict(image=ref.name,passed=False,error=f'size {x.size} != {y.size}'));continue
    diff=np.abs(np.asarray(x,dtype=float)-np.asarray(y,dtype=float))/255
    # Downsampling suppresses vendor/filter edge noise while retaining missing
    # geometry, shifted viewports, wrong orientation and missing UI elements.
    coarse=np.abs(np.asarray(x.resize((64,64)),dtype=float)-np.asarray(y.resize((64,64)),dtype=float))/255
    mean=float(diff.mean()); structural=float(coarse.mean())
    changed=float((diff.max(axis=2)>0.1).mean())
    results.append(dict(image=ref.name,mean=mean,structural=structural,
        fractionOver10Percent=changed,
        passed=mean<=a.mean_limit and structural<=a.structural_limit and changed<=a.changed_limit))
passed=bool(results) and all(r['passed'] for r in results)
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps(dict(passed=passed,reference=str(a.reference),candidate=str(a.candidate),
    meanLimit=a.mean_limit,structuralLimit=a.structural_limit,changedLimit=a.changed_limit,images=results),indent=2)+'\n')
print(a.output); print(f'{sum(r["passed"] for r in results)}/{len(results)} captures passed')
raise SystemExit(0 if passed else 1)
