#!/usr/bin/env python3
"""Ordinary native 2/4/8-client networking smoke matrix, with isolated preferences.

Runs the existing real server and -netcheck -nographics hitrig modes. Optional explicit loopback loadouts exercise the intended weapon without trusting
client inventory. Native source/resource/proof gates remain enabled. A private runtime copy freezes
the build for the entire run; only this runner's child processes are signalled.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import time

RTTS = (0, 25, 50, 100, 150, 250, 350, 500)
LOSSES = (0, 1, 2, 5, 10)
PROFILES = [dict(name=f"rtt{rtt}-loss{loss}", rtt_ms=rtt, loss_percent=loss,
                 jitter_ms=min(60, rtt // 5), reorder_percent=0 if rtt == loss == 0 else 2,
                 duplicate_percent=0 if rtt == loss == 0 else 1)
            for rtt in RTTS for loss in LOSSES]
MODES = {"jump", "sniper", "duel", "missile", "magmaul", "judicator", "battlehammer", "shockcoil", "voltdriver", "powerbeam",
         "omega", "imperialist-unscoped", "powerbeam-charged", "missile-charged", "voltdriver-charged", "judicator-charged", "magmaul-charged", "shockcoil-all", "shockcoil-morph", "alt-static", "alt-lateral", "alt-morph", "alt-contact", "alt-crossing"}
GATES = ("src/MphRead/Mods/Network/HitRig.cs", "src/MphRead/Mods/Network/NetCheckSimulation.cs",
         "src/MphRead/Entities/Players/PlayerInput.cs", "src/MphRead/Mods/Network/PlayerEntityNetAim.cs",
         "src/MphRead/Mods/Network/NetFireEvents.cs")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def text(path):
    return path.read_text(errors="replace") if path.is_file() else ""


def last_line(path, prefix):
    return next((line.strip() for line in reversed(text(path).splitlines()) if line.strip().startswith(prefix)), None)


def stop_children(children, stopped):
    # A suspended process must be resumed before graceful termination can run.
    for child in stopped:
        if child.poll() is None:
            try: child.send_signal(signal.SIGCONT)
            except ProcessLookupError: pass
    for child in reversed(children):
        if child.poll() is None:
            try: child.send_signal(signal.SIGINT if os.name == "posix" else signal.SIGTERM)
            except ProcessLookupError: pass
    deadline = time.monotonic() + 12
    for child in children:
        try:
            child.wait(timeout=max(.1, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            child.kill()
            child.wait()


WEAPONS = {"jump":"Imperialist", "sniper":"Imperialist", "duel":"Imperialist", "missile":"Missile",
    "magmaul":"Magmaul", "judicator":"Judicator", "battlehammer":"Battlehammer", "shockcoil":"ShockCoil",
    "voltdriver":"VoltDriver", "powerbeam":"PowerBeam"}
WEAPONS.update({"omega":"OmegaCannon", "imperialist-unscoped":"Imperialist", "shockcoil-all":"ShockCoil", "shockcoil-morph":"ShockCoil"})
WEAPONS.update({name+"-charged":WEAPONS[name] for name in ("powerbeam","missile","voltdriver","judicator","magmaul")})
WEAPON_IDS = {name:i for i,name in enumerate(("PowerBeam","VoltDriver","Missile","Battlehammer","Imperialist","Judicator","Magmaul","ShockCoil","OmegaCannon"))}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", type=Path, help="Built/staged directory containing ProjectPrime.dll and native dependencies")
    parser.add_argument("--data", type=Path, help="Read-only source directory containing the operator's paths.txt")
    parser.add_argument("--output", type=Path, help="New evidence directory; the runner refuses existing contents")
    parser.add_argument("--dotnet", default=str(Path.home() / ".dotnet/dotnet"))
    parser.add_argument("--seconds", type=int, default=30)
    parser.add_argument("--owner-grace", type=int, default=0, help="Extra owner seconds so staggered peers export before an owner-driven session close")
    parser.add_argument("--seed", type=int, default=431)
    parser.add_argument("--modes", default="jump")
    parser.add_argument("--profiles", default="rtt0-loss0,rtt100-loss2,rtt500-loss10", help="Comma-separated names or all (40 profiles)")
    parser.add_argument("--jitter-ms", type=int, help="Override configured jitter for nonzero RTT profiles")
    parser.add_argument("--players", type=int, choices=(2,4,8), default=2)
    parser.add_argument("--impacts", action="store_true", help="Opt in to impact delivery/debug/profile; no rendered success claim")
    parser.add_argument("--no-live", action="store_true", help="Keep impact diagnostics but disable cosmetic delivery for matched performance controls")
    parser.add_argument("--fixture-loadout", action="store_true", help="Explicit unlisted loopback authority loadout for the selected weapon")
    parser.add_argument("--server-scratch", action="store_true", help="Enable measured server scratch optimizations")
    parser.add_argument("--claim-mode", choices=("off","shadow","enabled"), default="shadow")
    parser.add_argument("--rendered", action="store_true", help="Run native graphical clients and save local screenshots")
    parser.add_argument("--render-view", choices=("all","shooter","victim","observer"), default="all")
    parser.add_argument("--no-shots", action="store_true", help="Avoid screenshot readback during draw-cadence checks")
    parser.add_argument("--fps", type=int, default=60, choices=(60,120,144,240,360))
    parser.add_argument("--observer", action="store_true", help="Add one rendered spectator, within the eight-slot limit")
    parser.add_argument("--backend", default="opengl", choices=("opengl","metal","vulkan"))
    parser.add_argument("--record-demo", action="store_true")
    parser.add_argument("--predicted-kills", action="store_true")
    parser.add_argument("--hunter", default="Samus")
    parser.add_argument("--map", default="MP1 SANCTORUS")
    parser.add_argument("--mapdir", type=Path)
    parser.add_argument("--startup-timeout", type=float, default=90)
    parser.add_argument("--pause-after", type=float, default=0, help="Seconds after both clients load/synchronize; 0 disables suspension")
    parser.add_argument("--pause-seconds", type=float, default=4)
    parser.add_argument("--pause-role", choices=("peer0", "peer1"), default="peer0")
    parser.add_argument("--pause-profiles", default="rtt100-loss2", help="Profiles which receive the optional mobile-like gap, or all")
    parser.add_argument("--require-combat", action="store_true", help="Also fail an arm with no authority-confirmed client hit")
    parser.add_argument("--list-profiles", action="store_true")
    args = parser.parse_args()
    if args.list_profiles:
        print(json.dumps(PROFILES, indent=2))
        return 0
    if not all((args.runtime, args.data, args.output)):
        parser.error("--runtime, --data, and --output are required for execution")
    if args.render_view == "observer" and not args.observer: parser.error("observer view needs --observer")
    if args.observer and (not args.rendered or args.players == 8): parser.error("observer requires rendered mode and a free player slot")
    if args.fixture_loadout and args.require_combat and not args.impacts: parser.error("fixture combat proof requires --impacts")
    if args.jitter_ms is not None and not 0 <= args.jitter_ms <= 250: parser.error("jitter must be 0..250 ms")
    if args.seconds < 5 or not 0 <= args.owner_grace <= 30 or args.startup_timeout <= 0 or args.pause_seconds <= 0 or args.pause_after < 0:
        parser.error("invalid duration/deadline")
    if args.pause_after and (os.name != "posix" or args.pause_after + args.pause_seconds >= args.seconds):
        parser.error("pause requires POSIX signals and time for active simulation after resume")
    modes = args.modes.split(",")
    if any(mode not in MODES for mode in modes):
        parser.error("unknown existing hitrig mode")
    requested = {profile["name"] for profile in PROFILES} if args.profiles == "all" else set(args.profiles.split(","))
    if not requested <= {profile["name"] for profile in PROFILES}:
        parser.error("unknown profile; use --list-profiles")
    runtime, data, output = args.runtime.resolve(), args.data.resolve(), args.output.resolve()
    if not (runtime / "ProjectPrime.dll").is_file() or not (data / "paths.txt").is_file():
        parser.error("runtime needs ProjectPrime.dll; data needs paths.txt")
    if output.exists() and any(output.iterdir()):
        parser.error("output must be new or empty to preserve earlier evidence")
    if runtime == output or runtime in output.parents:
        parser.error("output cannot be inside the source runtime")
    output.mkdir(parents=True, exist_ok=True)
    frozen = output / "runtime"
    shutil.copytree(runtime, frozen)
    repo = Path(__file__).resolve().parents[2]
    source_hashes = {name: sha(repo / name) for name in GATES if (repo / name).is_file()}
    manifest = dict(seed=args.seed, runtime_source=str(runtime), runtime_sha256=sha(frozen / "ProjectPrime.dll"),
                    ownerGraceSeconds=args.owner_grace,
                    liveDelivery=not args.no_live,
                    paths_source=str(data / "paths.txt"), map=args.map, modes=modes,
                    profiles=[dict(profile, jitter_ms=args.jitter_ms) if args.jitter_ms is not None and profile["rtt_ms"] else profile for profile in PROFILES if profile["name"] in requested],
                    seconds=args.seconds, players=args.players, observer=args.observer, rendered=args.rendered, renderView=args.render_view, screenshotCapture=not args.no_shots, fps=args.fps if args.rendered else None, backend=args.backend if args.rendered else None, impacts=args.impacts, fixtureLoadout=args.fixture_loadout, serverScratch=args.server_scratch, claimMode=args.claim_mode, freezeSource=source_hashes, native_gates="unchanged existing netcheck simulation/scenario gates",
                    evidence_scope="real native UDP and graphical clients; screenshot inspection reported separately" if args.rendered else "real native simulation and UDP; no rendered/device acceptance")
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    mapdir = args.mapdir.resolve() if args.mapdir else output / "empty-maps"
    mapdir.mkdir(parents=True, exist_ok=True)
    results = []
    for mode in modes:
        for profile in manifest["profiles"]:
            folder = output / f"{mode}-{profile['name']}"
            folder.mkdir()
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
                probe.bind(("127.0.0.1", 0))
                port = str(probe.getsockname()[1])
            children, logs, stopped = [], [], set()
            total_players = args.players + int(args.observer)
            row = dict(mode=mode, profile=profile, seed=args.seed, port=int(port), commands={}, pause=None)
            def launch(role, options):
                user = folder / role
                user.mkdir()
                shutil.copy2(data / "paths.txt", user / "paths.txt")
                (user / "maprotation.txt").write_text(f"{args.map} | Battle | 15 | 99\n")
                config = user / "telemetry-config.json"
                config.write_text(json.dumps(dict(enabled=False, detail="Off", upload=False)))
                env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(user), ALSOFT_DRIVERS="null", PRIME_TELEMETRY_CONFIG=str(config))
                if args.impacts:
                    env["PRIME_IMPACT_LOG"] = str(folder / f"{role}-impacts.json")
                    options += ["-noliveimpacts" if args.no_live else "-liveimpacts", "-liveimpactdebug", "-impactprofile"]
                    if role == "server": options += ["-netdebug"]
                command = [args.dotnet, str(frozen / "ProjectPrime.dll"), "-mapdir", str(mapdir), *options]
                row["commands"][role] = command
                log = open(folder / f"{role}.log", "w")
                logs.append(log)
                child = subprocess.Popen(command, cwd=frozen, env=env, stdout=log, stderr=subprocess.STDOUT)
                children.append(child)
                return child
            try:
                server_options=["-server", "-port", port, "-players", str(total_players), "-nomaster", "-serverreplays", "off", "-debuglog", "-claimfastpath", args.claim_mode]
                if args.fixture_loadout and mode in WEAPONS: server_options += ["-hitrigloadout", WEAPONS[mode]]
                if args.server_scratch: server_options += ["-impactserverscratch"]
                server = launch("server", server_options)
                deadline = time.monotonic() + args.startup_timeout
                while "authoritative server ready" not in text(folder / "server.log"):
                    if server.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError("real server failed readiness; inspect server.log")
                    time.sleep(.1)
                peers = {}
                for index in range(total_players):
                    role = f"peer{index}"
                    options = ["-netcheck", "127.0.0.1", "-port", port, "-name", role,
                        "-hunter", args.hunter, "-seconds", str(args.seconds+(args.owner_grace if index==0 else 0)),
                        "-netchecklobbyplayers", str(total_players),
                        "-netlag", f"{profile['rtt_ms']}:{profile['jitter_ms']}", "-netloss", f"{profile['loss_percent']}%",
                        "-netreorder", f"{profile['reorder_percent']}%", "-netduplicate", f"{profile['duplicate_percent']}%",
                        "-netseed", str(args.seed + index), "-debuglog"]
                    if index < args.players: options += ["-hitrig", mode]
                    else: options += ["-spectate", "0"]
                    render_index={"shooter":0,"victim":1,"observer":args.players}.get(args.render_view)
                    if args.rendered and (render_index is None or index==render_index):
                        options += ["-netcheckfps", str(args.fps), "-renderer", args.backend, "-size", "640x360"]
                        if not args.no_shots:
                            shots=folder / f"{role}-shots";shots.mkdir()
                            options += ["-shots", str(shots)]
                    else: options += ["-nographics"]
                    if args.record_demo: options += ["-recorddemo"]
                    if args.predicted_kills: options += ["-predictedkillvisuals"]
                    peers[role] = launch(role, options)
                    time.sleep(.4)
                started = time.monotonic()
                gap = bool(args.pause_after and (args.pause_profiles == "all" or profile["name"] in args.pause_profiles.split(",")))
                paused_at = None
                ready_at = None
                deadline = started + args.seconds + args.owner_grace + args.startup_timeout + 15
                while any(peer.poll() is None for peer in peers.values()):
                    elapsed = time.monotonic() - started
                    loaded_slots = set(re.findall(r"\bslot (\d+) synchronizing\b", text(folder / "server.log")))
                    if ready_at is None and len(loaded_slots) >= total_players:
                        ready_at = time.monotonic()
                        row["both_clients_loaded_seconds"] = elapsed
                    target = peers[args.pause_role]
                    if gap and paused_at is None and ready_at is not None \
                            and time.monotonic() - ready_at >= args.pause_after and target.poll() is None:
                        target.send_signal(signal.SIGSTOP); stopped.add(target); paused_at = time.monotonic()
                        row["pause"] = dict(role=args.pause_role, started_seconds=elapsed)
                    if target in stopped and time.monotonic() - paused_at >= args.pause_seconds:
                        target.send_signal(signal.SIGCONT); stopped.remove(target)
                        row["pause"]["elapsed_seconds"] = time.monotonic() - paused_at
                    if time.monotonic() > deadline:
                        raise TimeoutError("bounded native client deadline expired")
                    time.sleep(.05)
                row["client_exit_codes"] = {role: peer.returncode for role, peer in peers.items()}
                reports = {}
                for role in peers:
                    logpath = folder / f"{role}.log"
                    reports[role] = dict(rendered=last_line(logpath, "[netcheck] rendered frames="), simulation=last_line(logpath, "[netchecksim] steps="),
                        scenario=last_line(logpath, "[netchecksim] altScenarioExercised="),
                        rig=last_line(logpath, "hit rig:"), hits=last_line(logpath, "hit prediction:"),
                        rewind=last_line(logpath, "lag compensation:"), contact=last_line(logpath, "alt contact:"))
                row["reports"] = reports
                if args.rendered:
                    # A one-way rig cannot require every participant to shoot.
                    # Instead compare each actually firing owner's counter with
                    # every other view's observed counter. A spectator need not fire.
                    owner_shots={}
                    for role in peers:
                        values=re.findall(rf"netcheck {role} mine {role} shots ([0-9.]+)",text(folder/f"{role}.log"))
                        owner_shots[role]=float(values[-1]) if values else 0
                    pairs=[]
                    for shooter,count in owner_shots.items():
                        if count <= 0: continue
                        for view in peers:
                            if view==shooter: continue
                            values=re.findall(rf"netcheck {view} saw {shooter} shots ([0-9.]+)",text(folder/f"{view}.log"))
                            pairs.append(dict(shooter=shooter,view=view,owner=count,observed=float(values[-1]) if values else 0))
                    row["shot_replication_pairs"]=pairs
                    row["shot_replication_observed"]=(bool(pairs) and all(pair["observed"]>0 for pair in pairs)) if args.render_view=="all" else None
                row["native_checks_passed"] = all(code == 0 for code in row["client_exit_codes"].values()) \
                    and all(bool(report["rendered"]) if report["rendered"] else bool(report["simulation"] and report["scenario"]
                            and "altScenarioExercised=True" in report["scenario"]) for report in reports.values())
                if args.rendered and args.render_view=="all": row["native_checks_passed"] &= row["shot_replication_observed"]
                row["trigger_attempts"] = sum(int(value) for report in reports.values()
                    for value in re.findall(r"(\d+) triggers", report["rig"] or ""))
                row["authority_confirmed_hits"] = sum(int(value) for report in reports.values()
                    for value in re.findall(r"hit prediction: \d+ predicted, (\d+) confirmed", report["hits"] or ""))
                # Native authority-only hits are legitimate combat too: ricochets
                # can hit a target that this client never predicted. Keep the
                # prediction-confirmed subset separate from all authority arrivals.
                row["authority_arrived_hits"] = row["authority_confirmed_hits"] + sum(int(value) for report in reports.values()
                    for match in re.findall(r"(?:\((\d+) hits arrived from the authority\)|(?<!\d)(\d+) unpredicted)", report["hits"] or "")
                    for value in match if value)
                row["combat_confirmation_observed"] = row["authority_arrived_hits"] > 0
                row["passed"] = row["native_checks_passed"] and row["trigger_attempts"] > 0 and (not args.require_combat or row["combat_confirmation_observed"])
            except Exception as error:
                row.update(passed=False, error=str(error))
            finally:
                stop_children(children, stopped)
                for log in logs: log.close()
                row["server_exit_code"] = children[0].returncode if children else None
            authority_path=folder / "server-impacts.json"
            if authority_path.is_file():
                diagnostics=json.loads(authority_path.read_text());events=diagnostics["events"]
                row["native_emissions"]=diagnostics.get("nativeEmissions",[])
                row["authority_facts_by_weapon"]={name:sum(e["Stage"]==1 and e["Weapon"]==code for e in events) for name,code in WEAPON_IDS.items()}
                authority_keys={json.dumps(e["Identity"],sort_keys=True) for e in events if e["Stage"]==1}
                row["authority_backed_live_ingress"]={}
                for role in row.get("reports",{}):
                    path=folder/f"{role}-impacts.json"
                    received=json.loads(path.read_text())["events"] if path.is_file() else []
                    keys={json.dumps(e["Identity"],sort_keys=True) for e in received if e["Stage"]==3}
                    row["authority_backed_live_ingress"][role]=len(keys&authority_keys)
                # Prediction counters omit some continuous/authority-only paths and
                # can reset on slot departure. With explicit impact diagnostics,
                # require actual authority damage joined by full identity to every
                # client's live receive lane; keep prediction feedback separate.
                row["combat_confirmation_observed"] = bool(authority_keys) and (args.no_live or
                    bool(row["authority_backed_live_ingress"]) and all(row["authority_backed_live_ingress"].values()))
                row["passed"] = row.get("native_checks_passed",False) and row.get("trigger_attempts",0)>0 \
                    and (not args.require_combat or row["combat_confirmation_observed"])
                if args.fixture_loadout and args.require_combat and mode in WEAPONS:
                    row["intended_weapon_exercised"]=row["authority_facts_by_weapon"][WEAPONS[mode]]>0
                    row["passed"] = row["passed"] and row["intended_weapon_exercised"]
                    if mode.endswith("-charged"):
                        row["charged_native_emission_observed"]=any(e["weapon"]==WEAPON_IDS[WEAPONS[mode]] and e["charged"]>0 for e in row["native_emissions"])
                        row["passed"] = row["passed"] and row["charged_native_emission_observed"]
            elif args.fixture_loadout and args.require_combat and mode in WEAPONS:
                row["passed"]=False;row["intended_weapon_exercised"]=False
            # A client can finish its timed simulation after a server refusal or
            # disconnect has reset its counters. Exit zero is not proof that the
            # intended population stayed connected through the campaign.
            row["unexpected_disconnects"] = [line.strip() for line in text(folder/"server.log").splitlines()
                if "semantic history overrun" in line or "reliable control failed" in line or " timed out (slot " in line]
            if row["unexpected_disconnects"]: row["passed"] = False
            results.append(row)
            (output / "summary.json").write_text(json.dumps(results, indent=2) + "\n")
            print(json.dumps(row), flush=True)
    unchanged = {name: sha(repo / name) for name in source_hashes} == source_hashes
    manifest["freezeSourceUnchanged"] = unchanged
    manifest["frozen_runtime_unchanged"] = sha(frozen / "ProjectPrime.dll") == manifest["runtime_sha256"]
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    return int(not unchanged or not manifest["frozen_runtime_unchanged"] or any(not row["passed"] for row in results))


if __name__ == "__main__":
    raise SystemExit(main())
