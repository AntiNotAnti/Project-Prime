"""Actual native font role/restoration/retention checks; optional real GL captures."""
import argparse
import ctypes as C
import importlib.util
import json
from pathlib import Path
import re
import shutil
import tempfile

ROOT = next(p for p in Path(__file__).resolve().parents if (p / 'src/MphRead').exists())
spec = importlib.util.spec_from_file_location('layout_check', ROOT / 'tools/rmlui-layout-check/check.py')
layout = importlib.util.module_from_spec(spec)
spec.loader.exec_module(layout)

parser = argparse.ArgumentParser()
parser.add_argument('library', type=Path)
parser.add_argument('--glfw', type=Path)
parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/rmlui-typography-check')
args = parser.parse_args()
bridge = layout.Bridge(args.library.resolve())
font = bridge.lib.pp_rmlui_typography
font.restype, font.argtypes = C.c_int, [C.c_int] * 3
checks = 0
def check(value, message):
    global checks
    checks += 1
    if not value:
        raise AssertionError(message)

class Status(C.Structure):
    _fields_ = [('size', C.c_uint32), ('version', C.c_uint32), ('generation', C.c_uint64),
                ('revision', C.c_uint64), ('delay', C.c_double), ('flags', C.c_uint32), ('reserved', C.c_uint32)]
status = bridge.lib.pp_rmlui_update_status
status.restype, status.argtypes = C.c_int, [C.POINTER(Status)]
def revision():
    value = Status(size=C.sizeof(Status), version=1)
    check(status(C.byref(value)) == 1, 'update status available')
    return value.revision

