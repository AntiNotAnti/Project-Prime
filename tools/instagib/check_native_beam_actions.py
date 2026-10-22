#!/usr/bin/env python3
"""Native cosmetic ownership acceptance: Hunters-only, cycle-only UI.

The native RmlUi bridge has its own ABI mapping. Keep both cycle commands
wired, but do not bring back the overwhelming beam/color button matrices or
Hunter License customization routes.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
def read(path: str) -> str:
    return (root / path).read_text(encoding="utf-8")
hunter = read("src/MphRead/Mods/Launcher/RmlUi/Pages/hunters/native-picker.rml")
native_license = read("src/MphRead/Mods/Launcher/RmlUi/Pages/license/native-overview.rml")
legacy_license = read("src/MphRead/Mods/Launcher/RmlUi/Pages/license/license.rml")
native = read("native/rmlui-poc/projectprime_rmlui.cpp")
header = read("native/rmlui-poc/projectprime_rmlui_api.h")
managed = read("src/MphRead/Mods/Launcher/RmlUi/Host/RmlUiIntent.cs")
presenter = read("src/MphRead/Mods/Launcher/RmlUi/Pages/hunters/NativeHunterPagePresenter.cs")
controller = read("src/MphRead/Mods/Launcher/Portable/LauncherCosmeticDraftController.cs")
license_page = read("src/MphRead/Mods/Launcher/RmlUi/Pages/license/NativeLicenseOverviewPage.cs")
license_workspace = read("src/MphRead/Mods/Launcher/Gui/Workspaces/LicenseWorkspace.cs")
license_ui = read("src/MphRead/Mods/Launcher/Gui/Workspaces/LicenseCustomization.cs")

def check(ok: bool, why: str) -> None:
    if not ok:
        raise AssertionError(why)

for name, number, action, element in (
    ("HunterBeamNext", 278, "hunter:beam-next", "hunter_beam"),
    ("HunterBeamColorNext", 279, "hunter:beam-color-next", "hunter_beam_color"),
):
    pattern = rf"\b{re.escape(name)}\s*=\s*{number}\b"
    check(bool(re.search(pattern, managed)) and bool(re.search(pattern, header)),
          f"native/C# ABI mismatch for {name}")
    check(f"PrimeIntentKind::{name}" in native and action in native,
          f"native decoder drops {action}")
    check(f"RmlUiIntentKind.{name}" in presenter and action in managed,
          f"managed handler not connected to {action}")
    check(f'id="{element}"' in hunter and f'data-action="{action}"' in hunter,
          f"visible Hunters cycle button missing for {element}")

for i in range(9):
    for tag in (f'hunter_beam{i}', f'hunter_beam_color{i}'):
        check(f'id="{tag}"' not in hunter,
              f"obsolete beam/color selector returned: {tag}")
    check(f'hunter:beam-select:{i}' not in hunter and
          f'hunter:beam-color-select:{i}' not in hunter,
          "old direct beam button returned")

check('id="hunter_beam_grid"' not in hunter and
      'class="beam-color-picker"' not in hunter and
      'class="beam-miniature"' not in hunter,
      "old gallery or text-only beam placeholder is still visible")
for text in ('native_license_customize', 'native_license_tab_customization',
             'license:customization'):
    check(text not in native_license and text not in legacy_license,
          "Hunter License still exposes cosmetics: " + text)
check('OpenCustomizationRequested' not in license_page and
      'case 1: OpenCustomizationRequested' not in license_page,
      "native License still owns cosmetic navigation")
check('Face.Customization' not in license_workspace and
      'private Control Customization()' not in license_ui,
      "Avalonia Hunter License still owns a cosmetic page")
check('NextBeam()' in controller and 'NextBeamColor()' in controller,
      "cycle selection is no longer available")
print("INSTA-GIB UI PASS: Hunter-only cycle buttons, native/C# actions, no License customization")
