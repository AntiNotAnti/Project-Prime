#!/usr/bin/env python3
"""Compare an approved HUD PNG baseline directory with a candidate directory.
Requires Pillow: python3 -m pip install Pillow
Use --max-channel-delta for rasterizer noise, --max-changed-fraction for a stated
pixel budget. Defaults require exact equality. Never updates the approved baseline.
"""
import argparse
from pathlib import Path
from PIL import Image, ImageChops

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('baseline', type=Path)
parser.add_argument('candidate', type=Path)
parser.add_argument('--max-channel-delta', type=int, default=0)
parser.add_argument('--max-changed-fraction', type=float, default=0)
parser.add_argument('--diffs', type=Path)
args = parser.parse_args()
if not 0 <= args.max_channel_delta <= 255 or not 0 <= args.max_changed_fraction <= 1:
    parser.error('tolerances must be in [0,255] and [0,1]')
files = sorted(args.baseline.glob('*.png'))
if not files:
    parser.error('baseline contains no PNGs')
failures = 0
for baseline in files:
    candidate = args.candidate / baseline.name
    if not candidate.is_file():
        print(f'FAIL {baseline.name}: candidate missing'); failures += 1; continue
    with Image.open(baseline) as source, Image.open(candidate) as result:
        if source.size != result.size:
            print(f'FAIL {baseline.name}: {source.size} != {result.size}'); failures += 1; continue
        diff = ImageChops.difference(source.convert('RGBA'), result.convert('RGBA'))
        channels = diff.split()
        mask = channels[0]
        for channel in channels[1:]: mask = ImageChops.lighter(mask, channel)
        histogram = mask.histogram()
        changed = sum(histogram[args.max_channel_delta + 1:]) / (source.width * source.height)
        passed = changed <= args.max_changed_fraction
        print(f'{"PASS" if passed else "FAIL"} {baseline.name}: {changed:.6%} changed')
        if not passed:
            failures += 1
            if args.diffs:
                args.diffs.mkdir(parents=True, exist_ok=True)
                mask.save(args.diffs / baseline.name)
raise SystemExit(1 if failures else 0)