report = {'kind': 'Actual RmlUi 6.3 CPU draw-list font policy; optional hidden GL2 captures', 'checks': 0}
args.output.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='prime-rmlui-fonts-') as folder:
    assets = Path(folder)
    shipped = ROOT / 'src/MphRead'
    for source in (shipped / 'Mods/Launcher/RmlUi/Assets').glob('*'):
        if source.suffix in ('.rml', '.rcss'):
            shutil.copy2(source, assets / source.name)
    (assets / 'fonts').mkdir()
    for filename in ['Rajdhani-SemiBold.ttf', 'Rajdhani-Bold.ttf', 'JetBrainsMono-Regular.ttf']:
        shutil.copy2(shipped / 'Assets/Fonts' / filename, assets / 'fonts' / filename)
    shutil.copytree(shipped / 'Assets/HudFonts', assets / 'hud-fonts', ignore=shutil.ignore_patterns('*.bin'))
    (assets / 'typography.rml').write_text('''<rml><head><title>Font role fixture</title><style>
        body { margin:0; padding:24px; font-family:Rajdhani; font-weight:600; font-size:24px; color:#d9edf8; background-color:#081b29; }
        div { display:block; margin:12px 0; }
        h2 { display:block; font-size:24px; font-weight:700; margin:12px 0; }
        code { font-family:"JetBrains Mono"; font-size:24px; }
        span { display:inline-block; }
        </style></head><body><div>BODY</div><div><span id="ui_body">WMWM li 012345</span></div>
        <h2><span id="ui_heading">WMWM li 012345</span></h2>
        <div><code><span id="ui_data">WMWM li 012345</span></code></div>
        <div id="dynamic"/><button id="press" data-action="settings:previous:95">PREVIOUS CHOICE</button>
        </body></rml>''', encoding='utf-8')
    def initialize():
        check(bridge.initialize_backend(960, 600, 1, layout.encode(assets), 1) == 1, 'draw-list initialization')
        doc = bridge.document_open(b'typography.rml', 0)
        check(doc != 0, 'font fixture opens')
        bridge.document_show(bridge.home_document(), 0)
        return doc
    def frame():
        bridge.update()
        bridge.render(960, 600)
    def widths(doc):
        frame()
        boxes = [bridge.bounds(doc, name) for name in ('ui_body', 'ui_heading', 'ui_data')]
        check(all(box and box[2] > 10 for box in boxes), 'native text geometry exists')
        return [box[2] for box in boxes]
    doc = initialize()
    try:
        baseline = widths(doc)
        check(font(33, -1, -1) == 1, 'body font loads from licensed TTF')
        body = widths(doc)
        check(body[0] != baseline[0] and body[1:] == baseline[1:], 'body choice changes only body role')
        check(font(-1, 12, -1) == 1, 'independent heading font policy')
        heading = widths(doc)
        check(heading[1] != baseline[1] and heading[0] == baseline[0] and heading[2] == baseline[2],
              'heading choice changes only heading role and restores body')
        check(font(-1, -1, 4) == 1, 'independent data font policy')
        data = widths(doc)
        check(data[2] != baseline[2] and data[:2] == baseline[:2], 'data choice changes only data role')
        for index in range(34):
            check(font(index, index, index) == 1, 'all licensed font variants load: ' + str(index))
            frame()
        check(font(33, 12, 4) == 1, 'combined role fonts')
        combined = widths(doc)
        later = bridge.document_open(b'typography.rml', 0)
        check(later != 0 and widths(later) == combined, 'new document receives current role policy')
        check(bridge.document_close(later) == 1, 'font styled document closes cleanly')
        frame()
        before = revision()
        for _ in range(200):
            check(font(33, 12, 4) == 1, 'unchanged typography accepted')
            frame()
        check(revision() == before, 'unchanged font policy keeps retained idle frame')
        check(font(-2, -1, -1) == 0 and font(-1, 34, -1) == 0, 'untrusted font indices rejected')
        check(font(-1, -1, -1) == 1 and widths(doc) == baseline, 'disable restores exact authored font geometry')
        report.update(baseline=baseline, body=body, headings=heading, data=data)
    finally:
        bridge.shutdown()
    doc = initialize()
    try:
        check(font(33, 12, 4) == 1, 'fonts reload in fresh native lifetime')
        check(widths(doc) != baseline, 'fresh lifetime applies selected fonts')
        # Exercise the new native parsers via real DOM focus/Enter dispatch,
        # comparing packet kinds/arguments to the actual managed registry.
        managed = (ROOT / 'src/MphRead/Mods/Launcher/RmlUi/Host/RmlUiIntent.cs').read_text(encoding='utf-8-sig')
        def kind(name):
            return int(re.search(r'\b' + name + r'\s*=\s*(\d+)', managed).group(1))
        hud_actions = re.findall(r'"([^"]+)"', re.search(r'string\[\] HudActions\s*=\s*\{([^}]+)\}', managed).group(1))
        commands = [('settings:previous:' + str(i), kind('SettingsPreviousChoice'), i) for i in range(96)]
        commands += [('offline:previous-choice:' + str(i), kind('OfflineChoicePrevious'), i) for i in range(17)]
        commands += [('hud:' + action, kind('HudAction'), i) for i, action in enumerate(hud_actions)]
        commands += [(action, kind(name), 0) for action, name in [
            ('lobby:rules-previous-mode', 'LobbyRulesModePrevious'), ('lobby:rules-previous-format', 'LobbyRulesFormatPrevious'),
            ('play:previous-map', 'PlayPreviousMap'), ('play:previous-mode', 'PlayPreviousMode')]]
        (assets / 'protocol.rml').write_text('<rml><head><title>Protocol</title><style>body { font-family:Rajdhani; font-size:18px; } button { display:block; height:24px; tab-index:auto; }</style></head><body>'
            + ''.join(f'<button id="command_{i}" data-action="{action}">ACTION {i}</button>' for i, (action, _, _) in enumerate(commands))
            + '</body></rml>', encoding='utf-8')
        protocol_doc = bridge.document_open(b'protocol.rml', 0)
        frame()
        class Intent(C.Structure):
            _fields_ = [('size', C.c_uint32), ('version', C.c_uint32), ('kind', C.c_uint32), ('argument', C.c_int32),
                        ('generation', C.c_uint64), ('document', C.c_uint64), ('sequence', C.c_uint64)]
        take = bridge.lib.pp_rmlui_take_intent
        take.restype, take.argtypes = C.c_int, [C.POINTER(Intent)]
        key = bridge.lib.pp_rmlui_key
        key.restype, key.argtypes = C.c_int, [C.c_int] * 3
        previous_sequence = 0
        for i, (action, expected_kind, argument) in enumerate(commands):
            check(bridge.document_focus(protocol_doc, layout.encode('command_' + str(i))) == 1, 'native control focus: ' + action)
            frame()
            key(2, 1, 0)
            key(2, 0, 0)
            frame()
            packet = Intent(size=C.sizeof(Intent), version=1)
            check(take(C.byref(packet)) == 1 and packet.kind == expected_kind and packet.argument == argument
                  and packet.document == protocol_doc and packet.sequence > previous_sequence,
                  'actual native packet agrees with managed registry: ' + action)
            previous_sequence = packet.sequence
            check(take(C.byref(packet)) == 0, 'one activation queues one action: ' + action)
        report['new_command_packets'] = len(commands)
        check(bridge.document_close(protocol_doc) == 1, 'protocol fixture retirement')
    finally:
        bridge.shutdown()
    if args.glfw:
        layout.FIXTURES['typography.rml'] = []
        layout.seed = lambda *values, **options: None
        report['baseline_gl'] = layout.run_gl(bridge, assets, args.glfw.resolve(), args.output / 'baseline', ['typography.rml'], [(960, 600, 1)])
        layout.seed = lambda *values, **options: check(font(33, 12, 4) == 1, 'GL font selection')
        report['selected_gl'] = layout.run_gl(bridge, assets, args.glfw.resolve(), args.output / 'selected', ['typography.rml'], [(960, 600, 1)])
        check('error' not in report['baseline_gl'] and len(report['baseline_gl'].get('captures', [])) == 1, 'baseline GL screenshot')
        check('error' not in report['selected_gl'] and len(report['selected_gl'].get('captures', [])) == 1, 'selected GL screenshot')
        check(report['baseline_gl']['captures'][0]['png_sha256'] != report['selected_gl']['captures'][0]['png_sha256'],
              'actual GL glyph pixels change with selected font roles')
report['checks'] = checks
(args.output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(f'PASS {checks} actual native font role/load/geometry/restoration/idle/lifetime checks')
