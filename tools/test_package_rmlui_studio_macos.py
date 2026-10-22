#!/usr/bin/env python3
"""CPU/package-only fixtures. No application executable is launched."""
import importlib.util
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('studio_package', Path(__file__).with_name('package-rmlui-studio-macos.py'))
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class StudioPackageTests(unittest.TestCase):
    def test_identity_and_association_policy(self):
        original = {'CFBundleExecutable':'Game', 'CFBundleVersion':'93', 'CFBundleIconFile':'Game.icns',
                    'CFBundleDocumentTypes':[{}], 'CFBundleURLTypes':[{}],
                    'UTImportedTypeDeclarations':[{}], 'UTExportedTypeDeclarations':[{}]}
        result = packager.studio_plist(original)
        self.assertEqual(result['CFBundleExecutable'],'ProjectPrimeStudio')
        self.assertEqual(result['CFBundleIdentifier'],'com.projectprime.studio')
        self.assertEqual(result['CFBundleVersion'],'93')
        self.assertEqual(result['CFBundleIconFile'],'Game.icns')
        self.assertFalse(set(result) & {'CFBundleDocumentTypes','CFBundleURLTypes','UTImportedTypeDeclarations','UTExportedTypeDeclarations'})
        self.assertIn('CFBundleDocumentTypes',original)
        self.assertEqual(packager.studio_plist(original,True)['CFBundleIconFile'],'ProjectPrimeStudio.icns')

    @unittest.skipUnless(sys.platform == 'darwin', 'macOS signing tools required')
    def test_independent_signed_sibling(self):
        with tempfile.TemporaryDirectory(prefix='prime-studio-package-test-') as temporary:
            root = Path(temporary)
            game = root/'Game.app'
            macos = game/'Contents/MacOS'
            resources = game/'Contents/Resources'
            macos.mkdir(parents=True)
            (resources/'dotnet').mkdir(parents=True)
            (resources/'client').mkdir()
            (resources/'client/ProjectPrime.dll').write_bytes(b'fixture only, never executed')
            source = root/'fixture.c'
            source.write_text('int main(void) { return 0; }')
            subprocess.run(['/usr/bin/xcrun','clang',str(source),'-o',str(macos/'Game')],check=True)
            shutil.copy2(macos/'Game',resources/'dotnet/dotnet')
            info = {'CFBundleExecutable':'Game','CFBundleIdentifier':'test.game','CFBundlePackageType':'APPL',
                    'CFBundleVersion':'1','CFBundleDocumentTypes':[{}]}
            (game/'Contents/Info.plist').write_bytes(plistlib.dumps(info))
            entitlement = root/'entitlements.plist'
            entitlement.write_bytes(plistlib.dumps({'com.apple.security.cs.allow-jit':True}))
            for target in (resources/'dotnet/dotnet',macos/'Game',game):
                subprocess.run(['/usr/bin/codesign','--force','--sign','-','--entitlements',str(entitlement),str(target)],check=True)
            studio = root/'Project Prime Studio.app'
            packager.package(game,studio)
            self.assertNotEqual((resources/'client/ProjectPrime.dll').stat().st_ino,
                (studio/'Contents/Resources/client/ProjectPrime.dll').stat().st_ino)
            (studio/'Contents/Resources/client/ProjectPrime.dll').write_bytes(b'changed copy')
            self.assertEqual((resources/'client/ProjectPrime.dll').read_bytes(),b'fixture only, never executed')
            final = plistlib.loads((studio/'Contents/Info.plist').read_bytes())
            self.assertEqual(final['CFBundleExecutable'],'ProjectPrimeStudio')
            self.assertNotIn('CFBundleDocumentTypes',final)
            self.assertFalse((studio/'Contents/MacOS/Game').exists())
            self.assertTrue((studio/'Contents/MacOS/ProjectPrimeStudio').is_file())
            with self.assertRaises(ValueError):
                packager.package(game,studio)


if __name__ == '__main__':
    unittest.main()
