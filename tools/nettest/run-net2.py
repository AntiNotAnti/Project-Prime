#!/usr/bin/env python3
"""N0 independent-process UDP campaigns. Mac/Windows only; stdlib only.

Build first. An output directory is immutable: use a new name for every attempt.
No public server/master, production profile, or existing process is touched.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import subprocess
import time
import uuid
from net2_evidence import adjudicate, drain_barrier, drain_prepared, DrainStability

PROFILES = {"lan": (0, 0, 0, 0, 0), "moderate": (100, 20, 1, 0, 0),
            "poor": (250, 40, 2, 0, 0), "extreme": (400, 80, 5, 3, 1)}
HUNTERS = ["Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Samus"]
BOMB_HUNTERS = {"bomb-samus": "Samus", "bomb-kanden": "Kanden", "bomb-lockjaw": "Sylux"}
NCX_FIXTURES = ("ncx-static", "ncx-lateral", "ncx-jump")


def server_flags(args):
    """Explicit server-only rule switches; ordinary campaign defaults stay off."""
    if getattr(args, "pacing_lobby_client", None):
        return ["-lobby"]
    return ["-powerups"] if args.powerups else []


def child_environment(args, name, profile, inherited=None):
    """Only declared campaign flags survive the reproducible child boundary."""
    inherited = os.environ if inherited is None else inherited
    env = {key: value for key, value in inherited.items()
           if not key.startswith(("PRIME_", "MPHREAD_", "PROJECT_PRIME_"))}
    env.update(PROJECT_PRIME_USER_DATA=str(profile), ALSOFT_DRIVERS="null",
               MPHREAD_PHASE_SECONDS="2", PRIME_NET2_EVIDENCE=str(args.out / f"{name}.json"),
               PRIME_SHOT_PROOF=args.shot_proof)
    if args.timed_grant_proof != "off":
        env["PRIME_TIMED_GRANT_PROOF"] = args.timed_grant_proof
    if getattr(args, "shot_timeline", False):
        env["PRIME_NCX_TIMELINE"] = "1"
    if getattr(args, "shot_visibility", False) and name != "server":
        env["PRIME_NCX_VISIBILITY"] = str((profile / "ncx-visibility.json").resolve())
    if getattr(args, "presentation_hz", None) is not None and name != "server":
        env["PRIME_NCX_PRESENTATION_HZ"] = str(args.presentation_hz)
    if getattr(args, "scenario", None) in NCX_FIXTURES and name.startswith("peer-"):
        env["PRIME_NCX_FIXTURE_PLAYERS"] = str(args.players)
    if args.evidence_row_limit is not None:
        env["PRIME_NET2_EVIDENCE_ROW_LIMIT"] = str(args.evidence_row_limit)
    if args.timed_replay_facts and name == "server":
        env["PRIME_TIMED_REPLAY_FACTS"] = "1"
    return env


def authority_arguments(args, rotation):
    # Client --record remains independent. Only the explicit archival option
    # enables authority files, whose certificates cannot be produced remotely.
    replay = [] if args.timed_replay_facts else ["-noserverreplays"]
    return ["-server", "-port", str(args.port), "-players", str(args.players + int(args.server_bot)),
            "-nomaster", *replay, "-rotation", str(rotation), "-netdebug", *server_flags(args)]


def current_protocol():
    """Read the checkout contract; historical binaries need --protocol explicitly."""
    source = Path(__file__).resolve().parents[2] / "src/MphRead/Mods/Network/NetProtocol.cs"
    matches = re.findall(r"public const int ProtocolVersion\s*=\s*(\d+)\s*;", source.read_text())
    if len(matches) != 1:
        raise ValueError("cannot determine checkout protocol; supply --protocol explicitly")
    return int(matches[0])


def fault_profile(args):
    """Keep named controls reproducible while allowing independent fault sweeps."""
    names = ("rtt_ms", "jitter_ms", "loss_percent", "reorder_percent", "duplicate_percent")
    values = {name: getattr(args, name) if getattr(args, name, None) is not None else value
              for name, value in zip(names, PROFILES[args.profile])}
    return values


def fault_arguments(args, slot):
    values = fault_profile(args)
    return ["-netlag", f"{values['rtt_ms']}:{values['jitter_ms']}",
            "-netloss", f"{values['loss_percent']:g}%",
            "-netreorder", f"{values['reorder_percent']:g}%",
            "-netduplicate", f"{values['duplicate_percent']:g}%",
            "-netseed", str(args.seed + slot)]


def client_role(args, slot):
    """Native hunter, spectator role and actual shot workload for this slot."""
    if args.scenario in NCX_FIXTURES:
        return "Samus", slot == 2, slot < 2
    hunter = BOMB_HUNTERS.get(args.scenario, "Sylux" if args.scenario == "shockcoil-mixed" else HUNTERS[slot])
    if args.scenario == "alt-morph" and slot == 1:
        hunter = getattr(args, "form_hunter", "Weavel")
    observer = getattr(args, "ai_net_client", None) is None and args.players == 3 and slot == 2 and (
        args.rendered or args.scenario in BOMB_HUNTERS or args.scenario == "alt-morph")
    expects_shots = not observer and args.scenario not in BOMB_HUNTERS and (
        getattr(args, "ai_net_client", None) is not None or args.soak or args.scenario in ("tour", "duel", "shockcoil-all", "shockcoil-mixed") or slot % 2 == 0)
    return hunter, observer, expects_shots


def turnover_arguments(args, slot):
    """Dispatch existing native spectator timers only to the declared client."""
    if getattr(args, "turnover_client_slot", None) != slot:
        return []
    return ["-spectate", str(args.spectate_at), "-rejoin", str(args.rejoin_at)]


def digest(path):
    value = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def stage_build(build, stage):
    """Copy the frozen entry point; retain shared dependency links without expansion."""
    required = ("ProjectPrime.dll", "ProjectPrime.deps.json", "ProjectPrime.runtimeconfig.json")
    for name in required:
        path = build / name
        if path.is_symlink() or not path.is_file():
            raise RuntimeError(f"Frozen build entry point must be a physical file: {name}")
    shutil.copytree(build, stage, symlinks=True)
    return {name: {"sha256": digest(stage / name), "bytes": (stage / name).stat().st_size}
            for name in required}


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def prepare_prime_fixture_packages(args, dotnet, assembly, stage, profile_text, manifest):
    """Cook declared recipes with the staged build's ordinary mapbundle CLI."""
    from fruity_comparator import qualified_fixture
    qualified_fixture(args)
    profile = args.out / "fixture-preparation"
    profile.mkdir()
    exports = profile / "exports"
    exports.mkdir()
    lines = [line for line in profile_text.splitlines() if not line.startswith("Export=")]
    (profile / "paths.txt").write_text("\n".join([*lines, f"Export={exports}", ""]), encoding="utf-8")
    recipes, packages = profile / "recipes", profile / "packages"
    shutil.copytree(args.fixture_maps.resolve(), recipes)
    packages.mkdir()
    preparation = {"scope": "Existing mapbundle CLI in private preparation profile; identical packages copied to every process MapDir",
                   "commands": [], "rawRecipes": [], "packages": []}
    manifest["fixturePreparation"] = preparation
    env = {key: value for key, value in os.environ.items()
           if not key.startswith(("PRIME_", "MPHREAD_", "PROJECT_PRIME_"))}
    env.update(PROJECT_PRIME_USER_DATA=str(profile), ALSOFT_DRIVERS="null")
    for recipe in sorted(recipes.glob("*.json")):
        definition = json.loads(recipe.read_text())
        package = packages / (recipe.stem + ".ppmap")
        command = [dotnet, str(assembly), "-mapdir", str(recipes), "-noupdate", "-noautoupdate",
                   "-mapbundle", definition["name"], "-out", str(package)]
        log = profile / (recipe.stem + ".log")
        row = {"argv": command, "cwd": str(stage), "log": str(log), "exit": None,
               "environment": {key: env[key] for key in ("PROJECT_PRIME_USER_DATA", "ALSOFT_DRIVERS")}}
        preparation["commands"].append(row)
        preparation["rawRecipes"].append({"path": str(recipe), "sha256": digest(recipe)})
        save(args.out / "manifest.json", manifest)
        try:
            with log.open("x", encoding="utf-8") as output:
                result = subprocess.run(command, cwd=stage, env=env, stdin=subprocess.DEVNULL,
                                        stdout=output, stderr=subprocess.STDOUT, timeout=180)
            row["exit"] = result.returncode
        except subprocess.TimeoutExpired as error:
            row["timedOut"] = True
            raise RuntimeError("Prime fixture mapbundle timed out: " + recipe.name) from error
        finally:
            if log.is_file(): row["logSha256"] = digest(log)
            save(args.out / "manifest.json", manifest)
        if row["exit"] != 0 or not package.is_file() or package.stat().st_size == 0:
            raise RuntimeError("Prime fixture mapbundle failed or produced no package: " + recipe.name)
        preparation["packages"].append({"path": str(package), "sha256": digest(package),
                                         "bytes": package.stat().st_size})
        save(args.out / "manifest.json", manifest)
    if len(preparation["packages"]) != len(NCX_FIXTURES):
        raise RuntimeError("Prime fixture preparation did not produce every declared package")
    return preparation["packages"]


