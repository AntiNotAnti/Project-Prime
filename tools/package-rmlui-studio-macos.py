#!/usr/bin/env python3
"""Create an independently sealed Studio sibling from a verified native game app."""
import argparse
import ctypes
import errno
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile

BOOTSTRAP = r'''
#include <mach-o/dyld.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
int main(int argc, char **argv) {
    char raw[PATH_MAX], self[PATH_MAX], runtime[PATH_MAX], root[PATH_MAX], client[PATH_MAX];
    uint32_t size = sizeof(raw);
    if (_NSGetExecutablePath(raw, &size) || !realpath(raw, self)) {
        perror("Cannot locate Project Prime Studio"); return 1;
    }
    char *slash = strrchr(self, '/');
    if (!slash) return 1;
    *slash = 0;
    if (snprintf(runtime,sizeof(runtime),"%s/../Resources/dotnet/dotnet",self) >= sizeof(runtime)
        || snprintf(root,sizeof(root),"%s/../Resources/dotnet",self) >= sizeof(root)
        || snprintf(client,sizeof(client),"%s/../Resources/client/ProjectPrime.dll",self) >= sizeof(client)) return 1;
    if (setenv("DOTNET_ROOT",root,1) || setenv("DOTNET_ROOT_ARM64",root,1)
        || setenv("DOTNET_ROOT_X64",root,1) || setenv("DOTNET_MULTILEVEL_LOOKUP","0",1)
        || setenv("PROJECT_PRIME_RMLUI_GL_SMOKE","1",1)) {
        perror("Cannot configure bundled Studio runtime"); return 1;
    }
    char **next = calloc((size_t)argc+3,sizeof(char*));
    if (!next) return 1;
    next[0]=runtime; next[1]=client; next[2]="--studio";
    for (int i=1;i<argc;i++) next[i+2]=argv[i];
    execv(runtime,next);
    perror("Cannot start Project Prime Studio"); free(next); return 1;
}
'''
MACH_MAGICS = {bytes.fromhex(x) for x in ('feedface','cefaedfe','feedfacf','cffaedfe','cafebabe','bebafeca','cafebabf','bfbafeca')}


def run(*args, capture=False):
    return subprocess.run([str(a) for a in args], check=True,
                          stdout=subprocess.PIPE if capture else None).stdout


def studio_plist(original, icon=None):
    result = dict(original)
    result.update(CFBundleExecutable='ProjectPrimeStudio', CFBundleName='Project Prime Studio',
                  CFBundleDisplayName='Project Prime Studio', CFBundleIdentifier='com.projectprime.studio')
    for key in ('CFBundleDocumentTypes', 'CFBundleURLTypes', 'UTExportedTypeDeclarations',
                'UTImportedTypeDeclarations'):
        result.pop(key, None)
    if icon:
        result['CFBundleIconFile'] = 'ProjectPrimeStudio.icns'
    return result


def macho(path):
    if path.is_symlink() or not path.is_file():
        return False
    with path.open('rb') as stream:
        return stream.read(4) in MACH_MAGICS


def validate_links(bundle):
    for path in bundle.rglob('*'):
        if path.is_symlink() and not path.resolve().is_relative_to(bundle):
            raise ValueError(f'Bundle contains an external symbolic link: {path.relative_to(bundle)}')


