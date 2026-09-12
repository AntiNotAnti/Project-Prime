"""Tests for the gradual package and authoritative-source guardrails."""
import importlib.util
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "check-build-guardrails.py"
SPEC = importlib.util.spec_from_file_location("prime_build_guardrails", SCRIPT)
assert SPEC and SPEC.loader
GUARDS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARDS)


class BuildGuardrailTests(unittest.TestCase):
    def test_repository_editorconfig_is_root_owned(self):
        self.assertTrue((ROOT / ".editorconfig").is_file())
        self.assertFalse((ROOT / "src" / ".editorconfig").exists())

    def test_repository_source_baseline_is_clean(self):
        self.assertEqual([], GUARDS._source_errors(ROOT))

    def test_new_hot_path_linq_is_not_hidden_by_the_baseline(self):
        with tempfile.TemporaryDirectory(prefix="prime-build-guardrails-") as directory:
            root = Path(directory)
            source = root / "src/Game/Gameplay/NewHotPath.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                "namespace Fixture;\n"
                "internal static class NewHotPath\n"
                "{\n"
                "    internal static int[] Build(int[] values) => values.Where(value => value > 0).ToArray();\n"
                "}\n",
                encoding="utf-8",
            )
            errors = GUARDS._source_errors(root)
            self.assertEqual(1, len(errors))
            self.assertIn("NewHotPath.cs:4", errors[0])
            self.assertIn("LINQ allocation/query", errors[0])

    def test_math_intrinsics_are_not_misclassified_as_linq(self):
        with tempfile.TemporaryDirectory(prefix="prime-build-guardrails-") as directory:
            root = Path(directory)
            source = root / "src/Game/Gameplay/MathHotPath.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                "using System;\n"
                "internal static class MathHotPath\n"
                "{\n"
                "    internal static float Clamp(float value) => MathF.Min(value, 1);\n"
                "}\n",
                encoding="utf-8",
            )
            self.assertEqual([], GUARDS._source_errors(root))


if __name__ == "__main__":
    unittest.main()
