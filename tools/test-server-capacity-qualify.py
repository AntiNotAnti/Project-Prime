#!/usr/bin/env python3
"""Asset-free unit checks for source-consistent Linux capacity qualification."""
from importlib.machinery import SourceFileLoader
from importlib.util import module_from_spec, spec_from_loader
from pathlib import Path
from copy import deepcopy


def module():
    path = Path(__file__).with_name("server-capacity-qualify.py")
    loader = SourceFileLoader("server_capacity_qualify", str(path))
    spec = spec_from_loader(loader.name, loader)
    loaded = module_from_spec(spec)
    loader.exec_module(loaded)
    return loaded


def report():
    step = dict(steps=3000, p99Milliseconds=4.0, p95Milliseconds=1.0,
                droppedTicks=0, overruns=0, stepFailures=0)
    arm = dict(instances=1, seconds=120, clientsPerMatch=2, mode="active",
               activeSimulationVerified=True, colocatedClientLoad=True,
               serverBinarySha256="abc", clientBinarySha256="def",
               healthySimulation=True, host=dict(cpuCount=8, platform="Linux test"),
               aggregateCpuPercent=dict(mean=32.0), aggregateRssMiB=dict(mean=200.0),
               processes=[dict(lastServerStep=step)])
    return dict(room="MP1 SANCTORUS", mode="active", arms=[arm])


def main():
    q = module()
    base = report()
    candidate = deepcopy(base)
    candidate["arms"][0]["aggregateCpuPercent"]["mean"] = 25.0
    result = q.qualify(base, candidate)
    assert result["passGuardrails"] and not result["comparisonSupportsImprovementClaim"]
    assert result["arms"][0]["aggregateCpuPercentDelta"] == -7.0
    bad = deepcopy(candidate)
    bad["arms"][0]["processes"][0]["lastServerStep"]["droppedTicks"] = 1
    assert not q.qualify(None, bad)["passGuardrails"]
    bad = deepcopy(candidate)
    bad["arms"][0]["processes"][0]["lastServerStep"]["p99Milliseconds"] = 19.0
    assert not q.qualify(None, bad)["passGuardrails"]
    bad = deepcopy(candidate)
    bad["arms"][0]["activeSimulationVerified"] = False
    assert not q.qualify(None, bad)["passGuardrails"]
    bad = deepcopy(candidate)
    bad["arms"][0]["host"]["cpuCount"] = 4
    try:
        q.qualify(base, bad)
    except ValueError as ex:
        assert "host/workload" in str(ex)
    else:
        raise AssertionError("unequal hardware must be rejected")
    print("PASS: live capacity qualification, evidence and mismatch refusals")


if __name__ == "__main__":
    main()
