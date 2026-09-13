"""Regression tests for explicit Client.Presentation Android opt-in."""
from pathlib import Path
import importlib.util
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-client-presentation-targets.py"
SPEC = importlib.util.spec_from_file_location("check_client_presentation_targets", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(GUARD)


PRESENTATION = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TargetFramework Condition="'$(PrimeEnableAndroidPresentation)' == 'true'">net10.0-android36.0</TargetFramework>
    <RuntimeIdentifiers Condition="'$(PrimeEnableAndroidPresentation)' == 'true'">android-arm64;android-x64</RuntimeIdentifiers>
    <DefaultItemExcludes>$(DefaultItemExcludes);obj/**</DefaultItemExcludes>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../Game/Game.csproj"
    GlobalPropertiesToRemove="BaseIntermediateOutputPath;MSBuildProjectExtensionsPath" /></ItemGroup>
</Project>
"""
ANDROID = """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><AndroidPresentationIntermediatePath>$(MSBuildProjectDirectory)/../Client.Presentation/obj/android/</AndroidPresentationIntermediatePath></PropertyGroup>
<ItemGroup>
  <ProjectReference Include="../Client.Presentation/Client.Presentation.csproj"
    SetTargetFramework="TargetFramework=net10.0-android36.0"
    AdditionalProperties="PrimeEnableAndroidPresentation=true;BaseIntermediateOutputPath=$(AndroidPresentationIntermediatePath);MSBuildProjectExtensionsPath=$(AndroidPresentationIntermediatePath)"
    GlobalPropertiesToRemove="RuntimeIdentifier" />
</ItemGroup>
<Target Name="RestoreAndroidPresentation" BeforeTargets="PrepareForBuild">
  <MSBuild Projects="../Client.Presentation/Client.Presentation.csproj" Targets="Restore"
    Properties="PrimeEnableAndroidPresentation=true;TargetFramework=net10.0-android36.0;BaseIntermediateOutputPath=$(AndroidPresentationIntermediatePath);MSBuildProjectExtensionsPath=$(AndroidPresentationIntermediatePath);RestoreRecursive=false" />
</Target>
</Project>
"""
CLIENT = """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
  <ProjectReference Include="../Client.Presentation/Client.Presentation.csproj" />
</ItemGroup></Project>
"""


class ClientPresentationTargetGuardTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="project-prime-presentation-targets-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.write("src/Client.Presentation/Client.Presentation.csproj", PRESENTATION)
        self.write("src/Android/Android.csproj", ANDROID)
        self.write("src/Client/Client.csproj", CLIENT)

    def write(self, relative: str, contents: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents)

    def inspect(self) -> list[str]:
        return GUARD.inspect(self.root)

    def test_explicit_android_opt_in_passes(self):
        self.assertEqual([], self.inspect())

    def test_ordinary_multi_targeting_fails(self):
        self.write("src/Client.Presentation/Client.Presentation.csproj",
                   PRESENTATION.replace("<TargetFramework>net10.0</TargetFramework>",
                                        "<TargetFrameworks>net10.0;net10.0-android36.0</TargetFrameworks>"))
        self.assertIn("Client.Presentation must not multi-target in the ordinary build graph",
                      self.inspect())

    def test_unconditioned_android_runtime_identifiers_fail(self):
        self.write("src/Client.Presentation/Client.Presentation.csproj",
                   PRESENTATION.replace(
                       ' Condition="\'$(PrimeEnableAndroidPresentation)\' == \'true\'">android-arm64',
                       ">android-arm64"))
        self.assertIn(
            "Client.Presentation Android RuntimeIdentifiers must require the explicit opt-in",
            self.inspect())

    def test_android_reference_without_opt_in_fails(self):
        self.write("src/Android/Android.csproj",
                   ANDROID.replace("PrimeEnableAndroidPresentation=true;", "", 1))
        self.assertIn("Android must explicitly opt Client.Presentation into its Android target",
                      self.inspect())

    def test_android_without_isolated_restore_fails(self):
        self.write("src/Android/Android.csproj",
                   ANDROID.replace('RestoreRecursive=false', 'RestoreRecursive=true'))
        self.assertIn("Android presentation restore must be isolated and non-recursive",
                      self.inspect())

    def test_desktop_reference_with_android_opt_in_fails(self):
        self.write("src/Client/Client.csproj",
                   CLIENT.replace(" />", ' AdditionalProperties="PrimeEnableAndroidPresentation=true" />'))
        self.assertIn("desktop Client must not opt into the Android presentation target",
                      self.inspect())


if __name__ == "__main__":
    unittest.main()
