"""Tests for the source/document live protocol contract."""
from pathlib import Path
import importlib.util
import tempfile
import unittest
import sys


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-current-protocol.py"
SPEC = importlib.util.spec_from_file_location("check_current_protocol", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = GUARD
SPEC.loader.exec_module(GUARD)


class CurrentProtocolTests(unittest.TestCase):
    def test_repository_alias_resolves_to_documented_live_protocol(self):
        self.assertEqual(
            GUARD.resolve_source_protocol(
                (ROOT / "src/Game/Protocol/NetHeader.cs").read_text()),
            GUARD.read_documented_protocol(
                (ROOT / "docs/CURRENT_PROTOCOL.md").read_text()),
        )

    def test_alias_chain_and_constant_expression_are_resolved(self):
        source = """
        public readonly record struct NetHeader {
            public const byte Base = 20;
            public const byte EnhancedHuntersVersion = Base + 3;
            public const byte Version = EnhancedHuntersVersion;
        }
        """
        self.assertEqual(23, GUARD.resolve_source_protocol(source))

    def test_current_status_does_not_use_historical_table_rows(self):
        document = """
        # Current Project Prime protocol
        Status: authoritative live-wire reference. The current
        authoritative wire family is `Authoritative`, protocol **23**.

        | Protocol | Current meaning |
        | 21 | historical |
        | 23 | current |
        """
        self.assertEqual(23, GUARD.read_documented_protocol(document))

    def test_mismatch_is_reported(self):
        with tempfile.TemporaryDirectory(prefix="project-prime-protocol-") as name:
            root = Path(name)
            (root / "src/Game/Protocol").mkdir(parents=True)
            (root / "docs").mkdir()
            (root / "src/Game/Protocol/NetHeader.cs").write_text(
                "public const byte Version = EnhancedHuntersVersion;\n"
                "public const byte EnhancedHuntersVersion = 23;\n")
            (root / "docs/CURRENT_PROTOCOL.md").write_text(
                "Status: current authoritative wire family is `Authoritative`, protocol **22**.\n")
            errors = GUARD.check(root)
        self.assertEqual(1, len(errors))
        self.assertIn("live protocol is 22", errors[0])
        self.assertIn("resolves NetHeader.Version to 23", errors[0])

    def test_missing_current_status_fails_closed(self):
        with self.assertRaises(ValueError):
            GUARD.read_documented_protocol("| Protocol | Current meaning |\n| 23 | live |\n")


if __name__ == "__main__":
    unittest.main()
