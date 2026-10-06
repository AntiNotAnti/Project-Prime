"""Build a rectangular UASTC encoder in a NEW candidate directory only.

The existing Samus encoder is square-only. This extension uses the same pinned
KTX library/quality settings, accepts width and height independently, and can
decode any authored mip for an independent compression-quality audit.
"""
import hashlib
import json
import platform
import subprocess
from pathlib import Path

KTX_REVISION = "6b3d8bf15788f604c6b91dd95fafabb6c59cd723"
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
  if(argc==5 && std::string(argv[1])=="decode") {
   unsigned level=std::stoul(argv[4]);
   check(ktxTexture2_CreateFromNamedFile(argv[2],KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,&tex));
   if(level>=tex->numLevels) throw std::runtime_error("invalid mip");
   check(ktxTexture2_TranscodeBasis(tex,KTX_TTF_RGBA32,KTX_TF_HIGH_QUALITY));
   ktx_size_t offset=0;check(ktxTexture_GetImageOffset(ktxTexture(tex),level,0,0,&offset));
   size_t count=std::max(1u,tex->baseWidth>>level)*std::max(1u,tex->baseHeight>>level)*4u;
   std::ofstream out(argv[3],std::ios::binary);out.write((char*)tex->pData+offset,count);
   if(!out) throw std::runtime_error("cannot write decoded mip");
  } else {
   if(argc!=7) throw std::runtime_error("usage: encoder input.raw output.ktx2 width height levels srgb");
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
   params.threadCount=4;params.uastcFlags=2;
   check(ktxTexture2_CompressBasisEx(tex,&params));check(ktxTexture2_DeflateZstd(tex,9));
   check(ktxTexture2_WriteToNamedFile(tex,argv[2]));
  }
  ktxTexture_Destroy(ktxTexture(tex));return 0;
 }catch(const std::exception&e){if(tex)ktxTexture_Destroy(ktxTexture(tex));std::fprintf(stderr,"%s\n",e.what());return 1;}
}
'''


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


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
               "uastcLevel": 2, "zstdLevel": 9, "threads": 4}
    (directory / "encoder.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return target, receipt


def encode(encoder, levels, target, srgb):
    raw = Path(target).with_suffix(".raw")
    try:
        with raw.open("xb") as stream:
            for level in levels:
                stream.write(level.tobytes())
        height, width = levels[0].shape[:2]
        subprocess.run([str(encoder), str(raw), str(target), str(width), str(height), str(len(levels)), str(int(srgb))], check=True)
    finally:
        raw.unlink(missing_ok=True)


def decode(encoder, source, raw, level=0):
    subprocess.run([str(encoder), "decode", str(source), str(raw), str(level)], check=True)
