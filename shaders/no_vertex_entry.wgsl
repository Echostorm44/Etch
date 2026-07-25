@group(0) @binding(0) var<uniform> transform: mat4x4<f32>;

fn helper() { }

@fragment
fn fs() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}