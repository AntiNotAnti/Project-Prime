"""Build a rectangular UASTC encoder in a NEW candidate directory only.

The existing Samus encoder is square-only. This extension uses the same pinned
KTX library/quality settings, accepts width and height independently, and can
decode any authored mip for an independent compression-quality audit.
"""
import hashlib
import json
import platform
import math
import subprocess
import tempfile
from pathlib import Path

KTX_REVISION = "6b3d8bf15788f604c6b91dd95fafabb6c59cd723"
ENCODING_POLICY = "guarded-uastc2-uastc4-lossless-rgba8-zstd-v1"
PROTOCOL = {"format": 2, "ktxRevision": KTX_REVISION,
            "uastcLevels": [2, 4], "losslessRgba8Zstd": True,
            "authoredMipInput": True}
SOURCE = r'''
#include <ktx.h>
#include <algorithm>
#include <fstream>
#include <vector>
#include <string>
#include <stdexcept>
#include <cstdio>
#include <cstdlib>
static void check(KTX_error_code c) { if(c!=KTX_SUCCESS) throw std::runtime_error(ktxErrorString(c)); }
int main(int argc,char**argv) {
 ktxTexture2*tex=nullptr;
 try {
  if(argc==2 && std::string(argv[1])=="protocol") {
   std::puts("{\"format\":2,\"ktxRevision\":\"6b3d8bf15788f604c6b91dd95fafabb6c59cd723\",\"uastcLevels\":[2,4],\"losslessRgba8Zstd\":true,\"authoredMipInput\":true}");return 0;
  }
  if(argc==5 && std::string(argv[1])=="decode") {
   unsigned level=std::stoul(argv[4]);
   check(ktxTexture2_CreateFromNamedFile(argv[2],KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,&tex));
   if(level>=tex->numLevels) throw std::runtime_error("invalid mip");
   if(ktxTexture2_NeedsTranscoding(tex))
    check(ktxTexture2_TranscodeBasis(tex,KTX_TTF_RGBA32,KTX_TF_HIGH_QUALITY));
   if(tex->vkFormat!=37 && tex->vkFormat!=43) throw std::runtime_error("decoder requires RGBA8");
   ktx_size_t offset=0;check(ktxTexture_GetImageOffset(ktxTexture(tex),level,0,0,&offset));
   size_t count=std::max(1u,tex->baseWidth>>level)*std::max(1u,tex->baseHeight>>level)*4u;
   std::ofstream out(argv[3],std::ios::binary);out.write((char*)tex->pData+offset,count);
   if(!out) throw std::runtime_error("cannot write decoded mip");
  } else {
   if(argc!=8) throw std::runtime_error("usage: encoder input.raw output.ktx2 width height levels srgb uastc2|uastc4|rgba8-zstd");
   std::string mode=argv[7];
   if(mode!="uastc2" && mode!="uastc4" && mode!="rgba8-zstd") throw std::runtime_error("invalid encoding policy");
   unsigned width=std::stoul(argv[3]),height=std::stoul(argv[4]),levels=std::stoul(argv[5]);
   if(!width||!height||width>16384||height>16384||!levels||levels>15) throw std::runtime_error("invalid dimensions");
   ktxTextureCreateInfo info{};info.vkFormat=std::stoul(argv[6])?43:37;
   info.baseWidth=width;info.baseHeight=height;info.baseDepth=1;info.numDimensions=2;
   info.numLevels=levels;info.numLayers=1;info.numFaces=1;
   check(ktxTexture2_Create(&info,KTX_TEXTURE_CREATE_ALLOC_STORAGE,&tex));
   std::ifstream in(argv[1],std::ios::binary);
   for(unsigned level=0;level<levels;level++) {
    size_t count=std::max(1u,width>>level)*std::max(1u,height>>level)*4u;
    std::vector<unsigned char> data(count);in.read((char*)data.data(),data.size());
    if(!in) throw std::runtime_error("truncated raw mip chain");
    check(ktxTexture_SetImageFromMemory(ktxTexture(tex),level,0,0,data.data(),data.size()));
   }
   if(in.peek()!=std::char_traits<char>::eof()) throw std::runtime_error("extra raw mip bytes");
   ktxBasisParams params{};params.structSize=sizeof(params);params.uastc=KTX_TRUE;
   params.threadCount=4;params.uastcFlags=mode=="uastc4"?4:2;
   // XYZ normal maps retain RGB channels. normalMap swizzles channels and
   // is deliberately not enabled. Every authored mip is supplied above.
   if(mode!="rgba8-zstd") check(ktxTexture2_CompressBasisEx(tex,&params));
   check(ktxTexture2_DeflateZstd(tex,9));
   check(ktxTexture2_WriteToNamedFile(tex,argv[2]));
  }
  ktxTexture_Destroy(ktxTexture(tex));return 0;
 }catch(const std::exception&e){if(tex)ktxTexture_Destroy(ktxTexture(tex));std::fprintf(stderr,"%s\n",e.what());return 1;}
}
'''


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def check_protocol(encoder):
    value = json.loads(subprocess.run([str(encoder), "protocol"], check=True,
                                     capture_output=True, text=True).stdout)
    if value != PROTOCOL:
        raise ValueError("Encoder does not implement the guarded authored-mip protocol")
    return value


