use std::{env,fs};
fn main() {
    let args: Vec<String> = env::args().collect();
    if args[1] == "--check-postprocess-hlsl" {
        check_postprocess_hlsl(&args[2], &args[3]);
        return;
    }
    let source = fs::read_to_string(&args[1]).unwrap();
    let stage = if args[1].ends_with(".vert") { naga::ShaderStage::Vertex } else { naga::ShaderStage::Fragment };
    let module = naga::front::glsl::Frontend::default().parse(&naga::front::glsl::Options::from(stage), &source).unwrap_or_else(|e| panic!("{e:#?}"));
    let info = naga::valid::Validator::new(naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::CONTROL_FLOW_UNIFORMITY, naga::valid::Capabilities::all()).validate(&module).unwrap_or_else(|e| panic!("{e:#?}"));
    let output = naga::back::wgsl::write_string(&module, &info, naga::back::wgsl::WriterFlags::EXPLICIT_TYPES).unwrap();
    fs::write(&args[2], output).unwrap();
}

fn check_postprocess_hlsl(input: &str, output: &str) {
    let source = fs::read_to_string(input).unwrap();
    let module = naga::front::wgsl::parse_str(&source).unwrap();
    let info = naga::valid::Validator::new(
        naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::CONTROL_FLOW_UNIFORMITY,
        naga::valid::Capabilities::all(),
    ).validate(&module).unwrap();
    let targets = ["prime_tex_tex", "prime_tex_history_tex", "prime_tex_pbr_albedo",
        "prime_tex_pbr_normal", "prime_tex_pbr_material"];
    let mut samples = 0;
    for function in module.functions.iter().map(|(_, function)| function)
        .chain(module.entry_points.iter().map(|entry| &entry.function)) {
        for (_, expression) in function.expressions.iter() {
            if let naga::Expression::ImageSample { image, level, .. } = expression {
                let image_name = match function.expressions[*image] {
                    naga::Expression::GlobalVariable(handle) => module.global_variables[handle].name.as_deref(),
                    _ => None,
                };
                if !targets.iter().any(|name| Some(*name) == image_name) { continue; }
                let naga::SampleLevel::Exact(level) = level else {
                    panic!("PostProcess color sampling requires explicit LOD: FXC cannot unroll the divergent reflection loop with implicit gradients");
                };
                assert!(matches!(function.expressions[*level],
                    naga::Expression::Literal(naga::Literal::F32(value)) if value == 0.0),
                    "PostProcess scene/history/G-buffer inputs require base level 0");
                samples += 1;
            }
        }
    }
    assert_eq!(samples, 5, "Expected the five actual PostProcess color target helpers");
    let mut hlsl = String::new();
    naga::back::hlsl::Writer::new(&mut hlsl, &naga::back::hlsl::Options::default())
        .write(&module, &info).unwrap();
    for name in targets {
        assert!(!hlsl.contains(&format!("{name}.Sample(")), "FXC would encounter an implicit color-target gradient");
        assert_eq!(hlsl.matches(&format!("{name}.SampleLevel(")).count(), 1);
    }
    fs::write(output, hlsl).unwrap();
    println!("PostProcess Naga/HLSL PASS: five explicit base-level color samples; no implicit Sample in the reflection loop");
}
