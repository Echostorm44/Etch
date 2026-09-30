// Dual-filter blur, downsample pass: each destination texel is the plain average of the 2x2 source
// texels under it (clamped at the edge for odd sizes). Texel-exact loads, no filtering, so it
// matches the CPU reference (Etch.Raster.Cpu.Blur.BjorgeBlur) to rounding.

struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var source: texture_2d<f32>;
@group(1) @binding(1) var source_sampler: sampler;

// A single triangle covering the target; xy arrives in clip space.
@vertex fn vs_main(@location(0) xy: vec2<f32>) -> @builtin(position) vec4<f32> {
    return vec4(xy, 0.0, 1.0);
}

@fragment fn fs_main(@builtin(position) frag: vec4<f32>) -> @location(0) vec4<f32> {
    let last = vec2<i32>(textureDimensions(source)) - vec2(1, 1);
    let p0 = min(vec2<i32>(frag.xy) * 2, last);
    let p1 = min(vec2<i32>(frag.xy) * 2 + vec2(1, 1), last);

    let sum = textureLoad(source, p0, 0)
            + textureLoad(source, vec2(p1.x, p0.y), 0)
            + textureLoad(source, vec2(p0.x, p1.y), 0)
            + textureLoad(source, p1, 0);
    return sum * 0.25;
}