def stage_prime_fixture_packages(packages, maps):
    """Every role receives the exact once-cooked bytes, without loose recipes."""
    maps.mkdir()
    copied = []
    for package in packages:
        source = Path(package["path"])
        if digest(source) != package["sha256"]:
            raise RuntimeError("Prepared Prime fixture package changed")
        destination = maps / source.name
        shutil.copy2(source, destination)
        if digest(destination) != package["sha256"]:
            raise RuntimeError("Prime fixture package copy differs")
        copied.append({"path": str(destination), "sha256": package["sha256"]})
    return copied


def prime_fixture_admission(text, expected_slot):
    """Observe the existing authenticated Lobby receipt, never launch order."""
    slots = [int(value) for value in re.findall(
        r"^\[net\] connected in Lobby, slot (\d+)\s*$", text, flags=re.MULTILINE)]
    if not slots:
        return {"status": "WAIT", "expectedSlot": expected_slot}
    return {"status": "PASS" if all(slot == expected_slot for slot in slots) else "FAIL",
            "expectedSlot": expected_slot, "assignedSlots": slots}


def wait_prime_fixture_admission(args, name, expected_slot, peer, server, manifest):
    """Bound fixture startup on ordinary authenticated slot admission only."""
    log = args.out / (name + ".log")
    row = {"status": "WAIT", "expectedSlot": expected_slot, "log": str(log), "timeoutSeconds": 35,
           "scope": "Existing authenticated Lobby admission before next peer launch; no map-ready or drain assertion"}
    manifest.setdefault("fixtureAdmissions", {})[name] = row
    save(args.out / "manifest.json", manifest)
    deadline = time.monotonic() + 35
    reason = "Prime fixture Lobby admission timed out"
    while time.monotonic() < deadline:
        if server.poll() is not None:
            reason = "Prime fixture server exited before peer admission"
            break
        if peer.poll() is not None:
            reason = "Prime fixture peer exited before admission"
            break
        observed = prime_fixture_admission(log.read_text(errors="replace") if log.is_file() else "", expected_slot)
        row.update(observed)
        if observed["status"] == "PASS":
            row["logSha256AtAdmission"] = digest(log)
            save(args.out / "manifest.json", manifest)
            return
        if observed["status"] == "FAIL":
            reason = "Prime fixture assigned slot differs from declared role"
            break
        time.sleep(.1)
    row.update(status="FAIL", reason=reason)
    if log.is_file(): row["logSha256AtFailure"] = digest(log)
    save(args.out / "manifest.json", manifest)
    raise RuntimeError(reason + ": " + name)


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


