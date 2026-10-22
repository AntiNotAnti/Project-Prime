#!/usr/bin/env python3
"""Export path-sanitized GL receipts without changing their numeric observations.

The source bundle remains external. Hashes/sizes identify original and derivative
payloads separately; matching numeric projections do not imply byte equality.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def numeric_projection(value, prefix='$'):
    rows = []
    if isinstance(value, dict):
        for key in sorted(value):
            rows.extend(numeric_projection(value[key], prefix + '.' + key))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            rows.extend(numeric_projection(item, f'{prefix}[{index}]'))
    elif isinstance(value, (int, float)) and not isinstance(value, bool):
        rows.append([prefix, value])
    return rows


def projection(raw, suffix):
    if suffix == '.json':
        rows = numeric_projection(json.loads(raw))
        scope = 'Every numeric JSON leaf, identified by object key/list index; string values excluded'
    else:
        rows = []
        for index, line in enumerate(raw.decode().splitlines()):
            match = re.search(r'\[([\w-]+-json)\]\s*(\{.*)', line)
            if match:
                rows.extend(numeric_projection(json.loads(match[2]), f'line[{index}].{match[1]}'))
        scope = 'Every numeric leaf in structured *-json log events; unstructured diagnostic text excluded'
    payload = json.dumps(rows, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()
    return {'sha256': digest(payload), 'numericLeaves': len(rows), 'scope': scope}


def replace_paths(raw, workspace):
    text = raw.decode()
    if workspace:
        text = text.replace(workspace, '${WORKSPACE}')
    text = re.sub(r'/Users/[^/\s"\'\\]+', '${HOME}', text)
    text = re.sub(r'/home/[^/\s"\'\\]+', '${HOME}', text)
    text = re.sub(r'[A-Za-z]:\\\\Users\\\\[^\\\s"\']+', '${HOME}', text)
    return text.encode()


def export(source, output):
    source, output = Path(source), Path(output)
    if output.exists():
        raise ValueError('output must be fresh; keep original external payloads')
    original_manifest = (source / 'manifest.json').read_bytes()
    manifest = json.loads(original_manifest)
    command = manifest['records'][0]['command'][1]
    workspace = command.split('/work/', 1)[0] if '/work/' in command else ''
    output.mkdir(parents=True)
    files = []
    for entry in manifest['files']:
        name = entry['file']
        original = (source / name).read_bytes()
        if digest(original) != entry['sha256']:
            raise ValueError('original source inventory differs: ' + name)
        derivative = replace_paths(original, workspace)
        before = projection(original, Path(name).suffix)
        after = projection(derivative, Path(name).suffix)
        if before != after:
            raise ValueError('numeric observation changed during filtering: ' + name)
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(derivative)
        files.append({'file': name, 'sha256': digest(derivative), 'derivativeBytes': len(derivative),
                      'originalSha256': digest(original), 'originalBytes': len(original),
                      'pathFiltered': original != derivative, 'numericProjection': before,
                      'numericEquality': True})
    manifest = json.loads(replace_paths(original_manifest, workspace))
    manifest['files'] = files
    by_name = {row['file']: row for row in files}
    for record in manifest['records']:
        for kind in ('result', 'log'):
            entry = by_name[record[kind]]
            record['original' + kind.title() + 'Sha256'] = record[kind + 'Sha256']
            record[kind + 'Sha256'] = entry['sha256']
    for campaign in manifest.get('excludedCampaigns', []):
        campaign['files'] = [by_name[row['file']] for row in campaign['files']]
    manifest['sanitization'] = {'schema': 1, 'originalManifestSha256': digest(original_manifest),
                                'originalManifestBytes': len(original_manifest),
                                'replacementRoots': ['${WORKSPACE}', '${HOME}'],
                                'scope': 'Private host-path strings only. Numeric result/counter observations unchanged. Command paths are labels, not local dependencies.',
                                'originalPayloads': 'Retained external; not bundled. Original and derivative digests/lengths are distinct.'}
    manifest['limits'] = manifest['limits'].replace('Raw result JSONs/logs are exact copies.',
        'Bundled JSONs/logs are path-sanitized derivatives; original and derivative hashes/lengths plus numeric-projection equality are recorded.')
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


def verify(root, original_root=None, manifest=None):
    root = Path(root)
    if manifest is None:
        manifest = json.loads((root / 'manifest.json').read_text())
    if manifest.get('sanitization', {}).get('schema') != 1:
        raise ValueError('missing derivative provenance')
    for row in manifest['files']:
        target = (root / row['file']).resolve()
        if not target.is_relative_to(root.resolve()):
            raise ValueError('derivative path escapes bundle')
        if re.fullmatch('[0-9a-f]{64}', row.get('originalSha256', '')) is None or row.get('originalBytes', 0) <= 0:
            raise ValueError('missing original fingerprint: ' + row['file'])
        raw = target.read_bytes()
        if digest(raw) != row['sha256'] or len(raw) != row['derivativeBytes']:
            raise ValueError('changed derivative digest/length: ' + row['file'])
        if projection(raw, Path(row['file']).suffix) != row['numericProjection'] or row['numericEquality'] is not True:
            raise ValueError('numeric projection differs: ' + row['file'])
        if re.search(rb'/(?:Users|home)/[^/\s"\\]+', raw):
            raise ValueError('private host root remains: ' + row['file'])
        if original_root is not None:
            original = (Path(original_root) / row['file']).read_bytes()
            if digest(original) != row['originalSha256'] or len(original) != row['originalBytes']:
                raise ValueError('original source digest/length differs: ' + row['file'])
            if projection(original, Path(row['file']).suffix) != row['numericProjection']:
                raise ValueError('original numeric projection differs: ' + row['file'])
    if original_root is not None:
        original = (Path(original_root) / 'manifest.json').read_bytes()
        if digest(original) != manifest['sanitization']['originalManifestSha256'] or len(original) != manifest['sanitization']['originalManifestBytes']:
            raise ValueError('original manifest fingerprint differs')
    if re.search(r'/(?:Users|home)/[^/\s"\\]+', (root / 'manifest.json').read_text()):
        raise ValueError('private host root remains in derivative manifest')
    return {'schema': 1, 'status': 'PASS', 'derivativeFiles': len(manifest['files']),
            'originalsVerified': original_root is not None,
            'scope': 'Original/derivative SHA256+bytes and scoped numeric equality; no native execution or new image comparison'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    create = sub.add_parser('export')
    create.add_argument('source', type=Path)
    create.add_argument('output', type=Path)
    check = sub.add_parser('verify')
    check.add_argument('root', type=Path)
    check.add_argument('--original-root', type=Path)
    args = parser.parse_args()
    try:
        report = export(args.source, args.output) if args.command == 'export' else verify(args.root, args.original_root)
        print(json.dumps(report if args.command == 'verify' else {'status': 'EXPORTED', 'files': len(report['files'])}, indent=2))
        return 0
    except (OSError, KeyError, ValueError, TypeError) as error:
        print('SANITIZED GL EVIDENCE INVALID: ' + str(error))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
