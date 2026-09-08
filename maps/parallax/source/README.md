# PARALLAX — Alimbic visual pass v0.2

Original competitive 1v1 arena for Fruity Prime. Following the user's design-audit
approval, this pass replaces the graybox materials with original Alimbic-inspired
stone, bronze panels and geometric route glyphs. The BSP and gameplay recipe are
byte-identical to the approved layout. See [visual-pass evidence](art/README.md)
and the historical [graybox design review](REVIEW.md).

## Play

Place the generated `PARALLAX.fpmap` in the game's `maps/` directory, launch the
game and select **Parallax** for a local or multiplayer Battle match. Each peer
needs the same map. The bundle includes the recipe, BSP and original texture pack;
players still need their normal MPH game files.

For development, the existing project build copies `parallax.json` and
`parallax.pk3` to its output. No engine registration or feature changes are needed.

## Authoritative inputs

- `maps/parallax.map`: editable Quake 3 world brushes, with named sections.
- `scripts/parallax.shader`, `scripts/shaderlist.txt`, `textures/parallax/*.tga`:
  ten original 64×64 materials. No cartridge or commercial Quake art.
- `art/`: original stone master, deterministic material authoring, previews and
  the guarded material-only packaging workflow.
- `../parallax.json`: MPH spawns, items, jump pads, lighting and preview camera.
- `../parallax.pk3`: compiled development input, rebuilt from the above Q3 source.

The `.tex`, room binaries, thumbnails and `.fpmap` are generated outputs.
Do not edit them as source. Textures use the repository's MIT license.

## Build and test

Requirements: Python 3, [q3map2 from NetRadiant](https://github.com/xonotic/netradiant),
and a working Fruity Prime build with extracted game files. This map was compiled
with q3map2 `2.5.17n-git-b4b295d`; only its BSP/meta stage is needed. Quake lighting
and VIS are not consumed by this importer. No game pack or Quake installation is needed.

From the repository root:

```sh
python3 maps/parallax/source/build.py --q3map2 /path/to/q3map2
python3 maps/parallax/source/validate.py

FruityPrime -mapgen PARALLAX -mapdir maps/parallax -noupdate
FruityPrime -maptest PARALLAX -mapdir maps/parallax -players 2 -seconds 60 -noupdate
FruityPrime -maptest PARALLAX -mapdir maps/parallax -players 2 -renderprobe -shots /tmp/parallax-spawns -noupdate
FruityPrime -thumbnail PARALLAX -mapdir maps/parallax -noupdate
FruityPrime -mapbundle PARALLAX -mapdir maps/parallax -out /absolute/output/PARALLAX.fpmap -noupdate
```

Replace `FruityPrime` with the executable's full path, or `dotnet /path/FruityPrime.dll`.
`-mapdir maps/parallax` intentionally selects the development folder: a bundle at
the top of `maps/` would otherwise take precedence over the edited recipe.

`build.py` compiles in a temporary directory, rejects shell leaks and invalid BSPs,
packages only the BSP/shaders/textures, and invalidates this map's baked texture
cache. ZIP timestamps and permissions are fixed. Two builds with the tested compiler
produced identical PK3 bytes. Run `-mapgen` after every geometry or entity edit.
Recook the bundle after validation; an older bundle is not updated by `-mapgen`.

The optional CPU movement check uses the current checkout's existing headless scene
API. It creates a temporary .NET 10 project; it never edits the engine or the selected
game build. Run after generating the room in the named extracted asset directory:

```sh
python3 maps/parallax/source/check-movement.py \
  --game-dir /path/to/game-build \
  --data-dir /path/to/extracted/AMHE1 \
  --map-dir /absolute/path/to/maps/parallax
```

Pass `--dotnet /path/to/dotnet` if the .NET 10 SDK is not on PATH. This check drives
both forms of all seven Hunters through the north ramp, east lower ramp and north
pad. It checks landing, not merely pad activation. Its final Spire check measures
reaching the climb's top height, **not dismounting onto the deck**. Mirrored routes
are checked for exact BSP symmetry, not independently timed by this probe.

## Layout and tuning

All coordinates here and in JSON are MPH `(x, y, z)`. In the editor, use Quake
`(32*x, -32*z, 32*y)`. `unitsPerUnit=32`, `scaleFactor=4`. Rotational counterpart:
`(-x, y, -z)`. The 56-unit playable footprint has a one-unit outer shell; total
collision bounds are 58×17.5×58, with floor -4 and ceiling 11.5.

| Feature | North/east representative; counterpart is rotated 180° |
|---|---|
| Core | Octagonal floor at Y=0, radius about 11; split reactor and overhead span, AW at (0, 0.6, 0) |
| Main ring exits | Four 5.5-wide cardinal bridges; hard cover interrupts straight axial views |
| Upper deck | X=-13…4, Z=16…21, Y=6; landing apron X=-1…5, Z=15…16 |
| Standard ramp | X=-13…-9, Z=0…16, rises 0→6; 20.6° slope |
| Spire wall | X=-10…-5, Z=15.5; Y=0→6 from the main-level approach, no overhanging lip |
| Turret shelf | X=-17…-13, Z=7…11, Y=3; joined to the ramp, rear screen and drop exit |
| Lower loop | Annular floor at Y=-4, under the four bridges and upper decks; staggered baffles and columns |
| Lower exit ramp | X=12…22, Z=5…9, rises -4→0; landing continues to X=24 |
| Boost routes | Broad faceted perimeter arcs; uninterrupted supported centerline at radius 24 |
| Duel lane | NE↔SW diagonal through the split reactor, verified clear over 29.7 units; covered endpoints |
| Spawns | Four roofed corner alcoves at radius 27, ±19.09188 on X/Z; two dogleg exits each |

Each deck has a ramp, a JSON pad, a Spire approach and a drop. The lower loop has
two ramps, two pads, four bridge underpasses and drop entrances. The perimeter
connects all four sectors without requiring the center.

Pickup economy: one AffinityWeapon (300 frames), two HealthMedium (300), four
UASmall (150), two MissileSmall (240). No fixed affinity weapons or powerups.
Health is below deck level; the upper decks carry only small UA.

**Jump-pad tuning differs from the illustrative brief:** pads are at
`(±6, -3.9, ±12)` with destinations `(±2, 6.2, ±18)`, cooldown 20 and control lock
**0**. The actual player code delays gravity updates during that lock; 24 caused
ceiling impacts despite the importer's ballistic solver predicting clearance.
Zero avoids those impacts. The engine still applies its minimum input lock and
Hunter-specific alt-form adjustment. The wider apron catches shorter alt-form
arcs. Re-run both static and runtime checks when changing this geometry or timing.

The fixed compiler-only Quake spawn at the origin seals/floods the BSP; it is
discarded by `keepSpawns=false`. The four JSON spawns are the only runtime starts.
No Quake items or push triggers are imported.

## Visual key

Weathered carved stone walls; cyan circuit reactor; quiet neutral main floor;
bronze upper-route chevrons; blue-gray lower loop; teal perimeter lanes; green
spawn arch glyphs; pale climb chevrons and concentric diamond pad markings.
The ceiling uses subdued stone coffers to reduce overhead visual noise. The cyan
is painted into the albedo: this pass adds no engine emission or dynamic lighting.
No decorative collision, patches, sky, moving platforms, hazards, teleports or
doors were added. The original lighting and all gameplay settings are preserved.
