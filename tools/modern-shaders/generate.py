#!/usr/bin/env python3
"""Generate native WGSL and uniform layouts from the checked-in desktop shaders.
Usage: generate.py exported.json /path/to/prime-shader-translate
Run export/export.csproj first. Commit generated files; no Rust runtime is shipped.
"""
import json, re, subprocess, sys, tempfile
from pathlib import Path
root = Path(__file__).resolve().parents[2]
out = root / 'src/MphRead/Mods/Render/Generated'
out.mkdir(exist_ok=True)
# Accept both historical GLSL array spellings.
uniform = re.compile(r'\buniform\s+(\w+)(?:\[(\d+)\])?\s+(\w+)(?:\[(\d+)\])?\s*;')
sources = json.loads(Path(sys.argv[1]).read_text())
manifest=[]

def deferred_pbr_mrt(wgsl):
    wgsl=wgsl.replace(
        'struct FragmentOutput {\n    @location(0) prime_output: vec4<f32>,\n}',
        'struct FragmentOutput {\n    @location(0) prime_output: vec4<f32>,\n    @location(1) prime_output_normal: vec4<f32>,\n    @location(2) prime_output_material: vec4<f32>,\n}')
    wgsl=wgsl.replace(
        'var<private> prime_output: vec4<f32>;\n',
        'var<private> prime_output: vec4<f32>;\nvar<private> prime_mrt_mode: i32;\n')
    wgsl=wgsl.replace('global.gbuffer_mode','prime_mrt_mode')
    tail=r'''    main_1\(\);
    let _e\d+: vec4<f32> = prime_output;
    return FragmentOutput\(_e\d+\);
\}
$'''
    replacement='''    prime_mrt_mode = 1i;
    main_1();
    let prime_mrt_albedo = prime_output;
    prime_mrt_mode = 2i;
    main_1();
    let prime_mrt_normal = prime_output;
    prime_mrt_mode = 3i;
    main_1();
    let prime_mrt_material = prime_output;
    return FragmentOutput(prime_mrt_albedo, prime_mrt_normal, prime_mrt_material);
}
'''
    wgsl,count=re.subn(tail,replacement,wgsl,flags=re.M)
    if count!=1: raise RuntimeError('Deferred PBR MRT tail transform failed')
    return wgsl

