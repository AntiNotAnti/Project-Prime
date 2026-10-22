import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("check-avares.py")
SPEC = importlib.util.spec_from_file_location("check_avares", SCRIPT)
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


FONT_TABLE = '''
namespace MphRead.Mods.Render.Hud;
public static class HudFonts
{
    public sealed record Entry(string Id, string Name);
    public static readonly Entry[] All = {
        new("font00", "Rajdhani"),
        new("font17", "Libertine")
    };
}
'''


class AvaresCheckerTests(unittest.TestCase):
    def make_root(self, source_text, font_table=FONT_TABLE):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        source = root / "src/MphRead/Mods/Launcher/Gui/fixture.cs"
        source.parent.mkdir(parents=True)
        source.write_text(source_text, encoding="utf-8")
        table = root / checker.HUD_FONT_TABLE
        table.parent.mkdir(parents=True)
        table.write_text(font_table, encoding="utf-8")
        return root

    def test_dynamic_prefix_expands_registry_and_preserves_literal_assets(self):
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n'
            'var literal = new("avares://ProjectPrime/Assets/HudFonts/custom.ttf#Custom");\n'
        )

        assets = checker.wanted(root)

        self.assertEqual(set(assets), {
            "Assets/HudFonts/font00.ttf",
            "Assets/HudFonts/font17.ttf",
            "Assets/HudFonts/custom.ttf",
        })
        self.assertIn("fixture.cs:1", assets["Assets/HudFonts/font00.ttf"][0])
        self.assertIn("fixture.cs:2", assets["Assets/HudFonts/custom.ttf"][0])

        missing = checker.missing_assets(
            assets,
            {"Assets/HudFonts/font00.ttf", "Assets/HudFonts/custom.ttf"},
        )
        self.assertEqual(set(missing), {"Assets/HudFonts/font17.ttf"})

    def test_unrecognized_dynamic_font_expression_fails_closed(self):
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.FileName + ".ttf#" + font.Family);\n'
        )

        with self.assertRaisesRegex(checker.AssetScanError, "unrecognized dynamic HudFonts"):
            checker.wanted(root)

    def test_malformed_font_registry_fails_closed(self):
        malformed = FONT_TABLE.replace(
            'new("font17", "Libertine")',
            'new(font.Id, "Libertine")',
        )
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=malformed,
        )

        with self.assertRaisesRegex(checker.AssetScanError, "unrecognized entry declaration"):
            checker.wanted(root)

    def test_typed_constructor_is_included_in_dynamic_font_expansion(self):
        typed = FONT_TABLE.replace(
            'new("font17", "Libertine")',
            'new Entry("font17", "Libertine")',
        )
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=typed,
        )

        self.assertIn("Assets/HudFonts/font17.ttf", checker.wanted(root))

    def test_unknown_typed_constructor_fails_closed(self):
        unknown = FONT_TABLE.replace(
            'new("font17", "Libertine")',
            'new UnexpectedEntry("font17", "Libertine")',
        )
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=unknown,
        )

        with self.assertRaisesRegex(checker.AssetScanError, "unrecognized entry declaration"):
            checker.wanted(root)

    def test_constructor_trailing_expression_fails_closed(self):
        malformed = FONT_TABLE.replace(
            'new("font17", "Libertine")',
            'new("font17", "Libertine") + UnexpectedEntry()',
        )
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=malformed,
        )

        with self.assertRaisesRegex(checker.AssetScanError, "malformed entry declaration"):
            checker.wanted(root)

    def test_empty_initializer_entry_fails_closed(self):
        malformed = FONT_TABLE.replace(
            'new("font17", "Libertine")',
            ', new("font17", "Libertine")',
        )
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=malformed,
        )

        with self.assertRaisesRegex(checker.AssetScanError, "empty entry"):
            checker.wanted(root)

    def test_unsafe_font_id_fails_closed(self):
        unsafe = FONT_TABLE.replace('new("font17",', 'new("../font17",')
        root = self.make_root(
            'var family = new("avares://ProjectPrime/Assets/HudFonts/"'
            ' + font.Id + ".ttf#" + font.Family);\n',
            font_table=unsafe,
        )

        with self.assertRaisesRegex(checker.AssetScanError, "unsafe font ID"):
            checker.wanted(root)


if __name__ == "__main__":
    unittest.main()
