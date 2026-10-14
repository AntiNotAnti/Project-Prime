#!/usr/bin/env python3
"""Portable focused friend benchmark against an explicitly qualified physical client DLL.

No ProjectReference, source worktree edit, source client rebuild, or receipt replacement.
Every invocation gets a new output directory, preserving all failures and repeats.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def portable(value, labels):
    """Replace deployment paths; generated sanitized derivatives retain separate digests."""
    if isinstance(value, dict):
        return {portable(k, labels): portable(v, labels) for k, v in value.items()}
    if isinstance(value, list):
        return [portable(v, labels) for v in value]
    if isinstance(value, str):
        for path, label in sorted(labels.items(), key=lambda item: -len(item[0])):
            value = value.replace(path, label)
        value = re.sub(r"/(?:Users|home|private|tmp|var|opt|Applications|Volumes|usr|Library)/[^\s\"<>]+", "<external-host-path>", value)
    return value


def sanitize_log(path, labels):
    original = path.read_bytes()
    original_digest = hashlib.sha256(original).hexdigest()
    derivative = path.with_name(path.stem + ".sanitized" + path.suffix)
    derivative.write_text(portable(original.decode("utf-8", errors="replace"), labels))
    path.unlink()
    return {"kind": "sanitized_text_derivative", "original_sha256": original_digest,
            "original_bytes": len(original), "derivative": derivative.name,
            "derivative_sha256": sha(derivative), "policy": "input path placeholders and external host paths removed; content otherwise retained"}


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()


def source_snapshot(repo):
    return {"head": git(repo, "rev-parse", "HEAD"), "tree": git(repo, "rev-parse", "HEAD^{tree}"),
            "status_porcelain": git(repo, "status", "--porcelain=v1", "--untracked-files=all")}


def group_exists(pgid):
    try:
        os.killpg(pgid, 0)
        return True
    except ProcessLookupError:
        return False


def run_owned(command, cwd, log, timeout, env):
    """Close only this invocation's new process group; preserve timeout/failure evidence."""
    cpu0 = resource.getrusage(resource.RUSAGE_CHILDREN)
    started = time.monotonic()
    with open(log, "wb") as f:
        proc = subprocess.Popen(command, cwd=cwd, stdout=f, stderr=subprocess.STDOUT,
                                env=env, start_new_session=True)
        pgid = proc.pid
        timed_out = False
        interrupted = False
        try:
            code = proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(pgid, signal.SIGTERM)
            try:
                code = proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(pgid, signal.SIGKILL)
                code = proc.wait(timeout=10)
        except (KeyboardInterrupt, SystemExit):
            interrupted = True
            os.killpg(pgid, signal.SIGTERM)
            try:
                code = proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(pgid, signal.SIGKILL)
                code = proc.wait(timeout=10)
        leftover = group_exists(pgid)
        if leftover:
            os.killpg(pgid, signal.SIGTERM)
            deadline = time.monotonic() + 5
            while group_exists(pgid) and time.monotonic() < deadline:
                time.sleep(.1)
            if group_exists(pgid):
                os.killpg(pgid, signal.SIGKILL)
                time.sleep(.1)
        closed = not group_exists(pgid)
    cpu1 = resource.getrusage(resource.RUSAGE_CHILDREN)
    return {"command": command, "cwd": str(cwd), "pid": proc.pid, "owned_pgid": pgid,
            "returncode": code, "timeout": timed_out, "interrupted": interrupted, "leftover_group_after_parent_exit": leftover,
            "owned_group_closed": closed, "wall_seconds": time.monotonic() - started,
            "children_cpu_seconds": cpu1.ru_utime + cpu1.ru_stime - cpu0.ru_utime - cpu0.ru_stime,
            "log": str(log)}


