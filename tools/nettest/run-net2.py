#!/usr/bin/env python3
"""N0 independent-process UDP campaigns. Mac/Windows only; stdlib only.

Build first. An output directory is immutable: use a new name for every attempt.
No public server/master, production profile, or existing process is touched.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import time
from net2_evidence import adjudicate, drain_barrier, drain_prepared, DrainStability

PROFILES = {"lan": (0, 0, 0, 0, 0), "moderate": (100, 20, 1, 0, 0),
            "poor": (250, 40, 2, 0, 0), "extreme": (400, 80, 5, 3, 1)}
HUNTERS = ["Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Samus"]
BOMB_HUNTERS = {"bomb-samus": "Samus", "bomb-kanden": "Kanden", "bomb-lockjaw": "Sylux"}


def client_role(args, slot):
    """Native hunter, spectator role and actual shot workload for this slot."""
    hunter = BOMB_HUNTERS.get(args.scenario, "Sylux" if args.scenario == "shockcoil-mixed" else HUNTERS[slot])
    if args.scenario == "alt-morph" and slot == 1:
        hunter = "Weavel"
    observer = args.players == 3 and slot == 2 and (
        args.rendered or args.scenario in BOMB_HUNTERS or args.scenario == "alt-morph")
    expects_shots = not observer and args.scenario not in BOMB_HUNTERS and (
        args.soak or args.scenario in ("tour", "duel", "shockcoil-all", "shockcoil-mixed") or slot % 2 == 0)
    return hunter, observer, expects_shots


def digest(path):
    value = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def release_drain(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    save(temporary, value)
    temporary.replace(path)


def stop(process):
    if process.poll() is None:
        process.terminate()
    try:
        return process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        return process.wait(timeout=10)


def bomb_summary(out, records, manifest, scenario):
    """Bomb campaigns use independent gameplay evidence, never shot coverage."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("bomb_audit",Path(__file__).with_name("bomb-audit.py"))
    module = importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    try:
        reports,logs = {},{}
        for record in records:
            name = record["name"]
            if not isinstance(name,str) or Path(name).name != name or name in reports:
                raise ValueError("unsafe/duplicate process name")
            reports[name] = json.loads((out/(name+".json")).read_text())
            if (out/(name+".log")).exists():
                logs[name] = (out/(name+".log")).read_text(errors="replace")
            for log in (out/name).glob("**/*.txt"):
                logs[name] = logs.get(name,"")+"\n"+log.read_text(errors="replace")
        kind = {"bomb-samus":"MorphBall","bomb-kanden":"Stinglarva","bomb-lockjaw":"Lockjaw"}[scenario]
        live_reports = module.load_live_reports(out,reports.keys()-{"server"}) if manifest["settings"].get("bomb_visibility") else None
        audit = module.audit(reports.pop("server"),reports,manifest,dict(processes=records),(kind,),logs,live_visibility_reports=live_reports)
    except (OSError,KeyError,TypeError,ValueError) as error:
        audit = dict(status="FAIL",gameplayStatus="FAIL",liveVisibilityStatus="INCOMPLETE",renderFixtureStatus="NOT_RUN",
                     findings=[dict(status="FAIL",process="campaign",field="evidence",message=str(error))],metrics={})
    save(out/"bomb-audit.json",audit)
    gameplay = [f for f in audit["findings"] if f["process"] != "render-fixture" and not f["field"].startswith("LiveVisibility")]
    requested_visibility = manifest["settings"].get("bomb_visibility",False)
    scoped_findings = audit["findings"] if requested_visibility else gameplay
    messages = lambda status: [f"{f['process']}: {f['field']}: {f['message']}" for f in scoped_findings if f["status"] == status]
    drain = "FAIL" if any(f["status"] == "FAIL" and "drain" in f["field"].lower() for f in gameplay) else (
        "INCOMPLETE" if not audit.get("metrics",{}).get("peers") or any("drain" in f["field"].lower() for f in gameplay) else "PASS")
    return dict(status=audit["status"] if requested_visibility else audit["gameplayStatus"],
                gameplayStatus=audit["gameplayStatus"],
                scope="bomb gameplay and requested live pixel gate" if requested_visibility else "bomb gameplay gate; live pixels reported separately",
                processes=records,failures=messages("FAIL"),incomplete=messages("INCOMPLETE"),
                parity=audit.get("metrics",{}).get("peers",{}),bombAudit=audit,
                coverage=dict(canonicalBombSequences=audit["gameplayStatus"],boundedHeadlessDrain=drain,
                              renderedBombVisibility=audit["liveVisibilityStatus"],renderFixture=audit["renderFixtureStatus"]))