for kind, pair in sources.items():
    values={}; samplers=[]; varying={}
    for source in pair.values():
        for m in uniform.finditer(source):
            typ, n1, name, n2=m.groups(); count=int(n1 or n2 or 1)
            if typ=='sampler2D':
                if name not in samplers: samplers.append(name)
            else:
                values[name]=(typ,count)
        for typ,name in re.findall(r'varying\s+(\w+)\s+(\w+)\s*;',source):
            if name not in varying: varying[name]=len(varying)
    values['prime_viewport']=('vec4',1)
    if kind in ('World','DeferredPbr'):
        values['prime_imm_color']=('vec4',1)
        values['prime_imm_normal']=('vec4',1)
    if kind=='World':
        values['prime_alpha_func']=('int',1)
        values['prime_alpha_ref']=('float',1)
    fields=[]; macros=[]; offsets=[]; offset=0
    for name,(typ,count) in values.items():
        actual='int' if typ=='bool' else typ
        field='_prime_'+name if typ=='bool' else name
        fields.append(f'{actual} {field}'+(f'[{count}]' if count>1 else '')+';')
        if typ=='bool': macros.append(f'#define {name} ({field} != 0)')
        components={'bool':1,'int':1,'float':1,'vec2':2,'vec3':3,'vec4':4,'mat4':16}[typ]
        stride=64 if typ=='mat4' else 16
        offsets.append(dict(name=name,offset=offset,components=components,count=count,stride=stride,integer=typ in ('bool','int')))
        offset+=stride*count
        if count==1 and components<4:
            for pad in range(4-components): fields.append(f'float _pad_{offset}_{pad};')
    flip_offset=offset
    fields.append(f'vec4 prime_texture_flip[{max(1,len(samplers))}];')
    offset+=16*max(1,len(samplers))
    base='#version 450\nlayout(set=0,binding=0,std140) uniform PrimeUniforms {\n'+'\n'.join(fields)+'\n};\n'+'\n'.join(macros)+'\n'
    texture_header=''
    for i,name in enumerate(samplers):
        texture_header+=f'layout(set=0,binding={1+i*2}) uniform texture2D prime_tex_{name};\nlayout(set=0,binding={2+i*2}) uniform sampler prime_sampler_{name};\n'
        # Post-process inputs are base-only scene/history/G-buffer targets.
        # Explicit LOD retains their sampling while avoiding implicit gradients
        # inside divergent SSR/AA loops, which FXC otherwise tries to unroll.
        # World/material textures keep their authored mip/anisotropic behavior.
        base_target=kind=='PostProcess' and name in {
            'tex', 'history_tex', 'pbr_albedo', 'pbr_normal', 'pbr_material'}
        lookup='textureLod' if base_target else 'texture'
        lod=', 0.0' if base_target else ''
        texture_header+=f'vec4 prime_sample_{name}(vec2 uv) {{ return {lookup}(sampler2D(prime_tex_{name}, prime_sampler_{name}), vec2(uv.x, mix(uv.y,1.0-uv.y,prime_texture_flip[{i}].x)){lod}); }}\n'
    for stage,source in pair.items():
        source=re.sub(r'^\s*#version[^\n]*','',source,flags=re.M)
        source=uniform.sub('',source)
        source=re.sub(r'varying\s+(\w+)\s+(\w+)\s*;',lambda m:f'layout(location={varying[m[2]]}) '+('out' if stage=='vertex' else 'in')+f' {m[1]} {m[2]};',source)
        source=re.sub(r'^\s*#define SAMPLE[^\n]*','',source,flags=re.M)
        for name in samplers:
            source=re.sub(r'\b(?:SAMPLE|texture2D)\(\s*'+name+r'\s*,',f'prime_sample_{name}(',source)
        source=source.replace('gl_FragColor','prime_output')
        header=base+texture_header
        if stage=='vertex':
            # Forward and G-buffer passes use exact depth equality. Preserve
            # identical position math across separately compiled pipelines.
            header+='invariant gl_Position;\n'
            header+='layout(location=0) in vec3 prime_position;\nlayout(location=1) in vec4 prime_color;\nlayout(location=2) in vec3 prime_normal;\nlayout(location=3) in vec3 prime_uv;\nlayout(location=4) in float prime_color_set;\nlayout(location=5) in float prime_normal_set;\n'
            source=source.replace('gl_Vertex','vec4(prime_position,1.0)').replace('gl_Normal','(prime_normal_set > 0.5 ? prime_normal : prime_imm_normal.xyz)').replace('gl_MultiTexCoord0','vec4(prime_uv,0.0)').replace('gl_Color','(prime_color_set > 0.5 ? prime_color : prime_imm_color)')
            # Keep OpenGL's clip convention in shared projection matrices.
            source=source.replace('void main()', 'void prime_original_main()')
            source+='\nvoid main() { prime_original_main(); gl_Position.z = (gl_Position.z + gl_Position.w)*0.5; gl_Position.xy = gl_Position.xy * prime_viewport.xy + prime_viewport.zw * gl_Position.w; }\n'
        else:
            header+='layout(location=0) out vec4 prime_output;\n'
            if kind=='World':
                source=source.replace('void main()', 'void prime_original_main()')
                source+='''\nvoid main() {
    prime_original_main();
    float a=prime_output.a, r=prime_alpha_ref;
    bool keep=prime_alpha_func==519 || (prime_alpha_func==513 && a<r)
        || (prime_alpha_func==514 && a==r) || (prime_alpha_func==515 && a<=r)
        || (prime_alpha_func==516 && a>r) || (prime_alpha_func==517 && a!=r)
        || (prime_alpha_func==518 && a>=r);
    if (!keep) discard;
}\n'''

        with tempfile.TemporaryDirectory() as d:
            src=Path(d)/('shader.vert' if stage=='vertex' else 'shader.frag'); dst=Path(d)/'shader.wgsl'
            src.write_text(header+source)
            # Retain source on failure for meaningful shader diagnostics.
            try: subprocess.run([sys.argv[2],str(src),str(dst)],check=True)
            except subprocess.CalledProcessError:
                Path('/tmp/prime-failed-shader.glsl').write_text(header+source); raise
            wgsl=dst.read_text()
        for name in ('depth_tex','shadow_tex'):
            if name not in samplers: continue
            wgsl=wgsl.replace(f'prime_tex_{name}: texture_2d<f32>',f'prime_tex_{name}: texture_depth_2d')
            index=samplers.index(name)
            # Depth is loaded explicitly, matching GL nearest depth sampling.
            # The generated helper alone touches the depth texture.
            pattern=r'fn prime_sample_'+name+r'\([^\n]*\)[^{]*\{.*?\n\}'
            helper=f'''fn prime_sample_{name}(uv: vec2<f32>) -> vec4<f32> {{
    let dims = vec2<i32>(textureDimensions(prime_tex_{name}));
    let p = clamp(vec2<i32>(vec2<f32>(uv.x, 1.0-uv.y) * vec2<f32>(dims)), vec2<i32>(0), dims-vec2<i32>(1));
    return vec4<f32>(textureLoad(prime_tex_{name}, p, 0));
}}'''
            wgsl=re.sub(pattern,helper,wgsl,flags=re.S)
        # Derivatives in the original AA/material branches are intentional.
        # Pinned wgpu accepts this diagnostic control with its existing shaders.
        (out/f'{kind}.{stage}.wgsl').write_text('\n'.join(line.rstrip() for line in wgsl.splitlines()) + '\n')
    manifest.append(dict(kind=kind,uniforms=offsets,samplers=samplers,size=offset,flipOffset=flip_offset))
