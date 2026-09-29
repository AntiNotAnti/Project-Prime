#!/usr/bin/env python3
"""Create a reproducible local biped starting state from the user's AMHE1 ROM."""
import argparse
import json
import math
from pathlib import Path
import struct
from record import Bridge, sha, vector

p = argparse.ArgumentParser(description=__doc__)
for name in ('bridge', 'rom', 'output'):
    p.add_argument('--' + name, type=Path, required=True)
p.add_argument('--room', choices=('MP1 SANCTORUS', 'MP3 PROVING GROUND'), default='MP1 SANCTORUS')
a = p.parse_args()
profile_path = Path(__file__).with_name('AMHE1.json')
profile = json.loads(profile_path.read_text())
if sha(a.rom) != profile['romSha256']:
    raise SystemExit('ROM differs from the verified AMHE1 profile')
a.output.mkdir(parents=True, exist_ok=False)
with (a.output / 'emulator.log').open('w') as log, (a.output / 'commands.txt').open('w') as commands:
    b = Bridge(a.bridge, a.rom, log)
    try:
        def command(text):
            commands.write(text + '\n'); commands.flush()
            return b.command(text)
        def step(n, mask=0xfff): command(f'step {n} {mask:x}')
        def touch(x, y, wait=300):
            command(f'touch {x} {y}'); step(6); command('release'); step(wait)
        step(1500); step(2, 0xff7); step(120); step(6, 0xff7); step(120)
        touch(128, 96, 120); step(90); touch(128, 100, 90)
        touch(172, 73, 90)       # Multiplayer
        touch(128, 124, 330)    # New volatile save nickname notice
        touch(126, 70, 120)     # Multi-card play (offline bots)
        touch(76, 76, 120)      # Create game
        touch(40, 90, 210)      # Battle
        if a.room == 'MP1 SANCTORUS':
            touch(235, 52, 60)  # Arena: Combat Hall -> Data Shrine
        touch(211, 172, 240)    # Arena confirmation
        touch(26, 133, 300)     # Samus
        step(10, 0xffe); step(300) # Hunter confirmation
        touch(225, 169, 300)    # Add bot
        touch(212, 140, 300)    # Accept bot setup
        touch(196, 40, 600)     # Start game
        step(2, 0xdff); step(110) # Fire to spawn, then let the initial shot expire
        state = b.read(int(profile['gameStateAddress'], 0), 4)
        player = b.read(int(profile['playerAddress'], 0), 0x850)
        command('screen ' + json.dumps(str((a.output / 'starting-screen.ppm').resolve())))
        if state[0] != 3 or state[1] != profile['roomIds'][a.room] or player[0x400] != 0 or struct.unpack_from('<H', player, 0xda)[0] != 99:
            raise RuntimeError('Setup did not establish a healthy Samus in the requested Battle arena; inspect starting-screen.ppm')
        facing = vector(player, 0x4c)
        if facing['y'] != 0 or abs(math.hypot(facing['x'], facing['z']) - 1) > 1/4096:
            raise RuntimeError(f'Starting facing is not a horizontal native unit vector: {facing}')
        path = a.output / 'native-biped.state'
        command('save ' + json.dumps(str(path.resolve())))
        meta = dict(room=a.room, hunter='Samus', form='Biped', stateSha256=sha(path), romSha256=sha(a.rom),
                    playerPosition=vector(player,0x1c), facing=facing, nativeGameStateBytes=state.hex(),
                    verifiedBy=f"Scripted original ROM menus plus live game mode=3, room={profile['roomIds'][a.room]}, hunter=0, health=99")
        Path(str(path) + '.json').write_text(json.dumps(meta, indent=2) + '\n')
        print(path)
    finally:
        b.close()
