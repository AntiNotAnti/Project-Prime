#!/usr/bin/env python3
"""Refresh/check GLES sync hashes from the shared C# shader source expressions.

This is a source-only helper; it does not compile or qualify either GL backend.
Only the explicit EsShaders.CheckInSync expected hashes are modified by --write.
"""
from pathlib import Path
import argparse
import hashlib
import re

ROOT = Path(__file__).resolve().parents[2]
RENDER = ROOT / "src/MphRead/Mods/Render"


def verbatim(text, index):
    if text[index:index + 2] != '@"':
        raise ValueError("Expected C# verbatim string")
    index += 2
    parts = []
    while True:
        end = text.index('"', index)
        parts.append(text[index:end])
        index = end + 1
        if text[index:index + 1] == '"':
            parts.append('"')
            index += 1
        else:
            return ''.join(parts), index


def property_source(text, name, references):
    match = re.search(r'string ' + re.escape(name) + r'\s*(?:\{ get; \})?\s*=', text)
    if match is None:
        raise ValueError("Missing shader property: " + name)
    index = match.end()
    parts = []
    while True:
        while text[index].isspace() or text[index] == '+':
            index += 1
        if text[index] == ';':
            return ''.join(parts)
        if text[index:index + 2] == '@"':
            value, index = verbatim(text, index)
            parts.append(value)
        else:
            match = re.match(r'[\w.]+', text[index:])
            if match is None or match.group() not in references:
                raise ValueError("Unsupported shader expression near " + text[index:index + 80])
            key = match.group()
            index += len(key)
            parts.append(references[key])


def without_depth_specialization(source):
    # Mirror the source-only transformation in Shaders.cs, not its GLSL math.
    for declaration in ('uniform bool depth_vertex_fast;', 'uniform bool depth_only;',
                        'uniform bool use_override;', 'uniform bool player_outline_mask;',
                        'uniform int cosmetic_skin;', 'uniform int cosmetic_effect;',
                        'uniform float cosmetic_dissolve;'):
        source = source.replace(declaration + '\n', '')
    start = source.index('    if (depth_vertex_fast && depth_only')
    body = source.index('    vec4 vtx_color =', start)
    source = source[:start] + source[body:]
    end = source.index('    }\n    }\n', start) + len('    }\n')
    return source[:end] + source[end + len('    }\n'):]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true', help='write expected hashes (otherwise check only)')
    parser.add_argument('--check', action='store_true', help='check expected hashes without changing files')
    args = parser.parse_args()
    if args.write and args.check:
        parser.error('select --write or --check')
    desktop = (ROOT / 'src/MphRead/Shaders.cs').read_text(encoding='utf-8')
    es_path = RENDER / 'EsShaders.cs'
    es = es_path.read_text(encoding='utf-8')
    helper = (RENDER / 'SceneColorShaderSource.cs').read_text(encoding='utf-8')
    refs = {}
    prefix = 'MphRead.Mods.Render.SceneColorShaderSource.'
    for name in ('Input', 'Normal'):
        refs[prefix + name] = property_source(helper, name, refs)
    cosmetic = property_source((ROOT / 'src/MphRead/Mods/Cosmetics/CosmeticShader.cs').read_text(encoding='utf-8'), 'Source', refs)
    palette_match = re.search(r'string\[\] palettes = \{(.*?)\};', helper, re.S)
    if palette_match is None:
        raise ValueError('Cosmetic palette source shape changed')
    for palette in re.findall(r'"([^"\r\n]*)"', palette_match.group(1)):
        cosmetic = cosmetic.replace('vec3(' + palette + ')', 'scene_input_color(vec3(' + palette + '))')
    cosmetic = cosmetic.replace('mix(cosmetic_primary, cosmetic_secondary, wave)',
                                'mix(scene_input_color(cosmetic_primary), scene_input_color(cosmetic_secondary), wave)')
    cosmetic = cosmetic.replace('col.rgb += cosmetic_primary *', 'col.rgb += scene_input_color(cosmetic_primary) *')
    refs[prefix + 'Cosmetics'] = cosmetic
    names = ('VertexShader', 'FragmentShader', 'RttVertexShader', 'RttFragmentShader', 'CelFragmentShader', 'ShiftFragmentShader')
    values = {name: property_source(desktop, name, refs) for name in names}
    values['LegacyVertexShader'] = without_depth_specialization(values['VertexShader'])
    values['LegacyEsVertexShader'] = without_depth_specialization(property_source(es, 'VertexShader', refs))
    failed = False
    for name, source in values.items():
        digest = hashlib.sha256(source.replace('\r\n', '\n').encode('utf-8')).hexdigest()
        pattern = r'(Check\("' + name + r'",[^;]*?\")([0-9a-f]{64})("\);)'
        match = re.search(pattern, es)
        if match is None:
            raise ValueError('Missing sync check: ' + name)
        changed = match[2] != digest
        failed |= changed
        print(f'{name}: {digest}' + (' (updated)' if changed and args.write else ' (MISMATCH)' if changed else ''))
        es = re.sub(pattern, lambda result: result[1] + digest + result[3], es)
    if args.write:
        es_path.write_text(es, encoding='utf-8')
        return 0
    return int(failed)


if __name__ == '__main__':
    raise SystemExit(main())
