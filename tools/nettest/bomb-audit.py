"""N8 exact bomb campaign audit. No shot-parity or GPU-visibility inference.

API: audit(server, peers, manifest, summary=None, kinds=KINDS, logs=None,
           visibility=None). CLI: bomb-audit.py CAMPAIGN [--kind KIND] [--output JSON].
Snapshots compare full decoded states against authority captures at identical
frame/frontier. Visibility requires separately specified GPU evidence; headless
entity/state evidence can never establish pixels.
"""
import argparse
from collections import defaultdict
import json
import hashlib
import struct
import zlib
import math
from pathlib import Path

from net2_evidence import FATAL, drain_coverage, health_value, slow_lane_parity, state_key

KINDS = ("MorphBall", "Stinglarva", "Lockjaw")
KEY_FIELDS = ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")
EVENT_KINDS = {"Placed", "Refused", "DetonateAccepted", "Detonated", "Removed"}
TERMINALS = {"Detonated", "Removed"}
RESOLUTIONS = {"Placed", "Refused", "DetonateAccepted"}
# NetBombProtocol.BombEvent: protocol51 introduced the native owner-contact
# jump flag in a detonation's reason byte. Other kinds retain refusal semantics.
OWNER_JUMP = 1
EVENT_REASON_CONTRACT = "BombEvent protocol51+: Refused1..7; Detonated0|OwnerJump1; other kinds0"


def integer(value, maximum=2**32 - 1, minimum=0):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError("invalid bounded integer: " + repr(value))
    return value


def key(value):
    limits = ((1, 2**64-1), (1, 65535), (0, 7), (1, 65535), (1, 65535), (1, 2**32-1))
    return tuple(integer(value[f], high, low) for f, (low, high) in zip(KEY_FIELDS, limits))


def vector(value, limit):
    result = tuple(value[f] for f in ("X", "Y", "Z"))
    if any(type(x) not in (int, float) or not math.isfinite(x) or abs(x) > limit for x in result):
        raise ValueError("invalid bounded finite vector")
    return result


def state(value, identity_only=False):
    identity = key(value["Key"])
    kind = value["Kind"]
    if kind not in KINDS:
        raise ValueError("unknown bomb state kind")
    placed = integer(value["PlacedFrame"])
    pos, facing, up, speed = (vector(value[f], limit) for f, limit in
                             (("Position", 32768), ("Facing", 1.01), ("Up", 1.01), ("Speed", 1024)))
    countdown = integer(value["Countdown"], 65535)
    index = integer(value["Index"], 2)
    links = tuple(integer(value[f]) for f in ("Link0", "Link1"))
    recolor = integer(value["Recolor"], 7)
    if identity_only:
        if kind != "MorphBall" or any((placed, countdown, index, *links, recolor, *pos, *facing, *up, *speed)):
            raise ValueError("request resolution must have exact identity-only state")
    else:
        if abs(sum(x*x for x in facing)-1) >= .05 or abs(sum(x*x for x in up)-1) >= .05 or abs(sum(x*y for x,y in zip(facing,up))) >= .05:
            raise ValueError("invalid orthonormal bomb pose")
        if identity[5] in links or links[0] and links[0] == links[1] or kind != "Lockjaw" and any(links):
            raise ValueError("invalid bomb links")
    return (identity, kind, placed, pos, facing, up, speed, countdown, index, *links, recolor)


def rows(report, name, finding):
    result = {"events": [], "requests": [], "snapshots": [], "damage": []}
    if "Bombs" not in report:
        finding("INCOMPLETE", name, "Bombs", "bomb evidence unavailable")
        return result
    try:
        ledger = report["Bombs"]
        if integer(ledger["Overflow"], 2**63-1):
            finding("FAIL", name, "Bombs.Overflow", "bounded evidence overflow")
        if not isinstance(ledger["Rows"], list):
            raise ValueError("Rows must be a list")
        for ordinal, row in enumerate(ledger["Rows"]):
            try:
                row_type, stage = row["Type"], row["Stage"]
                integer(row["Frame"])
                if row_type == "Event":
                    seq = integer(row["Sequence"], 2**64-1, 1)
                    if row["Kind"] not in EVENT_KINDS or stage != ("authority" if name == "server" else "applied"):
                        raise ValueError("invalid event kind/stage")
                    reason = integer(row["Reason"], 7)
                    valid_reason = (reason > 0 if row["Kind"] == "Refused" else
                                    (reason & ~OWNER_JUMP) == 0 if row["Kind"] == "Detonated" else reason == 0)
                    if not valid_reason:
                        raise ValueError("event refusal reason inconsistent with kind")
                    payload = state(row["State"], row["Kind"] in {"Refused", "DetonateAccepted"})
                    if key(row["Key"]) != payload[0]:
                        raise ValueError("event outer key differs from state key")
                    result["events"].append((seq, row["Frame"], row["Kind"], row["Reason"], payload))
                elif row_type == "Request":
                    if stage != ("received" if name == "server" else "requested") or row["Action"] not in {"Place", "Detonate"}:
                        raise ValueError("invalid request stage/action")
                    result["requests"].append((key(row["Key"]), integer(row["RequestFrame"]), row["Action"]))
                elif row_type == "Snapshot":
                    if stage != ("authority" if name == "server" else "applied") or not isinstance(row["States"], list) or len(row["States"]) > 24:
                        raise ValueError("invalid snapshot stage/states")
                    states = [state(s) for s in row["States"]]
                    if len({s[0] for s in states}) != len(states):
                        raise ValueError("duplicate exact snapshot key")
                    result["snapshots"].append((row["Frame"], integer(row["Frontier"], 2**64-1), tuple(sorted(states))))
                elif row_type == "Damage":
                    if name != "server" or stage != "authority-damage" or row["Kind"] not in KINDS:
                        raise ValueError("invalid damage stage/kind")
                    identity = key(row["Key"])
                    victim = (integer(row["VictimSlot"],7), integer(row["VictimGeneration"],65535,1), integer(row["VictimLife"],65535,1))
                    damage_event = integer(row["DamageEvent"], minimum=1)
                    health, turret = integer(row["HealthDamage"],65535), integer(row["TurretDamage"],65535)
                    if not health and not turret:
                        raise ValueError("non-positive authority damage")
                    integer(row["HealthAfter"],65535); integer(row["TurretHealthAfter"],65535)
                    vector(row["Position"],32768)
                    result["damage"].append((identity,row["Frame"],row["Kind"],victim,damage_event))
                else:
                    raise ValueError("unknown bomb evidence row type")
            except (KeyError, TypeError, ValueError) as error:
                finding("FAIL", name, f"Bombs.Rows[{ordinal}]", str(error))
    except (KeyError, TypeError, ValueError) as error:
        finding("FAIL", name, "Bombs", "malformed bomb evidence: " + str(error))
    return result


