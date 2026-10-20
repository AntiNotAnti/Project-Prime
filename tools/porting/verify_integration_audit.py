"""Verify SH-00 frozen source metadata without game assets or network access."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def git(repository, *arguments):
    return subprocess.check_output(
        ["git", "-C", str(repository), *arguments], stderr=subprocess.PIPE)


def verify(root=ROOT, upstream=None):
    manifest = json.loads((root / "docs/porting/second-hunt-manifest.json").read_text())
    audit = manifest["integrationAudit"]
    delta = json.loads((root / "docs/porting" / audit["sourceDelta"]).read_text())
    baseline = audit["targetBaseline"]
    for witness in audit["frozenSourceFiles"]:
        data = git(root, "show", baseline + ":" + witness["path"])
        if hashlib.sha256(data).hexdigest() != witness["sha256"]:
            raise ValueError("Frozen target source witness mismatch: " + witness["path"])
    if upstream is not None:
        old, new = delta["fromExclusive"], delta["toInclusive"]
        git(upstream, "merge-base", "--is-ancestor", old, new)
        paths = git(upstream, "diff", "--name-only", old, new).decode().splitlines()
        if paths != delta["changedPaths"]:
            raise ValueError("Frozen upstream comparison paths differ")
        commits = git(upstream, "log", "--reverse", "--format=%H %s", old + ".." + new).decode().splitlines()
        expected = [c["commit"] + " " + c["subject"] for c in delta["commits"]]
        if commits != expected:
            raise ValueError("Frozen upstream commit ledger differs")
        for commit in delta["commits"]:
            files = git(upstream, "diff-tree", "--no-commit-id", "--name-only", "-r", commit["commit"]).decode().splitlines()
            if files != commit["changedPaths"]:
                raise ValueError("Upstream commit paths differ: " + commit["commit"])
    return len(audit["frozenSourceFiles"]), delta["commitCount"], delta["changedPathCount"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream", type=Path, help="Optional local frozen SecondHunt Git checkout")
    args = parser.parse_args()
    try:
        witnesses, commits, paths = verify(upstream=args.upstream)
    except (OSError, KeyError, ValueError, subprocess.CalledProcessError) as exc:
        parser.exit(1, "FAIL: " + str(exc) + "\n")
    print(f"PASS: {witnesses} frozen target witnesses; {commits} commits / {paths} upstream paths")
    print("Upstream Git objects verified" if args.upstream else "Upstream Git objects not checked (supply --upstream)")
    print("Metadata only; no runtime, source-media, device or release acceptance implied")


if __name__ == "__main__":
    main()