def operator_summary(out, protocol):
    try:
        report = json.loads((out / "server.operator.json").read_text())
        def keys(value, fields):
            if type(value) is not dict or set(value) != set(fields):
                raise ValueError("operator schema allowlist mismatch")
        keys(report, ("Schema", "Protocol", "Phase", "Queue", "Traffic", "ClaimVerdictResponses", "VerdictCountingUnit"))
        if type(report["Schema"]) is not int or type(report["Protocol"]) is not int or report["Schema"] != 1 or report["Protocol"] != protocol:
            raise ValueError("operator version mismatch")
        # Enum vocabulary is checked by the C# fixture; no arbitrary nested objects are accepted.
        if report["Phase"] not in ("Lobby", "Starting", "InMatch", "PostMatch"):
            raise ValueError("operator phase mismatch")
        keys(report["Queue"], ("Current", "HighWater", "Drops", "BufferExhaustions", "BufferExhaustionStatus"))
        keys(report["Traffic"], ("ReceivedPackets", "SentPackets", "ReceivedBytes", "SentBytes"))
        for value in [report["Queue"][f] for f in ("Current", "HighWater", "Drops")] + list(report["Traffic"].values()):
            if type(value) is not int or value < 0: raise ValueError("operator numeric field mismatch")
        if report["Queue"]["BufferExhaustions"] is not None or report["Queue"]["BufferExhaustionStatus"] != "UNAVAILABLE":
            raise ValueError("unmeasured exhaustion mislabeled")
        expected = {"Applied", "AlreadyResolved", "RejectedDeadShooter", "RejectedDeadVictim", "RejectedPlausibility", "RejectedTooOld", "RejectedLifecycle", "RejectedGeometry", "RejectedDamage", "RejectedLaunch", "RejectedNoDamage", "RejectedImpulse", "ClaimCapacity", "Corrected"}
        rows = report["ClaimVerdictResponses"]
        if type(rows) is not list or len(rows) != len(expected): raise ValueError("operator verdict vocabulary mismatch")
        for row in rows:
            keys(row, ("Outcome", "Responses"))
            if type(row["Responses"]) is not int or row["Responses"] < 0: raise ValueError("operator verdict counter mismatch")
        if {row["Outcome"] for row in rows} != expected: raise ValueError("operator verdict vocabulary mismatch")
        if report["VerdictCountingUnit"] != "Authority Answer response decisions; retries included; not unique denied shots or delivered ACKs":
            raise ValueError("operator counting unit mismatch")
        return {"status": "PASS", "scope": "identity-free typed aggregate schema; not unique shot or delivery accounting"}
    except (OSError, ValueError, TypeError, KeyError) as error:
        return {"status": "FAIL", "reason": str(error)}


def bomb_visibility_environment(args,name):
    if not getattr(args,"bomb_visibility",False) or name == "server":
        return {}
    if not args.rendered or args.scenario not in BOMB_HUNTERS:
        raise ValueError("live bomb visibility requires a rendered bomb campaign")
    return dict(PRIME_NET2_BOMB_VISIBILITY=str((args.out/(name+".visibility.json")).resolve()),
                PRIME_NET2_SOURCE=args.source)