def health_snapshot(row, schema):
    identity = state_key(row)
    limits = ((1,2**64-1),(1,65535),(0,2**32-1),(0,7),(1,65535),(1,65535))
    for value,(low,high) in zip(identity,limits):
        integer(value,high,low)
    value = health_value(row,schema)
    for item in value:
        integer(item)
    integer(row["Hp"],65535);integer(row["TurretHp"],65535)
    return identity,value


def without_shots(value):
    """Remove only the unrelated shot-key join from reusable drain checks."""
    if isinstance(value, dict):
        return {k: [] if k in ("OfferedShotKeys", "AcceptedShotKeys") else without_shots(v) for k,v in value.items()}
    if isinstance(value, list):
        return [without_shots(v) for v in value]
    return value


def png_rgb(path):
    """Decode the renderer's bounded noninterlaced 8-bit RGB PNGs, stdlib only."""
    raw = Path(path).read_bytes()
    if len(raw) > 20*1024*1024 or raw[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("invalid/beyond-bound PNG")
    offset, compressed, dimensions = 8, bytearray(), None
    while offset < len(raw):
        length = struct.unpack(">I",raw[offset:offset+4])[0]
        tag = raw[offset+4:offset+8];data = raw[offset+8:offset+8+length]
        crc = raw[offset+8+length:offset+12+length]
        if len(data) != length or len(crc) != 4 or zlib.crc32(tag+data)&0xffffffff != struct.unpack(">I",crc)[0]:
            raise ValueError("invalid PNG chunk/checksum")
        if tag == b"IHDR":
            dimensions = struct.unpack(">IIBBBBB",data)
        elif tag == b"IDAT":
            compressed.extend(data)
        offset += length+12
        if tag == b"IEND":
            break
    if dimensions is None:
        raise ValueError("missing PNG dimensions")
    width,height,depth,color,compression,filter_method,interlace = dimensions
    if not 0 < width <= 4096 or not 0 < height <= 4096 or depth != 8 or color != 2 or any((compression,filter_method,interlace)):
        raise ValueError("unsupported/beyond-bound PNG format")
    stride = width*3;expected = (stride+1)*height
    decoder = zlib.decompressobj();scanlines = decoder.decompress(bytes(compressed),expected+1)
    if len(scanlines) != expected or not decoder.eof or decoder.unconsumed_tail:
        raise ValueError("invalid/beyond-bound PNG scanlines")
    rgb, previous = bytearray(),bytearray(stride)
    for y in range(height):
        at = y*(stride+1);method = scanlines[at];row = bytearray(scanlines[at+1:at+1+stride])
        if method > 4:
            raise ValueError("invalid PNG filter")
        for x in range(stride):
            left = row[x-3] if x >= 3 else 0;up = previous[x];upper_left = previous[x-3] if x >= 3 else 0
            if method == 1: predictor = left
            elif method == 2: predictor = up
            elif method == 3: predictor = (left+up)//2
            elif method == 4:
                estimate = left+up-upper_left
                distances = (abs(estimate-left),abs(estimate-up),abs(estimate-upper_left))
                predictor = (left,up,upper_left)[distances.index(min(distances))]
            else: predictor = 0
            row[x] = (row[x]+predictor)&255
        rgb.extend(row);previous = row
    return raw,width,height,bytes(rgb)


def slash_key(value):
    if not isinstance(value,str):
        raise ValueError("GPU full key must be canonical slash-separated string")
    fields = value.split("/")
    if len(fields) != 6 or any(not field.isdecimal() or str(int(field)) != field for field in fields):
        raise ValueError("GPU full key must have exactly six canonical integer fields")
    return key(dict(zip(KEY_FIELDS,map(int,fields))))


def render_fixture(report, engine_sha, finding):
    if report is None:
        return "NOT_RUN"
    verdicts = []
    def issue(status,field,message):
        verdicts.append(status);finding(status,"render-fixture",field,message)
    try:
        if "assemblySha256" not in report:
            issue("INCOMPLETE","assemblySha256","GPU report cannot bind to exact campaign engine")
        elif report["assemblySha256"] != engine_sha or report.get("assemblyAfterSha256",engine_sha) != engine_sha:
            issue("FAIL","assemblySha256","GPU fixture and campaign engine identities differ")
        proof = report["bombVisibility"]
        if report["schema"] != 1 or proof["schema"] != 1 or report["complete"] is not True:
            raise ValueError("incomplete/unsupported GPU report")
        if proof["status"] != "PASS" or proof["failures"]:
            issue("FAIL","bombVisibility.status","GPU fixture reported failure")
        if proof["matchState"] != "InProgress" or proof["MenuPause"] is not False or proof["DialogPause"] is not False or proof["processFrame"] is not True:
            raise ValueError("GPU fixture paused/outside active production rendering")
        if integer(proof["pixelThreshold"],255) != 2:
            raise ValueError("unexpected GPU pixel difference threshold")
        fixtures = {}
        for row in proof["fixtures"]:
            identity = slash_key(row["key"])
            if identity in fixtures or row["kind"] not in KINDS:
                raise ValueError("duplicate/invalid GPU fixture full key/kind")
            if len(row["position"]) != 3 or any(type(v) not in (int,float) or not math.isfinite(v) for v in row["position"]):
                raise ValueError("invalid GPU fixture position")
            integer(row["Index"],2);integer(row["Link0"]);integer(row["Link1"])
            fixtures[identity] = row
        if len(fixtures) != 5 or {k:sum(r["kind"] == k for r in fixtures.values()) for k in KINDS} != dict(MorphBall=1,Stinglarva=1,Lockjaw=3):
            raise ValueError("need exactly five bodies across all three kinds")
        linked = sorted(((k,r) for k,r in fixtures.items() if r["kind"] == "Lockjaw"),key=lambda pair:pair[1]["Index"])
        if [r["Index"] for _,r in linked] != [0,1,2] or len({k[:5] for k,_ in linked}) != 1:
            raise ValueError("Lockjaw fixture lacks same-owner three-index triangle")
        if [(r["Link0"],r["Link1"]) for _,r in linked] != [(0,0),(linked[0][0][5],0),(linked[0][0][5],linked[1][0][5])]:
            raise ValueError("GPU triangle links differ from exact progressive owner keys")
        expected = {(k,None,occluded) for k in fixtures for occluded in (False,True)}
        edges = ((linked[1][0],linked[0][0]),(linked[2][0],linked[0][0]),(linked[2][0],linked[1][0]))
        expected.update((a,b,occluded) for a,b in edges for occluded in (False,True))
        observed, capture_cache = set(),{}
        for row in proof["cases"]:
            source = slash_key(row["key"])
            target = None if row["targetKey"] is None else slash_key(row["targetKey"])
            if type(row["occluded"]) is not bool or (source,target,row["occluded"]) in observed:
                raise ValueError("duplicate/invalid GPU body/edge case")
            observed.add((source,target,row["occluded"]))
            if row["status"] != "PASS" or row["geometryVerified"] is not True:
                raise ValueError("GPU case failed geometry/pixel predicate")
            world,fixture = row["worldDepth"],row["fixtureDepth"]
            if any(type(v) not in (int,float) or not math.isfinite(v) or not 0 <= v <= 1 for v in (world,fixture)):
                raise ValueError("invalid GPU depth samples")
            if not (0 < world < fixture-.00001 if row["occluded"] else world > fixture-.00001):
                raise ValueError("GPU depth cannot prove asserted visible/occluded geometry")
            if len(row["captures"]) != 3:
                raise ValueError("need before/enabled/after PNGs per GPU case")
            images=[]
            for capture in row["captures"]:
                path = capture["path"]
                if path not in capture_cache:
                    capture_cache[path] = png_rgb(path)
                raw,width,height,rgb = capture_cache[path]
                if hashlib.sha256(raw).hexdigest() != capture["pngSha256"] or (width,height) != (capture["width"],capture["height"]):
                    raise ValueError("GPU PNG hash/dimensions differ from capture report")
                flipped = b"".join(rgb[y*width*3:(y+1)*width*3] for y in reversed(range(height)))
                if capture["rgbSha256"] not in (hashlib.sha256(rgb).hexdigest(),hashlib.sha256(flipped).hexdigest()):
                    raise ValueError("GPU PNG RGB differs from framebuffer hash")
                images.append((width,height,rgb))
            if len({image[:2] for image in images}) != 1:
                raise ValueError("GPU bracket dimensions differ")
            def difference(a,b):
                return sum(any(abs(x-y) > 2 for x,y in zip(a[i:i+3],b[i:i+3])) for i in range(0,len(a),3))
            changed = difference(images[0][2],images[1][2]);unstable = difference(images[0][2],images[2][2])
            if changed != integer(row["changedPixels"]) or unstable != integer(row["unstablePixels"]) or unstable or (changed != 0 if row["occluded"] else changed == 0):
                raise ValueError("GPU pixel comparison failed recomputed stable visible/wall control")
        if observed != expected:
            raise ValueError("need exactly five bodies and three edges, visible and wall controls")
    except FileNotFoundError as error:
        issue("INCOMPLETE","bombVisibility.captures",str(error))
    except (OSError,KeyError,TypeError,ValueError,struct.error,zlib.error) as error:
        issue("FAIL","bombVisibility",str(error))
    return "FAIL" if "FAIL" in verdicts else "INCOMPLETE" if verdicts else "PASS"


def pixel_bracket(captures):
    if not isinstance(captures,list) or len(captures) != 3:
        raise ValueError("need before/enabled/after pixel captures")
    images = []
    for capture in captures:
        raw,width,height,rgb = png_rgb(capture["Path"])
        if hashlib.sha256(raw).hexdigest() != capture["PngSha256"] or (width,height) != (capture["Width"],capture["Height"]):
            raise ValueError("live PNG hash/dimensions differ from report")
        flipped = b"".join(rgb[y*width*3:(y+1)*width*3] for y in reversed(range(height)))
        if capture["RgbSha256"] not in (hashlib.sha256(rgb).hexdigest(),hashlib.sha256(flipped).hexdigest()):
            raise ValueError("live PNG RGB differs from framebuffer hash")
        images.append((width,height,rgb))
    if len({i[:2] for i in images}) != 1:
        raise ValueError("live pixel bracket dimensions differ")
    def difference(a,b):
        return sum(any(abs(x-y) > 2 for x,y in zip(a[i:i+3],b[i:i+3])) for i in range(0,len(a),3))
    return difference(images[0][2],images[1][2]),difference(images[0][2],images[2][2]),images[0][:2]


def float32(value):
    return struct.unpack("<f",struct.pack("<f",value))[0]


def capture_orientation(facing,up):
    """EntityBase.GetTransformMatrix followed by normalized vector getters."""
    def cross(a,b):
        return tuple(float32(float32(a[i]*b[j])-float32(a[j]*b[i]))
                     for i,j in ((1,2),(2,0),(0,1)))
    def normalize(a):
        squares=[float32(x*x) for x in a]
        length=float32(math.sqrt(float32(float32(squares[0]+squares[1])+squares[2])))
        if not length:raise ValueError("degenerate live capture orientation")
        inverse=float32(1/length)
        return tuple(float32(x*inverse) for x in a)
    facing,up=(tuple(float32(x) for x in v) for v in (facing,up))
    right=normalize(cross(up,facing))
    return normalize(facing),normalize(cross(facing,right))


def native_capture_matches(payload,canonical):
    """LIVE ONLY: reconstruct orientation; every other decoded field is exact.

    A single binary32 step around the derived components covers native/JIT
    operation rounding. Kanden10 reproduced 8/11 changed rows bit-exact;
    the other three had exactly one ULP residual in derived Up only. This
    is not a tolerance around the transmitted pose or world coordinates.
    Wire snapshot/event comparisons elsewhere remain tuple equality.
    """
    if payload==canonical:return True
    if payload[:4]!=canonical[:4] or payload[6:]!=canonical[6:]:return False
    expected=capture_orientation(canonical[4],canonical[5])
    def ordered(value):
        bits=struct.unpack("<I",struct.pack("<f",value))[0]
        return 0x80000000-bits if bits&0x80000000 else bits
    return all(abs(ordered(actual)-ordered(derived))<=1
               for vector_actual,vector_derived in zip(payload[4:6],expected)
               for actual,derived in zip(vector_actual,vector_derived))


def live_pixels(peers,parsed,placements,damage,kinds,manifest,live_reports,finding,authority_events):
    metrics = {}
    if live_reports is None:
        finding("INCOMPLETE","campaign","LiveVisibility","live network bomb pixels were not measured")
        return "INCOMPLETE",metrics
    verdicts = []
    def issue(status,name,field,message):
        verdicts.append(status);finding(status,name,"LiveVisibility."+field,message)
    extra = live_reports.keys()-peers.keys()
    if extra:
        issue("FAIL","campaign","processes","live pixel report from unexpected process")
    for name,peer in peers.items():
        report = live_reports.get(name)
        if report is None:
            issue("INCOMPLETE",name,"report","live pixel report unavailable")
            continue
        bodies,links,seen,terminal_pixels = set(),set(),set(),set()
        derived_joins = []
        terminal_classifications = []
        snapshots = parsed[name]["snapshots"]
        eligible = {identity for identity,frame,kind,_,_ in damage
                    if kind in kinds and snapshots and frame >= snapshots[0][0]}
        before_bootstrap = {identity for identity,frame,kind,_,_ in damage
                            if kind in kinds and snapshots and frame < snapshots[0][0]}-eligible
        try:
            if report.get("_Error"):
                raise ValueError(report["_Error"])
            if report["Schema"] != 1 or integer(report["PixelThreshold"],255) != 2:
                raise ValueError("unsupported live pixel schema/threshold")
            if report["EngineSha256"] != manifest["assemblySha256"] or report["Source"] != manifest["source"]:
                raise ValueError("live pixel engine/source differs from campaign")
            slots = {w["LocalSlot"] for w in peer["Windows"] if 0 <= w.get("LocalSlot",-1) < 8}
            if len(slots) != 1 or integer(report["ClientSlot"],7) != next(iter(slots)):
                raise ValueError("live pixel client slot differs from independent process")
            if integer(report["Overflow"],2**63-1):
                issue("FAIL",name,"Overflow","live pixel evidence budget overflow")
            if report.get("Failures") or report.get("Status") == "FAIL":
                issue("FAIL",name,"Status","live pixel probe reported failure")
            if not isinstance(report["Rows"],list) or len(report["Rows"]) > 512:
                raise ValueError("invalid/beyond-bound live pixel rows")
            if not report["Rows"]:
                issue("INCOMPLETE",name,"Rows","zero live pixel workload")
            body_attempts={key(row["Key"]) for row in report["Rows"] if row.get("Type")=="Body"}
            peer_births={e[4][0]:e for e in parsed[name]["events"] if e[2]=="Placed"}
            peer_terminals={e[4][0]:e for e in parsed[name]["events"] if e[2]=="Detonated"}
            canonical = {(frame,s) for frame,_,states in snapshots for s in states}
            births = {e[4] for e in parsed[name]["events"] if e[2] == "Placed"}
            def canonical_payload(payload,snapshot_frame):
                candidates=[s for frame,s in canonical if frame==snapshot_frame and s[0]==payload[0]]
                candidates.extend(s for s in births if s[0]==payload[0] and s[2]>snapshot_frame)
                for source in candidates:
                    if native_capture_matches(payload,source):
                        if payload!=source:derived_joins.append(dict(key=list(payload[0]),snapshotFrame=snapshot_frame))
                        return True
                return False
            for ordinal,row in enumerate(report["Rows"]):
                try:
                    identity = key(row["Key"]);payload = state(row["State"])
                    if payload[0] != identity or identity not in placements or payload[1] != placements[identity][4][1]:
                        raise ValueError("live body key/kind lacks exact authority placement")
                    integer(row["Frame"]);integer(row["SceneFrame"],2**64-1);integer(row["SnapshotFrame"]);integer(row["PlayerSnapshotFrame"])
                    integer(row["Attempts"],minimum=1)
                    # SnapshotFrame is the canonical bomb lane, independent of
                    # the separately reported PlayerSnapshotFrame. A birth can
                    # legitimately follow the last accepted bomb snapshot.
                    terminal = row["Type"]=="Terminal"
                    target = None
                    if terminal:
                        birth=peer_births.get(identity);death=peer_terminals.get(identity)
                        if birth!=placements[identity] or death is None or death not in authority_events:
                            raise ValueError("terminal pixel key lacks exact accepted authority birth/detonation")
                        if (integer(row["BirthSequence"],2**64-1,1),integer(row["BirthFrame"]))!=birth[:2] or (integer(row["TerminalSequence"],2**64-1,1),integer(row["TerminalFrame"]))!=death[:2] or death[0]<=birth[0]:
                            raise ValueError("terminal pixel sequence/frame differs from exact accepted facts")
                        if payload!=death[4] or row.get("TargetKey") is not None or integer(row["EffectId"],65535)!={"MorphBall":145,"Stinglarva":128,"Lockjaw":146}[payload[1]]:
                            raise ValueError("terminal pixel state/effect differs from exact native detonation")
                        if integer(row["BodyDrawOpportunities"]) or integer(row["BodyAttempts"]) or identity in body_attempts or row["MissingExpectedGhost"] is not False:
                            raise ValueError("terminal pixels cannot replace an observed/attempted/missing bomb body")
                        if vector(row["EffectPosition"],32768)!=payload[3]:
                            raise ValueError("actual native terminal effect position differs from exact terminal packet")
                        lifecycles=report["Lifecycles"]
                        if not isinstance(lifecycles,list) or len(lifecycles)>512:
                            raise ValueError("invalid terminal lifecycle ledger")
                        matching=[l for l in lifecycles if key(l["Key"])==identity]
                        fields=("BirthSequence","BirthFrame","TerminalSequence","TerminalFrame","AppliedBirthDrawIndex","AppliedTerminalDrawIndex","BodyDrawOpportunities","BodyAttempts","MissingExpectedGhost")
                        if len(matching)!=1 or any(matching[0][field]!=row[field] for field in fields) or matching[0]["MissingExpectedGhost"] is not False:
                            raise ValueError("terminal metadata differs from independent lifecycle ledger")
                        for field in fields[:-1]:integer(matching[0][field],2**64-1)
                        if integer(row["CaptureDrawIndex"],2**64-1)>integer(report["DrawIndex"],2**64-1):
                            raise ValueError("terminal capture exceeds recorded renderer draw index")
                        birth_draw=integer(row["AppliedBirthDrawIndex"],2**64-1)
                        death_draw=integer(row["AppliedTerminalDrawIndex"],2**64-1)
                        if integer(row["CaptureDrawIndex"],2**64-1)<death_draw:
                            raise ValueError("terminal pixels preceded accepted detonation")
                        if row["Classification"]=="BirthTerminalBeforeBodyDraw":
                            if birth_draw!=death_draw:
                                raise ValueError("terminal proof is not birth/detonation coalesced before a body draw")
                        elif row["Classification"]=="ExpiredBeforeFirstBodyDraw":
                            retirement_fields=("RetirementSnapshotFrame","RetirementSnapshotFrontier","RetirementSnapshotKeyAbsent","RetirementDrawIndex","RetirementWitnessStage")
                            if any(matching[0][field]!=row[field] for field in retirement_fields):
                                raise ValueError("retirement witness differs from lifecycle ledger")
                            retirement_frame=integer(row["RetirementSnapshotFrame"])
                            retirement_frontier=integer(row["RetirementSnapshotFrontier"],2**64-1)
                            integer(matching[0]["RetirementSnapshotFrame"])
                            integer(matching[0]["RetirementSnapshotFrontier"],2**64-1)
                            integer(matching[0]["RetirementDrawIndex"],2**64-1)
                            if row["RetirementSnapshotKeyAbsent"] is not True or matching[0]["RetirementSnapshotKeyAbsent"] is not True or integer(row["RetirementDrawIndex"],2**64-1)!=birth_draw or not birth_draw<death_draw or retirement_frame<death[1] or retirement_frontier<death[0]:
                                raise ValueError("retirement snapshot does not cover detonation before first body draw")
                            journal=peer["Bombs"]["Rows"]
                            birth_order=next(i for i,r in enumerate(journal) if r.get("Type")=="Event" and r.get("Sequence")==birth[0])
                            death_order=next(i for i,r in enumerate(journal) if r.get("Type")=="Event" and r.get("Sequence")==death[0])
                            stage=row["RetirementWitnessStage"]
                            if stage=="AtBirth":
                                prior=[r for r in journal[:birth_order] if r.get("Type")=="Snapshot" and r.get("Stage")=="applied"]
                                witness=prior[-1] if prior else None
                            elif stage=="SubsequentSnapshot":
                                witness=next((r for r in journal[birth_order+1:death_order] if r.get("Type")=="Snapshot" and r.get("Stage")=="applied" and not any(key(s["Key"])==identity for s in r["States"])),None)
                            else:raise ValueError("unknown retirement witness stage")
                            if witness is None or witness["Frame"]!=retirement_frame or witness["Frontier"]!=retirement_frontier or any(key(s["Key"])==identity for s in witness["States"]):
                                raise ValueError("retirement does not match exact ordered accepted snapshot/key absence")
                        else:raise ValueError("unknown terminal pixel classification")
                    elif not canonical_payload(payload,row["SnapshotFrame"]):
                        raise ValueError("live captured state differs from every applied canonical bomb payload")
                    if row["Type"] == "Link":
                        target = key(row["TargetKey"]);target_state = state(row["TargetState"])
                        if payload[1] != "Lockjaw" or target_state[1] != "Lockjaw" or target != target_state[0] or identity[:5] != target[:5] or target[5] not in payload[9:11]:
                            raise ValueError("live link differs from exact owner/life topology")
                        if not canonical_payload(target_state,row["SnapshotFrame"]):
                            raise ValueError("live target state lacks exact applied canonical payload")
                    elif row["Type"] not in ("Body","Terminal"):
                        raise ValueError("unknown live pixel row kind")
                    row_identity = (identity,"Terminal" if terminal else target)
                    if row_identity in seen:
                        raise ValueError("duplicate live body/link evidence identity")
                    seen.add(row_identity)
                    if row["Status"] == "FAIL":
                        issue("FAIL",name,"Rows", "probe reported invisible/failed exact bomb body, link, or terminal effect: "+str(row_identity))
                        continue
                    if row["Status"] == "INCOMPLETE":
                        # No vantage or a moving baseline establishes no pixels.
                        continue
                    if row["Status"] != "PASS" or row["CollisionClear"] is not True:
                        raise ValueError("invalid live pixel status/collision-clear predicate")
                    vector(row["Camera"],32768)
                    world,fixture = row["WorldDepth"],row["FixtureDepth"]
                    if any(type(v) not in (int,float) or not math.isfinite(v) or not 0 <= v <= 1 for v in (world,fixture)):
                        raise ValueError("invalid live world/fixture depth")
                    if type(row["CenterDepthClear"]) is not bool or row["CenterDepthClear"] != (world >= fixture-.00001):
                        raise ValueError("live center-depth flag differs from measured predicate")
                    changed,unstable,(width,height) = pixel_bracket(row["Captures"])
                    integer(row["SamplePixel"]["X"],width-1);integer(row["SamplePixel"]["Y"],height-1)
                    if changed != integer(row["ChangedPixels"]) or unstable != integer(row["UnstablePixels"]) or changed == 0 or unstable:
                        raise ValueError("live pixel bracket lacks stable positive exact-key pixels")
                    if terminal:
                        integer(row["EffectParticles"],minimum=1)
                        terminal_pixels.add(identity)
                        terminal_classifications.append(dict(key=list(identity),classification=row["Classification"],
                            retirementWitnessStage=row.get("RetirementWitnessStage"),retirementSnapshotFrame=row.get("RetirementSnapshotFrame"),
                            retirementSnapshotFrontier=row.get("RetirementSnapshotFrontier")))
                    elif target is None:bodies.add(identity)
                    else:links.add((identity,target))
                except FileNotFoundError as error:
                    issue("INCOMPLETE",name,f"Rows[{ordinal}].Captures",str(error))
                except (OSError,KeyError,TypeError,ValueError,struct.error,zlib.error) as error:
                    issue("FAIL",name,f"Rows[{ordinal}]",str(error))
        except (KeyError,TypeError,ValueError) as error:
            issue("FAIL",name,"report",str(error))
        if not eligible:
            issue("INCOMPLETE",name,"DamageCoverage","no relevant post-bootstrap authority damaging key")
        missing = eligible-bodies-terminal_pixels
        no_state = {identity for identity in missing if not any(s[0] == identity for _,_,states in snapshots for s in states)}
        if missing:
            issue("INCOMPLETE",name,"DamageKeys","body pixels or classified coalesced terminal-effect pixels missing for "+str(len(missing))+" exact damaging keys; "+str(len(no_state))+" had no applied live snapshot")
        if "Lockjaw" in kinds:
            triangles=[]
            for _,_,states in snapshots:
                groups=defaultdict(list)
                for s in states:
                    if s[1] == "Lockjaw":groups[s[0][:5]].append(s)
                for group in groups.values():
                    ordered=sorted(group,key=lambda s:s[8])
                    if len(ordered) == 3 and [s[8] for s in ordered] == [0,1,2] and any(s[0] in eligible for s in ordered):
                        edges={(s[0],(*s[0][:5],linked)) for s in ordered for linked in s[9:11] if linked}
                        if len(edges) == 3:triangles.append(edges)
            if not any(edges <= links for edges in triangles):
                issue("INCOMPLETE",name,"LockjawEdges","three exact visible links from an actual damaging triangle not established")
        metrics[name] = dict(requiredDamagingKeys=[list(k) for k in sorted(eligible)],
            damagingKeysWithPositiveBodyPixels=len(eligible&bodies),
            damagingKeysWithPositiveTerminalPixels=len(eligible&terminal_pixels),
            terminalFallbackKeys=[list(k) for k in sorted(eligible&terminal_pixels)],
            terminalFallbackProofs=[p for p in terminal_classifications if tuple(p["key"]) in eligible],
            verifiedTerminalEffects=len(terminal_pixels),missingDamagingKeys=[list(k) for k in sorted(missing)],
            preBootstrapDamageKeysExcluded=[list(k) for k in sorted(before_bootstrap)],
            missingWithNoAppliedSnapshot=[list(k) for k in sorted(no_state)],verifiedBodies=len(bodies),verifiedLinks=len(links),
            derivedOrientationJoins=derived_joins)
    return ("FAIL" if "FAIL" in verdicts else "INCOMPLETE" if verdicts else "PASS"),metrics


def load_live_reports(directory,names):
    result={}
    for name in names:
        path=Path(directory)/(name+".visibility.json")
        if path.exists():
            try:result[name]=json.loads(path.read_text())
            except (OSError,ValueError) as error:result[name]=dict(_Error=str(error))
    return result


def audit(server, peers, manifest, summary=None, kinds=KINDS, logs=None, visibility=None,live_visibility_reports=None):
    findings, finding_index = [], {}
    def finding(status, process, field, message):
        identity = (status,process,field,message)
        if identity in finding_index:
            finding_index[identity]["count"] = finding_index[identity].get("count",1)+1
        else:
            row = dict(status=status,process=process,field=field,message=message)
            findings.append(row);finding_index[identity] = row
    reports = {"server": server, **peers}
    metrics = {"peers": {}, "kinds": {}, "placements": 0, "damage": 0}
    processes = (summary or {}).get("processes", manifest.get("processes", []))
    inventory = [p.get("name") for p in processes]
    if len(inventory) != len(set(inventory)) or set(inventory) != set(reports) or len(peers) < 1:
        finding("FAIL", "campaign", "processes", "exact authority/client inventory differs from evidence")
    try:
        if integer(manifest["settings"]["players"],8,1) != len(peers):
            finding("FAIL", "campaign", "settings.players", "client count differs from configured campaign")
        before, after = manifest["assemblySha256"], manifest["assemblyAfterSha256"]
        if not isinstance(before,str) or len(before) != 64 or any(c not in "0123456789abcdef" for c in before) or before != after:
            finding("FAIL", "campaign", "assemblySha256", "missing/changed immutable engine identity")
        if manifest.get("runnerError"):
            finding("FAIL", "campaign", "runnerError", str(manifest["runnerError"]))
        for p in processes:
            if p.get("exit") != 0 or p.get("timedOut") or p.get("unexpectedExit"):
                finding("FAIL", p.get("name"), "process.exit", "process failed, timed out or exited unexpectedly")
    except (KeyError, TypeError, ValueError) as error:
        finding("INCOMPLETE", "campaign", "manifest", str(error))
    for name, report in reports.items():
        try:
            for field in ("Overflow", "ConnectionLost", "SemanticOverrun", "SemanticDisconnects", "LastError"):
                if report[field]:
                    finding("FAIL", name, field, str(report[field]))
            if report["Schema"] not in (1,2) or report["Protocol"] != server["Protocol"] or report["Protocol"] != manifest["settings"]["protocol"]:
                finding("FAIL", name, "Protocol", "schema/protocol mismatch")
            if not report["Frames"] or not report["Windows"] or not report["States"] or not report["Transport"]:
                finding("INCOMPLETE", name, "States/Transport", "missing simulation/health/window/transport evidence")
            for field in ("QueueDrops", "SocketErrors"):
                if integer(report["Transport"][field],2**63-1):
                    finding("FAIL", name, "Transport."+field, "transport failure")
            if logs is None or name not in logs:
                finding("INCOMPLETE", name, "log", "process log unavailable")
            elif match := FATAL.search(logs[name]):
                finding("FAIL", name, "log", match.group(0))
        except (KeyError, TypeError, ValueError) as error:
            finding("INCOMPLETE", name, "genericEvidence", str(error))
    try:
        if not server["ServerSegments"]:
            finding("INCOMPLETE", "server", "ServerSegments", "no authority simulation sample")
        if any(s["StepFailures"] for s in server["ServerSegments"]):
            finding("FAIL", "server", "ServerSegments.StepFailures", "authority simulation failure")
        health = {}
        for s in server["States"]:
            identity,value = health_snapshot(s,server["Schema"])
            if identity in health and health[identity] != value:
                finding("FAIL", "server", "States", "conflicting exact health snapshot")
            health[identity] = value
        for name, peer in peers.items():
            peer_health = [health_snapshot(s,peer["Schema"]) for s in peer["States"]]
            common = [s for s,(identity,_) in zip(peer["States"],peer_health) if identity in health]
            mismatches = sum(value != health[identity] for identity,value in peer_health if identity in health)
            metrics["peers"][name] = dict(healthSamplesCompared=len(common), healthMismatches=mismatches)
            if peer["Schema"] != server["Schema"] or mismatches:
                finding("FAIL",name,"States","exact authority health/event mismatch")
            if not common or len({s["Frame"] for s in common}) < 2:
                finding("INCOMPLETE",name,"States","need health samples at multiple identical authority frames")
            if peer["Schema"] == 2:
                parity, errors = slow_lane_parity(server,peer)
                metrics["peers"][name].update(parity)
                for error in errors:
                    finding("FAIL",name,"SlowStates",error)
    except (KeyError, TypeError, ValueError) as error:
        finding("FAIL","campaign","States", "malformed health parity: "+str(error))
    drain_status, errors = drain_coverage(without_shots(manifest), without_shots(reports))
    if drain_status != "PASS":
        finding("FAIL" if drain_status == "FAIL" else "INCOMPLETE","campaign","Drain", "; ".join(errors) or "bounded drain not run")
    try:
        release = manifest["drain"]["release"]
        final = integer(release["Bomb"],2**64-1)
        stream = (integer(release["Epoch"],2**64-1,1),integer(release["Match"],65535,1))
        for name, report in reports.items():
            at = report["Drain"]["AtRelease"]
            if (at["Epoch"],at["Match"]) != stream or integer(at["BombFrontier"],2**64-1) != final:
                finding("FAIL",name,"Drain.AtRelease.BombFrontier","released bomb identity/frontier differs from authority")
            # Capture() runs after normal shutdown too. The saved barrier and
            # immutable AtRelease represent the gameplay frontier, whereas the
            # current session may already have reset epoch/match to zero.
            d = manifest["drain"]["server"] if name == "server" else manifest["drain"]["peers"][name]
            if (d["Epoch"],d["Match"]) != stream or integer(d["BombFrontier"],2**64-1) != final:
                finding("FAIL",name,"manifest.drain.BombFrontier","bomb frontier differs from authority")
            for field in ("BombPendingRequests","BombPendingEvents","BombFaulted"):
                expected_type = bool if field == "BombFaulted" else int
                if d[field] != 0 or type(d[field]) is not expected_type:
                    finding("FAIL",name,"manifest.drain."+field,"pending, faulted or malformed bomb stream at drain")
                if report["Drain"][field] != 0 or type(report["Drain"][field]) is not expected_type:
                    finding("FAIL",name,"Drain."+field,"pending, faulted or malformed bomb stream at export")
            if (report["Drain"]["Epoch"],report["Drain"]["Match"]) == stream and report["Drain"]["BombFrontier"] != final:
                finding("FAIL",name,"Drain.BombFrontier","active frozen stream frontier changed after release")
    except (KeyError, TypeError, ValueError) as error:
        finding("INCOMPLETE","campaign","Drain.BombFrontier",str(error))
        final, stream = None, None

    parsed = {name: rows(report,name,finding) for name,report in reports.items()}
    authority = parsed["server"]
    events = authority["events"]
    if not events:
        finding("INCOMPLETE","server","Bombs.Event","no authority bomb events")
    seqs = [e[0] for e in events]
    if seqs != list(range(1,len(events)+1)) or final is not None and (seqs[-1] if seqs else 0) != final:
        finding("FAIL","server","Bombs.Sequence","authority event stream has missing, duplicate, reordered or unexported sequences")
    by_seq = {e[0]:e for e in events}
    placements, terminals, resolutions = {}, {}, {}
    for e in events:
        identity = e[4][0]
        if stream and identity[:2] != stream:
            finding("FAIL","server","Bombs.Key","mixed authority streams outside single-match audit scope")
        target = placements if e[2] == "Placed" else terminals if e[2] in TERMINALS else None
        if target is not None:
            if identity in target:
                finding("FAIL","server","Bombs.Key","duplicate placement/terminal full key: "+str(identity))
            target[identity] = e
        if e[2] in RESOLUTIONS:
            if identity in resolutions:
                finding("FAIL","server","Bombs.Request","duplicate request resolution: "+str(identity))
            resolutions[identity] = e
    if placements.keys() != terminals.keys():
        finding("FAIL","server","Bombs.Terminal","placed and terminal exact key/count sets differ")
    for identity in placements.keys() & terminals.keys():
        if terminals[identity][0] <= placements[identity][0] or terminals[identity][1] < placements[identity][1] or terminals[identity][4][1] != placements[identity][4][1] or terminals[identity][4][2] != placements[identity][4][2]:
            finding("FAIL","server","Bombs.Terminal","terminal precedes/differs from birth identity/kind/placed frame")
    requests = {}
    for r in authority["requests"]:
        if r[0] in requests:
            finding("FAIL","server","Bombs.Request","duplicate full request key")
        requests[r[0]] = r
    if requests.keys() != resolutions.keys():
        finding("FAIL","server","Bombs.Request","received and resolved full request sets/counts differ")
    for identity in requests.keys() & resolutions.keys():
        action, resolution = requests[identity][2], resolutions[identity][2]
        if action == "Place" and resolution == "DetonateAccepted" or action == "Detonate" and resolution == "Placed":
            finding("FAIL","server","Bombs.Request.Action","request action differs from resolution")
    for name, peer in parsed.items():
        if name == "server":
            continue
        offered = [r[0] for r in peer["requests"]]
        if len(offered) != len(set(offered)):
            finding("FAIL",name,"Bombs.Request","duplicate offered full request key")
        for r in peer["requests"]:
            if requests.get(r[0]) != r or r[0] not in resolutions:
                finding("FAIL",name,"Bombs.Request","offered request lacks identical authority request/resolution")
    paid = set()
    for identity, frame, kind, victim, damage_event in authority["damage"]:
        damage_identity = (*identity[:2],*victim,damage_event)
        if damage_identity in paid:
            finding("FAIL","server","Bombs.DamageEvent","duplicate victim damage event")
        paid.add(damage_identity)
        birth, death = placements.get(identity),terminals.get(identity)
        if not birth or not death or death[2] != "Detonated" or birth[4][1] != kind or requests.get(identity,(None,None,None))[2] != "Place":
            finding("FAIL","server","Bombs.Damage.Key","damage lacks admitted/detonated exact same key/kind")
        elif not birth[1] <= frame <= death[1]:
            # Contact damage can precede its terminal (native Lockjaw delay 44 frames).
            finding("FAIL","server","Bombs.Damage.Frame","damage outside admitted bomb lifetime")
    metrics.update(placements=len(placements),damage=len(authority["damage"]),requests=len(requests),events=len(events),
                   authorityEventCounts={kind:sum(e[2] == kind for e in events) for kind in sorted(EVENT_KINDS)})

    snapshots = {}
    previous_frontier = 0
    for frame, frontier, states in authority["snapshots"]:
        if frontier < previous_frontier:
            finding("FAIL","server","Bombs.Snapshot.Frontier","authority snapshot frontier regressed")
        previous_frontier = frontier
        if frontier > (final if final is not None else len(events)):
            finding("FAIL","server","Bombs.Snapshot.Frontier","snapshot frontier beyond authority stream")
        live = {k for k,b in placements.items() if b[0] <= frontier and (k not in terminals or terminals[k][0] > frontier)}
        if {s[0] for s in states} != live:
            finding("FAIL","server","Bombs.Snapshot.States","snapshot membership differs from exact live lifecycle set")
        for s in states:
            for linked in s[9:11]:
                linked_key = (*s[0][:5],linked)
                if linked and (linked_key not in placements or placements[linked_key][0] > frontier or placements[linked_key][4][1] != "Lockjaw"):
                    finding("FAIL","server","Bombs.Snapshot.Link","link has no earlier same-owner/life Lockjaw placement")
            birth = placements.get(s[0])
            if not birth or (s[1],s[2]) != (birth[4][1],birth[4][2]):
                finding("FAIL","server","Bombs.Snapshot.State","snapshot kind/placed frame differs from birth")
        if (frame,frontier) in snapshots and snapshots[frame,frontier] != states:
            finding("FAIL","server","Bombs.Snapshot","conflicting same-frame/frontier snapshot")
        snapshots[frame,frontier] = states
    if not snapshots:
        finding("INCOMPLETE","server","Bombs.Snapshot","authority snapshot captures required for exact pose/link parity")
    peer_states, peer_snapshots = {}, {}
    for name, peer in parsed.items():
        if name == "server":
            continue
        seen = peer["snapshots"]
        if not seen:
            finding("INCOMPLETE",name,"Bombs.Snapshot","bootstrap frontier/live baseline unavailable")
            continue
        start = seen[0][1]
        # Only a frozen release establishes the authority's terminal interval.
        # Without it, compare every observed event in exact ordinal order, but
        # do not misclassify a suffix created after this process exited as loss.
        end = final if final is not None else max((e[0] for e in peer["events"]),default=start)
        expected = [by_seq[n] for n in sorted(by_seq) if start < n <= end]
        if peer["events"] != expected:
            finding("FAIL",name,"Bombs.Event","post-bootstrap event sequence/payload differs from exact authority interval")
        if final is None:
            finding("INCOMPLETE",name,"Bombs.Event.TerminalInterval","no frozen release frontier; only the observed event prefix is verified")
        if final is not None and start > final:
            finding("FAIL",name,"Bombs.Snapshot.Frontier","bootstrap exceeds final authority frontier")
        previous_frontier = start
        for frame,frontier,states in seen:
            if frontier < previous_frontier:
                finding("FAIL",name,"Bombs.Snapshot.Frontier","applied snapshot frontier regressed")
            previous_frontier = frontier
            if (frame,frontier) not in snapshots:
                finding("INCOMPLETE",name,"Bombs.Snapshot","authority frame/frontier snapshot unavailable")
            elif snapshots[frame,frontier] != states:
                finding("FAIL",name,"Bombs.Snapshot.State","full snapshot identity/kind/links/pose differs from authority")
        peer_states[name] = {s for _,_,ss in seen for s in ss}
        peer_snapshots[name] = [set(ss) for _,_,ss in seen]
        metrics["peers"].setdefault(name,{}).update(bootstrapFrontier=start,bootstrapLiveKeys=[list(s[0]) for s in seen[0][2]],
                bootstrapLiveCount=len(seen[0][2]),eventIntervalEnd=end,terminalIntervalFrozen=final is not None,
                eventsCompared=len(expected),snapshotsCompared=len(seen))
    if not kinds:
        finding("FAIL","campaign","kind","at least one relevant bomb kind must be required")
    for kind in kinds:
        if kind not in KINDS:
            finding("FAIL","campaign","kind","unknown required bomb kind")
            continue
        placed = sum(e[4][1] == kind for e in placements.values())
        damage = sum(d[2] == kind for d in authority["damage"])
        metrics["kinds"][kind] = dict(placements=placed,positiveDamage=damage)
        if not placed or not damage:
            finding("INCOMPLETE","server","coverage."+kind,"required kind needs placement and positive authority damage")
    if "Lockjaw" in kinds:
        triangles = []
        for _,_,states in authority["snapshots"]:
            groups = defaultdict(list)
            for s in states:
                if s[1] == "Lockjaw":
                    groups[s[0][:5]].append(s)
            for group in groups.values():
                ordered = sorted(group,key=lambda s:s[8])
                if len(ordered) == 3 and [s[8] for s in ordered] == [0,1,2] and [s[9:11] for s in ordered] == [(0,0),(ordered[0][0][5],0),(ordered[0][0][5],ordered[1][0][5])]:
                    triangles.append(set(ordered))
        if not triangles:
            finding("INCOMPLETE","server","coverage.Lockjaw.triangle","no coexisting three-bomb progressive three-edge state")
        for name in peers:
            if not any(t <= ss for ss in peer_snapshots.get(name,[]) for t in triangles):
                finding("INCOMPLETE",name,"coverage.Lockjaw.triangle","three linked triangle states not observed exactly")
    if "Stinglarva" in kinds:
        def motion_rotation(states):
            # Three representatives suffice: anchor, a different position, a
            # different orientation. Avoid quadratic pairs on long captures.
            representatives = defaultdict(list)
            for s in states:
                if s[1] != "Stinglarva":
                    continue
                previous = representatives[s[0]]
                if any(s[3] != p[3] and (s[4],s[5]) != (p[4],p[5]) for p in previous):
                    return True
                if not previous or len(previous) < 3 and not any((s[3],s[4],s[5]) == (p[3],p[4],p[5]) for p in previous):
                    previous.append(s)
            return False
        if not motion_rotation(s for _,_,ss in authority["snapshots"] for s in ss):
            finding("INCOMPLETE","server","coverage.Stinglarva.motionRotation","same-key changing position and orientation not captured")
        for name in peers:
            if not motion_rotation(peer_states.get(name,set())):
                finding("INCOMPLETE",name,"coverage.Stinglarva.motionRotation","moving/rotating same-key poses not observed exactly")
    gameplay = "FAIL" if any(f["status"] == "FAIL" for f in findings) else "INCOMPLETE" if findings else "PASS"
    render_status = render_fixture(visibility,manifest.get("assemblySha256"),finding)
    live_status,live_metrics = live_pixels(peers,parsed,placements,authority["damage"],kinds,manifest,live_visibility_reports,finding,authority["events"])
    return dict(status="FAIL" if any(f["status"] == "FAIL" for f in findings) else "INCOMPLETE" if any(f["status"] == "INCOMPLETE" for f in findings) else "PASS",
                gameplayStatus=gameplay,visibilityStatus=live_status,liveVisibilityStatus=live_status,
                eventReasonContract=EVENT_REASON_CONTRACT,
                liveVisibilityCoverage=live_metrics,
                renderFixtureStatus=render_status, scope=dict(authorityMatches=1,requiredKinds=list(kinds),
                checked="request admission, exact bomb lifecycle/event/state parity, health and bounded reliable drain",
                independentRenderFixture="same-engine local canonical ghost fixture: five bodies and three Lockjaw edges, positive and wall-negative pixels; full fixture keys are independent of network keys",
                livePixelScope="stable positive body pixels for relevant post-bootstrap authority damaging keys on each peer; exact accepted terminal-effect pixels may cover only classified birth/detonation or exact accepted snapshot retirement before any body draw or attempt; fallback keys reported separately and do not prove persistent body pixels; three links of an actual damaging Lockjaw triangle; exact canonical non-orientation fields plus native float32 captured orientation (one ULP derived bound); no claim of visibility before damage",
                excluded=["pre-bootstrap pixel visibility","persistent body pixels for classified terminal fallback keys","shot parity","cross-match rotation","all maps"]),
                findings=findings,metrics=metrics)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign",type=Path)
    parser.add_argument("--kind",action="append",choices=KINDS)
    parser.add_argument("--visibility",type=Path,help="Independent local GPU fixture report")
    parser.add_argument("--live-visibility",type=Path,help="Directory of peer-N.visibility.json live reports")
    parser.add_argument("--output",type=Path)
    args = parser.parse_args()
    try:
        read = lambda path: json.loads(path.read_text())
        manifest = read(args.campaign/"manifest.json")
        summary = read(args.campaign/"summary.json")
        names = [p["name"] for p in summary["processes"]]
        reports, logs = {}, {}
        for name in names:
            if not isinstance(name,str) or Path(name).name != name or name in reports:
                raise ValueError("unsafe/duplicate process name")
            reports[name] = read(args.campaign/(name+".json"))
            if (args.campaign/(name+".log")).exists():
                logs[name] = (args.campaign/(name+".log")).read_text(errors="replace")
            for log in (args.campaign/name).glob("**/*.txt"):
                logs[name] = logs.get(name,"") + "\n" + log.read_text(errors="replace")
        live_dir = args.live_visibility or (args.campaign if manifest["settings"].get("bomb_visibility") else None)
        live_reports = load_live_reports(live_dir,(name for name in names if name != "server")) if live_dir else None
        result = audit(reports.pop("server"),reports,manifest,summary,args.kind or KINDS,logs,
                       read(args.visibility) if args.visibility else None,live_reports)
    except (OSError,KeyError,TypeError,ValueError) as error:
        result = dict(status="FAIL",gameplayStatus="FAIL",visibilityStatus="INCOMPLETE",
                      findings=[dict(status="FAIL",process="campaign",field="evidence",message=str(error))])
    text = json.dumps(result,indent=2)+"\n"
    if args.output:
        with args.output.open("x") as stream:
            stream.write(text)
    else:
        print(text,end="")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
