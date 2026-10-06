"""Constrain actual generated paint to registered Source UV tiles.

These recipes compose artist bitmaps with immutable Source registration and
derive explicit material-class ORM/micro-normal data. They do not generate
paint from a filter, infer physical measurements or change mesh UVs.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


PROFILES = {
    "Samus_Armor": (.36,"coated armor"), "Samus_Armor2":(.78,"flexible armor joint"),
    "Samus_Armor3":(.74,"flexible armor joint"), "Samus_Armor4":(.46,"coated armor"),
    "Samus_Light":(.24,"emitter"), "ArmCanon1":(.38,"coated weapon"),
    "ArmCanon2":(.30,"exposed weapon rim"), "ArmCanon3":(.35,"coated weapon"),
    "Kanden_Body1":(.57,"organic carapace"), "Kanden_Body2":(.66,"organic carapace"),
    "Black":(.92,"dark flexible surface"), "Kanden_Eyes":(.24,"emitter"), "Kanden_Light":(.24,"emitter"),
    "Noxus_Body1":(.48,"smooth shell"), "Noxus_Body2":(.38,"satin cellular shell"), "Noxus_Light":(.24,"emitter"),
    "Spire_Body":(.84,"rough rock"), "Spire_Torso":(.82,"rough rock"),
    "Spire_Crystal":(.20,"crystal emitter"), "Spire_Eyes":(.24,"emitter"),
    "Sylux_Body1":(.40,"coated armor"), "Sylux_Body2":(.53,"coated armor"),
    "Metal":(.32,"exposed metal"), "Sylux_Glow":(.24,"emitter"),
    "Trace_Body":(.59,"organic shell"), "Trace_Black":(.92,"dark flexible surface"), "Trace_Glow":(.24,"emitter"),
    "Weavel_Body":(.44,"coated armor"), "Weavel_Handgun":(.37,"mixed coated weapon and exposed neutral steel"),
    "Weavel_Handgun2":(.27,"exposed weapon trim"), "Weavel_Hair":(.83,"alpha hair"), "Weavel_Light":(.24,"emitter"),
}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def pixel_sha(array):
    return hashlib.sha256(array.tobytes()).hexdigest()


def rgba(path, size=None, method=Image.Resampling.LANCZOS):
    image = Image.open(path).convert("RGBA")
    if size and tuple(size) != image.size:
        # Source alpha can be a shader mask or an ORM encoding marker, while
        # RGB remains meaningful even when A is zero. Pillow RGBA Lanczos
        # implicitly premultiplies and would erase those RGB data channels.
        # Filter each independent channel, including alpha, in isolation.
        image = Image.merge("RGBA",tuple(channel.resize(tuple(size),method) for channel in image.split()))
    return np.asarray(image,dtype=np.uint8).copy()


def save(array, path):
    path = Path(path); path.parent.mkdir(parents=True,exist_ok=True)
    Image.fromarray(array).save(path)
    return {"path":str(path.resolve()),"sha256":sha(path),"pixelSha256":pixel_sha(array),"size":list(array.shape[1::-1])}


def reference_registration(target, prompt_path):
    prompt = json.loads(Path(prompt_path).read_text())
    reference = Path(prompt.get("reference",prompt.get("referencePath","")))
    if not reference.is_file():
        raise ValueError(f"Generated image prompt lacks an inspectable original reference: {prompt_path}")
    reference_pixels = rgba(reference)
    size = reference_pixels.shape[1::-1]
    candidates = []
    for basis in (target["sourceAuthoredAlbedo"]["path"],target["originalChannels"]["albedo"]["path"]):
        pixels = rgba(basis,size)
        for flip in (False,True):
            compared = pixels[::-1] if flip else pixels
            mae = float(np.abs(compared[...,:3].astype(float)-reference_pixels[...,:3]).mean())
            candidates.append((mae,flip,basis))
    mae,flip,basis = min(candidates)
    if mae > .02:
        raise ValueError(f"Cannot prove generated reference orientation against immutable Source: {target['sourceMaterial']}: {mae}")
    return {"referencePath":str(reference.resolve()),"referenceSha256":sha(reference),
        "basisPath":basis,"basisSha256":sha(basis),"referenceRgbMeanError255":mae,
        "generatedToGltfTransform":"vertical flip" if flip else "identity"}


def protected_mask(target, size):
    source = rgba(target["originalChannels"]["albedo"]["path"])
    rgb = source[...,:3].astype(float)/255
    gray = rgb @ np.array([.2126,.7152,.0722])
    edge = np.maximum(np.max(np.abs(rgb-np.roll(rgb,1,axis=0)),axis=2),
                      np.max(np.abs(rgb-np.roll(rgb,1,axis=1)),axis=2))
    # Protect high contrast authored boundaries and deep mechanical cavities.
    # The threshold is a registration guard, not a semantic classifier.
    mask = (edge >= .15) | (gray <= .055)
    if target["sourceMaterial"] in ("Weavel_Body","Weavel_Handgun","Spire_Torso"):
        # These specific sheets contain authoritative orange/yellow markings.
        # Dominant blue, violet or red armor pigment is not a marking mask.
        orange = (rgb[...,0] >= .45) & (rgb[...,1] >= .15) & (rgb[...,2] <= .25)
        orange &= rgb[...,0] - rgb[...,2] >= .30
        mask |= orange
        if target["sourceMaterial"] == "Spire_Torso":
            mask |= gray >= .94  # small original tooth pieces
    image = Image.fromarray(mask.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(3))
    return np.asarray(image.resize(tuple(size),Image.Resampling.NEAREST)) != 0


def paint_tile(target, art_root, output):
    name = target["sourceMaterial"]; roughness,surface = PROFILES[name]
    generated = Path(art_root) / (name+"-repaint.png")
    prompt_path = Path(art_root) / (name+"-prompt.json")
    old_tiles = list(target["shippingAuthoredAlbedoTiles"].values())
    size = max((row["size"] for row in old_tiles),key=lambda pair:pair[0]*pair[1])
    old = rgba(max(old_tiles,key=lambda row:row["size"][0]*row["size"][1])["path"],size)
    protected = protected_mask(target,size)
    folder = Path(output)/target["hunter"].lower()/name; folder.mkdir(parents=True,exist_ok=True)
    registration = None; operation = "preserve"; generated_proof = []
    color = old.copy()
    if generated.is_file():
        if not prompt_path.is_file():
            raise FileNotFoundError(prompt_path)
        registration = reference_registration(target,prompt_path)
        paint = rgba(generated,size)
        if registration["generatedToGltfTransform"] == "vertical flip":
            paint = paint[::-1]
        # New paint is the visible primary surface. Original deep seams and
        # registered markings remain exact, and conservative color-family
        # bounds prevent accidental chrome/black/white redesign.
        source = old[...,:3].astype(np.float32)
        authored = paint[...,:3].astype(np.float32)
        residual = np.clip(authored-source,-72,72)
        composed = np.clip(source + .72*residual,0,255)
        color[...,:3] = np.rint(composed).astype(np.uint8)
        color[protected] = old[protected]
        color[...,3] = old[...,3]
        operation = "repaint"
        generated_proof = [{"tool":"image_gen","path":str(generated.resolve()),"sha256":sha(generated),
            "promptPath":str(prompt_path.resolve()),"promptSha256":sha(prompt_path)}]
    elif target["hunter"] == "Samus" and target.get("priorGeneratedSamusArt",{}).get("generatedBitmap"):
        previous = target["priorGeneratedSamusArt"]
        if not (previous.get("allShippingUsesExactlyMatchKnownGoodArtwork")
                or previous.get("allShippingUsesExactlyMatchKnownGoodJpeg")):
            raise ValueError("Samus accepted artwork does not match current shipping tile")
        operation = "reusedAuthoredPaint"
        generated_proof = [{"tool":"image_gen","path":previous["generatedBitmap"],"sha256":previous["sha256"],
            "promptPath":previous["promptFile"],"promptSha256":previous["promptFileSha256"]}]
    elif target["hunter"] != "Samus" and not (target["sourceMaterialContract"].get("selfIllum")
                    or name in ("Black","Trace_Black","Weavel_Hair")):
        raise FileNotFoundError(f"Missing authorized generated surface paint: {generated}")
    albedo = save(color,folder/"albedo.png")
    guard = save(protected.astype(np.uint8)*255,folder/"protected-registration.png")
    finish_color = color
    if operation == "reusedAuthoredPaint":
        # Reuse the artist's lossless master for finish detail, avoiding normal
        # relief fabricated from desktop JPEG block/quantization artifacts.
        finish_color = rgba(target["priorGeneratedSamusArt"]["acceptedArtworkPath"],size)
    gray = finish_color[...,:3].astype(np.float32) @ np.array([.2126,.7152,.0722])/255
    # Pillow GaussianBlur does not support F mode; derive a small normalized
    # finish-height signal from RGBA luminance and a periodic 5-tap mean.
    smooth = (gray+np.roll(gray,1,0)+np.roll(gray,-1,0)+np.roll(gray,1,1)+np.roll(gray,-1,1))/5
    height = np.clip(gray-smooth,-.08,.08)
    dx = (np.roll(height,-1,1)-np.roll(height,1,1))*.5
    dy = (np.roll(height,-1,0)-np.roll(height,1,0))*.5
    amplitude = .35 if "organic" in surface else .50 if "rock" in surface else .40
    if operation == "preserve" or "emitter" in surface or "hair" in surface:
        amplitude = 0
    micro = np.stack([-dx*amplitude,-dy*amplitude],axis=2).astype(np.float32)
    micro *= np.minimum(1,.025/np.maximum(np.linalg.norm(micro,axis=2,keepdims=True),1e-8))
    micro_path = folder/"micro-normal-xy.npy"; np.save(micro_path,micro)
    base_gray = old[...,:3].astype(np.float32) @ np.array([.2126,.7152,.0722])/255
    cavities = np.clip((.20-base_gray)/.20,0,1)
    ao = 1-.08*cavities
    if "emitter" in surface:
        ao = np.ones_like(ao)
    rough = np.clip(roughness + .10*cavities + .035*np.clip(height/.08,-1,1),.08,.96)
    metallic = np.zeros_like(ao)
    if name in ("Metal","Weavel_Handgun2"):
        metallic = np.full_like(ao,.78)
    elif name in ("Weavel_Handgun","ArmCanon2"):
        rgb = old[...,:3].astype(float)/255
        # Explicitly authored exposed neutral weapon-steel class. Avoid dark
        # openings and colored coated panels. This is an art mask, not a
        # physically measured classification.
        chroma = rgb.max(axis=2)-rgb.min(axis=2)
        neutral = np.clip((.14-chroma)/.07,0,1)
        middle = np.clip((base_gray-.16)/.16,0,1)*np.clip((.88-base_gray)/.18,0,1)
        metallic = .72*neutral*middle
    orm = np.stack([ao,rough,metallic,np.zeros_like(ao)],axis=2)
    orm_image = save(np.rint(np.clip(orm,0,1)*255).astype(np.uint8),folder/"orm.png")
    recipe = {"format":1,"hunter":target["hunter"],"sourceMaterial":name,"semanticMaterials":[surface],
        "operation":operation,"registration":registration,"generatedImages":generated_proof,
        "oldShippingTiles":old_tiles,"sourceOriginalAlbedo":target["originalChannels"]["albedo"],
        "albedoComposite":{"formula":"old.rgb + .72 * clamp(generated.rgb-old.rgb,-72,72); source protected RGB/alpha exact",
            "protectedMask":guard,"sourceEdgeThreshold":.15,"deepCavityLuminanceThreshold":.055,
            "explicitMarkingSheets":["Weavel_Body","Weavel_Handgun","Spire_Torso"],
            "markingPolicy":"registered orange/yellow Source pigment and small Spire white tooth pieces, not dominant armor saturation",
            "originalIslandUvPositionUnchanged":True},
        "normalRefinement":{"method":"normalize original Source XYZ first, add periodic generated finish micro XY bounded in unit space, normalize again",
            "microXYPath":str(micro_path.resolve()),"microXYSha256":sha(micro_path),"amplitude":amplitude,
            "maximumAddedXYNorm":.025,"measuredMaximumAddedXYNorm":float(np.linalg.norm(micro,axis=2).max()),
            "finishHeightSource":"lossless accepted authoring PNG" if operation == "reusedAuthoredPaint" else "registered generated paint composite",
            "originalNormalMapsDominant":True,"normalFactorPreserved":True},
        "orm":{"packing":"R AO/G roughness/B metalness/A0","roughnessBase":roughness,
            "ambientOcclusionMinimum":.92,"materialClass":surface,
            "formula":"AO=1-.08*clamp((.2-originalLuminance)/.2); rough=base+.1*cavity+.035*finish; metal explicit material-class/neutral-weapon-steel mask",
            "physicalMeasurements":False,"preserveEmitterMask":True},
        "priorGeneratedSamusArt":target.get("priorGeneratedSamusArt"),"toolSha256":sha(__file__),
        "outputs":{"albedo":albedo,"orm":orm_image}}
    recipe_path = folder/"recipe.json"; recipe_path.write_text(json.dumps(recipe,indent=2)+"\n")
    return {"recipePath":str(recipe_path.resolve()),"recipeSha256":sha(recipe_path),"operation":operation,
        "albedo":albedo,"orm":orm_image,"protectedMask":guard,"microXYPath":str(micro_path.resolve()),
        "semanticMaterials":[surface],"generatedImages":generated_proof,"size":size,
        "priorGeneratedSamusArt":target.get("priorGeneratedSamusArt")}


def build_tiles(inventory, art_root, output):
    output = Path(output).resolve()
    if output.exists():
        raise ValueError("Authoring tile output must be fresh")
    output.mkdir(parents=True)
    results = {key:paint_tile(target,art_root,output) for key,target in sorted(inventory["authoringTargets"].items())}
    (output/"TILES.json").write_text(json.dumps({"format":1,"targets":results},indent=2)+"\n")
    return results
