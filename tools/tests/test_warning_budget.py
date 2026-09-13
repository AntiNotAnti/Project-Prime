"""Focused tests for the inspectable production warning budget."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "check-warning-budget.py"
SPEC = importlib.util.spec_from_file_location("check_warning_budget", SCRIPT)
WARNING_BUDGET = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(WARNING_BUDGET)


class WarningBudgetTests(unittest.TestCase):
    def test_parser_keeps_owned_warnings_and_deduplicates_msbuild_summary(self):
        target = ROOT / "src" / "Backend" / "Backend.csproj"
        dependency = ROOT / "src" / "Game" / "Game.csproj"
        line = (
            f"{ROOT}/src/Backend/Program.cs(25,34): warning CA1861: "
            "Prefer a static readonly array "
            f"[{target}]"
        )
        dependency_line = (
            f"{ROOT}/src/Game/World.cs(4,2): warning CA1000: "
            f"Dependency warning [{dependency}]"
        )

        warnings = WARNING_BUDGET.parse_warnings(
            "\n".join((line, "Build succeeded.", line, dependency_line)), ROOT, target
        )

        self.assertEqual(1, len(warnings))
        self.assertEqual("CA1861", warnings[0]["code"])
        self.assertEqual("src/Backend/Program.cs", warnings[0]["path"])

    def test_compare_passes_equal_counts_and_retains_project_rows(self):
        baseline = {
            "schemaVersion": 1,
            "projects": {
                "Game": {"warningCount": 1, "warnings": {"CA1000": 1}},
            },
        }
        current = {
            "schemaVersion": 1,
            "projects": {
                "Game": {"buildExitCode": 0, "warningCount": 1, "warnings": {"CA1000": {"count": 1}}},
            },
        }

        result = WARNING_BUDGET.compare(baseline, current)

        self.assertEqual("pass", result["status"])
        self.assertEqual(0, result["projects"][0]["delta"])
        self.assertFalse(result["violations"])

    def test_compare_rejects_new_code_even_when_total_is_unchanged(self):
        baseline = {
            "schemaVersion": 1,
            "projects": {
                "Game": {"warningCount": 1, "warnings": {"CA1000": 1}},
            },
        }
        current = {
            "schemaVersion": 1,
            "projects": {
                "Game": {"warningCount": 1, "warnings": {"CA2000": 1}},
            },
        }

        result = WARNING_BUDGET.compare(baseline, current)

        self.assertEqual("fail", result["status"])
        self.assertIn("CA2000 +1", result["violations"][0])

    def test_cli_check_writes_inspectable_comparison_and_fails_on_increase(self):
        with tempfile.TemporaryDirectory(prefix="prime-warning-budget-") as temporary:
            root = Path(temporary)
            baseline = root / "baseline.json"
            current = root / "current.json"
            output = root / "comparison.json"
            baseline.write_text(json.dumps({
                "schemaVersion": 1,
                "projects": {"Game": {"warningCount": 0, "warnings": {}}},
            }))
            current.write_text(json.dumps({
                "schemaVersion": 1,
                "projects": {"Game": {"buildExitCode": 0, "warningCount": 1,
                                         "warnings": {"CA9999": {"count": 1}}}},
            }))

            status = WARNING_BUDGET.main([
                "check", "--baseline", str(baseline), "--current", str(current),
                "--output", str(output),
            ])

            self.assertEqual(1, status)
            comparison = json.loads(output.read_text())
            self.assertEqual("fail", comparison["status"])
            self.assertIn("CA9999 +1", comparison["violations"][0])


if __name__ == "__main__":
    unittest.main()
