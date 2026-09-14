"""Release workflow contracts that keep public drafts behind Windows execution."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ReleaseWorkflowContractTests(unittest.TestCase):
    def test_windows_uses_resolved_tag_and_exact_private_artifacts(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        windows = workflow.split("\n  updater-windows:\n", 1)[1].split(
            "\n  publish-draft:\n", 1
        )[0]

        self.assertIn("ref: refs/tags/${{ needs.release.outputs.tag }}", windows)
        self.assertNotIn("ref: ${{ github.sha }}", windows)
        self.assertIn("ProjectPrime-release-windows-client-${{ needs.release.outputs.tag }}", windows)
        self.assertIn("ProjectPrime-release-windows-server-${{ needs.release.outputs.tag }}", windows)
        self.assertIn("ProjectPrime.exe", windows)
        self.assertIn("ProjectPrime.Editor.exe", windows)
        self.assertIn("--runtime-smoke-frames", windows)
        self.assertIn("worker/ProjectPrime.Server.Worker.exe", windows)
        self.assertIn("@('--runtime-smoke', 'true')", windows)
        self.assertIn("WaitForExit(120000)", windows)

    def test_public_draft_is_gated_on_windows_validation(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        build = workflow.split("\n  release:\n", 1)[1].split(
            "\n  updater-windows:\n", 1
        )[0]
        draft = workflow.split("\n  publish-draft:\n", 1)[1]

        self.assertNotIn("publish public player feed draft", build)
        self.assertIn("needs: [release, updater-windows]", draft)
        self.assertIn(
            "needs.release.result == 'success' && needs.updater-windows.result == 'success'",
            draft,
        )
        self.assertIn("ProjectPrime-release-dist-${{ needs.release.outputs.tag }}", draft)
        self.assertIn("publish public player feed draft", draft)
        self.assertIn("--draft", draft)

    def test_release_records_tag_commit_identity(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")

        self.assertIn("commit: ${{ steps.source.outputs.commit }}", workflow)
        self.assertIn('echo "commit=$(git rev-parse HEAD)"', workflow)
        self.assertIn("COMMIT_SHA: ${{ steps.source.outputs.commit }}", workflow)

    def test_editor_runtime_smoke_is_bounded_by_rendered_frames(self):
        program = (ROOT / "src/Editor/Program.cs").read_text(encoding="utf-8")
        application = (ROOT / "src/Editor/App/EditorApplication.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn('args.Contains("--runtime-smoke"', program)
        self.assertIn('PositiveNumber(args, "--runtime-smoke-frames", 1)', program)
        self.assertIn(".Run(frames)", program)
        self.assertIn("public int Run(int? frameLimit = null)", application)
        self.assertIn("if (frameLimit is <= 0)", application)
        self.assertIn("_frameNumber >= frameLimit.Value", application)
        self.assertIn("surface.Close()", application)


if __name__ == "__main__":
    unittest.main()