def run_fruity_control(args):
    """Native engine route in this controller; upstream owns all game behavior."""
    import fruity_comparator as native
    native.validate_options(args)
    if platform.system() != "Darwin":
        raise RuntimeError("The qualified comparator package is macOS-only")
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=False)
    processes, handles, records = [], [], []
    listening, runner_error = False, None
    started = time.monotonic()
    manifest = {"schema": 1, "engine": "fruity-native", "source": args.source,
        "platform": platform.platform(), "python": platform.python_version(),
        "commands": [], "settings": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
        "harnessSha256": {name: digest(Path(__file__).with_name(name))
                          for name in ("run-net2.py", "fruity_comparator.py")},
        "capabilityScope": "Native upstream text harness only; no Prime drain, oracle, pixel or timeline equivalence",
        "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    binary = None
    try:
        package = native.qualify_package(args.build.resolve(), args.comparator_manifest.resolve(), args.source)
        if args.scenario in NCX_FIXTURES: manifest["emulatedFixture"] = native.qualified_fixture(args)
        save(args.out / "comparator-package.json", package)
        manifest["packageManifestSha256"] = digest(args.out / "comparator-package.json")
        binary = native.stage_package(args.build.resolve(), args.out / "bin")
        manifest["binarySha256"] = digest(binary)
        profile_text = args.paths.resolve().read_text(encoding="utf-8-sig")
        assets, isolated_lines, asset_roots = [], [], []
        for line in profile_text.splitlines():
            if "=" not in line:
                isolated_lines.append(line)
                continue
            key, value = line.split("=", 1)
            if key == "Export": continue
            if not value:
                isolated_lines.append(line)
                continue
            root = Path(value)
            if not root.is_absolute() or not root.is_dir():
                raise RuntimeError("Comparator paths.txt needs absolute existing private asset directories")
            if not re.fullmatch(r"[A-Za-z0-9_-]+", key):
                raise RuntimeError("Comparator asset version is not a safe directory label")
            private_assets = args.out / "assets" / key
            shutil.copytree(root, private_assets)
            isolated_lines.append(f"{key}={private_assets}")
            asset_roots.append({"version": key, "source": str(root), "privateCopy": str(private_assets)})
            for file in sorted(root.rglob("*")):
                if file.is_file(): assets.append({"version": key, "path": file.relative_to(root).as_posix(),
                    "size": file.stat().st_size, "sha256": digest(file)})
        if not assets: raise RuntimeError("Comparator assets are unavailable")
        profile_text = "\n".join(isolated_lines) + "\n"
        manifest["assetRoots"] = asset_roots
        save(args.out / "assets.json", assets)
        manifest["assetManifestSha256"] = digest(args.out / "assets.json")
        maps = args.out / "maps"
        if args.fixture_maps:
            shutil.copytree(args.fixture_maps.resolve(), maps)
            manifest["fixtureFiles"] = [{"path": p.relative_to(maps).as_posix(), "sha256": digest(p)}
                                        for p in sorted(maps.rglob("*")) if p.is_file()]
            maps = native.stage_fixture_maps(binary, maps)
            manifest["nativeFixtureRegistration"] = {
                "path": str(maps), "scope": "Private staged app resource maps available before C++ global metadata initialization",
                "files": [{"path": p.relative_to(maps).as_posix(), "sha256": digest(p)}
                          for p in sorted(maps.rglob("*")) if p.is_file()]}
        else: maps.mkdir()
        rotation = args.out / "rotation.txt"
        rotation.write_text("\n".join(f"{room} | Battle | {args.round_minutes} | 999"
                                      for room in args.rooms.split(",")), encoding="utf-8")
        manifest["faultProfile"] = {**fault_profile(args), "scope": "existing native client line simulator",
            "seedRule": "base seed + launch index", "semanticEquivalence": "Not yet certified against Prime"}

        def launch(name, slot=0):
            profile = args.out / name
            profile.mkdir()
            exports = profile / "exports"
            exports.mkdir()
            lines = [line for line in profile_text.splitlines() if not line.startswith("Export=")]
            (profile / "paths.txt").write_text("\n".join([*lines, f"Export={exports}", ""]), encoding="utf-8")
            env = native.environment(profile)
            shots = args.out / ("shots-" + name) if args.rendered and name != "server" else None
            if shots: shots.mkdir()
            command = native.command(args, binary, maps, name, rotation=rotation, slot=slot,
                faults=fault_arguments(args, slot), hitlog=args.out / ("hl-" + name + ".csv"), shots=shots)
            handle = open(args.out / (name + ".log"), "x", encoding="utf-8")
            handles.append(handle)
            process = subprocess.Popen(command, cwd=profile, env=env, stdin=subprocess.DEVNULL,
                                       stdout=handle, stderr=subprocess.STDOUT)
            processes.append(process)
            record = {"name": name, "pid": process.pid, "exit": None, "timedOut": False,
                      "startedSeconds": time.monotonic() - started}
            if name == "server": record["expectedClients"] = args.players
            records.append(record)
            manifest["commands"].append({"name": name, "argv": command, "cwd": str(profile),
                "environment": {k: env[k] for k in ("FRUITY_NCX_USER_DATA", "ALSOFT_DRIVERS")},
                "inheritedGameEnvironment": "cleared PRIME_*, MPHREAD_*, PROJECT_PRIME_*, FRUITY_*, DYLD_*"})
            save(args.out / "manifest.json", manifest)
            print("[fruity-native] launched " + name, flush=True)
            return process, record

        server, server_record = launch("server")
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            if server.poll() is not None: raise RuntimeError("Native server exited during startup")
            if "listening on UDP" in (args.out / "server.log").read_text(errors="replace"):
                listening = True
                break
            time.sleep(.1)
        if not listening:
            server_record["timedOut"] = True
            raise RuntimeError("Native server startup timeout")
        if args.fixture_maps:
            scene_deadline = time.monotonic() + 90
            while time.monotonic() < scene_deadline:
                if server.poll() is not None: raise RuntimeError("Native server exited before fixture scene loaded")
                if native.scene_started((args.out / "server.log").read_text(errors="replace")):
                    manifest["nativeFixtureSceneStart"] = {"observed": True,
                        "marker": "[server] this server runs the match itself",
                        "scope": "Initial native scene load completed before client launch; no drain assertion"}
                    break
                time.sleep(.1)
            else:
                server_record["timedOut"] = True
                raise RuntimeError("Native fixture scene startup timeout")
        time.sleep(2)
        clients = []
        for slot in range(args.players):
            clients.append((*launch("peer-" + str(slot), slot), time.monotonic() + args.seconds + 120))
            time.sleep(1)
        while any(process.poll() is None for process, _, _ in clients):
            if server.poll() is not None: raise RuntimeError("Native server exited while clients were running")
            for process, record, deadline in clients:
                if process.poll() is None and time.monotonic() > deadline:
                    record["timedOut"] = True
                    stop(process)
            time.sleep(.1)
        manifest["shutdown"] = "Clients exited naturally; server terminated for cleanup only. No drain assertion."
    except Exception as error:
        runner_error = type(error).__name__ + ": " + str(error)
        manifest["runnerError"] = runner_error
    finally:
        for process, record in zip(processes, records):
            if process.poll() is None: stop(process)
            record["exit"] = process.returncode
        for handle in handles: handle.close()
        texts = {record["name"]: (args.out / (record["name"] + ".log")).read_text(errors="replace") for record in records}
        summary = native.summarize(records, texts, listening, runner_error)
        if binary is not None:
            manifest["binaryAfterSha256"] = digest(binary)
            if manifest.get("binarySha256") != manifest["binaryAfterSha256"]:
                summary["status"] = "FAIL"
                summary["failures"].append("Running comparator binary changed")
            try:
                native.qualify_package(args.build.resolve(), args.comparator_manifest.resolve(), args.source)
                manifest["runtimeAfterStatus"] = "MATCH: declared binary, adapter and loaded non-system libraries"
            except (OSError, ValueError, KeyError) as error:
                manifest["runtimeAfterStatus"] = "FAIL: " + str(error)
                summary["status"] = "FAIL"
                summary["failures"].append("Comparator package/runtime changed during control")
        manifest["processes"] = records
        manifest["elapsedSeconds"] = time.monotonic() - started
        manifest["outputs"] = [{"path": p.relative_to(args.out).as_posix(), "sha256": digest(p)}
                               for p in sorted(args.out.glob("*.log"))] + [
                              {"path": p.relative_to(args.out).as_posix(), "sha256": digest(p)}
                               for p in sorted(args.out.glob("hl-*.csv"))]
        save(args.out / "manifest.json", manifest)
        save(args.out / "summary.json", summary)
        print(json.dumps({"status": summary["status"], "udpControlStatus": summary["udpControlStatus"],
                          "nativeHarnessStatus": summary["nativeHarnessStatus"], "out": str(args.out)}), flush=True)
    return 0 if summary["status"] == "PASS" else 1


def run(args):
    if getattr(args, "engine", "prime") == "fruity-native":
        return run_fruity_control(args)
    if platform.system() not in ("Darwin", "Windows"):
        raise RuntimeError("This campaign is restricted to Mac and Windows")
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=False)
    stage = args.out / "bin"
    staged_entry = stage_build(args.build.resolve(), stage)
    if args.pacing_lobby_client:
        args.pacing_owner_token = str(uuid.UUID(int=0))
        # The frozen nettest metadata supplies the unchanged client's complete
        # dependency graph; only this isolated entry DLL is replaced in this run.
        for name in ("nettest.dll", "nettest.deps.json", "nettest.runtimeconfig.json"):
            if not (stage / name).is_file():
                raise RuntimeError("Pacing route requires physical frozen nettest loader metadata")
        shutil.copy2(args.pacing_lobby_client.resolve(), stage / "nettest.dll")
    assembly = stage / "ProjectPrime.dll"
    if not assembly.exists():
        raise RuntimeError("Build directory has no ProjectPrime.dll")
    profile_text = args.paths.resolve().read_text(encoding="utf-8-sig")
    if args.scenario in NCX_FIXTURES:
        isolated = []
        for line in profile_text.splitlines():
            if "=" not in line or not line.split("=", 1)[1] or line.startswith("Export="):
                isolated.append(line)
                continue
            key, value = line.split("=", 1)
            root = Path(value)
            if not re.fullmatch(r"[A-Za-z0-9_-]+", key) or not root.is_absolute() or not root.is_dir():
                raise RuntimeError("NCX fixture assets require safe version keys and absolute existing directories")
            destination = args.out / "fixture-assets" / key
            shutil.copytree(root, destination)
            isolated.append(f"{key}={destination}")
        profile_text = "\n".join(isolated) + "\n"
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
        "assemblySha256": digest(assembly), "stagedEntry": staged_entry,
        "assetManifestSha256": digest(args.out / "assets.json"),
        "harnessSha256": {name: digest(Path(__file__).with_name(name)) for name in
                         (("run-net2.py","net2_evidence.py","bomb-audit.py") if args.scenario in BOMB_HUNTERS else ("run-net2.py","net2_evidence.py"))},
        "settings": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
        "faultProfile": {**fault_profile(args), "scope": "each client, inbound and outbound",
                         "jitterSemantics": "nonnegative extra hold per direction, not symmetric plus/minus",
                         "seedRule": "base seed + client launch index"},
        "commands": [], "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    if args.pacing_lobby_client:
        manifest["pacingLobbyClient"] = {"sha256": digest(stage / "nettest.dll"),
            "mode": "Eight native AI clients; authenticated lobby UpdateMatch enables powerups on both sources",
            "sourcePath": str(args.pacing_lobby_client.resolve()),
            "loaderMetadata": {name: digest(stage / name) for name in ("nettest.deps.json", "nettest.runtimeconfig.json")}}
    if args.ai_net_client is not None:
        manifest["harnessSha256"]["net_ai_evidence.py"] = digest(Path(__file__).with_name("net_ai_evidence.py"))
    if args.server_performance:
        manifest["harnessSha256"]["net13_performance.py"] = digest(Path(__file__).with_name("net13_performance.py"))
    if args.shot_timeline:
        manifest["harnessSha256"]["ncx_timeline.py"] = digest(Path(__file__).with_name("ncx_timeline.py"))
    if args.shot_visibility:
        manifest["shotVisibility"] = {"scope": "Intrusive same-prepared-frame render exclusion/readback, actual camera",
            "timingComparability": "DISQUALIFIED: extra prepared renders/readbacks; do not compare CPU timing",
            "flightIsAuthorityConfirmation": False}
    if args.presentation_hz is not None:
        manifest["presentationRate"] = {"requestedHz": args.presentation_hz, "observedHz": "UNAVAILABLE",
            "scope": "Requested rendered-client cadence only; actual observed rate must be measured separately; simulation remains 60 Hz"}
    if args.scenario in NCX_FIXTURES:
        from fruity_comparator import qualified_fixture
        manifest["harnessSha256"]["fruity_comparator.py"] = digest(Path(__file__).with_name("fruity_comparator.py"))
        manifest["emulatedFixture"] = qualified_fixture(args)
    save(args.out / "manifest.json", manifest)
    processes, handles, records = [], [], []
    server = None
    started = time.monotonic()
    fixture_packages = None

    def launch(name, extra, duration, expects_shots=True):
        profile = args.out / name
        profile.mkdir()
        exports = profile / "exports"
        exports.mkdir()
        isolated_paths = [line for line in profile_text.splitlines() if not line.startswith("Export=")]
        (profile / "paths.txt").write_text("\n".join([*isolated_paths, f"Export={exports}", ""]), encoding="utf-8")
        maps = profile / "empty-maps"
        if args.scenario in NCX_FIXTURES:
            copies = stage_prime_fixture_packages(fixture_packages, maps)
            manifest.setdefault("fixturePackageCopies", {})[name] = copies
        else: maps.mkdir()
        # Do not silently inherit a developer's experimental/test flags into a
        # supposedly reproducible control. Keep ordinary OS/runtime environment.
        env = child_environment(args, name, profile)
        if args.server_bot and name == "server":
            env["PRIME_NET_AI_SERVER_BOT"] = str(args.players)
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
        entry = assembly
        if args.pacing_lobby_client and name != "server":
            entry = stage / "nettest.dll"
            env["PRIME_PACING_EXPECTED_CLIENT_SHA256"] = digest(assembly)
            env["PRIME_PACING_LOBBY_RECEIPT"] = str(args.out / f"{name}.lobby.json")
        command = [dotnet, str(entry), "-mapdir", str(maps), "-noupdate", "-noautoupdate", *extra]
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
        for key in ("PRIME_TIMED_GRANT_PROOF", "PRIME_TIMED_REPLAY_FACTS", "PRIME_NET2_EVIDENCE_ROW_LIMIT",
                    "PRIME_NCX_TIMELINE", "PRIME_NCX_VISIBILITY", "PRIME_NCX_PRESENTATION_HZ", "PRIME_NCX_FIXTURE_PLAYERS"):
            if key in env:
                manifest["commands"][-1]["environment"][key] = env[key]
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
        if args.server_bot and name == "server":
            manifest["commands"][-1]["environment"]["PRIME_NET_AI_SERVER_BOT"] = env["PRIME_NET_AI_SERVER_BOT"]
        if args.server_performance and name == "server":
            manifest["commands"][-1]["environment"]["PRIME_SERVER_PERF_OUTPUT"] = env["PRIME_SERVER_PERF_OUTPUT"]
        if args.pacing_lobby_client and name != "server":
            for key in ("PRIME_PACING_EXPECTED_CLIENT_SHA256", "PRIME_PACING_LOBBY_RECEIPT"):
                manifest["commands"][-1]["environment"][key] = env[key]
        save(args.out / "manifest.json", manifest)
        return process, record

    try:
        if args.scenario in NCX_FIXTURES:
            fixture_packages = prepare_prime_fixture_packages(args, dotnet, assembly, stage, profile_text, manifest)
        server, server_record = launch("server", authority_arguments(args, rotation), args.seconds, False)
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

        def client(slot, name, seconds):
            # Only affinity Shock Coil drains health. Combat roles use assigned
            # network slots, which can differ from concurrent launch order.
            hunter, observer, expects = client_role(args, slot)
            extra = ["-netcheck", "127.0.0.1", "-port", str(args.port), "-name", name,
                "-hunter", hunter, "-seconds", str(seconds), "-size", "480x270",
                *fault_arguments(args, slot)]
            if args.ai_net_client is not None:
                extra += ["-ai-net-client", str(args.ai_net_client), "-ai-seed", str(args.ai_seed + slot)]
            if args.pacing_lobby_client:
                extra += ["-pacing-room", args.rooms, "-pacing-players", str(args.players)]
                if slot == 0:
                    extra += ["-pacing-owner", "-pacing-token", args.pacing_owner_token]
            if not args.rendered:
                extra.append("-nographics")
            if args.record:
                extra.append("-recorddemo")
            if args.rendered and args.players == 3:
                shots = args.out / f"shots-{name}"
                shots.mkdir()
                extra += ["-shots", str(shots)]
            if args.ai_net_client is None and args.scenario != "tour":
                extra += ["-hitrig", "ncx-observe" if observer and args.scenario in NCX_FIXTURES else args.scenario]
            if observer and args.scenario not in NCX_FIXTURES:
                extra += ["-spectate", "0"]
            if args.soak and slot == args.players - 2:
                extra += ["-spectate", "90", "-rejoin", "150"]
            extra += turnover_arguments(args, slot)
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
            if args.scenario in NCX_FIXTURES:
                wait_prime_fixture_admission(args, f"peer-{slot}", slot, clients[-1][0], server, manifest)
            else:
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
        if args.shot_timeline:
            from ncx_timeline import audit as audit_timeline
            reports = {}
            for record in records:
                try:
                    reports[record["name"]] = json.loads((args.out / (record["name"] + ".json")).read_text())
                except (OSError, ValueError):
                    reports[record["name"]] = {}
            timeline = audit_timeline(reports)
            save(args.out / "ncx-timeline.json", timeline)
            # Gameplay/drain acceptance and measurement coverage are distinct.
            # A partial timeline, a legal miss or absent pixels never becomes
            # a fictitious gameplay PASS, payment fault or legal-hit denominator.
            summary["shotTimeline"] = {"status": timeline["status"], "scope": timeline["scope"],
                "counts": timeline["counts"], "reportSha256": digest(args.out / "ncx-timeline.json")}
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
        if args.ai_net_client is not None:
            from net_ai_evidence import audit_campaign
            ai = audit_campaign(args.out, records, summary)
            save(args.out / "ai-summary.json", ai)
            summary["aiClient"] = ai
            if ai["status"] != "PASS":
                summary["status"] = ai["status"] if summary["status"] == "PASS" else summary["status"]
                summary["failures"].extend(ai["failures"])
                summary["incomplete"].extend(ai["incomplete"])
        save(args.out / "summary.json", summary)
        print(json.dumps({"status": summary["status"], "out": str(args.out), "failures": summary["failures"], "incomplete": summary["incomplete"]}), flush=True)
    return 0 if summary["status"] == "PASS" else 1


