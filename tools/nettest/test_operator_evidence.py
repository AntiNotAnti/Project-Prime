"""Exact allowlist checks for the opt-in identity-free operator report."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("operator_runner", Path(__file__).with_name("run-net2.py"))
runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(runner)

PROTOCOL = 50
COUNTING_UNIT = "Authority Answer response decisions; retries included; not unique denied shots or delivered ACKs"
OUTCOMES = ("Applied", "AlreadyResolved", "RejectedDeadShooter", "RejectedDeadVictim", "RejectedPlausibility",
            "RejectedTooOld", "RejectedLifecycle", "RejectedGeometry", "RejectedDamage", "RejectedLaunch",
            "RejectedNoDamage", "RejectedImpulse", "ClaimCapacity", "Corrected")


def report():
    return dict(Schema=1, Protocol=PROTOCOL, Phase="InMatch",
                Queue=dict(Current=0, HighWater=3, Drops=2, BufferExhaustions=None, BufferExhaustionStatus="UNAVAILABLE"),
                Traffic=dict(ReceivedPackets=20, SentPackets=15, ReceivedBytes=200, SentBytes=150),
                ClaimVerdictResponses=[dict(Outcome=outcome, Responses=index) for index, outcome in enumerate(OUTCOMES)],
                VerdictCountingUnit=COUNTING_UNIT)


class OperatorEvidenceTests(unittest.TestCase):
    def check(self, value, expected="FAIL", protocol=PROTOCOL):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            (out / "server.operator.json").write_text(json.dumps(value), encoding="utf-8")
            result = runner.operator_summary(out, protocol)
        self.assertEqual(result["status"], expected, result)
        if expected == "FAIL": self.assertTrue(result.get("reason"), result)
        return result

    def test_valid_report_all_phases_and_explicit_scope(self):
        for phase in ("Lobby", "Starting", "InMatch", "PostMatch"):
            with self.subTest(phase=phase):
                value = report(); value["Phase"] = phase
                result = self.check(value, "PASS")
                self.assertEqual(result["scope"], "identity-free typed aggregate schema; not unique shot or delivery accounting")
        value = report()
        for group in (value["Queue"], value["Traffic"]):
            for field in group:
                if type(group[field]) is int: group[field] = 0
        for row in value["ClaimVerdictResponses"]: row["Responses"] = 0
        self.check(value, "PASS")  # Empty aggregate is valid schema, not gameplay coverage.

    def test_exact_recursive_allowlist_rejects_unknown_endpoint_fields(self):
        for location in ("root", "queue", "traffic", "outcome"):
            with self.subTest(location=location):
                value = report()
                target = {"root": value, "queue": value["Queue"], "traffic": value["Traffic"],
                          "outcome": value["ClaimVerdictResponses"][0]}[location]
                target["Endpoints"] = [dict(Address="forbidden", PlayerName="forbidden")]
                self.check(value)

    def test_exact_recursive_allowlist_requires_every_field(self):
        for location in ("root", "queue", "traffic", "outcome"):
            baseline = report()
            target = {"root": baseline, "queue": baseline["Queue"], "traffic": baseline["Traffic"],
                      "outcome": baseline["ClaimVerdictResponses"][0]}[location]
            for field in target:
                with self.subTest(location=location, field=field):
                    value = copy.deepcopy(baseline)
                    changed = {"root": value, "queue": value["Queue"], "traffic": value["Traffic"],
                               "outcome": value["ClaimVerdictResponses"][0]}[location]
                    del changed[field]; self.check(value)

    def test_numeric_metrics_reject_boolean_noninteger_and_negative(self):
        fields = [("Queue", field) for field in ("Current", "HighWater", "Drops")]
        fields += [("Traffic", field) for field in ("ReceivedPackets", "SentPackets", "ReceivedBytes", "SentBytes")]
        for group, field in fields:
            for invalid in (True, False, 1.0, 1.5, "1", None, [], {}, -1, float("nan"), float("inf")):
                with self.subTest(group=group, field=field, invalid=invalid):
                    value = report(); value[group][field] = invalid; self.check(value)

    def test_verdict_counts_reject_boolean_noninteger_and_negative(self):
        for invalid in (True, False, 1.0, "1", None, [], {}, -1, float("nan"), float("inf")):
            with self.subTest(invalid=invalid):
                value = report(); value["ClaimVerdictResponses"][0]["Responses"] = invalid; self.check(value)

    def test_version_fields_are_exact_integers_and_match_live_protocol(self):
        for field, invalid in (("Schema", True), ("Schema", 1.0), ("Schema", "1"), ("Schema", 0), ("Schema", 2),
                               ("Protocol", True), ("Protocol", 50.0), ("Protocol", "50"), ("Protocol", 0),
                               ("Protocol", PROTOCOL - 1), ("Protocol", PROTOCOL + 1)):
            with self.subTest(field=field, invalid=invalid):
                value = report(); value[field] = invalid; self.check(value)
        self.check(report(), protocol=PROTOCOL + 1)

    def test_phase_is_a_known_exact_enum_string(self):
        for invalid in ("inmatch", "InProgress", "Unknown", "", "InMatch ", True, 1, None, [], {}):
            with self.subTest(invalid=invalid):
                value = report(); value["Phase"] = invalid; self.check(value)

    def test_outcome_enum_cannot_be_unknown_malformed_or_duplicated(self):
        for invalid in ("applied", "Unknown", "RejectedLaunch ", "", True, 1, None, [], {}):
            with self.subTest(invalid=invalid):
                value = report(); value["ClaimVerdictResponses"][0]["Outcome"] = invalid; self.check(value)
        value = report(); value["ClaimVerdictResponses"][0] = copy.deepcopy(value["ClaimVerdictResponses"][1])
        self.check(value)  # Right row count alone must not hide a duplicate/missing bucket.

    def test_row_count_and_container_shapes_are_exact(self):
        for change in (lambda rows: rows[:-1], lambda rows: rows + [copy.deepcopy(rows[0])],
                       lambda rows: None, lambda rows: {}, lambda rows: "rows",
                       lambda rows: [None] + rows[1:], lambda rows: [[]] + rows[1:]):
            value = report(); value["ClaimVerdictResponses"] = change(value["ClaimVerdictResponses"]); self.check(value)
        for group in ("Queue", "Traffic"):
            for invalid in (None, [], True, "aggregate"):
                value = report(); value[group] = invalid; self.check(value)
        for invalid in (None, [], True, "report"): self.check(invalid)

    def test_unmeasured_buffer_exhaustion_is_only_null_unavailable(self):
        for invalid in (0, 1, True, False, "0", [], {}):
            value = report(); value["Queue"]["BufferExhaustions"] = invalid; self.check(value)
        for invalid in ("PASS", "AVAILABLE", "unavailable", "UNAVAILABLE ", None, True, [], {}):
            value = report(); value["Queue"]["BufferExhaustionStatus"] = invalid; self.check(value)

    def test_counting_unit_is_static_and_does_not_claim_unique_denials_or_delivery(self):
        for invalid in ("unique denied shots", "delivered ACKs", COUNTING_UNIT + " ", None, True, [], {}):
            value = report(); value["VerdictCountingUnit"] = invalid; self.check(value)

    def test_missing_or_invalid_json_file_fails_without_exception(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            result = runner.operator_summary(out, PROTOCOL)
            self.assertEqual(result["status"], "FAIL", result)
            self.assertTrue(result.get("reason"), result)
            for malformed in ("", "{", "not JSON"):
                (out / "server.operator.json").write_text(malformed, encoding="utf-8")
                result = runner.operator_summary(out, PROTOCOL)
                self.assertEqual(result["status"], "FAIL", result)


if __name__ == "__main__": unittest.main()
