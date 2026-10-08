#!/usr/bin/env python3
"""Build the actual opt-in client project from a live-source snapshot."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--work", type=Path, help="New or empty directory; never the checkout")
    parser.add_argument("--snapshot-only", action="store_true")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--server", action="store_true", help="Check the dedicated server boundary in its own snapshot")
    mode.add_argument("--legacy", action="store_true", help="Check the transitional client with legacy and native presentation available")
    mode.add_argument("--default-client", action="store_true", help="Check the unchanged ordinary legacy client without the native feature")
    arguments = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    work = arguments.work.resolve() if arguments.work else Path(tempfile.mkdtemp(prefix="prime-native-client-"))
    if work == repository or repository in work.parents:
        raise ValueError("The check snapshot must be outside the checkout.")
    work.mkdir(parents=True, exist_ok=True)
    if any(work.iterdir()):
        raise ValueError("The snapshot directory must be empty.")
    # Copy uncommitted source as well as tracked source. Project references use
    # their real project files; each snapshot owns its normal bin/obj directories.
    for name in ("MphRead", "NcsfPlay", "MphRead.Protocol.Generator", "ProjectPrime.Studio.Protocol"):
        shutil.copytree(repository / "src" / name, work / "src" / name,
                        ignore=shutil.ignore_patterns("bin", "obj", ".git"))
    for name in ("global.json", "NuGet.Config", "nuget.config", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        if (repository / name).is_file():
            shutil.copy2(repository / name, work / name)
    # These are read-only build inputs. This command never publishes or writes
    # to the shared native artifact or map directories.
    for name in ("artifacts", "maps", "tools"):
        if (repository / name).exists():
            (work / name).symlink_to(repository / name, target_is_directory=True)
    manifest = {}
    for path in sorted((work / "src").rglob("*")):
        if path.is_file():
            manifest[str(path.relative_to(work))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (work / "source-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Live-source snapshot: {work}", flush=True)
    if arguments.snapshot_only:
        return
    command = [arguments.dotnet, "build", str(work / "src/MphRead/MphRead.csproj"), "-c", "Release",
               "-p:MphReadAvalonia=" + ("true" if arguments.legacy or arguments.default_client else "false"),
               "-p:MphReadRmlUi=" + ("false" if arguments.server or arguments.default_client else "true"), "-v:minimal"]
    if arguments.server:
        command.append("-p:MphReadServer=true")
    log = work / "build.log"
    with log.open("w") as output:
        result = subprocess.run(command, cwd=work, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        print("\n".join(line for line in log.read_text().splitlines() if "error " in line))
        raise RuntimeError(f"Native client build failed; diagnostics: {log}")
    output = work / "src/MphRead/bin/Release/net10.0"
    assets = json.loads((work / "src/MphRead/obj/project.assets.json").read_text())
    dependencies = json.loads((output / "ProjectPrime.deps.json").read_text())
    toolkit = [name for graph in (assets["libraries"], dependencies["libraries"])
               for name in graph if name.lower().startswith("avalonia")]
    toolkit += [str(path) for path in output.rglob("Avalonia*.dll")]
    if toolkit and not (arguments.legacy or arguments.default_client):
        raise RuntimeError("Native client toolkit dependencies: " + ", ".join(toolkit))
    if (arguments.legacy or arguments.default_client) and not toolkit:
        raise RuntimeError("The transitional client lost its available legacy presentation.")
    required_files = ("ProjectPrime.dll",) if arguments.server else ("ProjectPrime.dll", "ProjectPrime.Studio.Protocol.dll", "SkiaSharp.dll")
    for required in required_files:
        if not (output / required).is_file():
            raise RuntimeError("Missing authoritative client dependency: " + required)
    if arguments.server and ((output / "rmlui").exists() or list(output.rglob("*.ttf")) or list(output.rglob("*.otf"))):
        raise RuntimeError("Dedicated server contains native UI documents or fonts.")
    label = "DEDICATED SERVER" if arguments.server else "TRANSITIONAL CLIENT" if arguments.legacy else "DEFAULT CLIENT" if arguments.default_client else "NATIVE CLIENT"
    boundary = "both presentation dependencies available" if arguments.legacy else "legacy presentation available" if arguments.default_client else "no Avalonia packages, dependency entries or output assemblies"
    report = dict(label=label, dll=str(output / "ProjectPrime.dll"),
                  sha256=hashlib.sha256((output / "ProjectPrime.dll").read_bytes()).hexdigest(),
                  avaloniaEntriesAndFiles=len(toolkit), sourceManifest=str(work / "source-manifest.json"))
    (work / "boundary-check.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"{label} PASS: actual project build; {boundary}.\nBinary: {output / 'ProjectPrime.dll'}\nDiagnostics: {log}")


if __name__ == "__main__":
    main()
