override subpixel_mode: u32 = 0u;

struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}

struct PerDraw {
    transform: mat3x3<f32>,
    color: vec4<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var texture: texture_2d<f32>;
@group(1) @binding(1) var texture_sampler: sampler;
@group(2) @binding(0) var<uniform> per_draw: PerDraw;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) subpixel_phase: f32,
}

struct GlyphVertex {
    @location(0) atlas_uv: vec2<f32>,
    @location(1) quad_size: vec2<f32>,
    @location(2) subpixel_offset: f32,
}

@vertex fn vs_main(in: GlyphVertex) -> VsOut {
    var out: VsOut;
    let world_pos = in.atlas_uv * in.quad_size;
    let clip = per_draw.transform * vec3(world_pos, 1.0);
    out.pos = vec4(clip.xy / per_frame.surface_size * vec2(2.0, -2.0) + vec2(-1.0, 1.0), 0.0, 1.0);
    out.uv = in.atlas_uv;
    out.subpixel_phase = in.subpixel_offset;
    return out;
}

fn sample_subpixel_3(tex: texture_2d<f32>, samp: sampler, uv: vec2<f32>, phase: f32) -> vec3<f32> {
    let tex_size = vec2<f32>(textureDimensions(tex));
    let subpixel_offset = vec2<f32>(phase / 3.0, 0.0) / tex_size;
    let r = textureSample(tex, samp, uv + vec2(subpixel_offset.x * 0.0, 0.0)).r;
    let g = textureSample(tex, samp, uv + vec2(subpixel_offset.x * 1.0, 0.0)).r;
    let b = textureSample(tex, samp, uv + vec2(subpixel_offset.x * 2.0, 0.0)).r;
    return vec3(r, g, b);
}

fn sample_subpixel_5(tex: texture_2d<f32>, samp: sampler, uv: vec2<f32>, phase: f32) -> f32 {
    let tex_size = vec2<f32>(textureDimensions(tex));
    let offsets = array<f32, 5>(-2.0, -1.0, 0.0, 1.0, 2.0);
    var total = 0.0;
    for (var i = 0u; i < 5u; i = i + 1u) {
        let subpixel_offset = (phase + offsets[i]) / 5.0;
        let sample_uv = uv + vec2<f32>(subpixel_offset, 0.0) / tex_size;
        total = total + textureSample(tex, samp, sample_uv).r;
    }
    return total / 5.0;
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    var alpha: f32;
    switch (subpixel_mode) {
        case 1u: {
            let rgb = sample_subpixel_3(texture, texture_sampler, in.uv, in.subpixel_phase);
            alpha = (rgb.r + rgb.g + rgb.b) / 3.0;
        }
        case 2u: {
            alpha = sample_subpixel_5(texture, texture_sampler, in.uv, in.subpixel_phase);
        }
        default: {
            alpha = textureSample(texture, texture_sampler, in.uv).r;
        }
    }
    return vec4(per_draw.color.rgb * alpha, alpha);
}