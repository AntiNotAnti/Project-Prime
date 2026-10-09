#!/usr/bin/env python3
"""Serial, reproducible contracts. Each result retains exit status and raw stdout."""
import argparse, hashlib, json, os, platform, subprocess, time
from pathlib import Path
p = argparse.ArgumentParser()
p.add_argument("--dotnet", default="dotnet")
p.add_argument("--binary", default="tools/nettest/bin/Release/net10.0/nettest.dll")
p.add_argument("--output", required=True)
p.add_argument("--assets")
p.add_argument("--baseline", action="store_true")
a = p.parse_args()
out = Path(a.output).resolve(); out.mkdir(parents=True, exist_ok=True)
checks = ["architecture", "health-shots", "claim-stress", "lagcomp-shadow", "weapon-policy", "transport-stress", "network-lifecycle", "replay-protocol42"]
if not a.baseline: checks += ["impact-baseline", "live-impact", "impact-transport", "impact-crossview", "impact-claim-fastpath", "impact-visual-kills", "impact-profile"]
results = []
def run(label, command):
    start = time.monotonic()
    with (out / (label + ".log")).open("w") as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
    row = dict(check=label, exitCode=result.returncode, seconds=round(time.monotonic()-start, 3), log=label+".log")
    results.append(row); print(json.dumps(row), flush=True)
for check in checks: run(check, [a.dotnet, a.binary, "--"+check])
if a.assets:
    for check in (["authority-combat", "accepted-fire-context"] + ([] if a.baseline else ["impact-claim-native"])):
        run(check, [a.dotnet, a.binary, "--"+check, a.assets, "MP1 SANCTORUS"])
    for players in [2,4,8]:
        run("server-"+str(players), [a.dotnet, a.binary, "--server-performance", a.assets, "MP1 SANCTORUS", str(players), str(out / ("server-"+str(players)+".json")), "10"])
report = dict(schema=1, sourceCommit=subprocess.check_output(["git","rev-parse","HEAD"],text=True).strip(), host=platform.platform(), sourceDirty=bool(subprocess.check_output(["git","status","--porcelain"],text=True).strip()),
    assemblySha256=hashlib.sha256(Path(a.binary).with_name("ProjectPrime.dll").read_bytes()).hexdigest(), results=results,
    limitations=["Headless synthetic intent simulation is not a rendered or impaired multiplayer acceptance run.", "Other OS/backend coverage requires their runners."])
(out / "results.json").write_text(json.dumps(report, indent=2)+"\n")
raise SystemExit(any(x["exitCode"] != 0 for x in results))
