#!/usr/bin/env python3
"""Package an existing native ARM64 Radiant candidate; never builds or downloads.

Requires the locally installed Qt 5 macdeployqt and Apple's command line tools.
The result is an ad-hoc signed qualification candidate, with GUI acceptance pending.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import struct
import subprocess
import tempfile

MACHO_MAGICS = {bytes.fromhex(x) for x in (
    'feedface', 'cefaedfe', 'feedfacf', 'cffaedfe',
    'cafebabe', 'bebafeca', 'cafebabf', 'bfbafeca')}
SYSTEM_ROOTS = ('/usr/lib/', '/System/Library/', '/Library/Apple/System/Library/')
FORBIDDEN_SUFFIXES = {'.pk3', '.wad', '.bsp', '.ppmap', '.rom', '.nds'}
LOAD_COMMANDS = {'LC_LOAD_DYLIB', 'LC_LOAD_WEAK_DYLIB', 'LC_REEXPORT_DYLIB',
                 'LC_LOAD_UPWARD_DYLIB', 'LC_LAZY_LOAD_DYLIB'}


def run(args):
    return subprocess.run([str(x) for x in args], check=True, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout.strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def macho(path):
    if path.is_symlink() or not path.is_file():
        return False
    with path.open('rb') as stream:
        return stream.read(4) in MACHO_MAGICS


def binaries(root):
    return sorted(p for p in root.rglob('*') if macho(p))


def inside(path, root):
    return path.resolve().is_relative_to(root.resolve())


def check_links(root):
    for path in root.rglob('*'):
        if path.is_symlink():
            try:
                target = path.resolve(strict=True)
            except (OSError, RuntimeError) as exc:
                raise ValueError('Broken or cyclic symlink: ' + str(path)) from exc
            if not inside(target, root):
                raise ValueError('External symlink: ' + str(path))


def commands(path):
    """Read dependency load commands (excluding a dylib's own install name)."""
    deps, rpaths, identity = [], [], None
    command = None
    for line in run(['/usr/bin/otool', '-l', path]).splitlines():
        line = line.strip()
        if line.startswith('cmd '):
            command = line[4:]
        elif command in LOAD_COMMANDS | {'LC_ID_DYLIB'} and line.startswith('name '):
            name = line[5:].rsplit(' (offset ', 1)[0]
            if command == 'LC_ID_DYLIB':
                identity = name
            else:
                deps.append(name)
        elif command == 'LC_RPATH' and line.startswith('path '):
            rpaths.append(line[5:].rsplit(' (offset ', 1)[0])
    return deps, rpaths, identity


def system_dependency(name):
    return name.startswith(SYSTEM_ROOTS)


def expanded(name, loader, executable):
    if name == '@loader_path' or name.startswith('@loader_path/'):
        return loader.parent / name[len('@loader_path'):].lstrip('/')
    if name == '@executable_path' or name.startswith('@executable_path/'):
        return executable.parent / name[len('@executable_path'):].lstrip('/')
    if name.startswith('/'):
        return Path(name)
    return None


class Relocator:
    """Close the dependency graph macdeployqt can miss (e.g. Homebrew glib)."""
    def __init__(self, app, candidate, qt, origins, original_commands):
        self.app, self.candidate, self.qt = app, candidate, qt
        self.executable = app / 'Contents/MacOS/radiant'
        self.frameworks = app / 'Contents/Frameworks'
        self.frameworks.mkdir(exist_ok=True)
        self.origins, self.original_commands = origins, original_commands
        self.copied = {}
        self.dest_sources = {}
        self.deployed = []
        self.index_originals()

    def index_originals(self):
        # macdeployqt may copy a library while leaving its children's runpaths
        # unchanged. Recover origins from the untouched input dependency graph,
        # including plugins macdeployqt selected automatically.
        for path in binaries(self.app / 'Contents/PlugIns'):
            original = self.qt / 'plugins' / path.relative_to(self.app / 'Contents/PlugIns')
            if original.is_file():
                self.origins[path] = original
        queue = list(self.origins.values())
        seen, by_name = set(), {}
        main = self.candidate / 'radiant.arm64'
        main_rpaths = commands(main)[1]
        while queue:
            original = queue.pop()
            if not original.is_file():
                continue
            canonical = original.resolve()
            by_name.setdefault(original.name, set()).add(canonical)
            by_name.setdefault(canonical.name, set()).add(canonical)
            if canonical in seen:
                continue
            seen.add(canonical)
            info = commands(original)
            self.original_commands[original] = info
            self.original_commands[canonical] = info
            for dep in info[0]:
                if system_dependency(dep):
                    continue
                target = expanded(dep, original, main)
                if target is None and dep.startswith('@rpath/'):
                    suffix = dep[len('@rpath/'):]
                    bases = [expanded(p, original, main) for p in info[1]]
                    bases += [expanded(p, main, main) for p in main_rpaths]
                    bases.append(self.qt / 'lib')
                    target = next((base / suffix for base in bases
                                   if base is not None and (base / suffix).exists()), None)
                if target is not None and target.is_file():
                    by_name.setdefault(Path(dep).name, set()).add(target.resolve())
                    queue.append(target)
        for path in binaries(self.app):
            if path in self.origins:
                continue
            candidates = by_name.get(path.name, set())
            if len(candidates) == 1:
                self.origins[path] = next(iter(candidates))

    def resolve(self, name, loader, rpaths):
        direct = expanded(name, loader, self.executable)
        if direct is not None and direct.exists():
            return direct.resolve()
        if not name.startswith('@rpath/'):
            raise ValueError(f'Unresolved dependency {name} in {loader}')
        suffix = name[len('@rpath/'):]
        contexts = [(loader, self.executable, rpaths)]
        origin = self.origins.get(loader)
        if origin:
            contexts.append((origin, self.candidate / 'radiant.arm64',
                             self.original_commands.get(origin, ([], [], None))[1]))
        # The executable's runpath stack applies to dynamically loaded modules.
        for executable in (self.executable, self.candidate / 'radiant.arm64'):
            if executable.exists() and macho(executable):
                contexts.append((executable, executable, commands(executable)[1]))
        for context_loader, executable, paths in contexts:
            for entry in paths:
                base = expanded(entry, context_loader, executable)
                if base is not None and (base / suffix).exists():
                    return (base / suffix).resolve()
        for base in (self.frameworks, self.qt / 'lib'):
            if (base / suffix).exists():
                return (base / suffix).resolve()
        raise ValueError(f'Unresolved runpath dependency {name} in {loader}')

    def copy_dependency(self, source):
        source = source.resolve(strict=True)
        if source in self.copied:
            return self.copied[source]
        framework = next((p for p in source.parents if p.suffix == '.framework'), None)
        if framework:
            target_root = self.frameworks / framework.name
            relative = source.relative_to(framework)
            source_root = framework
        else:
            target_root = self.frameworks / source.name
            relative = Path('.')
            source_root = source
        if target_root.exists():
            # Never silently merge two different libraries with the same name.
            previous = self.dest_sources.get(target_root)
            if previous != source_root:
                raise ValueError(f'Dependency destination collision: {target_root} ({source})')
        else:
            if framework:
                check_links(framework)
                shutil.copytree(framework, target_root, symlinks=True)
                for item in binaries(target_root):
                    self.origins[item] = framework / item.relative_to(target_root)
            else:
                shutil.copy2(source, target_root)
                self.origins[target_root] = source
            self.dest_sources[target_root] = source_root
            self.deployed.append({'source': str(source_root),
                                  'destination': str(target_root.relative_to(self.app)),
                                  'sourceBinarySha256': sha256(source)})
        target = target_root / relative if framework else target_root
        self.copied[source] = target
        return target

    def relocate(self):
        done = set()
        while True:
            pending = [p for p in binaries(self.app) if p not in done]
            if not pending:
                break
            for path in pending:
                deps, rpaths, identity = commands(path)
                changes = []
                for name in deps:
                    if system_dependency(name):
                        continue
                    source = self.resolve(name, path, rpaths)
                    target = source if inside(source, self.app) else self.copy_dependency(source)
                    replacement = '@loader_path/' + os.path.relpath(target, path.parent)
                    if name != replacement:
                        changes += ['-change', name, replacement]
                if identity:
                    changes += ['-id', '@rpath/' + path.name]
                # Every dependency now has a concrete loader-relative reference.
                for rpath in set(rpaths):
                    changes += ['-delete_rpath', rpath]
                if changes:
                    path.chmod(path.stat().st_mode | 0o200)
                    run(['/usr/bin/install_name_tool', *changes, path])
                done.add(path)
        check_links(self.app)
        graph = {}
        for path in binaries(self.app):
            archs = run(['/usr/bin/lipo', '-archs', path]).split()
            if 'arm64' not in archs:
                raise ValueError('Missing ARM64 slice: ' + str(path))
            deps, rpaths, identity = commands(path)
            if rpaths:
                raise ValueError('Unexpected remaining runpaths: ' + str(path))
            for dep in deps:
                if system_dependency(dep):
                    continue
                target = expanded(dep, path, self.executable)
                if target is None or not target.exists() or not inside(target, self.app):
                    raise ValueError(f'External or unresolved dependency {dep} in {path}')
            graph[str(path.relative_to(self.app))] = {'architectures': archs, 'dependencies': deps}
        return graph


def flatten_frameworks(app, origins):
    """Keep updater ownership symlink-free; Qt's framework aliases need links.

    dyld loads the same Qt Mach-O images as plain dylibs. All framework data
    files and notices survive under Resources; dependency edges are rewritten.
    """
    frameworks = sorted((app / 'Contents/Frameworks').glob('*.framework'))
    replacements, records, copied_loaders = {}, [], {}
    for framework in frameworks:
        images = binaries(framework)
        if len(images) != 1:
            raise ValueError('Expected one Qt framework image: ' + str(framework))
        original = images[0]
        destination = framework.parent / (framework.stem + '.dylib')
        if destination.exists():
            raise ValueError('Flat framework destination collision: ' + str(destination))
        shutil.copy2(original, destination)
        replacements[original.resolve()] = destination
        copied_loaders[destination] = original
        if original in origins:
            origins[destination] = origins[original]
        metadata = app / 'Contents/Resources/qt-framework-metadata' / framework.stem
        resource_hashes = {}
        for item in framework.rglob('*'):
            if item.is_symlink() or not item.is_file() or item == original or '_CodeSignature' in item.parts:
                continue
            target = metadata / item.relative_to(framework)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(item, target)
            resource_hashes[str(target.relative_to(app))] = sha256(item)
        records.append({'framework': framework.name,
                        'binary': str(destination.relative_to(app)),
                        'resourceFilesSha256': resource_hashes})
    for image in binaries(app):
        if any(inside(image, framework) for framework in frameworks):
            continue
        deps, _, identity = commands(image)
        changes = []
        for dep in deps:
            if system_dependency(dep):
                continue
            loader = copied_loaders.get(image, image)
            target = expanded(dep, loader, app / 'Contents/MacOS/radiant')
            if target is not None:
                replacement = replacements.get(target.resolve(), target)
                changes += ['-change', dep, '@loader_path/' + os.path.relpath(replacement, image.parent)]
        if identity:
            changes += ['-id', '@rpath/' + image.name]
        if changes:
            run(['/usr/bin/install_name_tool', *changes, image])
    for framework in frameworks:
        shutil.rmtree(framework)
    if any(path.is_symlink() for path in app.rglob('*')):
        raise ValueError('Native app must be symlink-free for paired updater ownership.')
    return records


def sign(app):
    for path in binaries(app):
        if path != app / 'Contents/MacOS/radiant':
            run(['/usr/bin/codesign', '--force', '--sign', '-', '--timestamp=none', path])
    frameworks = sorted(app.rglob('*.framework'), key=lambda p: len(p.parts), reverse=True)
    for path in frameworks:
        run(['/usr/bin/codesign', '--force', '--sign', '-', '--timestamp=none', path])
    run(['/usr/bin/codesign', '--force', '--sign', '-', '--timestamp=none', app])
    for path in [*binaries(app), *frameworks]:
        run(['/usr/bin/codesign', '--verify', '--strict', path])
    run(['/usr/bin/codesign', '--verify', '--deep', '--strict', app])


def validate_prebuilt(app, rid):
    """Validate the optional third app without granting a global image exemption."""
    if rid not in ('osx-arm64', 'osx-x64'):
        raise ValueError('Unsupported native app RID: ' + rid)
    arch = 'arm64' if rid == 'osx-arm64' else 'x86_64'
    if not app.is_dir() or app.is_symlink():
        raise ValueError('Radiant input must be a regular app directory.')
    plist = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if (plist.get('CFBundleIdentifier') != 'com.projectprime.radiant'
            or plist.get('CFBundleExecutable') != 'radiant'):
        raise ValueError('Radiant bundle identifier/executable does not match the native contract.')
    for path in app.rglob('*'):
        if path.is_symlink():
            raise ValueError('Paired native app cannot contain symbolic links: ' + str(path))
        if path.suffix.lower() in FORBIDDEN_SUFFIXES | {'.bin', '.arc', '.narc', '.sdat', '.sbin', '.spc', '.wav', '.mp3', '.ogg', '.brstm'}:
            raise ValueError('Native app contains disallowed game/cartridge data: ' + str(path))
    resources = app / 'Contents/Resources/netradiant'
    receipt_path = resources / 'macos-package-receipt.json'
    if receipt_path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError('Native package receipt exceeds its size budget.')
    receipt = json.loads(receipt_path.read_text())
    if (receipt.get('schema') != 1 or receipt.get('bundleIdentifier') != 'com.projectprime.radiant'
            or receipt.get('validation', {}).get('symlinkFree') != 'passed'):
        raise ValueError('Native package provenance/flat-layout validation is missing.')
    for name in ('LICENSE', 'GPL', 'LGPL', 'upstream.lock.json',
                 'prime-build-receipt.json', 'upstream-source.tar'):
        if not (resources / 'source-notices' / name).is_file():
            raise ValueError('Missing native corresponding source/notices: ' + name)
    source_script = resources / 'source-notices/macos-packaging/package_macos.py'
    if sha256(source_script) != receipt.get('packagingScriptSha256'):
        raise ValueError('Native packaging source differs from its provenance receipt.')
    for name in ('radiant', 'q3map2'):
        path = app / 'Contents/MacOS' / name
        if not macho(path) or not os.access(path, os.X_OK):
            raise ValueError('Missing native executable: ' + str(path))
    graph = receipt.get('dependencyGraph', {})
    images = binaries(app)
    if set(graph) != {str(p.relative_to(app)) for p in images}:
        raise ValueError('Native dependency receipt does not match its binary file set.')
    for image in images:
        run(['/usr/bin/lipo', image, '-verify_arch', arch])
        run(['/usr/bin/codesign', '--verify', '--strict', image])
        deps, rpaths, _ = commands(image)
        if rpaths or deps != graph[str(image.relative_to(app))].get('dependencies'):
            raise ValueError('Native dependency load commands differ from its receipt: ' + str(image))
        for dep in deps:
            if system_dependency(dep):
                continue
            target = expanded(dep, image, app / 'Contents/MacOS/radiant')
            if target is None or not target.is_file() or not inside(target, app):
                raise ValueError('Native dependency escapes its app: ' + dep)
    # Source-backed editor artwork is distinct from cartridge/media payloads.
    # Upstream editor images must match the pinned build receipt. The one
    # Project Prime texture is the deterministic original graybox checkerboard.
    pixels = b''.join(bytes([96 if ((x//8+y//8)%2) else 144])*3
                      for y in range(64) for x in range(64))
    checker = hashlib.sha256(struct.pack('<BBBHHBHHHHBB', 0,0,2,0,0,0,0,0,64,64,24,0x20) + pixels).hexdigest()
    checker_name = 'gamepacks/projectprime.game/baseq3/textures/prime/gray.tga'
    build_images = receipt.get('buildReceipt', {}).get('binaries', {})
    candidate_files = receipt.get('candidateFilesSha256', {})
    metadata = {name: digest for framework in receipt.get('flattenedFrameworks', [])
                for name, digest in framework.get('resourceFilesSha256', {}).items()}
    for path in app.rglob('*'):
        if not path.is_file() or path.suffix.lower() not in {'.png', '.jpg', '.jpeg', '.gif', '.bmp', '.tga', '.dds'}:
            continue
        actual = sha256(path)
        if inside(path, resources):
            name = str(path.relative_to(resources))
            expected = checker if name == checker_name else build_images.get(name)
            if actual == expected and candidate_files.get(name) == expected:
                continue
        elif actual == metadata.get(str(path.relative_to(app))):
            continue
        raise ValueError('Native image lacks original editor/fixture provenance: ' + str(path))
    run(['/usr/bin/codesign', '--verify', '--deep', '--strict', app])
    print('Validated native Radiant identity, source-backed resources, signatures and ' + rid + ' dependency closure.')


def publish_exclusively(source, output):
    # renamex_np(RENAME_EXCL) keeps the no-overwrite guarantee even under a race.
    libc = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
    rename = libc.renamex_np
    rename.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    if rename(os.fsencode(source), os.fsencode(output), 0x00000004):
        error = ctypes.get_errno()
        raise OSError(error, os.strerror(error), str(output))


def package(candidate, output, qt):
    if platform.system() != 'Darwin' or platform.machine() != 'arm64':
        raise ValueError('Packaging requires a native ARM64 macOS host.')
    if output.suffix != '.app' or output.exists() or output.is_symlink():
        raise ValueError('--output must be a nonexistent .app path.')
    if not candidate.is_dir() or inside(output, candidate):
        raise ValueError('Candidate must be a directory outside the output.')
    check_links(candidate)
    for path in candidate.rglob('*'):
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            raise ValueError('Candidate contains disallowed game/cartridge data: ' + str(path))
    notices = candidate / 'source-notices'
    for name in ('LICENSE', 'GPL', 'LGPL', 'upstream.lock.json',
                 'prime-build-receipt.json', 'upstream-source.tar', 'patches', 'ProjectPrimeSource'):
        if not (notices / name).exists():
            raise ValueError('Missing corresponding source/notices: ' + name)
    build_receipt = json.loads((notices / 'prime-build-receipt.json').read_text())
    if build_receipt.get('compilerOnly'):
        raise ValueError('An editor candidate is required, not a compiler-only build.')
    hosts = {name: candidate / (name + '.arm64') for name in ('radiant', 'q3map2')}
    for name, path in hosts.items():
        if not macho(path) or 'arm64' not in run(['/usr/bin/lipo', '-archs', path]).split():
            raise ValueError('Missing native ARM64 apphost: ' + str(path))
    qt = qt.resolve(strict=True)
    version = run([qt / 'bin/qmake', '-query', 'QT_VERSION'])
    if not version.startswith('5.'):
        raise ValueError('Qt 5 required; selected version is ' + version)
    deployer = qt / 'bin/macdeployqt'
    if not deployer.is_file():
        raise ValueError('Qt 5 macdeployqt is missing.')
    for tool in ('otool', 'lipo', 'install_name_tool', 'codesign'):
        if not (Path('/usr/bin') / tool).is_file():
            raise ValueError('Missing Apple tool: ' + tool)
    input_hashes = {str(p.relative_to(candidate)): sha256(p)
                    for p in candidate.rglob('*') if p.is_file() and not p.is_symlink()}
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.prime-radiant-package-', dir=output.parent) as temporary:
        app = Path(temporary) / output.name
        contents = app / 'Contents'
        macos = contents / 'MacOS'
        macos.mkdir(parents=True)
        resources = contents / 'Resources/netradiant'
        shutil.copytree(candidate, resources, symlinks=True)
        origins, original_commands = {}, {}
        for path in binaries(resources):
            original = candidate / path.relative_to(resources)
            origins[path] = original
            original_commands[original] = commands(original)
        for name, original in hosts.items():
            destination = macos / name
            shutil.move(resources / original.name, destination)
            origins[destination] = original
            # Remove the upstream compatibility symlink or launcher, never keep
            # an additional executable copy outside the fixed apphost contract.
            alias = resources / name
            if alias.exists() or alias.is_symlink():
                alias.unlink()
        plist = {'CFBundleName': 'Project Prime Radiant',
                 'CFBundleDisplayName': 'Project Prime Radiant',
                 'CFBundleIdentifier': 'com.projectprime.radiant',
                 'CFBundleExecutable': 'radiant', 'CFBundlePackageType': 'APPL',
                 'CFBundleVersion': '1', 'CFBundleShortVersionString': '0.1.0',
                 'NSHighResolutionCapable': True,
                 'NSHumanReadableCopyright': 'NetRadiant contributors; GPL. See bundled source-notices.'}
        (contents / 'Info.plist').write_bytes(plistlib.dumps(plist))
        (contents / 'Resources/qt.conf').write_text('[Paths]\nPlugins = PlugIns\n')
        # Make platform + SVG support explicit, independent of macdeployqt's
        # framework/plugin inference. Include these in its executable scan.
        for name in ('platforms/libqcocoa.dylib', 'imageformats/libqsvg.dylib',
                     'iconengines/libqsvgicon.dylib'):
            original = qt / 'plugins' / name
            if not original.is_file():
                raise ValueError('Required Qt plugin is missing: ' + str(original))
            destination = contents / 'PlugIns' / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(original, destination)
            origins[destination] = original
            original_commands[original] = commands(original)
        extra = [p for p in binaries(app) if p != macos / 'radiant']
        deploy_log = run([deployer, app, '-verbose=2', '-no-strip',
                          *['-executable=' + str(p) for p in extra]])
        relocator = Relocator(app, candidate, qt, origins, original_commands)
        relocator.relocate()
        flattened = flatten_frameworks(app, origins)
        graph = relocator.relocate()
        provenance = resources / 'source-notices/macos-packaging'
        provenance.mkdir()
        shutil.copy2(Path(__file__).resolve(), provenance / 'package_macos.py')
        (provenance / 'macdeployqt.log').write_text(deploy_log + '\n')
        receipt = {'schema': 1, 'bundleIdentifier': plist['CFBundleIdentifier'],
                   'platform': platform.platform(), 'qtVersion': version,
                   'qtPrefix': str(qt), 'buildReceipt': build_receipt,
                   'candidateFilesSha256': input_hashes,
                   'dependencyGraph': graph, 'fallbackDeployments': relocator.deployed,
                   'frameworkLayout': 'flat dylibs; metadata preserved in Resources',
                   'flattenedFrameworks': flattened,
                   'packagingScriptSha256': sha256(Path(__file__).resolve()),
                   'validation': {'arm64Slices': 'passed', 'dependencyClosure': 'passed',
                                  'internalSymlinks': 'passed', 'symlinkFree': 'passed'},
                   'signature': 'ad-hoc; verified before exclusive publication',
                   'guiAcceptance': 'pending', 'redistributionReview': 'pending',
                   'notarization': 'not performed'}
        (resources / 'macos-package-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
        sign(app)
        publish_exclusively(app, output)
    # Verify the same seal after the final move as well.
    run(['/usr/bin/codesign', '--verify', '--deep', '--strict', output])
    print(str(output))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--validate-app', type=Path, help='Validate an already packaged native app without copying it')
    parser.add_argument('--rid', choices=['osx-arm64', 'osx-x64'])
    parser.add_argument('--qt-prefix', type=Path, default=Path('/opt/homebrew/opt/qt@5'))
    args = parser.parse_args()
    try:
        if args.validate_app is not None:
            if args.rid is None or args.candidate is not None or args.output is not None:
                parser.error('--validate-app requires --rid and cannot be combined with --candidate/--output')
            validate_prebuilt(args.validate_app.absolute(), args.rid)
        else:
            if args.candidate is None or args.output is None or args.rid is not None:
                parser.error('packaging requires --candidate and --output')
            package(args.candidate.resolve(), args.output.absolute(), args.qt_prefix)
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        detail = exc.stderr.strip() if isinstance(exc, subprocess.CalledProcessError) and exc.stderr else str(exc)
        parser.exit(1, 'Packaging failed: ' + detail + '\n')


if __name__ == '__main__':
    main()
