import json, pathlib, re

root = pathlib.Path(__file__).resolve().parents[2]
source = (root / 'src/MphRead/Mods/Network/NetProtocol.cs').read_text()
body = source.split('public enum PacketType : byte', 1)[1].split('{', 1)[1].split('}', 1)[0]
values = {}
for part in re.sub(r'//[^\n]*', '', body).split(','):
    if not part.strip(): continue
    name, raw = map(str.strip, part.split('='))
    values[name] = int(raw) if raw.isdigit() else values[raw]
baseline = json.loads((pathlib.Path(__file__).parent / 'packet-registry-baseline.json').read_text())
for name, value in baseline['packet_types'].items():
    assert values[name] == value, (name, value, values[name])
additions = {'PartyReservationRequest': 80, 'PartyReservationState': 81, 'PartyReservationAction': 82,
             'CustomWeaponManifest': 83, 'CustomWeaponReady': 84, 'CustomWeaponReadyConfirmed': 85,
             'CustomWeaponAction': 86, 'CustomWeaponReceipt': 87, 'CustomWeaponContact': 88,
             'CustomWeaponResultAck': 89, 'CustomWeaponState': 90,
             'CustomWeaponControl': 91, 'CustomWeaponControlDenied': 92, 'CustomWeaponInput': 93,
             'CustomWeaponReplicaPage': 94, 'CustomWeaponReplicaEvent': 95, 'CustomWeaponReplicaAck': 96}
assert {k:v for k,v in values.items() if k not in baseline['packet_types']} == additions
assert len(set(values.values())) == len(values) - 1
assert 'public const int ProtocolVersion = 71;' in source
status = source.split('public struct ServerStatusPacket', 1)[1].split('public struct ', 1)[0]
assert dict(re.findall(r'public const byte (Flag\w+) = (\d+);', status)) == {'FlagCanHost': '1', 'FlagPartyReservationsV1': '2'}
output = 'namespace MphRead.Mods.Network;\npublic enum PacketType : byte {\n' + ',\n'.join(f'{k}={v}' for k,v in values.items()) + '\n}\n'
(pathlib.Path(__file__).parent / 'Registry.generated.cs').write_text(output)
print(f'PASS actual registry: {len(baseline["packet_types"])} old names unchanged; IDs80-96 and optional flag2 added; protocol71')
