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


if __name__ == '__main__': unittest.main()
