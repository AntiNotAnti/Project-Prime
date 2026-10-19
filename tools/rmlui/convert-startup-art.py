"""Losslessly encode the authored launcher RGB PNG for RmlUi's uncompressed TGA loader."""
import hashlib
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = ROOT / 'src/MphRead/Assets/Backgrounds/launcher-bg.png'
target = ROOT / 'src/MphRead/Mods/Launcher/RmlUi/Pages/startup/startup-background.tga'
data = source.read_bytes()
assert data[:8] == b'\x89PNG\r\n\x1a\n'
width, height, bits, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', data[16:29])
assert (width, height, bits, color, compression, filtering, interlace) == (1672, 941, 8, 2, 0, 0, 0)
offset, chunks = 8, []
while offset < len(data):
    count = struct.unpack('>I', data[offset:offset + 4])[0]
    kind, payload = data[offset + 4:offset + 8], data[offset + 8:offset + 8 + count]
    assert zlib.crc32(kind + payload) == struct.unpack('>I', data[offset + 8 + count:offset + 12 + count])[0]
    if kind == b'IDAT': chunks.append(payload)
    offset += count + 12
raw = zlib.decompress(b''.join(chunks))
stride = width * 3
assert len(raw) == height * (stride + 1)
rgb = bytearray()
previous = bytearray(stride)
def paeth(a, b, c):
    prediction = a + b - c
    distances = [abs(prediction - n) for n in (a, b, c)]
    return (a, b, c)[distances.index(min(distances))]
for y in range(height):
    start = y * (stride + 1)
    mode, row = raw[start], bytearray(raw[start + 1:start + 1 + stride])
    assert mode in range(5)
    for x in range(stride):
        left = row[x - 3] if x >= 3 else 0
        above = previous[x]
        corner = previous[x - 3] if x >= 3 else 0
        predictor = (0, left, above, (left + above) // 2, paeth(left, above, corner))[mode]
        row[x] = (row[x] + predictor) & 255
    rgb.extend(row)
    previous = row
header = bytearray(18)
header[2], header[16], header[17] = 2, 24, 0x20
struct.pack_into('<HH', header, 12, width, height)
bgr = bytearray(rgb)
bgr[0::3], bgr[2::3] = rgb[2::3], rgb[0::3]
target.write_bytes(header + bgr)
assert target.read_bytes()[18:][2::3] == rgb[0::3]
print('Authored source SHA256:', hashlib.sha256(data).hexdigest())
print('RGB pixels SHA256:', hashlib.sha256(rgb).hexdigest())
print('Native TGA SHA256:', hashlib.sha256(target.read_bytes()).hexdigest())
