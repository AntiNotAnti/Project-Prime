from pathlib import Path
import importlib.util
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-client-online-runtime-boundary.py"
SPEC = importlib.util.spec_from_file_location("client_online_boundary", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(GUARD)


class ClientOnlineRuntimeBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-online-boundary-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        tokenizer = self.root / "tools/check-multiplayer-only.py"
        tokenizer.parent.mkdir(parents=True, exist_ok=True)
        tokenizer.write_text((ROOT / "tools/check-multiplayer-only.py").read_text())

    def test_new_legacy_reference_is_rejected(self):
        diff = """--- a/src/Client/HUD/Widget.cs
+++ b/src/Client/HUD/Widget.cs
@@ -1,0 +2 @@
+if (AuthoritativePlay.Current != null) return;
"""
        errors = GUARD.inspect_diff(diff)
        self.assertEqual(len(errors), 1)
        self.assertIn("src/Client/HUD/Widget.cs:2", errors[0])

    def test_owner_implementation_is_the_only_compatibility_seam(self):
        diff = """--- a/src/Client/Networking/AuthoritativePlay.cs
+++ b/src/Client/Networking/AuthoritativePlay.cs
@@ -1,0 +2 @@
+public static AuthoritativePlay? Current => NodeSessions.Current?.Play;
"""
        self.assertEqual(GUARD.inspect_diff(diff), [])

    def test_non_production_and_existing_lines_are_not_rewritten_by_guard(self):
        diff = """--- a/tests/Tests/Client/FacadeFixture.cs
+++ b/tests/Tests/Client/FacadeFixture.cs
@@ -1 +1 @@
-old
+AuthoritativePlay.Current = null;
"""
        self.assertEqual(GUARD.inspect_diff(diff), [])

    def test_tree_scan_rejects_non_allowlisted_existing_reference(self):
        source = self.root / "src/Client/HUD/Widget.cs"
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text("class Widget { object? Value => AuthoritativePlay.Current; }\n")

        errors = GUARD.inspect(self.root)

        self.assertEqual(len(errors), 1)
        self.assertIn("src/Client/HUD/Widget.cs:1", errors[0])

    def test_tree_scan_accepts_named_residual_shim(self):
        source = self.root / "src/Client/Rendering/RenderInterpolation.cs"
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text("class RenderInterpolation { object? Value => AuthoritativePlay.Current; }\n")

        self.assertEqual(GUARD.inspect(self.root), [])


if __name__ == "__main__":
    unittest.main()
