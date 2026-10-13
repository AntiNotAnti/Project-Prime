"""Compare the fixed 1280x720 ballot witness outside randomized gradient interiors.

The borders, layout, heading, footer and all other pixels must match exactly.
This narrow diagnostic is not a general screenshot acceptance tool.
"""
import json
from pathlib import Path
import struct
import sys
import zlib


def decode(path):
    data = Path(path).read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    offset, image_data = 8, []
    while offset < len(data):
        size = struct.unpack(">I", data[offset:offset + 4])[0]
        kind, value = data[offset + 4:offset + 8], data[offset + 8:offset + 8 + size]
        offset += size + 12
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", value)
            assert (depth, color, compression, filtering, interlace) == (8, 6, 0, 0, 0)
        elif kind == b"IDAT":
            image_data.append(value)
    raw = zlib.decompress(b"".join(image_data))
    stride = width * 4
    pixels, previous = bytearray(stride * height), bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        method, row = raw[start], bytearray(raw[start + 1:start + stride + 1])
        if method:
            for x in range(stride):
                left, above = row[x - 4] if x >= 4 else 0, previous[x]
                upper_left = previous[x - 4] if x >= 4 else 0
                if method == 1:
                    prediction = left
                elif method == 2:
                    prediction = above
                elif method == 3:
                    prediction = (left + above) // 2
                elif method == 4:
                    p = left + above - upper_left
                    a, b, c = abs(p - left), abs(p - above), abs(p - upper_left)
                    prediction = left if a <= b and a <= c else above if b <= c else upper_left
                else:
                    raise ValueError("Unsupported PNG filter")
                row[x] = (row[x] + prediction) & 255
        pixels[y * stride:(y + 1) * stride], previous = row, row
    return width, height, pixels


def compare(first, second):
    width, height, a = decode(first)
    width2, height2, b = decode(second)
    assert (width, height) == (width2, height2) == (1280, 720)
    interiors = [(944, 181, 1093, 263), (1100, 181, 1249, 263)]
    changed, outside, maximum = [], [], 0
    for y in range(height):
        for x in range(width):
            i = (y * width + x) * 4
            if a[i:i + 4] != b[i:i + 4]:
                changed.append((x, y))
                maximum = max(maximum, *(abs(a[i + k] - b[i + k]) for k in range(4)))
                if not any(l <= x < r and t <= y < bottom for l, t, r, bottom in interiors):
                    outside.append((x, y))

    def bounds(points):
        return [min(x for x, _ in points), min(y for _, y in points),
                max(x for x, _ in points) + 1, max(y for _, y in points) + 1] if points else None

    return {"dimensions": [width, height], "differentPixels": len(changed), "bbox": bounds(changed),
            "maxChannelDelta": maximum, "tileInteriors": interiors,
            "outsideTileInteriorDifferences": len(outside), "outsideBbox": bounds(outside)}


if __name__ == "__main__":
    result = compare(sys.argv[1], sys.argv[2])
    encoded = json.dumps(result, indent=2) + "\n"
    print(encoded, end="")
    if len(sys.argv) > 3:
        Path(sys.argv[3]).write_text(encoded)
    sys.exit(1 if result["outsideTileInteriorDifferences"] else 0)