def build(directory):
    repo = Path(__file__).resolve().parents[2]
    source_root = repo / "artifacts/ktx-source/KTX-Software"
    revision = subprocess.run(["git", "-C", str(source_root), "rev-parse", "HEAD"],
                              check=True, capture_output=True, text=True).stdout.strip()
    if revision != KTX_REVISION:
        raise ValueError("KTX source revision differs from the accepted Samus runtime")
    if platform.system() != "Darwin":
        raise ValueError("Candidate encoder build currently targets macOS; supply an encoder built from this source elsewhere")
    rid = "osx-arm64" if platform.machine() == "arm64" else "osx-x64"
    library = repo / "artifacts/ktx-native-macos" / rid / "libktx.dylib"
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=False)
    source = directory / "mobile-encode-basis.cpp"
    source.write_text(SOURCE)
    target = directory / "mobile-encode-basis"
    subprocess.run(["clang++", "-std=c++17", "-O2", str(source), "-I", str(source_root / "include"),
                    "-L", str(library.parent), "-Wl,-rpath," + str(library.parent), "-lktx", "-o", str(target)], check=True)
    subprocess.run(["install_name_tool", "-change", "libktx.dylib", "@rpath/libktx.dylib", str(target)], check=True)
    receipt = {"ktxRevision": revision, "ktxLibrary": str(library), "ktxLibrarySha256": sha(library),
               "encoderSha256": sha(target), "encoderSourceSha256": sha(source),
               "uastcLevels": [2, 4], "zstdLevel": 9, "threads": 4,
               "encodingPolicy": ENCODING_POLICY, "protocol": check_protocol(target)}
    (directory / "encoder.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return target, receipt


def encode(encoder, levels, target, srgb, mode="uastc2"):
    if mode not in ("uastc2", "uastc4", "rgba8-zstd"):
        raise ValueError("Unsupported mobile encoding mode")
    raw = Path(target).with_suffix(".raw")
    try:
        with raw.open("xb") as stream:
            for level in levels:
                stream.write(level.tobytes())
        height, width = levels[0].shape[:2]
        subprocess.run([str(encoder), str(raw), str(target), str(width), str(height), str(len(levels)), str(int(srgb)), mode], check=True)
    finally:
        raw.unlink(missing_ok=True)


def decode(encoder, source, raw, level=0):
    subprocess.run([str(encoder), "decode", str(source), str(raw), str(level)], check=True)


def measure_quality(encoder, source, levels, channel, budget):
    """Decode every authored mip; fixed limits select encoding, never shrink.

    PSNR is measured in the encoded RGB/alpha domain. Normal direction is
    checked separately in normalized XYZ. Raw RGBA additionally requires exact
    texel bytes, rather than merely passing a lossy budget.
    """
    import numpy as np
    checks = []
    exact = True
    with tempfile.TemporaryDirectory(prefix="mobile-quality-", dir=Path(source).parent) as scratch:
        raw = Path(scratch) / "decoded.raw"
        for level, expected_bytes in enumerate(levels):
            decode(encoder, source, raw, level)
            payload = raw.read_bytes()
            if len(payload) != expected_bytes.size:
                raise ValueError("Decoder returned an invalid authored mip size")
            actual_bytes = np.frombuffer(payload, dtype=np.uint8).reshape(expected_bytes.shape)
            exact = exact and payload == expected_bytes.tobytes()
            expected, actual = expected_bytes.astype(np.float64) / 255, actual_bytes.astype(np.float64) / 255
            rgb_mse = float(np.mean((expected[..., :3] - actual[..., :3]) ** 2))
            alpha_mse = float(np.mean((expected[..., 3] - actual[..., 3]) ** 2))
            row = {"mip": level, "size": [expected.shape[1], expected.shape[0]],
                   "rgbPsnrDb": -10 * math.log10(max(rgb_mse, 1e-12)),
                   "alphaPsnrDb": -10 * math.log10(max(alpha_mse, 1e-12)), "failedBudgets": []}
            if channel == "normal":
                a, b = actual[..., :3] * 2 - 1, expected[..., :3] * 2 - 1
                a /= np.maximum(np.linalg.norm(a, axis=2, keepdims=True), 1e-8)
                b /= np.maximum(np.linalg.norm(b, axis=2, keepdims=True), 1e-8)
                angles = np.degrees(np.arccos(np.clip(np.sum(a * b, axis=2), -1, 1)))
                row.update(meanAngularErrorDegrees=float(np.mean(angles)),
                           p99AngularErrorDegrees=float(np.percentile(angles, 99)))
                if row["meanAngularErrorDegrees"] > budget["maximumMeanNormalAngleDegrees"]:
                    row["failedBudgets"].append("meanNormalAngle")
                if row["p99AngularErrorDegrees"] > budget["maximumP99NormalAngleDegrees"]:
                    row["failedBudgets"].append("p99NormalAngle")
            if channel == "material" and np.all(expected_bytes[...,3] == 0):
                row["ormZeroAlphaMarkerExact"] = bool(np.all(actual_bytes[...,3] == 0))
                if not row["ormZeroAlphaMarkerExact"]:
                    row["failedBudgets"].append("ormZeroAlphaMarker")
            if row["rgbPsnrDb"] < budget["minimumRgbPsnrDb"]:
                row["failedBudgets"].append("rgbPsnr")
            if row["alphaPsnrDb"] < budget["minimumAlphaPsnrDb"]:
                row["failedBudgets"].append("alphaPsnr")
            checks.append(row)
    return {"pass": all(not row["failedBudgets"] for row in checks),
            "allAuthoredRgbaMipBytesExact": exact, "checks": checks,
            "worstRgbPsnrDb": min(row["rgbPsnrDb"] for row in checks),
            "worstAlphaPsnrDb": min(row["alphaPsnrDb"] for row in checks)}


def encode_guarded(encoder, levels, target, srgb, channel, budget):
    """Prefer UASTC2, then UASTC4, then lossless, preserving exact input mips."""
    attempts = []
    for mode in ("uastc2", "uastc4", "rgba8-zstd"):
        encode(encoder, levels, target, srgb, mode)
        quality = measure_quality(encoder, target, levels, channel, budget)
        if mode == "rgba8-zstd" and not quality["allAuthoredRgbaMipBytesExact"]:
            raise ValueError("Lossless RGBA/Zstd encoding changed authored mip texels")
        attempts.append({"mode": mode, "encodedSha256": sha(target),
                         "encodedBytes": Path(target).stat().st_size, "quality": quality})
        if quality["pass"]:
            return {"encodingMode": mode, "uastcLevel": {"uastc2": 2, "uastc4": 4}.get(mode),
                    "encodingPolicy": ENCODING_POLICY, "adaptiveAttempts": attempts,
                    "compressionQuality": quality}
    raise ValueError("No encoding satisfied the unchanged mobile fidelity budgets")
