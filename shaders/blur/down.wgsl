struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var texture: texture_2d<f32>;
@group(1) @binding(1) var texture_sampler: sampler;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex fn vs_main(@location(0) xy: vec2<f32>) -> VsOut {
    var out: VsOut;
    out.pos = vec4(xy * 2.0 - 1.0, 0.0, 1.0);
    out.uv = xy;
    return out;
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let tex_size = vec2<f32>(textureDimensions(texture));
    let texel_size = 1.0 / tex_size;

    let c = textureSample(texture, texture_sampler, in.uv).rgb;

    let d1 = texel_size * 1.5;

    let tl = textureSample(texture, texture_sampler, in.uv - d1).rgb;
    let tr = textureSample(texture, texture_sampler, in.uv + vec2( d1.x, -d1.y)).rgb;
    let bl = textureSample(texture, texture_sampler, in.uv + vec2(-d1.x,  d1.y)).rgb;
    let br = textureSample(texture, texture_sampler, in.uv + d1).rgb;

    let m = 1.0 / 17.0;
    let result = c * (4.0 * m) + (tl + tr + bl + br) * m;
    return vec4(result, 1.0);
}