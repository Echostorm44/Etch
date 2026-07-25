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

    let d1 = texel_size * 1.5;
    let d2 = texel_size * 3.5;

    let tl = textureSample(texture, texture_sampler, in.uv - d2).rgb;
    let t  = textureSample(texture, texture_sampler, in.uv + vec2( 0.0, -d2.y)).rgb;
    let tr = textureSample(texture, texture_sampler, in.uv + vec2( d2.x, -d2.y)).rgb;
    let l  = textureSample(texture, texture_sampler, in.uv + vec2(-d2.x,  0.0)).rgb;
    let c  = textureSample(texture, texture_sampler, in.uv).rgb;
    let r  = textureSample(texture, texture_sampler, in.uv + d2.x).rgb;
    let bl = textureSample(texture, texture_sampler, in.uv + vec2(-d2.x,  d2.y)).rgb;
    let b  = textureSample(texture, texture_sampler, in.uv + vec2( 0.0,  d2.y)).rgb;
    let br = textureSample(texture, texture_sampler, in.uv + d2).rgb;

    let m = 1.0 / 17.0;
    let result = (tl + tr + bl + br) * m + (t + l + r + b) * (2.0 * m) + c * (4.0 * m);
    return vec4(result, 1.0);
}