@group(0) @binding(0) var<uniform> transform: mat4x4<f32>;
@group(0) @binding(1) var<uniform> transform2: mat4x4<f32>;

@vertex
fn vs(@builtin(vertex_index) idx: u32) -> @builtin(position) vec4<f32> {
    var p = array<vec2<f32>, 3>(
        vec2(-0.5, -0.5),
        vec2( 0.5, -0.5),
        vec2( 0.0,  0.5));
    return vec4<f32>(p[idx], 0.0, 1.0);
}

@fragment
fn fs() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}