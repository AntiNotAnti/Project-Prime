"""Contract tests for the physical Game/Client/Server project graph guard."""
from pathlib import Path
import importlib.util
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-project-boundaries.py"
SOURCE_GUARD = ROOT / "tools/check-multiplayer-only.py"
SPEC = importlib.util.spec_from_file_location("check_project_boundaries", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
import sys
sys.modules[SPEC.name] = GUARD
SPEC.loader.exec_module(GUARD)


class ProjectBoundaryGuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="fruity-project-boundaries-")
        self.addCleanup(self.temporary.cleanup)
        # The macOS temporary-directory alias can resolve from /var to
        # /private/var. The guard compares resolved linked paths to its root.
        self.root = Path(self.temporary.name).resolve()
        self.write("tools/check-multiplayer-only.py", SOURCE_GUARD.read_text())

    def write(self, relative, contents):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents)
        return path

    def project(self, name, references=(), packages=(), links=()):
        items = []
        for reference in references:
            items.append(f'<ProjectReference Include="../{reference}/{reference}.csproj" />')
        for package in packages:
            items.append(f'<PackageReference Include="{package}" Version="1.0.0" />')
        for include in links:
            items.append(f'<Compile Include="{include}" Link="{include}" />')
        body = "\n    ".join(items)
        return f"""<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    {body}
  </ItemGroup>
</Project>
"""

    def write_baseline(self):
        links = {
            "Client": ("../Shared/Shared.cs", "../Shared/Shared.cs"),
            "Server": ("../Shared/Shared.cs",),
            "Tools": ("../Shared/Shared.cs",),
            "Android": ("../Shared/Shared.cs", "../Client/Client.cs"),
        }
        for name, references in GUARD.PROJECTS.items():
            packages = ("OpenTK.Mathematics",) if name == "Game" else (
                ("Microsoft.IdentityModel.JsonWebTokens",) if name == "Server" else ())
            self.write(
                f"src/{name}/{name}.csproj",
                self.project(name, references, packages, links.get(name, ())),
            )
            self.write(f"src/{name}/{name}.cs", "class Fixture { }\n")
        self.write("src/Shared/Shared.cs", "class SharedFixture { }\n")
        self.write("src/Client/Client.cs", "class ClientFixture { }\n")

    def inspect(self):
        return GUARD.inspect(self.root)

    def test_allowed_graph_and_explicit_shared_links_pass(self):
        self.write_baseline()

        self.assertEqual(self.inspect(), [])

    def test_forbidden_project_dependency_and_package_are_reported(self):
        self.write_baseline()
        self.write(
            "src/Server/Server.csproj",
            self.project("Server", ("Client",), ("Avalonia",), ("../Shared/Shared.cs",)),
        )

        errors = self.inspect()

        self.assertIn("Server: project references ['Client']; expected ['Game', 'Shared.Replay']", errors)
        self.assertIn("Server: unexpected platform packages: ['Avalonia']", errors)

    def test_backend_allows_game_only_and_rejects_client_dependency(self):
        self.write_baseline()
        self.write(
            "src/Backend/Backend.csproj",
            self.project("Backend", ("Client",), ("Avalonia",)),
        )

        errors = self.inspect()

        self.assertIn("Backend: project references ['Client']; expected ['Game']", errors)
        self.assertIn("Backend: unexpected platform packages: ['Avalonia']", errors)

    def test_shared_replay_allows_game_only_and_rejects_client_dependency(self):
        self.write_baseline()
        self.write(
            "src/Shared.Replay/Shared.Replay.csproj",
            self.project("Shared.Replay", ("Client",), ("Avalonia",)),
        )

        errors = self.inspect()

        self.assertIn("Shared.Replay: project references ['Client']; expected ['Game']", errors)
        self.assertIn("Shared.Replay: unexpected packages: ['Avalonia']", errors)

    def test_backend_platform_source_is_rejected(self):
        self.write_baseline()
        self.write("src/Backend/Ui.cs", "using Avalonia.Controls;\nclass UiFixture { }\n")

        errors = self.inspect()

        self.assertIn("src/Backend/Ui.cs:1: platform dependency Avalonia", errors)

    def test_backend_cannot_link_client_source(self):
        self.write_baseline()
        self.write(
            "src/Backend/Backend.csproj",
            self.project("Backend", ("Game",), links=("../Client/Client.cs",)),
        )

        errors = self.inspect()

        self.assertIn("src/Backend/Backend.csproj: invalid Backend source link: ../Client/Client.cs", errors)

    def test_cross_tree_source_glob_is_rejected(self):
        self.write_baseline()
        self.write(
            "src/Server/Server.csproj",
            self.project("Server", ("Game",), links=("../Shared/*.cs",)),
        )

        errors = self.inspect()

        self.assertIn(
            "src/Server/Server.csproj: shared sources must be explicit files: ../Shared/*.cs",
            errors,
        )

    def test_game_platform_source_is_rejected(self):
        self.write_baseline()
        self.write("src/Game/Window.cs", "using Avalonia;\nclass WindowFixture { }\n")

        errors = self.inspect()

        self.assertIn("src/Game/Window.cs:1: platform dependency Avalonia", errors)

    def test_missing_required_project_fails_closed(self):
        self.write_baseline()
        (self.root / "src/Game/Game.csproj").unlink()

        errors = self.inspect()

        self.assertIn("missing project: src/Game/Game.csproj", errors)
        self.assertTrue(any("missing project reference" in error for error in errors))

    def test_missing_linked_source_fails_closed(self):
        self.write_baseline()
        self.write(
            "src/Server/Server.csproj",
            self.project("Server", ("Game",), links=("../Shared/Missing.cs",)),
        )

        errors = self.inspect()

        self.assertIn("src/Server/Server.csproj: missing linked source: ../Shared/Missing.cs", errors)


if __name__ == "__main__":
    unittest.main()