def run(args):
    if platform.system() not in ("Darwin", "Windows"):
        raise RuntimeError("This campaign is restricted to Mac and Windows")
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=False)
    stage = args.out / "bin"
    shutil.copytree(args.build.resolve(), stage)
    assembly = stage / "ProjectPrime.dll"
    if not assembly.exists():
        raise RuntimeError("Build directory has no ProjectPrime.dll")
    profile_text = args.paths.resolve().read_text(encoding="utf-8-sig")
    asset_files = []
    for line in profile_text.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if value and key != "Export":
            root = Path(value)
            if not root.is_absolute() or not root.is_dir():
                raise RuntimeError("paths.txt asset paths must be absolute existing directories")
            for file in sorted(root.rglob("*")):
                if file.is_file():
                    asset_files.append({"version": key, "path": file.relative_to(root).as_posix(),
                                        "size": file.stat().st_size, "sha256": digest(file)})
    if not asset_files:
        raise RuntimeError("No extracted game assets in paths.txt")
    save(args.out / "assets.json", asset_files)
    rotation = args.out / "rotation.txt"
    rooms = args.rooms.split(",")
    rotation.write_text("\n".join(f"{room} | Battle | {args.round_minutes} | 999" for room in rooms), encoding="utf-8")
    dotnet = str(Path(args.dotnet).resolve()) if Path(args.dotnet).is_file() else args.dotnet
    manifest = {"source": args.source, "platform": platform.platform(), "python": platform.python_version(),
        "dotnetInfo": subprocess.check_output([dotnet, "--info"], text=True),
        "assemblySha256": digest(assembly), "assetManifestSha256": digest(args.out / "assets.json"),
        "harnessSha256": {name: digest(Path(__file__).with_name(name)) for name in
                         (("run-net2.py","net2_evidence.py","bomb-audit.py") if args.scenario in BOMB_HUNTERS else ("run-net2.py","net2_evidence.py"))},
        "settings": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
        "commands": [], "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    if args.server_performance:
        manifest["harnessSha256"]["net13_performance.py"] = digest(Path(__file__).with_name("net13_performance.py"))
    save(args.out / "manifest.json", manifest)
    processes, handles, records = [], [], []
    server = None
    started = time.monotonic()

    def launch(name, extra, duration, expects_shots=True):
        profile = args.out / name
        profile.mkdir()
        exports = profile / "exports"
        exports.mkdir()
        isolated_paths = [line for line in profile_text.splitlines() if not line.startswith("Export=")]
        (profile / "paths.txt").write_text("\n".join([*isolated_paths, f"Export={exports}", ""]), encoding="utf-8")
        maps = profile / "empty-maps"
        maps.mkdir()
        # Do not silently inherit a developer's experimental/test flags into a
        # supposedly reproducible control. Keep ordinary OS/runtime environment.
        env = {key: value for key, value in os.environ.items()
               if not key.startswith(("PRIME_", "MPHREAD_", "PROJECT_PRIME_"))}
        env.update(PROJECT_PRIME_USER_DATA=str(profile), ALSOFT_DRIVERS="null",
                   MPHREAD_PHASE_SECONDS="2", PRIME_NET2_EVIDENCE=str(args.out / f"{name}.json"),
                   PRIME_SHOT_PROOF=args.shot_proof)
        if args.server_operator and name == "server":
            env["PRIME_SERVER_OPERATOR_OUTPUT"] = str(args.out / "server.operator.json")
        if args.movement_validation_shadow:
            env["PRIME_MOVEMENT_VALIDATION_SHADOW"] = "1"
        if args.confirmed_impacts:
            env["PRIME_CONFIRMED_IMPACTS"] = "1"
        if args.soak:
            env["PRIME_NET2_SOAK"] = "1"
        if args.drain_seconds:
            env.update(PRIME_NET2_DRAIN_SECONDS=str(args.drain_seconds), PRIME_NET2_DRAIN_ROLE=name if name == "server" else "client",
                       PRIME_NET2_DRAIN_STATUS=str(args.out / f"{name}.drain.json"),
                       PRIME_NET2_DRAIN_RELEASE=str(args.out / ("drain.server-release.json" if name == "server" else "drain.release.json")))
        if args.scenario in BOMB_HUNTERS:
            env["PRIME_NET2_BOMB_EVIDENCE"] = str(args.out / f"{name}.bombs.json")
        if args.bomb_triangle_fixture:
            env["PRIME_NET2_TRIANGLE_SETUP"] = "1"
        if args.server_performance and name == "server":
            env["PRIME_SERVER_PERF_OUTPUT"] = str(args.out / "server-performance.json")
        env.update(bomb_visibility_environment(args,name))
        command = [dotnet, str(assembly), "-mapdir", str(maps), "-noupdate", "-noautoupdate", *extra]
        handle = open(args.out / f"{name}.log", "w", encoding="utf-8")
        handles.append(handle)
        process = subprocess.Popen(command, cwd=stage, env=env, stdin=subprocess.DEVNULL,
                                   stdout=handle, stderr=subprocess.STDOUT)
        processes.append(process)
        record = {"name": name, "pid": process.pid, "startedSeconds": time.monotonic() - started,
                  "requestedSeconds": duration, "expectsShots": expects_shots, "exit": None, "timedOut": False}
        records.append(record)
        manifest["commands"].append({"name": name, "argv": command,
            "environment": {k: env[k] for k in ("PROJECT_PRIME_USER_DATA", "ALSOFT_DRIVERS", "MPHREAD_PHASE_SECONDS", "PRIME_NET2_EVIDENCE", "PRIME_SHOT_PROOF")},
            "inheritedGameEnvironment": "cleared PRIME_*, MPHREAD_*, PROJECT_PRIME_*",
            "soak": args.soak})
        if args.server_operator and name == "server":
            manifest["commands"][-1]["environment"]["PRIME_SERVER_OPERATOR_OUTPUT"] = env["PRIME_SERVER_OPERATOR_OUTPUT"]
        if args.movement_validation_shadow:
            manifest["commands"][-1]["environment"]["PRIME_MOVEMENT_VALIDATION_SHADOW"] = "1"
        if args.confirmed_impacts:
            manifest["commands"][-1]["environment"]["PRIME_CONFIRMED_IMPACTS"] = "1"
        if args.drain_seconds:
            manifest["commands"][-1]["environment"].update({key: value for key, value in env.items() if key.startswith("PRIME_NET2_DRAIN_")})
        if args.scenario in BOMB_HUNTERS:
            manifest["commands"][-1]["environment"]["PRIME_NET2_BOMB_EVIDENCE"] = env["PRIME_NET2_BOMB_EVIDENCE"]
        if args.bomb_triangle_fixture:
            manifest["commands"][-1]["environment"]["PRIME_NET2_TRIANGLE_SETUP"] = "1"
            manifest["triangleFixture"] = "Explicit collision-verified initial authority geometry; each participant adopts its own initial snapshot pose once. Subsequent native movement, placement, damage and timers are unchanged."
        if args.server_performance and name == "server":
            manifest["commands"][-1]["environment"]["PRIME_SERVER_PERF_OUTPUT"] = env["PRIME_SERVER_PERF_OUTPUT"]
        save(args.out / "manifest.json", manifest)
        return process, record

    try:
        server, server_record = launch("server", ["-server", "-port", str(args.port), "-players", str(args.players),
            "-nomaster", "-noserverreplays", "-rotation", str(rotation), "-netdebug"], args.seconds, False)
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            if server.poll() is not None:
                raise RuntimeError("server exited during startup")
            if "listening on UDP" in (args.out / "server.log").read_text(errors="replace"):
                break
            time.sleep(.1)
        else:
            server_record["timedOut"] = True
            raise RuntimeError("server loader/startup timeout")
        time.sleep(2)
        clients = []
        rtt, jitter, loss, reorder, duplicate = PROFILES[args.profile]

        def client(slot, name, seconds):
            # Only affinity Shock Coil drains health. Combat roles use assigned
            # network slots, which can differ from concurrent launch order.
            hunter, observer, expects = client_role(args, slot)
            extra = ["-netcheck", "127.0.0.1", "-port", str(args.port), "-name", name,
                "-hunter", hunter, "-seconds", str(seconds), "-size", "480x270",
                "-netlag", f"{rtt}:{jitter}", "-netloss", f"{loss}%", "-netreorder", f"{reorder}%",
                "-netduplicate", f"{duplicate}%", "-netseed", str(args.seed + slot)]
            if not args.rendered:
                extra.append("-nographics")
            if args.record:
                extra.append("-recorddemo")
            if args.rendered and args.players == 3:
                shots = args.out / f"shots-{name}"
                shots.mkdir()
                extra += ["-shots", str(shots)]
            if args.scenario != "tour":
                extra += ["-hitrig", args.scenario]
            if observer:
                extra += ["-spectate", "0"]
            if args.soak and slot == args.players - 2:
                extra += ["-spectate", "90", "-rejoin", "150"]
            p, record = launch(name, extra, seconds, expects)
            if args.scenario in BOMB_HUNTERS:
                record["expectsBombs"] = slot == 0
            clients.append((p, record, time.monotonic() + seconds + args.drain_seconds + 120))

        # Soak: one late join, then a normal departure and a fresh process/connection.
        client_start = time.monotonic()
        for slot in range(args.players - (1 if args.soak else 0)):
            if args.observer_delay and slot == 2:
                continue
            client(slot, f"peer-{slot}", args.seconds)
            time.sleep(.25)
        late_started = reconnect_started = False
        observer_started = not args.observer_delay
        drain_started = None
        stability = DrainStability()
        drain_released = False
        server_quiesce_requested = False
        prepared_release = None
        preparation = None
        drain_reason = "waiting for peer quiescence"
        while True:
            elapsed = time.monotonic() - client_start
            if not observer_started and elapsed >= args.observer_delay:
                client(2, "peer-2", max(1, args.seconds - elapsed))
                observer_started = True
            bot_finished = not args.soak or "[net2] bot fill complete" in (args.out / "server.log").read_text(errors="replace")
            if args.soak and not late_started and elapsed >= 30 and (bot_finished or elapsed >= 120):
                client(args.players - 1, "peer-late", 180)
                late_started = True
                late_started_seconds = elapsed
            if args.soak and late_started and not reconnect_started and elapsed >= late_started_seconds + 210:
                client(args.players - 1, "peer-reconnect", max(1, args.seconds - elapsed))
                reconnect_started = True
            if args.drain_seconds and not drain_released:
                statuses = {}
                for name in ("server", *(record["name"] for _, record, _ in clients)):
                    path = args.out / f"{name}.drain.json"
                    try:
                        # Atomic publication makes partial JSON impossible; stale processes cannot pass.
                        if time.time() - path.stat().st_mtime <= 1:
                            statuses[name] = json.loads(path.read_text(encoding="utf-8"))
                    except (OSError, ValueError):
                        pass
                peers = {name: row for name, row in statuses.items() if name != "server"}
                if any(row.get("Quiescing") for row in peers.values()) and drain_started is None:
                    drain_started = time.monotonic()
                # Freeze only periodic authority refreshes once every expected
                # live peer has freshly reported quiescence in this same stream.
                expected = {record["name"] for process, record, _ in clients if process.poll() is None}
                authority = statuses.get("server", {})
                context = authority.get("Epoch"), authority.get("Match")
                if (not server_quiesce_requested and len(expected) == args.players
                        and expected.issubset(peers) and all(context)
                        and all(peers[name].get("Quiescing")
                            and (peers[name].get("Epoch"), peers[name].get("Match")) == context
                            and not peers[name].get("ConnectionLost") and not peers[name].get("Error")
                            for name in expected)):
                    request = {"Epoch": context[0], "Match": context[1]}
                    path = args.out / "drain.server-release.json.quiesce"
                    if path.exists():
                        raise RuntimeError("immutable server quiesce request already exists")
                    release_drain(path, request)
                    server_quiesce_requested = True
                    manifest["serverQuiesceRequest"] = {"path": str(path), "request": request,
                        "peerNames": sorted(expected), "requestedSeconds": time.monotonic() - started,
                        "serverFrame": authority.get("Frame"),
                        "peerFrames": {name: peers[name].get("Frame") for name in sorted(expected)}}
                    save(args.out / "manifest.json", manifest)
                release = None
                if "server" in statuses and len(peers) == args.players:
                    quiet, drain_reason, stable_seconds = stability.observe(statuses["server"], peers, time.monotonic())
                    release, drain_reason = drain_barrier(statuses["server"], list(peers.values()))
                else:
                    drain_reason = "missing/frozen process drain statuses"
                    _, _, stable_seconds = stability.observe({}, {}, time.monotonic())
                if prepared_release is None and release and stable_seconds >= statuses["server"]["ClaimGraceSeconds"]:
                    prepared_release = release
                    preparation = {"prepareServer": statuses["server"], "preparePeers": peers,
                        "prepareStableSeconds": stable_seconds,
                        "stabilityCondition": "gameplay frontier and offered keys stable; healthy queues during grace, empty at preparation and release"}
                    # Prepare authority first. Clients cannot see this isolated token,
                    # so no Bye/PlayerLeft event can race its saved AtRelease frontier.
                    release_drain(args.out / "drain.server-release.json", release)
                    manifest["drain"] = {"status": "PREPARING", "release": release,
                        "server": statuses["server"], "peers": peers, "stableSeconds": stable_seconds, **preparation}
                    save(args.out / "manifest.json", manifest)
                prepared, prepared_reason = drain_prepared(statuses.get("server", {}), prepared_release) if prepared_release else (False, None)
                if prepared and release == prepared_release and stable_seconds >= statuses["server"]["ClaimGraceSeconds"]:
                    manifest["drain"] = {"status": "PASS", "release": release, "server": statuses["server"],
                        "peers": peers, "stableSeconds": stable_seconds, "elapsedSeconds": time.monotonic() - drain_started,
                        **preparation}
                    drain_released = True
                elif drain_started is not None and time.monotonic() - drain_started >= args.drain_seconds:
                    authority = statuses.get("server", {})
                    release = {"Status": "FAIL", "Epoch": authority.get("Epoch", 0), "Match": authority.get("Match", 0),
                               "Semantic": authority.get("SemanticLatest", 0), "Award": authority.get("AwardLatest", 0)}
                    timeout_reason = (prepared_reason or drain_reason) if prepared_release else drain_reason or "claim-grace stability not reached"
                    manifest["drain"] = {"status": "FAIL", "reason": timeout_reason,
                        "release": release, "server": authority, "peers": peers, "stableSeconds": stable_seconds,
                        **(preparation or {})}
                    # A server that cached preparation PASS will retain that token;
                    # client FAIL and manifest FAIL still fail closed on export.
                    release_drain(args.out / "drain.server-release.json", release)
                    drain_released = True
                if drain_released:
                    release_drain(args.out / "drain.release.json", release)
                    save(args.out / "manifest.json", manifest)
            for process, record, limit in clients:
                if record["exit"] is not None:
                    continue
                if process.poll() is None and time.monotonic() > limit:
                    record["timedOut"] = True
                    stop(process)
                if process.poll() is not None:
                    record["exit"] = process.returncode
                    record["endedSeconds"] = time.monotonic() - started
            if server.poll() is not None:
                server_record["unexpectedExit"] = True
                raise RuntimeError("server exited before clients finished")
            if all(r["exit"] is not None for _, r, _ in clients) and observer_started and (not args.soak or reconnect_started):
                break
            time.sleep(.2)
        (args.out / "server.json.stop").touch()
        try:
            server_record["exit"] = server.wait(timeout=20)
        except subprocess.TimeoutExpired:
            server_record["timedOut"] = True
            # Frozen main predates the marker; retain that limitation, do not claim PASS.
            if platform.system() == "Darwin":
                server.send_signal(signal.SIGINT)
            server_record["exit"] = stop(server)
    except (RuntimeError, OSError) as error:
        manifest["runnerError"] = str(error)
    finally:
        for process, record in zip(processes, records):
            if process.poll() is None:
                record["timedOut"] = True
                stop(process)
            record["exit"] = process.returncode
        for handle in handles:
            handle.close()
        manifest["elapsedSeconds"] = time.monotonic() - started
        manifest["assemblyAfterSha256"] = digest(assembly)
        save(args.out / "manifest.json", manifest)
        summary = (bomb_summary(args.out,records,manifest,args.scenario) if args.scenario in BOMB_HUNTERS
                   else adjudicate(args.out,records,expected_protocol=args.protocol))
        if len(records) < args.players + 1 or manifest.get("runnerError") or manifest["assemblyAfterSha256"] != manifest["assemblySha256"]:
            summary["status"] = "FAIL"
            summary["failures"].append(manifest.get("runnerError", "missing participants or modified running assembly"))
        if args.server_performance:
            from net13_performance import audit
            try:
                manifest["serverPerformanceEvidence"] = audit(args.out / "server-performance.json")
                counts = manifest["serverPerformanceEvidence"]["operationCounts"]
                if any(counts[name] == 0 for name in ("Step", "PumpBefore", "PumpAfter", "Replication")):
                    raise ValueError("requested live server stages not exercised")
            except (OSError, ValueError, TypeError, KeyError) as error:
                summary["status"] = "FAIL"
                summary["failures"].append("server performance evidence: " + str(error))
            save(args.out / "manifest.json", manifest)
        if args.server_operator:
            summary["operator"] = operator_summary(args.out, args.protocol)
            if summary["operator"]["status"] != "PASS":
                summary["status"] = "FAIL"
                summary["failures"].append("server operator evidence: " + summary["operator"].get("reason", "schema failure"))
            operator_path = args.out / "server.operator.json"
            if operator_path.is_file(): manifest["serverOperatorSha256"] = digest(operator_path)
            save(args.out / "manifest.json", manifest)
        save(args.out / "summary.json", summary)
        print(json.dumps({"status": summary["status"], "out": str(args.out), "failures": summary["failures"], "incomplete": summary["incomplete"]}), flush=True)
    return 0 if summary["status"] == "PASS" else 1


def parse_args(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--build", type=Path, required=True)
    p.add_argument("--paths", type=Path, required=True)
    p.add_argument("--source", required=True, help="Exact source SHA plus dirty diff hash if applicable")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--protocol", type=int, default=50, help="Expected evidence protocol (historical campaigns may specify 41)")
    p.add_argument("--shot-proof", choices=("off", "shadow", "enforce"), default="shadow",
                   help="Record and apply the explicit N4 admission mode")
    p.add_argument("--players", type=int, choices=(2, 3, 4, 8), default=2)
    p.add_argument("--seconds", type=float, default=90)
    p.add_argument("--drain-seconds", type=float, default=0,
                   help="Opt-in bounded quiescence/frontier/empty-reliable-queue barrier; zero preserves legacy shutdown")
    p.add_argument("--port", type=int, default=27991)
    p.add_argument("--seed", type=int, default=8128)
    p.add_argument("--profile", choices=PROFILES, default="lan")
    p.add_argument("--scenario", choices=("tour", "duel", "shockcoil", "shockcoil-all", "shockcoil-mixed", "missile", "self-moving", "self-wall", "alt-morph", *BOMB_HUNTERS), default="tour")
    p.add_argument("--observer-delay", type=float, default=0,
                   help="Delay the third spectator in a short three-client bomb or alt-morph campaign")
    p.add_argument("--rooms", default="MP1 SANCTORUS")
    p.add_argument("--round-minutes", type=float, default=30)
    p.add_argument("--rendered", action="store_true")
    p.add_argument("--server-operator", action="store_true", help="Write identity-free server operator aggregates")
    p.add_argument("--movement-validation-shadow", action="store_true",
                   help="Observe accepted owner movement envelopes without correction or rejection")
    p.add_argument("--confirmed-impacts", action="store_true", help="Enable the independent optional live hit-cue lane")
    p.add_argument("--bomb-visibility",action="store_true",
                   help="Capture per-peer live bomb GPU evidence; requires --rendered and a bomb scenario")
    p.add_argument("--bomb-triangle-fixture", action="store_true",
                   help="Explicit initial collision-verified geometry for a short Combat Hall Lockjaw fixture; native subsequent controls")
    p.add_argument("--server-performance", action="store_true", help="Opt-in aggregate server wall-time/process CPU/GC evidence; no per-player facts")
    p.add_argument("--record", action="store_true")
    p.add_argument("--soak", action="store_true")
    args = p.parse_args(argv)
    if args.seconds <= 0 or args.round_minutes <= 0 or not 1024 <= args.port <= 65535:
        p.error("duration, round length and port must be positive and valid")
    if args.drain_seconds < 0 or args.drain_seconds > 60:
        p.error("--drain-seconds must be between zero and 60")
    if args.observer_delay < 0 or args.observer_delay >= args.seconds or (
            args.observer_delay and (args.players != 3 or (args.scenario not in BOMB_HUNTERS and args.scenario != "alt-morph") or args.soak)):
        p.error("--observer-delay requires a short three-client bomb or alt-morph scenario and must be less than --seconds")
    if args.drain_seconds and args.soak:
        p.error("--drain-seconds supports short campaigns; soak drain is not implemented")
    if args.bomb_triangle_fixture and (args.scenario != "bomb-lockjaw" or args.soak
            or args.players not in (2, 3) or args.rooms != "MP3 PROVING GROUND"):
        p.error("--bomb-triangle-fixture requires a short two/three-client bomb-lockjaw run in MP3 PROVING GROUND")
    if args.bomb_visibility and (not args.rendered or args.scenario not in BOMB_HUNTERS or args.soak):
        p.error("--bomb-visibility requires a short rendered bomb scenario")
    if args.soak and (args.players != 8 or args.seconds < 1200 or args.seconds > 1800):
        p.error("--soak requires eight slots and 1200–1800 seconds")
    return args


if __name__ == "__main__":
    raise SystemExit(run(parse_args()))