def parse_args(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--build", type=Path, required=True)
    p.add_argument("--paths", type=Path, required=True)
    p.add_argument("--source", required=True, help="Exact source SHA plus dirty diff hash if applicable")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--engine", choices=("prime", "fruity-native"), default="prime",
                   help="Explicit comparator mode; Prime dispatch and acceptance remain the default")
    p.add_argument("--comparator-manifest", type=Path,
                   help="Successful package-qualified Fruity smoke manifest with binary/runtime/adapter hashes")
    p.add_argument("--fixture-maps", type=Path, help="Explicit private native fixture map definitions; not certified matched geometry")
    p.add_argument("--fruity-hitrig", choices=("wells:powerbeam", "lanes:powerbeam", "strafe:powerbeam"),
                   help="Existing native fixed-weapon rig; stationary/wells/lanes depend on separately qualified map definitions")
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--protocol", type=int, help="Expected evidence protocol; defaults to checkout, set explicitly for historical binaries")
    p.add_argument("--shot-proof", choices=("off", "shadow", "enforce"), default="shadow",
                   help="Record and apply the explicit N4 admission mode")
    p.add_argument("--timed-grant-proof", choices=("off", "shadow"), default="off",
                   help="Explicit NET-01 timed certificate observation; independent of legacy --shot-proof")
    p.add_argument("--timed-replay-facts", action="store_true",
                   help="Enable authority archival timed facts and server recordings; requires timed Shadow")
    p.add_argument("--evidence-row-limit", type=int,
                   help="Opt-in diagnostic row budget262144..1048576; does not enable soak gameplay")
    p.add_argument("--players", type=int, choices=(2, 3, 4, 8), default=2)
    p.add_argument("--ai-net-client", type=int, nargs="?", const=2, choices=range(4),
                   help="Drive ordinary local clients with native tactical AI, level 0..3")
    p.add_argument("--ai-seed", type=int, help="Independent gameplay AI seed; per-client seed adds launch index")
    p.add_argument("--server-bot", action="store_true", help="Add one normal authority bot to an AI campaign")
    p.add_argument("--powerups", action="store_true",
                   help="Enable native power-up pickups on the server; disabled by default")
    p.add_argument("--pacing-lobby-client", type=Path,
                   help="Isolated compiled friend-assembly native client entry; enables powerups through authenticated lobby commands on main and A1")
    p.add_argument("--seconds", type=float, default=90)
    p.add_argument("--drain-seconds", type=float, default=0,
                   help="Opt-in bounded quiescence/frontier/empty-reliable-queue barrier; zero preserves legacy shutdown")
    p.add_argument("--port", type=int, default=27991)
    p.add_argument("--seed", type=int, default=8128)
    p.add_argument("--profile", choices=PROFILES, default="lan")
    p.add_argument("--rtt-ms", type=int, help="Override profile added round trip, 0..10000 ms")
    p.add_argument("--jitter-ms", type=int, help="Override profile extra hold per direction, 0..5000 ms")
    for fault in ("loss", "reorder", "duplicate"):
        p.add_argument(f"--{fault}-percent", type=float, help=f"Override profile {fault}, 0..100 percent")
    p.add_argument("--scenario", choices=("tour", "duel", "shockcoil", "shockcoil-all", "shockcoil-mixed", "powerbeam", "missile", "self-moving", "self-wall", "alt-morph", *NCX_FIXTURES, *BOMB_HUNTERS), default="tour")
    p.add_argument("--form-hunter", choices=HUNTERS[:-1], default=argparse.SUPPRESS,
                   help="Explicit alt-morph target Hunter in slot1; omitted preserves Weavel. Dispatch only; existing form audit remains Weavel-specific.")
    p.add_argument("--observer-delay", type=float, default=0,
                   help="Delay the third spectator in a short three-client bomb or alt-morph campaign")
    p.add_argument("--turnover-client-slot", type=int, default=argparse.SUPPRESS,
                   help="Opt-in native headless tour client launch slot for spectator/rejoin dispatch; requires both times")
    p.add_argument("--spectate-at", type=float, default=argparse.SUPPRESS,
                   help="Existing native spectator timer in seconds; requires --turnover-client-slot and --rejoin-at")
    p.add_argument("--rejoin-at", type=float, default=argparse.SUPPRESS,
                   help="Existing native rejoin timer in seconds; requires --turnover-client-slot and --spectate-at")
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
    p.add_argument("--shot-timeline", action="store_true",
                   help="Opt-in bounded full-key process clocks and distinct source/payment/receipt observations; not a pixel or legal-hit oracle")
    p.add_argument("--shot-visibility", action="store_true",
                   help="Intrusive actual-view prepared-frame pixel probe; requires --rendered --shot-timeline; disqualifies CPU timings")
    p.add_argument("--presentation-hz", type=int, choices=(30, 60, 120, 144, 240, 360, 500),
                   help="Prime rendered-client requested cadence; fixed 60 Hz simulation; observed rate is separate evidence")
    p.add_argument("--record", action="store_true")
    p.add_argument("--soak", action="store_true")
    args = p.parse_args(argv)
    if args.shot_visibility and (not args.rendered or not args.shot_timeline):
        p.error("--shot-visibility requires --rendered and --shot-timeline; timing values are not CPU-comparable")
    if args.presentation_hz is not None and (not args.rendered or not args.shot_timeline):
        p.error("--presentation-hz requires Prime --rendered --shot-timeline; a requested rate is not an observed rate")
    if args.engine == "prime" and any((args.comparator_manifest, args.fruity_hitrig)):
        p.error("Comparator options require --engine fruity-native")
    if args.fixture_maps and args.engine == "prime" and args.scenario not in NCX_FIXTURES:
        p.error("Prime --fixture-maps requires an explicit NCX emulated scenario")
    if args.scenario in NCX_FIXTURES:
        if args.soak or args.ai_net_client is not None or args.powerups or args.server_bot or args.bomb_triangle_fixture:
            p.error("NCX emulated fixtures do not combine with other gameplay workload variants")
        from fruity_comparator import qualified_fixture
        try: qualified_fixture(args)
        except (OSError, ValueError, KeyError) as error: p.error(str(error))
    if args.engine == "fruity-native":
        from fruity_comparator import validate_options
        try: validate_options(args)
        except ValueError as error: p.error(str(error))
    if hasattr(args, "form_hunter") and args.scenario != "alt-morph":
        p.error("--form-hunter requires --scenario alt-morph")
    if args.evidence_row_limit is not None and not 262144 <= args.evidence_row_limit <= 1048576:
        p.error("--evidence-row-limit must be between262144 and1048576")
    if args.timed_replay_facts and args.timed_grant_proof != "shadow":
        p.error("--timed-replay-facts requires --timed-grant-proof shadow")
    if args.pacing_lobby_client and (not args.pacing_lobby_client.is_file() or args.players != 8
            or args.ai_net_client is None or not args.powerups or args.server_bot or args.rendered
            or args.soak or args.observer_delay or args.scenario != "tour" or "," in args.rooms):
        p.error("--pacing-lobby-client requires an existing entry DLL, eight native AI clients, --powerups, one room and no rendered/bot/soak/scripted variants")
    if args.ai_net_client is None and (args.server_bot or args.ai_seed is not None):
        p.error("--server-bot and --ai-seed require --ai-net-client")
    if args.ai_net_client is not None:
        if args.soak or args.scenario != "tour" or args.observer_delay or args.bomb_triangle_fixture:
            p.error("AI clients cannot use scripted tour/HitRig/soak fixtures or delayed spectators")
        if args.server_bot and args.players + 1 > 8:
            p.error("AI clients plus server bot must fit eight slots")
        args.scenario = "ai"
        args.ai_seed = 1729 if args.ai_seed is None else args.ai_seed
        if not 1 <= args.ai_seed <= 4294967295 - (args.players - 1):
            p.error("AI seeds must be nonzero uint values for every client")
    if args.protocol is None:
        try:
            args.protocol = current_protocol()
        except (OSError, ValueError) as error:
            p.error(str(error))
    if not 1 <= args.protocol <= 255:
        p.error("--protocol must fit the Hello protocol byte (1..255)")
    for name, maximum in (("rtt_ms", 10000), ("jitter_ms", 5000),
                          ("loss_percent", 100), ("reorder_percent", 100), ("duplicate_percent", 100)):
        value = getattr(args, name)
        if value is not None and (not math.isfinite(value) or not 0 <= value <= maximum):
            p.error(f"--{name.replace('_', '-')} must be finite and between 0 and {maximum}")
    if not -2147483648 <= args.seed <= 2147483647 - (args.players - 1):
        p.error("--seed and all client seeds must fit signed 32-bit integers")
    if not all(math.isfinite(value) for value in
               (args.seconds, args.round_minutes, args.drain_seconds, args.observer_delay)):
        p.error("durations must be finite")
    if args.seconds <= 0 or args.round_minutes <= 0 or not 1024 <= args.port <= 65535:
        p.error("duration, round length and port must be positive and valid")
    turnover_fields = ("turnover_client_slot", "spectate_at", "rejoin_at")
    if any(hasattr(args, field) for field in turnover_fields):
        if not all(hasattr(args, field) for field in turnover_fields):
            p.error("--turnover-client-slot, --spectate-at and --rejoin-at must be supplied together")
        if (args.ai_net_client is not None or args.soak or args.rendered
                or args.scenario != "tour" or args.observer_delay):
            p.error("client turnover requires a headless non-AI tour without soak, HitRig or delayed observers")
        if not 0 <= args.turnover_client_slot < args.players:
            p.error("--turnover-client-slot must identify an actual launched client slot")
        if (not math.isfinite(args.spectate_at) or not math.isfinite(args.rejoin_at)
                or not 0 <= args.spectate_at < args.rejoin_at < args.seconds):
            p.error("turnover times must be finite and satisfy 0 <= spectate-at < rejoin-at < seconds")
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
