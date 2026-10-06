"""Source-model counterexamples, not a native GPU benchmark."""
import math

def reduce_shader(source):
    height, width = len(source), len(source[0])
    out_w, out_h = max(1, width // 2), max(1, height // 2)
    return [[max(source[min(height - 1, y * 2 + dy)][min(width - 1, x * 2 + dx)]
                 for dy in (0, 1) for dx in (0, 1))
             for x in range(out_w)] for y in range(out_h)]

source = [[0.1] * 5 for _ in range(3)]
for row in source:
    row[-1] = 1.0
reduced = reduce_shader(source)
assert max(map(max, source)) == 1.0
assert max(map(max, reduced)) == 0.1
print('Hi-Z WGSL source model: 5x3 -> 2x1; clear final column max1.0 -> max0.1 (dropped)')
print('Visibility comparison: candidate nearest0.5 > reduced0.1 + bias0.0025 => falsely occluded')

levels = []
width, height = 1920, 1080
while width > 1 or height > 1:
    if (width > 1 and width % 2) or (height > 1 and height % 2):
        levels.append((width, height))
    width, height = max(1, width // 2), max(1, height // 2)
print('1920x1080 native mip dimensions with dropped trailing footprints:', levels)

linear_light_half = 1.055 * math.pow(0.5, 1 / 2.4) - 0.055
print(f'Color mip arithmetic: encoded black/white mean={round(255*.5)}; linear-light encoded mean={round(255*linear_light_half)}')
assert round(255*linear_light_half) == 188

pages = []
for match in range(50):
    # Arbitrary example geometry, not observed room data. At each room close
    # entries disappear, but the allocator's cursors stay consumed.
    vertex, index = 2 * 1024 * 1024, 512 * 1024
    page = next((p for p in pages if p[0]+vertex <= 16*1024*1024 and p[1]+index <= 4*1024*1024), None)
    if page is None:
        page = [0, 0]
        pages.append(page)
    page[0] += vertex
    page[1] += index
print(f'Atlas source-model example: 50 allocate/remove cycles of assumed 2MiB+0.5MiB => {len(pages)} pages / {len(pages)*20}MiB capacity; live entries0')

print('Temporal source model: stationary target0.5, stationary camera, previous moving occluder0.1; occluder moves out => unchanged target passes motion gate but0.5 >0.1025 culls')
