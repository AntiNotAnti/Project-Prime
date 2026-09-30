use std::{env,fs};
fn main() {
    let args: Vec<String> = env::args().collect();
    let source = fs::read_to_string(&args[1]).unwrap();
    let stage = if args[1].ends_with(".vert") { naga::ShaderStage::Vertex } else { naga::ShaderStage::Fragment };
    let module = naga::front::glsl::Frontend::default().parse(&naga::front::glsl::Options::from(stage), &source).unwrap_or_else(|e| panic!("{e:#?}"));
    let info = naga::valid::Validator::new(naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::CONTROL_FLOW_UNIFORMITY, naga::valid::Capabilities::all()).validate(&module).unwrap_or_else(|e| panic!("{e:#?}"));
    let output = naga::back::wgsl::write_string(&module, &info, naga::back::wgsl::WriterFlags::EXPLICIT_TYPES).unwrap();
    fs::write(&args[2], output).unwrap();
}
