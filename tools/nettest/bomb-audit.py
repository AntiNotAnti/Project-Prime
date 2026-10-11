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
import math
from pathlib import Path

from net2_evidence import FATAL, drain_coverage, health_value, slow_lane_parity, state_key

KINDS = ("MorphBall", "Stinglarva", "Lockjaw")
KEY_FIELDS = ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")
EVENT_KINDS = {"Placed", "Refused", "DetonateAccepted", "Detonated", "Removed"}
TERMINALS = {"Detonated", "Removed"}
RESOLUTIONS = {"Placed", "Refused", "DetonateAccepted"}


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
                    integer(row["Reason"], 7)
                    if (row["Kind"] == "Refused") != (row["Reason"] != 0):
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


def audit(server, peers, manifest, summary=None, kinds=KINDS, logs=None, visibility=None):
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
    for frame, frontier, states in authority["snapshots"]:
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
        expected = [by_seq[n] for n in sorted(by_seq) if n > start]
        if peer["events"] != expected:
            finding("FAIL",name,"Bombs.Event","post-bootstrap event sequence/payload differs from exact authority interval")
        if final is not None and start > final:
            finding("FAIL",name,"Bombs.Snapshot.Frontier","bootstrap exceeds final authority frontier")
        for frame,frontier,states in seen:
            if (frame,frontier) not in snapshots:
                finding("INCOMPLETE",name,"Bombs.Snapshot","authority frame/frontier snapshot unavailable")
            elif snapshots[frame,frontier] != states:
                finding("FAIL",name,"Bombs.Snapshot.State","full snapshot identity/kind/links/pose differs from authority")
        peer_states[name] = {s for _,_,ss in seen for s in ss}
        peer_snapshots[name] = [set(ss) for _,_,ss in seen]
        metrics["peers"].setdefault(name,{}).update(bootstrapFrontier=start,bootstrapLiveKeys=[list(s[0]) for s in seen[0][2]],
                bootstrapLiveCount=len(seen[0][2]),eventsCompared=len(expected),snapshotsCompared=len(seen))
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
    # GPU report format intentionally remains conservative until independently
    # defined presentation captures can bind pixels to exact campaign identities.
    visibility_status = "INCOMPLETE"
    finding("INCOMPLETE","campaign","visibility", "GPU pixel visibility proof unavailable" if visibility is None else "supplied GPU report has no established exact-key presentation verification contract")
    return dict(status="FAIL" if gameplay == "FAIL" else "INCOMPLETE", gameplayStatus=gameplay,
                visibilityStatus=visibility_status, liveVisibilityStatus=visibility_status,
                renderFixtureStatus="NOT_RUN" if visibility is None else "INCOMPLETE", scope=dict(authorityMatches=1, requiredKinds=list(kinds),
                checked="request admission, exact bomb lifecycle/event/state parity, health and bounded reliable drain",
                excluded=["GPU pixel visibility", "shot parity", "cross-match rotation", "all maps"]),
                findings=findings, metrics=metrics)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign",type=Path)
    parser.add_argument("--kind",action="append",choices=KINDS)
    parser.add_argument("--visibility",type=Path)
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
        result = audit(reports.pop("server"),reports,manifest,summary,args.kind or KINDS,logs,
                       read(args.visibility) if args.visibility else None)
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
