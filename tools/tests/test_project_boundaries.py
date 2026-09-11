"""Contract tests for the physical Game/Client/Node/Worker project graph guard."""
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
        self.temporary = tempfile.TemporaryDirectory(prefix="project-prime-project-boundaries-")
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
            "Server.Worker": ("../Shared/Shared.cs",),
            "Tools": ("../Shared/Shared.cs",),
            "Android": ("../Shared/Shared.cs", "../Client/Client.cs"),
        }
        for name, references in GUARD.PROJECTS.items():
            packages = ("OpenTK.Mathematics",) if name == "Game" else (
                ("Microsoft.IdentityModel.JsonWebTokens",) if name == "Server.Worker" else ())
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
            "src/Server.Worker/Server.Worker.csproj",
            self.project("Server.Worker", ("Client",), ("Avalonia",), ("../Shared/Shared.cs",)),
        )

        errors = self.inspect()

        self.assertIn(
            "Server.Worker: project references ['Client']; expected ['Game', 'Imaging', 'MapPlatform', 'Server.Shared', 'Shared.Replay']",
            errors,
        )
        self.assertIn("Server.Worker: unexpected platform packages: ['Avalonia']", errors)

    def test_worker_map_preparation_package_is_allowed(self):
        self.write_baseline()
        self.write(
            "src/Server.Worker/Server.Worker.csproj",
            self.project(
                "Server.Worker", GUARD.PROJECTS["Server.Worker"],
                ("Microsoft.IdentityModel.JsonWebTokens", "ReFuel.StbImage"),
                ("../Shared/Shared.cs",),
            ),
        )

        self.assertEqual(self.inspect(), [])

    def test_backend_allows_game_only_and_rejects_client_dependency(self):
        self.write_baseline()
        self.write(
            "src/Backend/Backend.csproj",
            self.project("Backend", ("Client",), ("Avalonia",)),
        )

        errors = self.inspect()

        self.assertIn("Backend: project references ['Client']; expected ['Game', 'Server.Shared']", errors)
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
            "src/Server.Worker/Server.Worker.csproj",
            self.project("Server.Worker", ("Game",), links=("../Shared/*.cs",)),
        )

        errors = self.inspect()

        self.assertIn(
            "src/Server.Worker/Server.Worker.csproj: shared sources must be explicit files: ../Shared/*.cs",
            errors,
        )

    def test_game_platform_source_is_rejected(self):
        self.write_baseline()
        self.write("src/Game/Window.cs", "using Avalonia;\nclass WindowFixture { }\n")

        errors = self.inspect()

        self.assertIn("src/Game/Window.cs:1: platform dependency Avalonia", errors)

    def test_analyzer_project_reference_does_not_change_runtime_boundary(self):
        self.write_baseline()
        self.write(
            "src/Game/Game.csproj",
            self.project("Game", packages=("OpenTK.Mathematics",))[:-len("</Project>\n")] +
            "    <ProjectReference Include=\"../Protocol.Generator/Protocol.Generator.csproj\" "
            "OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />\n"
            "</Project>\n",
        )
        self.write("src/Protocol.Generator/Protocol.Generator.csproj", self.project("Protocol.Generator"))

        self.assertEqual(self.inspect(), [])

    def test_game_and_server_sdl_types_are_rejected(self):
        self.write_baseline()
        fixtures = {
            "Game": "using SDL;\nclass GpuFixture { }\n",
            "Server.Shared": "class GpuFixture { SDL_GPUDevice value; }\n",
            "Server.Node": "class GpuFixture { SDL3 value; }\n",
            "Server.Worker": "class GpuFixture { SDL_GPUTexture value; }\n",
            "Backend": "class GpuFixture { SDL_GPUBuffer value; }\n",
        }
        for project, source in fixtures.items():
            self.write(f"src/{project}/Gpu.cs", source)

        errors = self.inspect()

        expected = {
            "Game": "SDL",
            "Server.Shared": "SDL_GPUDevice",
            "Server.Node": "SDL3",
            "Server.Worker": "SDL_GPUTexture",
            "Backend": "SDL_GPUBuffer",
        }
        for project, symbol in expected.items():
            with self.subTest(project=project):
                self.assertIn(
                    f"src/{project}/Gpu.cs:1: platform dependency {symbol}", errors
                )

    def test_game_and_server_sdl_packages_are_rejected(self):
        self.write_baseline()
        for project in ("Game", "Server.Shared", "Server.Node", "Server.Worker", "Backend"):
            packages = ["ppy.SDL3-CS"]
            if project == "Game":
                packages.insert(0, "OpenTK.Mathematics")
            elif project == "Server.Worker":
                packages.insert(0, "Microsoft.IdentityModel.JsonWebTokens")
            links = ("../Shared/Shared.cs",) if project in {"Server.Worker", "Backend"} else ()
            self.write(
                f"src/{project}/{project}.csproj",
                self.project(project, GUARD.PROJECTS[project], packages, links),
            )

        errors = self.inspect()

        for project in ("Game", "Server.Shared", "Server.Node", "Server.Worker", "Backend"):
            with self.subTest(project=project):
                self.assertIn(
                    f"{project}: unexpected platform packages: ['ppy.SDL3-CS']", errors
                )

    def test_client_may_own_sdl_types_and_package(self):
        self.write_baseline()
        self.write("src/Client/Gpu.cs", "using SDL;\nclass GpuFixture { SDL_GPUDevice value; }\n")
        self.write(
            "src/Client/Client.csproj",
            self.project(
                "Client", GUARD.PROJECTS["Client"], ("ppy.SDL3-CS",),
                ("../Shared/Shared.cs",),
            ),
        )

        self.assertEqual(self.inspect(), [])

    def test_missing_required_project_fails_closed(self):
        self.write_baseline()
        (self.root / "src/Game/Game.csproj").unlink()

        errors = self.inspect()

        self.assertIn("missing project: src/Game/Game.csproj", errors)
        self.assertTrue(any("missing project reference" in error for error in errors))

    def test_missing_linked_source_fails_closed(self):
        self.write_baseline()
        self.write(
            "src/Server.Worker/Server.Worker.csproj",
            self.project("Server.Worker", ("Game",), links=("../Shared/Missing.cs",)),
        )

        errors = self.inspect()

        self.assertIn("src/Server.Worker/Server.Worker.csproj: missing linked source: ../Shared/Missing.cs", errors)

    def test_retired_server_project_is_rejected(self):
        self.write_baseline()
        self.write("src/Server/Server.csproj", self.project("Server"))

        errors = self.inspect()

        self.assertIn("retired project remains: src/Server", errors)

    def test_retired_server_project_reference_is_rejected(self):
        self.write_baseline()
        self.write(
            "tests/Tests/Tests.csproj",
            '<Project><ItemGroup><ProjectReference Include="../../src/Server/Server.csproj" /></ItemGroup></Project>',
        )

        errors = self.inspect()

        self.assertIn(
            "tests/Tests/Tests.csproj: reference to retired project: src/Server/Server.csproj",
            errors,
        )

    def test_retired_server_symbols_are_rejected(self):
        self.write_baseline()
        self.write("src/Server.Worker/Legacy.cs", "class Fixture { MasterServer value; }\n")

        errors = self.inspect()

        self.assertIn("src/Server.Worker/Legacy.cs:1: retired server symbol MasterServer", errors)


if __name__ == "__main__":
    unittest.main()
