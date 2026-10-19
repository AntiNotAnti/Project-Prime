#!/usr/bin/env python3
"""PORT-24: manually staged, non-installing local character presentation test pack.

Requires validated real, user-owned asset outputs. Catalog appearance and source
receipt are bound to one private biped candidate, with explicit per-seat input.
No selectable Dark Samus/Trooper, installation, gameplay, network or replay changes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import sys
import tempfile

from build_native_glb import validate_native_candidate
from retarget_contract import SOURCE_HUNTERS, SOURCE_APPEARANCES, MAX_MAP_BYTES, MAX_NATIVE_BYTES, _bounded_json

MAX_GLB = 128*1024*1024
MAX_JSON = 4*1024*1024
MAX_PALETTE = 32


def _reject_duplicate_keys(items):
    result={}
    for key,value in items:
        if key in result:
            raise ValueError("GLB contains a duplicate JSON property")
        result[key]=value
    return result


def sha256_file(path):
    digest=hashlib.sha256()
    with path.open("rb") as handle:
        for segment in iter(lambda: handle.read(1024*1024),b""):
            digest.update(segment)
    return digest.hexdigest()


def inspect_candidate_glb(path, expected_nodes, native_materials):
    if path.is_symlink() or not path.is_file() or not 28 <= path.stat().st_size <= MAX_GLB:
        raise ValueError("Missing, symbolic, or oversized candidate GLB")
    with path.open("rb") as file:
        magic,version,size=struct.unpack("<4sII",file.read(12))
        if magic != b"glTF" or version != 2 or size != path.stat().st_size:
            raise ValueError("Bad glTF2 binary header or size")
        length,chunk=struct.unpack("<I4s",file.read(8))
        if chunk != b"JSON" or not 2 <= length <= MAX_JSON or length%4:
            raise ValueError("Bounded glTF JSON chunk required")
        document=json.loads(file.read(length),object_pairs_hook=_reject_duplicate_keys)
        rest=file.read(8)
        if len(rest)!=8:
            raise ValueError("Embedded glTF binary buffer missing")
        binary_length,binary_type=struct.unpack("<I4s",rest)
        if binary_type != b"BIN\x00" or binary_length%4 or file.tell()+binary_length != size:
            raise ValueError("Only self-contained GLB with one binary chunk is supported")
    if document.get("asset",{}).get("version")!="2.0":
        raise ValueError("GLB asset version is not 2.0")
    for buffer in document.get("buffers",[]):
        if "uri" in buffer:
            raise ValueError("External GLB buffers cannot be part of a native test pack")
    nodes=document.get("nodes")
    skins=document.get("skins")
    meshes=document.get("meshes")
    materials=document.get("materials")
    accessors=document.get("accessors")
    if not all(isinstance(x,list) for x in (nodes,skins,meshes,materials,accessors)):
        raise ValueError("Incomplete skinned mesh GLB")
    if len(skins)!=1 or not 1 <= len(meshes) <= 128:
        raise ValueError("Expected one bounded native skinned mesh")
    joint_ids=skins[0].get("joints",[])
    if not isinstance(joint_ids,list) or not 1 <= len(joint_ids) <= MAX_PALETTE:
        raise ValueError("Skin must use 1-32 named native joints")
    joint_names=[]
    for index in joint_ids:
        if type(index) is not int or not 0 <= index < len(nodes):
            raise ValueError("Skin contains unknown glTF joint node")
        name=nodes[index].get("name")
        if not isinstance(name,str) or not name:
            raise ValueError("Skinned joint must be named")
        joint_names.append(name)
    if len(set(joint_names))!=len(joint_names) or set(joint_names)!=set(expected_nodes):
        raise ValueError("GLB skin bones do not exactly match calibrated native palette")
    inv=skins[0].get("inverseBindMatrices")
    if type(inv) is not int or not 0 <= inv < len(accessors):
        raise ValueError("Native-space preview must include explicit inverse-bind matrices")
    inverse=accessors[inv]
    if (inverse.get("count")!=len(joint_ids) or inverse.get("type")!="MAT4"
            or inverse.get("componentType")!=5126):
        raise ValueError("Inverse-bind matrix accessor does not match native joints")
    used_meshes=set()
    for node in nodes:
        if "skin" not in node:
            continue
        if node["skin"]!=0 or not isinstance(node.get("mesh"),int):
            raise ValueError("Skin mesh references the wrong native palette")
        mesh=node["mesh"]
        if not 0<=mesh<len(meshes):
            raise ValueError("Skinned mesh node index out of range")
        if "matrix" in node:
            if node["matrix"] != [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]:
                raise ValueError("Skinned mesh node has a nonidentity transform")
        else:
            for field,neutral in (("translation",[0,0,0]),("rotation",[0,0,0,1]),("scale",[1,1,1])):
                if field in node and node[field]!=neutral:
                    raise ValueError("Skinned mesh has a nonidentity local transform")
        used_meshes.add(mesh)
    if not used_meshes:
        raise ValueError("No skinned mesh node found")
    material_names={m.get("name") for m in materials if isinstance(m,dict)}
    if None in material_names or not material_names.issubset(set(native_materials)):
        raise ValueError("GLB material names are not native to the chosen fallback Hunter")
    for mesh_index in used_meshes:
        primitives=meshes[mesh_index].get("primitives",[])
        if not isinstance(primitives,list) or not 1<=len(primitives)<=4096:
            raise ValueError("Mesh primitives absent or oversized")
        for primitive in primitives:
            attrs=primitive.get("attributes",{})
            if (not isinstance(attrs,dict) or
                    not all(key in attrs for key in ("POSITION","JOINTS_0","WEIGHTS_0"))):
                raise ValueError("Weighted4 mesh is missing required vertex attributes")
            if primitive.get("mode",4)!=4:
                raise ValueError("Only triangle meshes are supported")
            mat=primitive.get("material")
            if type(mat) is not int or not 0<=mat<len(materials):
                raise ValueError("Native material index not assigned")
    return {"skinJoints":joint_names,"meshCount":len(used_meshes),
            "materials":sorted(material_names),"bytes":size}


def prepare_test_pack(source_root, character, native_path, mapping_path, candidate_glb, output_root):
    if character not in SOURCE_HUNTERS:
        raise ValueError("Unknown local original-source character identity")
    if source_root.is_symlink():
        raise ValueError("Private preview root may not be a symlink")
    native=_bounded_json(native_path,MAX_NATIVE_BYTES)
    mapping=_bounded_json(mapping_path,MAX_MAP_BYTES)
    if mapping.get("sourceCharacter")!=character:
        raise ValueError("Private mapping belongs to another source character")
    plan=validate_native_candidate(source_root,character,native,mapping)
    candidate=inspect_candidate_glb(candidate_glb,plan["skin"]["palette"],plan["mat_names"])
    hunter=SOURCE_HUNTERS[character]
    character_id,skin_source=SOURCE_APPEARANCES[character]
    destination=output_root.absolute()
    if (destination.exists() or destination.is_symlink() or
            [part.lower() for part in destination.parts][-2:]==["character-models","default"]):
        raise ValueError("Output pack must be a new dedicated staging path, never the live default pack")
    if output_root.parent.is_symlink() or not output_root.parent.exists():
        raise ValueError("Explicit staging parent must exist and not be a link")

    folder=hunter.lower()
    receipt={"format":1,"sourceCharacter":character,"nativeHunter":hunter,
             "characterId":character_id,"skinSource":skin_source,
             "presentationPart":"biped","visualOnly":True,"approvedForGameplay":False,
             "multiplayerEnabled":False,"sourceMotionRetargetVerified":False,
             "firstPersonVerified":False,"alternateFormVerified":False,
             "devicePerformanceVerified":False,"playerSelectionEnabled":False,
             "sourceGlbSha256":sha256_file(candidate_glb),
             "sourceNativeKitSha256":sha256_file(native_path),
             "sourceMappingSha256":sha256_file(mapping_path),
             "sourceSkeletonSha256":sha256_file(source_root/(character+".skeleton.json")),
             "sourceWeightsSha256":sha256_file(source_root/(character+".weights.json")),
             "calibration":plan["skin"]["calibration"],
             "GLB":candidate}
    receipt_bytes=(json.dumps(receipt,indent=2)+"\n").encode("utf-8")
    entry={"hunter":hunter,"part":"biped","lod":0,
           "model":folder+"/biped.glb","skinning":"weighted4",
           "characterId":character_id,"skinSource":skin_source,
           "provenance":"validation-receipt.json",
           "provenanceSha256":hashlib.sha256(receipt_bytes).hexdigest(),
           "boneMap":{name:name for name in candidate["skinJoints"]}}
    manifest={"format":1,"id":"retro-"+character+"-local-biped-visual-test","models":[entry]}

    with tempfile.TemporaryDirectory(prefix="prime-local-model-pack-",dir=output_root.parent) as tmp:
        stage=Path(tmp)/"payload"
        (stage/folder).mkdir(parents=True)
        shutil.copyfile(candidate_glb,stage/folder/"biped.glb")
        (stage/"characters.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
        (stage/"validation-receipt.json").write_bytes(receipt_bytes)
        if sha256_file(stage/folder/"biped.glb")!=receipt["sourceGlbSha256"]:
            raise ValueError("Staged GLB checksum mismatch")
        os.replace(stage,output_root)
    return receipt


def main():
    parser=argparse.ArgumentParser(description="Stage a private offline biped visual test pack")
    parser.add_argument("--source-root",type=Path,required=True)
    parser.add_argument("--character",choices=sorted(SOURCE_HUNTERS),required=True)
    parser.add_argument("--native-reference",type=Path,required=True)
    parser.add_argument("--mapping",type=Path,required=True)
    parser.add_argument("--candidate-glb",type=Path,required=True)
    parser.add_argument("--output-root",type=Path,required=True)
    args=parser.parse_args()
    data=prepare_test_pack(args.source_root,args.character,args.native_reference,
                           args.mapping,args.candidate_glb,args.output_root)
    print("Private visual test pack (not installed):",args.output_root)
    print("Source GLB SHA256:",data["sourceGlbSha256"])
    print("Run ProjectPrime -charactermodelvalidate",args.output_root,
          "for native-rig and geometry preflight")
    print("Private appearance:",data["characterId"],data["skinSource"],"parent",data["nativeHunter"])
    print("Explicit offline preview input is required; this does not replace native seats or enable selection.")


if __name__=="__main__":
    main()
