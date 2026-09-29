#!/usr/bin/env python3
"""Run a shared biped scenario in the actual ROM; never emulate physics in Python."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class Bridge:
    def __init__(self, executable, rom, log):
        self.process = subprocess.Popen([str(executable), str(rom)], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=log, text=True, bufsize=1)
        self.identity = self.process.stdout.readline().strip()
        if not self.identity.startswith('READY '):
            raise RuntimeError('Native core failed to boot; see log')

    def command(self, text):
        self.process.stdin.write(text + '\n')
        self.process.stdin.flush()
        answer = self.process.stdout.readline().strip()
        if answer != 'OK' and not answer.startswith(('DATA ', 'JSON ')):
            raise RuntimeError(f'{text}: {answer}')
        return answer

    def read(self, address, size):
        return bytes.fromhex(self.command(f'read {address:x} {size:x}')[5:])

    def write32(self, address, value):
        self.command(f'write32 {address:x} {value & 0xffffffff:x}')

    def close(self):
        if self.process.poll() is None:
            self.process.stdin.write('quit\n'); self.process.stdin.flush()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill(); self.process.wait()


def vector(data, offset):
    return dict(zip(('x', 'y', 'z'), (x / 4096 for x in struct.unpack_from('<3i', data, offset))))


def run(args):
    scenario = json.loads(args.scenario.read_text())
    profile = json.loads(args.profile.read_text())
    state_meta = json.loads(Path(str(args.state) + '.json').read_text())
    if scenario.get('placementRequired'):
        print('Unverified placement: ' + scenario['placementRequired']); return 2
    if scenario.get('version') != 1 or scenario.get('hunter') != 'Samus' or scenario.get('form') != 'Biped':
        raise ValueError('Only version 1 Samus biped scenarios are supported')
    if sha(args.rom) != profile['romSha256'] or sha(args.rom) != state_meta['romSha256']:
        raise ValueError('ROM hash differs from the verified profile/state')
    if sha(args.state) != state_meta['stateSha256'] or state_meta['room'] != scenario['room']:
        raise ValueError('State hash or room differs from verified starting-state metadata')
    if len(scenario['facing']) != 3 or scenario['facing'][1] != 0 or abs(math.hypot(scenario['facing'][0], scenario['facing'][2]) - 1) > 1/4096:
        raise ValueError('Verified starting states require a horizontal native unit vector')
    if scenario['facing'] != [state_meta['facing'][axis] for axis in ('x','y','z')]:
        raise ValueError('Scenario facing differs from the recorded native starting state')
    rows = scenario['inputs']; duration = scenario['nativeTicks']; settle = scenario.get('settleNativeTicks', 30)
    if not 1 <= duration <= 18000 or not 0 <= settle <= 300 or not rows or rows[0]['tick'] != 0:
        raise ValueError('Invalid duration or initial input')
    allowed = {'version','name','room','hunter','form','spawn','facing','nativeTicks','settleNativeTicks','seed1','seed2','placementRequired','inputs','initialImpulse','initialFreezeNativeTicks'}
    if set(scenario) - allowed: raise ValueError('Unknown scenario fields')
    freeze = scenario.get('initialFreezeNativeTicks', 0)
    if type(freeze) is not int or not 0 <= freeze <= duration: raise ValueError('Invalid freeze duration')
    impulse = scenario.get('initialImpulse')
    if impulse is not None:
        if set(impulse) != {'velocity','acceleration','nativeTicks'} or not 0 <= impulse['nativeTicks'] <= duration:
            raise ValueError('Invalid impulse fields or duration')
        for key in ('velocity','acceleration'):
            if len(impulse[key]) != 3 or any(not math.isfinite(v) or abs(v) > 4 or v*4096 != round(v*4096) for v in impulse[key]):
                raise ValueError('Impulse vectors must use finite native fixed-point values')
    last = -1; previous_jump = -2
    for row in rows:
        if not last < row['tick'] < duration or row.get('moveX', 0) not in (-1, 0, 1) or row.get('moveY', 0) not in (-1, 0, 1):
            raise ValueError('Invalid ordered digital input')
        if set(row) - {'tick','moveX','moveY','jump','fire'}: raise ValueError('Unknown input fields')
        if any(type(row.get(k, False)) is not bool for k in ('jump','fire')):
            raise ValueError('Jump and fire must be booleans')
        if row.get('jump', False):
            if row['tick'] - previous_jump < 2: raise ValueError('Native jump edges require an intervening release tick')
            previous_jump = row['tick']
        last = row['tick']
    if len(scenario['spawn']) != 3 or not all(math.isfinite(x) for x in scenario['spawn']):
        raise ValueError('Invalid spawn')
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / 'profile.json').write_bytes(args.profile.read_bytes())
    base = int(profile['playerAddress'], 0); frame_addr = int(profile['frameAddress'], 0)
    offsets = profile['verifiedOffsets']; samples = []; combat_samples = []
    combat = any(row.get('fire', False) for row in rows) or freeze > 0
    with (args.output / 'emulator.log').open('w') as log:
        bridge = Bridge(args.bridge, args.rom, log)
        try:
            if bridge.identity != f"READY {profile['romCode']} {profile['romVersion']}":
                raise ValueError('ROM identity mismatch')
            bridge.command('load ' + json.dumps(str(args.state.resolve())))
            bridge.command('release')
            game = bridge.read(int(profile['gameStateAddress'], 0), 4)
            if game[0] != 3 or game[1] != profile['roomIds'].get(scenario['room']):
                raise ValueError('Live native game mode or room does not match the scenario')
            initial = bridge.read(base, 0x850)
            if struct.unpack_from('<H', initial)[0] != 25 or initial[offsets['hunter']] != 0:
                raise ValueError('Player entity/hunter identity mismatch')
            if any(abs(vector(initial, offsets['facing'])[axis] - value) > 1/4096 for axis,value in zip(('x','y','z'),scenario['facing'])):
                raise ValueError('Native starting facing differs from the scenario')
            health = struct.unpack_from('<H', initial, offsets['health'])[0]
            if not health or struct.unpack_from('<I', initial, offsets['flags'])[0] & 0x200:
                raise ValueError('Expected living biped')
            def counter(): return struct.unpack('<I', bridge.read(frame_addr, 4))[0]
            def step(mask):
                before = counter()
                bridge.command(f'step 2 {mask:x}')
                after = counter()
                if (after - before) & 0xffffffff != 1:
                    raise ValueError('Native game did not advance exactly one tick per two video frames')
            for key in ('position', 'previousPosition'):
                for axis, value in enumerate(scenario['spawn']):
                    fixed = round(value * 4096)
                    if value != fixed / 4096:
                        raise ValueError('Shared spawn must be exactly representable in native fixed point')
                    bridge.write32(base + offsets[key] + axis * 4, fixed)
            for key in ('velocity', 'previousVelocity'):
                for axis in range(3): bridge.write32(base + offsets[key] + axis * 4, 0)
            # Prime Spawn resets this timer. Retaining the saved game's 55 idle
            # ticks makes native idle sway begin during settling and rotates the
            # movement basis before the shared input script starts.
            bridge.write32(base + offsets['timeSinceInput'], 0)
            # Prime Spawn starts Standing but not Grounded. Ground contact must
            # establish the latter, especially in zero-settle falling fixtures.
            spawn_flags = struct.unpack_from('<I', initial, offsets['flags'])[0] & ~0x300000
            bridge.write32(base + offsets['flags'], spawn_flags)
            bridge.write32(int(profile['rng1Address'], 0), scenario.get('seed1', 12345))
            bridge.write32(int(profile['rng2Address'], 0), scenario.get('seed2', 67890))
            for _ in range(settle): step(0xfff)
            if impulse is not None:
                for key in ('velocity','acceleration'):
                    for axis, value in enumerate(impulse[key]):
                        bridge.write32(base + offsets[key] + axis*4, round(value*4096))
                # Timer shares a 32-bit word with health; preserve the upper half.
                address = base + offsets['accelerationTimer']
                word = struct.unpack('<I', bridge.read(address, 4))[0]
                bridge.write32(address, (word & 0xffff0000) | impulse['nativeTicks'])
            if freeze:
                word = struct.unpack('<I', bridge.read(base + 0x4b8, 4))[0]
                bridge.write32(base + 0x4b8, (word & 0xffff0000) | freeze)
            start_counter = counter()
            index = 0
            for tick in range(duration):
                if index + 1 < len(rows) and rows[index + 1]['tick'] == tick: index += 1
                inp = rows[index]; x = inp.get('moveX', 0); y = inp.get('moveY', 0)
                jump = inp.get('jump', False) and inp['tick'] == tick
                mask = 0xfff
                for bit, pressed in ((4, x > 0), (5, x < 0), (6, y > 0), (7, y < 0), (1, jump), (9, inp.get('fire', False))):
                    if pressed: mask &= ~(1 << bit)
                before = bridge.read(base, 0x850)
                bridge.command(f'probe {base:x}')
                step(mask)
                native_stages = json.loads(bridge.command('events')[5:])
                required_stages = {'preMovement', 'afterAcceleration', 'afterDamping', 'afterGravity', 'afterIntegration', 'afterCollision'}
                if not required_stages <= native_stages.keys():
                    raise ValueError('Missing native instruction observations: ' + str(required_stages - native_stages.keys()))
                after = bridge.read(base, 0x850)
                flags = struct.unpack_from('<I', after, offsets['flags'])[0]
                hp = struct.unpack_from('<H', after, offsets['health'])[0]
                if hp != health or flags & 0x200:
                    raise ValueError(f'Native player damaged, dead, or changed form at tick {tick}; capture is not isolated')
                facing = vector(after, offsets['facing'])
                sample = dict(schemaVersion=2, frame=(tick + 1) * 2, player=0, hunter='Samus', form='Biped',
                    positionBefore=vector(before, offsets['position']), velocityBefore=vector(before, offsets['velocity']),
                    positionActual=vector(after, offsets['position']), velocityActual=vector(after, offsets['velocity']),
                    heading=math.degrees(math.atan2(facing['x'], facing['z'])), facing=facing,
                    jumpPadActive=bool(flags & 0x10000000), standing=bool(flags & 1), grounded=bool(flags & 0x100000), collisionFlags=flags & 0x90,
                    timeSinceGrounded=struct.unpack_from('<H', after, offsets['timeSinceGrounded'])[0] * 2,
                    input=json.dumps(dict(moveX=x, moveY=y, jump=jump), separators=(',', ':')),
                    nativeTick=tick + 1, nativeFrame=counter(), nativeFlags=flags, health=hp, nativeControlBytes=after[0x364:0x3a0].hex(), nativeInputBytes=after[0x464:0x4c0].hex(),
                    collisionPlane=None, collisionDepth=None, collisionPushout=None, stages=None,
                    nativeStages=native_stages, contacts=native_stages['contacts'])
                samples.append(sample)
                if combat:
                    # AMHE1 PlayerEntity +0x434 and embedded EquipInfo +0x850.
                    # Retain raw native ticks; comparison must state its mapping.
                    equipment = bridge.read(base + 0x850, 0x14)
                    ammo_addr = struct.unpack_from('<I', equipment, 0xc)[0]
                    ammo = struct.unpack('<H', bridge.read(ammo_addr, 2))[0] if ammo_addr else None
                    count = equipment[1]
                    beam_base = struct.unpack_from('<I', equipment, 4)[0]
                    if count != 5 or not 0x02000000 <= beam_base <= 0x023fffff - count*0x158:
                        raise ValueError('Unexpected native projectile pool')
                    projectiles = []
                    for slot in range(count):
                        beam = bridge.read(beam_base + slot*0x158, 0x158)
                        if struct.unpack_from('<H', beam)[0] != 26:
                            raise ValueError('Native beam pool identity mismatch')
                        lifespan, age = struct.unpack_from('<HH', beam, 0x2c)
                        if lifespan:
                            if struct.unpack_from('<I', beam, 0xf8)[0] != base:
                                raise ValueError('Native beam owner mismatch')
                            projectiles.append(dict(slot=slot, weapon=beam[0x18], flags=struct.unpack_from('<H',beam,0x22)[0],
                                position=vector(beam,0xa0), spawnPosition=vector(beam,0xac), velocity=vector(beam,0xc4),
                                age=age/30, lifespan=lifespan/30))
                    combat_samples.append(dict(frame=(tick+1)*2, fire=inp.get('fire', False),
                        shot=after[0x434] == 0, timeSinceShot=after[0x434], weapon=after[0x4ce],
                        freezeTimer=struct.unpack_from('<H', after, 0x4b8)[0],
                        chargeLevel=struct.unpack_from('<H', equipment, 0x10)[0], ammo=ammo, projectiles=projectiles))
            bridge.command('screen ' + json.dumps(str((args.output / 'final.ppm').resolve())))
        finally:
            bridge.close()
    # Publish the trace only after a complete, cadence-checked, health-stable run.
    with (args.output / 'native-reference.jsonl').open('x') as f:
        for sample in samples: f.write(json.dumps(sample, separators=(',', ':')) + '\n')
    if combat:
        with (args.output / 'native-combat.jsonl').open('x') as f:
            for sample in combat_samples: f.write(json.dumps(sample, separators=(',', ':')) + '\n')
    manifest = dict(scenario=scenario['name'], traceSha256=sha(args.output / 'native-reference.jsonl'), scenarioSha256=sha(args.scenario), recorderSha256=sha(Path(__file__)), romSha256=sha(args.rom),
        stateSha256=sha(args.state), profileSha256=sha(args.profile), executableSha256=sha(args.bridge),
        emulatorCommit=profile['emulatorCommit'], startNativeFrame=start_counter, nativeTicks=duration,
        cadence='one original game update per two NDS video frames', gameTickHz=30,
        primeBoundaryMapping='elapsed native tick N -> Prime frame 2*N',
        timerMapping='native uint16 timeSinceGrounded * 2', setupResets=['position','previousPosition','velocity','previousVelocity','timeSinceInput','groundedFlags'], rngSeeds=[scenario.get('seed1',12345),scenario.get('seed2',67890)],
        unmeasured=['native consumed-input scales and traction/cap internals'], nativeStageComparison='Whole 30 Hz stages; do not equate directly with a single 60 Hz substep', nativeParity='Not asserted; this is measured original-ROM state')
    if combat: manifest['combatTraceSha256'] = sha(args.output / 'native-combat.jsonl')
    (args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(f"{scenario['name']}: captured {len(samples)} native ticks")
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('bridge', 'rom', 'state', 'scenario', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--profile', type=Path, default=Path(__file__).with_name('AMHE1.json'))
    raise SystemExit(run(parser.parse_args()))
