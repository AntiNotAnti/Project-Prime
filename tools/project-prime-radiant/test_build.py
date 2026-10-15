import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('radiant_recipe', Path(__file__).with_name('build.py'))
recipe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recipe)


class SourceOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'; self.source.mkdir()
        self.here = self.root / 'recipe'; self.here.mkdir()
        self.old_here, self.old_lock = recipe.HERE, recipe.LOCK
        subprocess.run(['git', 'init', '-q', self.source], check=True)
        (self.source / 'example.cpp').write_text('int value = 1;\n')
        self.git('add', 'example.cpp')
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'baseline')
        commit = self.git('rev-parse', 'HEAD')
        (self.source / 'example.cpp').write_text('int value = 2;\n')
        patch = self.git('diff', 'HEAD') + '\n'
        (self.here / 'one.patch').write_text(patch)
        recipe.HERE = self.here
        recipe.LOCK = {'commit': commit, 'patches': ['one.patch']}
        (self.source / '.prime-source.json').write_text(json.dumps({'patches': {
            'one.patch': hashlib.sha256((self.here / 'one.patch').read_bytes()).hexdigest()}}))

    def git(self, *args):
        return subprocess.run(['git', *args], cwd=self.source, check=True, text=True, stdout=subprocess.PIPE).stdout.strip()

    def tearDown(self):
        recipe.HERE, recipe.LOCK = self.old_here, self.old_lock
        self.temp.cleanup()

    def test_pinned_patch_is_accepted(self):
        recipe.validate_source(self.source)

    def test_additional_edit_is_rejected(self):
        (self.source / 'example.cpp').write_text('int value = 3;\n')
        with self.assertRaises(ValueError): recipe.validate_source(self.source)

    def test_staged_edit_is_also_rejected(self):
        (self.source / 'extra.cpp').write_text('int hidden = 1;\n')
        self.git('add', 'extra.cpp')
        with self.assertRaises(ValueError): recipe.validate_source(self.source)

    def test_untracked_source_cannot_enter_build(self):
        (self.source / 'extra.cpp').write_text('int hidden = 1;\n')
        with self.assertRaises(ValueError): recipe.validate_source(self.source)

    def test_patch_bytes_are_pinned(self):
        with (self.here / 'one.patch').open('a') as output: output.write('\n')
        with self.assertRaises(ValueError): recipe.validate_source(self.source)


class FreshCheckoutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        upstream = self.root / 'upstream'
        upstream.mkdir()
        here = self.root / 'recipe'
        here.mkdir()
        self.source = self.root / 'checkout'
        subprocess.run(['git', 'init', '-q', upstream], check=True)
        (upstream / 'example.cpp').write_text('int value = 1;\n')
        self.git(upstream, 'add', 'example.cpp')
        self.git(upstream, '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
                 'commit', '-qm', 'baseline')
        commit = self.git(upstream, 'rev-parse', 'HEAD')
        (upstream / 'example.cpp').write_text('int value = 2;\n')
        (upstream / 'added').mkdir()
        (upstream / 'added/source.cpp').write_text('int added = 3;\n')
        (upstream / 'added/source.h').write_text('extern int added;\n')
        self.git(upstream, 'add', '--intent-to-add', 'added')
        self.patch = self.git(upstream, 'diff', 'HEAD', '--no-ext-diff', '--binary')
        (here / 'one.patch').write_text(self.patch + '\n')
        self.git(upstream, 'reset', '--hard', 'HEAD')
        old_here, old_lock = recipe.HERE, recipe.LOCK
        self.addCleanup(setattr, recipe, 'HERE', old_here)
        self.addCleanup(setattr, recipe, 'LOCK', old_lock)
        recipe.HERE = here
        recipe.LOCK = {'repository': str(upstream), 'commit': commit, 'patches': ['one.patch']}
        # Exercise the production clone/apply path with a local repository only.
        recipe.checkout(self.source)

    def git(self, source, *args):
        return subprocess.run(['git', *args], cwd=source, check=True, text=True,
                              stdout=subprocess.PIPE).stdout.strip()

    def test_checkout_includes_added_sources_in_pinned_diff(self):
        recipe.validate_source(self.source)
        self.assertEqual(self.git(self.source, 'diff', 'HEAD', '--no-ext-diff', '--binary'), self.patch)
        self.assertEqual((self.source / 'added/source.cpp').read_text(), 'int added = 3;\n')
        self.assertEqual((self.source / 'added/source.h').read_text(), 'extern int added;\n')
        self.assertEqual(self.git(self.source, 'ls-files', '--others', '--exclude-standard'),
                         '.prime-source.json')

    def test_checkout_rejects_additional_source(self):
        recipe.validate_source(self.source)
        (self.source / 'added/extra.cpp').write_text('int hidden = 1;\n')
        with self.assertRaisesRegex(ValueError, 'Unrecorded source files'):
            recipe.validate_source(self.source)

    def test_checkout_rejects_additional_modification(self):
        recipe.validate_source(self.source)
        (self.source / 'added/source.cpp').write_text('int added = 4;\n')
        with self.assertRaisesRegex(ValueError, 'Source modifications differ'):
            recipe.validate_source(self.source)


if __name__ == '__main__': unittest.main()
