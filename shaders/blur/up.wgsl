// Dual-filter blur, upsample pass: a 3x3 tent (corners 1, edges 2, centre 4, over 16) around the
// source texel under each destination texel, clamped at the edges. Texel-exact loads, so it matches
// the CPU reference (Etch.Raster.Cpu.Blur.BjorgeBlur) to rounding.

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
    let s = min(vec2<i32>(frag.xy) / 2, last);
    let lo = max(s - vec2(1, 1), vec2(0, 0));
    let hi = min(s + vec2(1, 1), last);

    let corners = textureLoad(source, lo, 0)
                + textureLoad(source, vec2(hi.x, lo.y), 0)
                + textureLoad(source, vec2(lo.x, hi.y), 0)
                + textureLoad(source, hi, 0);
    let edges = textureLoad(source, vec2(s.x, lo.y), 0)
              + textureLoad(source, vec2(lo.x, s.y), 0)
              + textureLoad(source, vec2(hi.x, s.y), 0)
              + textureLoad(source, vec2(s.x, hi.y), 0);
    let centre = textureLoad(source, s, 0);
    return corners * (1.0 / 16.0) + edges * (2.0 / 16.0) + centre * (4.0 / 16.0);
}
