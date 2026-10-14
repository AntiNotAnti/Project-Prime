#!/usr/bin/env python3
"""Compare unchanged native timed-pickup bytes from 42 fresh-process arms."""
import argparse
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys

from run_life_campaign import clean_environment, qualification_receipt, run_owned, sha

HUNTERS = ('Samus', 'Kanden', 'Spire', 'Noxus', 'Trace', 'Sylux', 'Weavel')
ITEMS = ('DoubleDamage', 'Cloak', 'Deathalt')
MODES = ('Off', 'Shadow')
PROOF_ONLY = ('Mode', 'CertificateKnowledge', 'Certificate')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate_arm(data, hunter, item, mode, engine_hash, engine_mvid, entry_hash):
    require(data.get('schema') == 3 and data.get('status') == 'PASS' and data.get('completedArms') == 1 and data.get('checks', 0) > 0, 'one complete actual native arm required')
    require(data.get('hunter') == hunter and data.get('item') == item and data.get('mode') == mode, 'requested arm identity mismatch')
    require(data.get('assemblySha256') == engine_hash and data.get('assemblyMvid', '').lower() == engine_mvid.lower() and data.get('entryAssemblySha256') == entry_hash and data.get('entryAssemblyMvid'), 'actual loaded engine/entry identity mismatch')
    arm = data['arm']
    require((arm['Hunter'], arm['Item'], arm['Mode']) == (hunter, item, mode), 'completed arm identity mismatch')
    contact = arm['ContactFrame']
    require(contact > 0 and arm['EffectiveFrame'] == contact + 1 and arm['ExpiryFrame'] == contact + 1800, 'native grant chronology missing')
    require(arm['NativeFrames'] == (1802 if item == 'Deathalt' else 1801) and arm['NativePresentation'] is True, 'native frame/presentation denominator missing')
    timers = [arm[name] for name in ('ContactTimer', 'EffectiveTimer', 'LastActiveTimer', 'ExpiredTimer')]
    require(timers == ([1799, 1798, 0, 0] if item == 'DoubleDamage' else [1800, 1799, 1, 0]), 'actual native timer boundary mismatch')
    spawn, life = arm['Spawn'], arm['Life']
    require(spawn['Room'] > 0 and spawn['Generation'] > 0 and spawn['Epoch'] == life['AuthorityEpoch'] and spawn['Match'] == life['MatchId'] and life['Generation'] > 0 and life['LifeId'] > 0 and life['ShotId'] == 0, 'actual native incarnation/life missing')
    outcome, fire, key = arm['Shot']['Outcome'], arm['Shot']['Fire'], arm['Shot']['Key']
    require(key['ShotId'] > 0 and all(key[field] == life[field] for field in ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId')) and fire['ShotId'] == key['ShotId'] and fire['SourceLife'] == key['LifeId'] and arm['GrantSequence'] > 0, 'actual full shot source identity mismatch')
    require(fire['SourceFrame'] == (contact if item == 'Deathalt' else contact + 1), 'truthful native fire phase mismatch')
    require(outcome['Damage'] > 0 and outcome['Fired'] == 1 and outcome['Semantic'] == 1 and outcome['ReplayWeaponFired'] == 1 and outcome['IntentBytes'] and outcome['ReplayFacts'] and fire['CanonicalBytes'], 'native emission/intent/replay positive denominator missing')
    records = json.loads(outcome['ReplayFacts'])
    require(0 < len(records) <= 32 and all(record['Payload'] for record in records), 'actual bounded replay records missing')
    certificate = arm['Certificate']
    if mode == 'Off':
        require(certificate is None and arm['CertificateKnowledge'] is None, 'Off must store no timed certificate')
    else:
        knowledge = 'Granted' if item == 'DoubleDamage' else 'NotGranted'
        require(certificate is not None and certificate['Knowledge'] == knowledge and arm['CertificateKnowledge'] == knowledge, 'Shadow certificate source truth mismatch')
        require(certificate['CaptureMode'] == 'Shadow' and certificate['Source']['Source'] == 'Verified' and certificate['Source']['Pose'] == 'Verified', 'actual source/pose proof and capture policy required')
        answer = certificate['LedgerAnswer']
        require(answer['Knowledge'] == knowledge and answer['Sequence'] == (arm['GrantSequence'] if item == 'DoubleDamage' else 0), 'raw ledger answer must agree with final certificate funding')
        require(certificate['Key'] == key and certificate['Fire'] == fire and certificate['Room'] == spawn['Room'] and certificate['Lane'] == 'NativeInput' and certificate['TrustedSource'] is True and certificate['Unavailable'] == 'None' and certificate['Components'] > 0 and certificate['Damage']['Present'] is True and certificate['Damage']['Body'] > 0, 'completed trusted native certificate required')
        funding = certificate['Funding']
        if item == 'DoubleDamage':
            require(certificate['AuthorizedDouble'] is True and funding is not None and funding['Type'] == 'DoubleDamage' and funding['Spawn'] == spawn and funding['Life'] == life and funding['Sequence'] == arm['GrantSequence'] and funding['ContactFrame'] == contact and funding['SourceContactFrame'] == contact and funding['ExpiresAtFrame'] == arm['ExpiryFrame'], 'Double must use exact genuine native funding')
        else:
            require(certificate['AuthorizedDouble'] is False and funding is None, 'Cloak/Death Alt must not authorize Double')
    return arm


def compare_pair(off, shadow):
    # Only intentional instrumentation policy/proof is different. All mechanical
    # fields, full native identities, Fire bytes, canonical IntentPacket bytes and
    # the complete original ReplayRecorder strings remain exactly untouched.
    first, second = copy.deepcopy(off), copy.deepcopy(shadow)
    for name in PROOF_ONLY:
        first.pop(name); second.pop(name)
    require(first == second, 'exact native outcome/identity/fire/intent/replay/timer parity failed')
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[2])
    for argument in ('nettest', 'client', 'filesystem', 'paths-template', 'qualification-receipt', 'output'):
        parser.add_argument('--' + argument, type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--room', default='MP1 SANCTORUS')
    parser.add_argument('--timeout', type=int, default=180)
    args = parser.parse_args()
    repository, output = args.repository.resolve(), args.output.resolve()
    require(args.timeout > 0, 'timeout must be positive')
    require(not output.exists() or not any(output.iterdir()), 'output must be fresh; preserve all prior evidence')
    paths = {name: getattr(args, name).resolve() for name in ('nettest', 'client', 'paths_template', 'qualification_receipt')}
    require(paths['paths_template'].name == 'paths.txt', 'paths template must be an existing paths.txt')
    paths['nettestClient'] = paths['nettest'].with_name('ProjectPrime.dll')
    paths['runner'] = Path(__file__).resolve()
    paths['groupRunner'] = Path(__file__).with_name('run_life_campaign.py').resolve()
    paths['assetHelper'] = repository / 'tools/native-render-check/campaign_identity.py'
    for name in ('nettest', 'client'):
        paths[name + 'Deps'] = paths[name].with_suffix('.deps.json')
        paths[name + 'RuntimeConfig'] = paths[name].with_suffix('.runtimeconfig.json')
    hashes = {name: sha(path) for name, path in paths.items()}
    require(hashes['client'] == hashes['nettestClient'], 'full tool must reference the qualified physical engine')
    def source_identity():
        def git(*command): return subprocess.check_output(['git', '-C', str(repository), *command], text=True).strip()
        return {'sha': git('rev-parse', 'HEAD'), 'status': git('status', '--porcelain'), 'diffSha256': hashlib.sha256(subprocess.check_output(['git', '-C', str(repository), 'diff', 'HEAD', '--'])).hexdigest()}
    source_before = source_identity()
    qualifier = json.loads(paths['qualification_receipt'].read_text())
    qualified = qualification_receipt(qualifier, source_before, hashes['client'])
    require(qualifier.get('physicalHashesAfter', {}).get('nettest.dll') == hashes['nettest'], 'full nettest physical hash must match its integrated qualification')
    helper_spec = importlib.util.spec_from_file_location('native_grant_asset_identity', paths['assetHelper'])
    helper = importlib.util.module_from_spec(helper_spec); helper_spec.loader.exec_module(helper)
    output.mkdir(parents=True, exist_ok=True)
    assets = helper.asset_identity(paths['paths_template'])
    require(Path(assets['roots'].get('AMHE1', '')).resolve() == args.filesystem.resolve(), 'template and supplied licensed filesystem differ')
    planned = [{'hunter': hunter, 'item': item, 'mode': mode, 'status': 'NOT_RUN'} for hunter in HUNTERS for item in ITEMS for mode in MODES]
    manifest = {'schema': 1, 'status': 'NOT_RUN', 'sourceBefore': source_before, 'qualification': qualified, 'inputHashes': hashes, 'assetsBefore': assets, 'room': args.room, 'runs': planned, 'parityPairs': [], 'scope': '42 genuine native contacts/shots in fresh isolated processes; exact unchanged bytes, no ledger incarnation reset or normalization; authored UDP/impact/GL/audio NOT_RUN'}
    def save(): (output / 'receipt.json').write_text(json.dumps(manifest, indent=2) + '\n')
    def changed_inputs():
        return [name for name, path in paths.items() if not path.is_file() or sha(path) != hashes[name]] + ([] if source_identity() == source_before else ['source'])
    save()
    pending = None
    try:
        for index, record in enumerate(planned):
            changed = changed_inputs(); require(not changed, 'immutable source/binary/input changed: ' + ','.join(changed))
            label = f'{index:02d}-{record["hunter"]}-{record["item"]}-{record["mode"]}'
            directory = output / label; directory.mkdir()
            userdata, arm_assets = helper.isolate_user_data(paths['paths_template'].parent, directory)
            require(arm_assets == assets, 'asset identity changed before native arm')
            environment, removed = clean_environment(os.environ, userdata)
            result = directory / 'arm.json'
            command = [args.dotnet, str(paths['nettest']), '--native-grant-hunter-tour-arm', str(args.filesystem.resolve()), record['hunter'], record['item'], record['mode'], args.room, str(result)]
            record.update(command=command, removedEnvironmentKeys=removed)
            save()
            record.update(run_owned(command, userdata, environment, directory / 'arm.log', args.timeout))
            save()
            require(record['status'] == 'PASS', 'native arm process or owned group failed: ' + label)
            require(not changed_inputs(), 'immutable source/binary/input changed after native arm')
            data = json.loads(result.read_text()); record['resultSha256'] = sha(result)
            arm = validate_arm(data, record['hunter'], record['item'], record['mode'], hashes['client'], qualified['mvid'], hashes['nettest'])
            record.update(positiveReceiptValid=True, entryMvid=data['entryAssemblyMvid'], nativeFrames=arm['NativeFrames'])
            if record['mode'] == 'Off':
                pending = arm
            else:
                compare_pair(pending, arm)
                manifest['parityPairs'].append({'hunter': record['hunter'], 'item': record['item'], 'status': 'PASS', 'roomIncarnation': arm['Spawn']['Room'], 'intentSha256': hashlib.sha256(bytes.fromhex(arm['Shot']['Outcome']['IntentBytes'])).hexdigest(), 'replaySha256': hashlib.sha256(arm['Shot']['Outcome']['ReplayFacts'].encode()).hexdigest()})
                print(f'PAIR PASS hunter={record["hunter"]} item={record["item"]} room={arm["Spawn"]["Room"]}', flush=True)
                pending = None
            save()
        manifest['assetsAfter'] = helper.asset_identity(paths['paths_template'])
        require(manifest['assetsAfter'] == assets and not changed_inputs(), 'inputs changed across complete native matrix')
        require(len(manifest['parityPairs']) == 21 and len(planned) == 42 and all(record.get('positiveReceiptValid') for record in planned), 'complete native matrix denominators required')
        manifest.update(status='PASS', genuineContacts=42, nativeShots=42, sourceAfter=source_identity(), nativeFrames=sum(record['nativeFrames'] for record in planned))
    except Exception as error:
        manifest.update(status='FAIL', failure=type(error).__name__ + ': ' + str(error), sourceAfter=source_identity())
        if 'record' in locals() and record['status'] != 'NOT_RUN': record['status'] = 'FAIL'
        print(manifest['failure'], file=sys.stderr)
    save()
    print(f'NATIVE GRANT TOUR {manifest["status"]} completedArms={sum(bool(record.get("positiveReceiptValid")) for record in planned)} parityPairs={len(manifest["parityPairs"])}', flush=True)
    return 0 if manifest['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