# MRT keeps the exact generated PBR math/layout. The derivative runs the
# existing mode-specific function three times in one fragment invocation and
# exposes the results as three color targets, eliminating two geometry/raster
# passes without creating a second material implementation.
pbr_index=next(i for i,item in enumerate(manifest) if item['kind']=='DeferredPbr')
pbr=manifest[pbr_index]
mrt=dict(pbr); mrt['kind']='DeferredPbrMrt'
manifest.insert(pbr_index+1,mrt)
(out/'DeferredPbrMrt.vertex.wgsl').write_text((out/'DeferredPbr.vertex.wgsl').read_text())
(out/'DeferredPbrMrt.fragment.wgsl').write_text(
    deferred_pbr_mrt((out/'DeferredPbr.fragment.wgsl').read_text()))

(out/'layouts.json').write_text(json.dumps(manifest,indent=2)+'\n')

lines=['// Generated by tools/modern-shaders/generate.py. Do not edit.', '#if !MPHREAD_SERVER', 'namespace MphRead.Mods.Render {', 'internal static class GeneratedShaderLayouts {', 'internal static ModernShaderLayout Get(ModernProgramKind kind) => kind switch {']
for item in manifest:
    lines.append('ModernProgramKind.'+item['kind']+' => new('+str(item['size'])+', '+str(item['flipOffset'])+', new ModernUniformLayout[] {')
    for u in item['uniforms']:
        lines.append('new("'+u['name']+'", '+', '.join(str(u[k]) for k in ('offset','components','count','stride'))+', '+str(u['integer']).lower()+'),')
    lines.append('}, new string[] {'+', '.join('"'+s+'"' for s in item['samplers'])+'}),')
lines += ['_ => throw new System.NotSupportedException(kind.ToString())', '}; }}', '#endif']
(out/'GeneratedShaderLayouts.cs').write_text('\n'.join(lines)+'\n')
