#!/usr/bin/env python3
"""Compare captures without resizing. Requires Pillow and NumPy.
Optional --thresholds JSON maps exact PNG names to metric limits, e.g.
{"world-hud.png": {"mean": 0, "rmse": 0, "maximum": 0}}.
All errors and limits use normalized [0, 1] RGB values.
"""
import argparse
import json
import math
from pathlib import Path
import numpy as np
from PIL import Image


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference', type=Path)
    p.add_argument('candidate', type=Path)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--mean-limit', type=float, default=0.06)
    p.add_argument('--structural-limit', type=float, default=0.08)
    p.add_argument('--changed-limit', type=float, default=0.10)
    p.add_argument('--rmse-limit', type=float, default=1.0)
    p.add_argument('--maximum-limit', type=float, default=1.0)
    p.add_argument('--thresholds', type=Path)
    a = p.parse_args()
    defaults = dict(mean=a.mean_limit, structural=a.structural_limit,
                    fractionOver10Percent=a.changed_limit, rmse=a.rmse_limit,
                    maximum=a.maximum_limit)
    overrides = json.loads(a.thresholds.read_text()) if a.thresholds else {}
    if not isinstance(overrides, dict):
        p.error('thresholds must be an object keyed by PNG filename')
    for name, limits in [('defaults', defaults), *overrides.items()]:
        if not isinstance(limits, dict) or set(limits) - set(defaults):
            p.error(f'invalid thresholds for {name}')
        if any(isinstance(v, bool) or not isinstance(v, (float, int))
               or not math.isfinite(v) or not 0 <= v <= 1 for v in limits.values()):
            p.error(f'thresholds for {name} must be finite numbers in [0, 1]')
    refs = {f.name: f for f in a.reference.glob('*.png')}
    candidates = {f.name: f for f in a.candidate.glob('*.png')}
    if set(overrides) - set(refs):
        p.error('thresholds name captures absent from reference')
    results = []
    diff_dir = a.output.parent / (a.output.stem + '-diffs')
    diff_dir.mkdir(parents=True, exist_ok=True)
    for name in sorted(set(refs) | set(candidates)):
        limits = defaults | overrides.get(name, {})
        result = dict(image=name, limits=limits, passed=False)
        results.append(result)
        if name not in refs or name not in candidates:
            result['error'] = 'missing reference' if name not in refs else 'missing candidate'
            continue
        try:
            with Image.open(refs[name]) as image:
                x = image.convert('RGB')
            with Image.open(candidates[name]) as image:
                y = image.convert('RGB')
            if x.size != y.size:
                result['error'] = f'size {x.size} != {y.size}'
                continue
            diff = np.abs(np.asarray(x, dtype=float) - np.asarray(y, dtype=float)) / 255
            coarse = np.abs(np.asarray(x.resize((64, 64)), dtype=float)
                            - np.asarray(y.resize((64, 64)), dtype=float)) / 255
            metrics = dict(mean=float(diff.mean()), structural=float(coarse.mean()),
                           fractionOver10Percent=float((diff.max(axis=2) > 0.1).mean()),
                           rmse=float(np.sqrt(np.square(diff).mean())), maximum=float(diff.max()))
            heatmap = np.zeros((*diff.shape[:2], 3), dtype=np.uint8)
            heatmap[:, :, 0] = np.rint(diff.max(axis=2) * 255).astype(np.uint8)
            heatmap_path = diff_dir / name
            Image.fromarray(heatmap).save(heatmap_path)
            result.update(metrics, differenceImage=str(heatmap_path),
                          passed=all(metrics[key] <= value for key, value in limits.items()))
        except (OSError, ValueError) as error:
            result['error'] = str(error)
    passed = bool(results) and all(r['passed'] for r in results)
    a.output.write_text(json.dumps(dict(passed=passed, reference=str(a.reference),
        candidate=str(a.candidate), meanLimit=a.mean_limit, structuralLimit=a.structural_limit,
        changedLimit=a.changed_limit, limits=defaults, images=results), indent=2) + '\n')
    print(a.output)
    print(f'{sum(r["passed"] for r in results)}/{len(results)} captures passed')
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