def main():
    def stop_owned_run(signum, frame):
        raise KeyboardInterrupt("Received signal " + str(signum))
    signal.signal(signal.SIGTERM, stop_owned_run)
    parser = argparse.ArgumentParser()
    parser.add_argument("--kind", choices=("net03", "net04"), required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--donor", type=Path, required=True)
    parser.add_argument("--sdk", type=Path, required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--qualified-revision", required=True)
    parser.add_argument("--qualification-receipt", type=Path, required=True)
    parser.add_argument("--classifier-revision")
    parser.add_argument("--client-sha", required=True)
    parser.add_argument("--client-mvid", required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--execution-slot-released", action="store_true", required=True)
    args = parser.parse_args()
    for name in ("repo", "runtime", "donor", "sdk", "out", "qualification_receipt"):
        setattr(args, name, getattr(args, name).resolve())
    if not re.fullmatch(r"[0-9a-fA-F]{64}", args.client_sha):
        raise ValueError("Require qualified client SHA256")
    args.client_sha = args.client_sha.lower()
    prep = Path(__file__).resolve().parent
    if args.kind == "net03" and not args.classifier_revision:
        raise ValueError("NET03 requires --classifier-revision to identify measured classifier source")
    if args.kind == "net04" and args.head != args.qualified_revision:
        raise ValueError("NET04 checkout HEAD must equal explicitly qualified client revision")
    try:
        args.out.relative_to(args.repo)
        raise ValueError("Output must be outside the source checkout so clean-source checks remain meaningful")
    except ValueError as error:
        if str(error).startswith("Output must"):
            raise
    args.out.mkdir(parents=True, exist_ok=False)
    labels = {str(args.out): "$EVIDENCE", str(args.repo): "$SOURCE", str(args.runtime): "$QUALIFIED_RUNTIME",
              str(args.donor): "$DEPENDENCIES", str(args.sdk): "$DOTNET", str(prep): "$BENCHMARK_SOURCE",
              str(args.qualification_receipt): "$QUALIFICATION_RECEIPT"}
    receipt = {"schema": 1, "scope": args.kind + " focused pure archival/classifier operations; native/fullfixture identity recorded only",
               "utc_started": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "args": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
               "success": False, "processes": [], "logs": [],
               "portability": "paths use CLI input placeholders; logs are explicitly sanitized text derivatives with independent original/derivative hashes"}
    receipt_path = args.out / "receipt.json"
    binaries_before = {}
    try:
        before = source_snapshot(args.repo)
        receipt["source_before"] = before
        if before["head"] != args.head or before["status_porcelain"]:
            raise RuntimeError("Current checkout must match supplied clean HEAD; frozen runtime identity is checked separately")
        qualification = json.loads(args.qualification_receipt.read_text())
        qualification = qualification.get("payload", qualification)
        qualified_source = qualification.get("sourceBefore", qualification.get("source_before", {}))
        if qualified_source.get("commit", qualified_source.get("head")) != args.qualified_revision:
            raise RuntimeError("Qualification receipt source does not match explicit qualified revision")
        qualified_client = qualification.get("actualLoadedClient", qualification.get("actualCompiledClient", {}))
        if qualified_client.get("sha256") != args.client_sha or qualified_client.get("mvid", "").lower() != args.client_mvid.lower():
            raise RuntimeError("Qualification receipt client SHA/MVID differs from explicit measured client")
        qualified_status = qualification.get("status", "")
        if not qualified_status.startswith("PASS"):
            raise RuntimeError("Require a passing full source qualification receipt")
        receipt["qualification"] = {"receipt_sha256": sha(args.qualification_receipt),
            "qualified_revision": args.qualified_revision, "recorded_source": qualified_source,
            "recorded_actual_client": qualified_client, "recorded_status": qualified_status}
        if args.kind == "net03":
            classifier_path = "src/MphRead/Mods/Network/NetTimedSourceCapabilities.cs"
            frozen_bytes = subprocess.check_output(["git", "-C", str(args.repo), "show", args.classifier_revision + ":" + classifier_path])
            classifier_before = sha(args.repo / classifier_path)
            receipt["classifier_source"] = {"path": classifier_path, "frozen_revision": args.classifier_revision,
                                            "frozen_sha256": hashlib.sha256(frozen_bytes).hexdigest(), "current_sha256_before": classifier_before}
            if classifier_before != receipt["classifier_source"]["frozen_sha256"]:
                raise RuntimeError("Current classifier source differs from frozen measured revision")
        client = args.runtime / "ProjectPrime.dll"
        fixture = args.runtime / "nettest.dll"
        for path in (client, fixture):
            if not path.is_file():
                raise FileNotFoundError(path)
            binaries_before[str(path)] = sha(path)
        receipt["qualified_binaries_before"] = binaries_before
        if sha(client) != args.client_sha:
            raise RuntimeError("Qualified actual client SHA differs before benchmark")
        project_dir = args.out / "external-entry-source"
        project_dir.mkdir()
        shutil.copy2(prep / args.kind / "Program.cs", project_dir / "Program.cs")
        shutil.copy2(Path(__file__), args.out / "run_benchmark.py")
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        props = ET.SubElement(project, "PropertyGroup")
        for k, v in {"OutputType": "Exe", "TargetFramework": "net10.0", "Nullable": "enable",
                     "AssemblyName": "nettest", "EnableDefaultCompileItems": "false", "UseAppHost": "false",
                     "UseSharedCompilation": "false", "EnableNETAnalyzers": "false", "Deterministic": "true"}.items():
            ET.SubElement(props, k).text = v
        items = ET.SubElement(project, "ItemGroup")
        ET.SubElement(items, "Compile", Include="Program.cs")
        dependency_paths = [p for p in sorted(args.donor.glob("*.dll"))
                            if p.name not in {"ProjectPrime.dll", "nettest.dll"}]
        if not any(p.name == "OpenTK.Mathematics.dll" for p in dependency_paths):
            raise RuntimeError("Shared dependency donor lacks OpenTK.Mathematics.dll")
        receipt["references"] = []
        for p in [client] + dependency_paths:
            reference = ET.SubElement(items, "Reference", Include=p.stem)
            ET.SubElement(reference, "HintPath").text = str(p)
            ET.SubElement(reference, "Private").text = "true"
            receipt["references"].append({"path": str(p), "sha256": sha(p), "role": "qualified_actual_client" if p == client else "shared_dependency_donor"})
        proj = project_dir / "benchmark.csproj"
        ET.indent(project)
        ET.ElementTree(project).write(proj, encoding="utf-8", xml_declaration=True)
        bin_dir = args.out / "runtime"
        env = dict(os.environ)
        env.update({"DOTNET_ROOT": str(args.sdk.parent), "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                    "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER": "1", "MSBUILDDISABLENODEREUSE": "1"})
        build = run_owned([str(args.sdk), "build", str(proj), "-c", "Release", "-o", str(bin_dir),
                           "--nologo", "-p:UseSharedCompilation=false", "-nodeReuse:false"],
                          project_dir, args.out / "external-entry-build.log", 240, env)
        receipt["processes"].append(build)
        receipt["logs"].append(sanitize_log(args.out / "external-entry-build.log", labels))
        if build["returncode"] != 0 or build["timeout"] or build["interrupted"] or not build["owned_group_closed"]:
            raise RuntimeError("External entry build failed or owned process group not closed")
        if sha(bin_dir / "ProjectPrime.dll") != args.client_sha:
            raise RuntimeError("External entry copied wrong actual client")
        receipt["thin_entry_source_sha256"] = sha(project_dir / "Program.cs")
        receipt["thin_entry_sha256"] = sha(bin_dir / "nettest.dll")
        receipt["actual_client_runtime_sha256"] = sha(bin_dir / "ProjectPrime.dll")
        result_path = args.out / "results.json"
        run = run_owned([str(args.sdk), str(bin_dir / "nettest.dll"), str(result_path),
                         args.client_sha, args.client_mvid, str(fixture)],
                        bin_dir, args.out / "benchmark-raw.log", 240, env)
        receipt["processes"].append(run)
        receipt["logs"].append(sanitize_log(args.out / "benchmark-raw.log", labels))
        if run["returncode"] != 0 or run["timeout"] or run["interrupted"] or not run["owned_group_closed"]:
            raise RuntimeError("Benchmark failed or owned process group not closed")
        result = json.loads(result_path.read_text())
        receipt["identities"] = {k: result[k] for k in ("QualifiedActualClient", "ThinBenchmarkEntry", "FullNativeFixtureIdentityOnly")}
        receipt["source_after"] = source_snapshot(args.repo)
        receipt["qualified_binaries_after"] = {path: sha(path) for path in binaries_before}
        receipt["source_clean_stable"] = receipt["source_after"] == before and not receipt["source_after"]["status_porcelain"]
        if args.kind == "net03":
            receipt["classifier_source"]["current_sha256_after"] = sha(args.repo / classifier_path)
            receipt["classifier_source_unchanged"] = receipt["classifier_source"]["current_sha256_after"] == classifier_before
        receipt["qualified_binaries_unchanged"] = receipt["qualified_binaries_after"] == binaries_before
        if not receipt["source_clean_stable"] or not receipt["qualified_binaries_unchanged"] or not receipt.get("classifier_source_unchanged", True):
            raise RuntimeError("Qualified source or binaries changed during benchmark")
        receipt["success"] = True
        print(json.dumps({"success": True, "receipt": str(receipt_path), "results": str(result_path),
                          "source_head": before["head"], "cases": len(result["Cases"])}))
    except Exception as error:
        receipt["error"] = f"{type(error).__name__}: {error}"
        try:
            receipt["source_after"] = source_snapshot(args.repo)
            receipt["qualified_binaries_after"] = {path: sha(path) for path in binaries_before}
        except Exception as post_error:
            receipt["postcheck_error"] = f"{type(post_error).__name__}: {post_error}"
        print(receipt["error"], file=sys.stderr)
        return 1
    finally:
        receipt["utc_ended"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        receipt_path.write_text(json.dumps(portable(receipt, labels), indent=2) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
