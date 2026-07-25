struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}
struct PerDraw {
    color: vec4<f32>,
    transform: mat3x3<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(2) @binding(0) var<uniform> per_draw: PerDraw;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) local: vec2<f32>,
}

@vertex fn vs_main(@location(0) xy: vec2<f32>) -> VsOut {
    var out: VsOut;
    let clip = per_draw.transform * vec3(xy, 1.0);
    out.pos = vec4(clip.xy / per_frame.surface_size * vec2(2.0, -2.0) + vec2(-1.0, 1.0), 0.0, 1.0);
    out.local = xy;
    return out;
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    return per_draw.color;
}