def package(game, output, icon=None):
    if sys.platform != 'darwin':
        raise ValueError('Packaging and signing require macOS.')
    game = game.resolve(strict=True)
    output = output.absolute()
    if game.suffix != '.app' or output.suffix != '.app' or not game.is_dir():
        raise ValueError('Both arguments must name .app bundles.')
    if output.exists() or output.is_symlink():
        raise ValueError('Output already exists; choose a new output app.')
    if output.parent.resolve() != game.parent:
        raise ValueError('Studio output must be a sibling of the verified game app.')
    output = game.parent / output.name
    validate_links(game)
    info = plistlib.loads((game/'Contents/Info.plist').read_bytes())
    executable = game/'Contents/MacOS'/info['CFBundleExecutable']
    if not macho(executable):
        raise ValueError('Expected a native Mach-O game bootstrap.')
    for required in ('Contents/Resources/dotnet/dotnet', 'Contents/Resources/client/ProjectPrime.dll'):
        if not (game/required).is_file():
            raise ValueError(f'Missing bundled runtime/client: {required}')
    run('/usr/bin/codesign','--verify','--deep','--strict', game)
    architectures = run('/usr/bin/lipo','-archs', executable, capture=True).decode().strip().split()
    if not architectures or any(a not in ('arm64','x86_64') for a in architectures):
        raise ValueError('Only arm64 and x86_64 game bootstraps are supported.')
    if icon is not None:
        icon = icon.resolve(strict=True)
        if icon.read_bytes()[:4] != b'icns':
            raise ValueError('Studio icon must be an existing ICNS file.')
    clone = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True).clonefile
    clone.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int]
    clone.restype = ctypes.c_int
    counts = {'cloned':0, 'copied':0}

    def independent_copy(source, target):
        if clone(os.fsencode(source), os.fsencode(target), 0) == 0:
            counts['cloned'] += 1
        else:
            failure = ctypes.get_errno()
            if failure not in (errno.ENOTSUP, errno.EXDEV, errno.EINVAL):
                raise OSError(failure, os.strerror(failure), str(source))
            shutil.copy2(source, target)
            counts['copied'] += 1
        if os.stat(source).st_ino == os.stat(target).st_ino:
            raise ValueError('A copied runtime file unexpectedly shares its source inode.')
        return str(target)

    # A failed package remains inspectable at its new output path; never replace a prior app.
    shutil.copytree(game, output, symlinks=True, copy_function=independent_copy)
    signature = output/'Contents/_CodeSignature'
    if signature.exists():
        shutil.rmtree(signature)
    old = output/'Contents/MacOS'/info['CFBundleExecutable']
    old.unlink()
    bootstrap = output/'Contents/MacOS/ProjectPrimeStudio'
    with tempfile.TemporaryDirectory(prefix='prime-studio-bootstrap-') as temporary:
        temporary = Path(temporary)
        source = temporary/'studio.c'
        source.write_text(BOOTSTRAP)
        entitlement = temporary/'entitlements.plist'
        # Preserve the input launcher's exact entitlement policy (normally allow-jit).
        data = run('/usr/bin/codesign','--display','--entitlements',':-', executable,capture=True)
        plistlib.loads(data)  # Reject missing/malformed policy before signing.
        entitlement.write_bytes(data)
        args = ['/usr/bin/xcrun','clang','-Os','-mmacosx-version-min=11.0']
        for architecture in architectures:
            args += ['-arch',architecture]
        run(*args,source,'-o',bootstrap)
        bootstrap.chmod(0o755)
        if icon:
            shutil.copy2(icon, output/'Contents/Resources/ProjectPrimeStudio.icns')
        with (output/'Contents/Info.plist').open('wb') as stream:
            plistlib.dump(studio_plist(info, icon),stream,sort_keys=True)
        run('/usr/bin/codesign','--force','--sign','-','--identifier','com.projectprime.studio',
            '--entitlements',entitlement,bootstrap)
        run('/usr/bin/codesign','--force','--sign','-','--entitlements',entitlement,output)
    for path in output.rglob('*'):
        if macho(path):
            run('/usr/bin/codesign','--verify','--strict',path)
    run('/usr/bin/codesign','--verify','--deep','--strict','--verbose=2',output)
    run('/usr/bin/codesign','--verify','--deep','--strict',game)
    print(f'Studio app: {output}\nIndependent files: {counts["cloned"]} APFS clones, {counts["copied"]} copies.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game_app',type=Path)
    parser.add_argument('studio_app',type=Path)
    parser.add_argument('--icon',type=Path,help='Optional Studio .icns; existing game icon otherwise retained')
    args = parser.parse_args()
    try:
        package(args.game_app,args.studio_app,args.icon)
    except (ValueError,OSError,subprocess.CalledProcessError) as error:
        parser.exit(1,f'error: {error}\n')


if __name__ == '__main__':
    main()
