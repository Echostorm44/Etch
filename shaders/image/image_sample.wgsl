override sample_mode: u32 = 0u;

struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}

struct PerDraw {
    transform: mat3x3<f32>,
    color: vec4<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var<uniform> per_draw: PerDraw;
@group(1) @binding(1) var texture: texture_2d<f32>;
@group(1) @binding(2) var texture_sampler: sampler;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex fn vs_main(@location(0) xy: vec2<f32>) -> VsOut {
    var out: VsOut;
    let clip = per_draw.transform * vec3(xy, 1.0);
    out.pos = vec4(clip.xy / per_frame.surface_size * vec2(2.0, -2.0) + vec2(-1.0, 1.0), 0.0, 1.0);
    out.uv = xy;
    return out;
}

fn cubic_weight(t: f32, c: f32) -> f32 {
    let t2 = t * t;
    let t3 = t2 * t;
    let a = -c;
    let b = 2.0 * c;
    let d = -a;
    return a * t3 + b * t2 + c * t + d;
}

fn sample_bilinear(tex: texture_2d<f32>, samp: sampler, uv: vec2<f32>) -> vec4<f32> {
    return textureSample(tex, samp, uv).rgba;
}

fn sample_bicubic(tex: texture_2d<f32>, samp: sampler, uv: vec2<f32>) -> vec4<f32> {
    let tex_size = vec2<f32>(textureDimensions(tex));
    let inv_tex_size = 1.0 / tex_size;
    let inv_tex_size_x = vec2<f32>(inv_tex_size.x, 0.0);
    let inv_tex_size_y = vec2<f32>(0.0, inv_tex_size.y);

    var pixel = uv * tex_size - 0.5;
    let fxy = fract(pixel);
    pixel = floor(pixel) + 0.5;

    let f00 = pixel + vec2(-1.0, -1.0);
    let f10 = pixel + vec2(0.0, -1.0);
    let f20 = pixel + vec2(1.0, -1.0);
    let f30 = pixel + vec2(2.0, -1.0);

    let f01 = pixel + vec2(-1.0, 0.0);
    let f11 = pixel + vec2(0.0, 0.0);
    let f21 = pixel + vec2(1.0, 0.0);
    let f31 = pixel + vec2(2.0, 0.0);

    let f02 = pixel + vec2(-1.0, 1.0);
    let f12 = pixel + vec2(0.0, 1.0);
    let f22 = pixel + vec2(1.0, 1.0);
    let f32 = pixel + vec2(2.0, 1.0);

    let f03 = pixel + vec2(-1.0, 2.0);
    let f13 = pixel + vec2(0.0, 2.0);
    let f23 = pixel + vec2(1.0, 2.0);
    let f33 = pixel + vec2(2.0, 2.0);

    let c00 = textureSample(tex, samp, f00 * inv_tex_size).rgb;
    let c10 = textureSample(tex, samp, f10 * inv_tex_size).rgb;
    let c20 = textureSample(tex, samp, f20 * inv_tex_size).rgb;
    let c30 = textureSample(tex, samp, f30 * inv_tex_size).rgb;

    let c01 = textureSample(tex, samp, f01 * inv_tex_size).rgb;
    let c11 = textureSample(tex, samp, f11 * inv_tex_size).rgb;
    let c21 = textureSample(tex, samp, f21 * inv_tex_size).rgb;
    let c31 = textureSample(tex, samp, f31 * inv_tex_size).rgb;

    let c02 = textureSample(tex, samp, f02 * inv_tex_size).rgb;
    let c12 = textureSample(tex, samp, f12 * inv_tex_size).rgb;
    let c22 = textureSample(tex, samp, f22 * inv_tex_size).rgb;
    let c32 = textureSample(tex, samp, f32 * inv_tex_size).rgb;

    let c03 = textureSample(tex, samp, f03 * inv_tex_size).rgb;
    let c13 = textureSample(tex, samp, f13 * inv_tex_size).rgb;
    let c23 = textureSample(tex, samp, f23 * inv_tex_size).rgb;
    let c33 = textureSample(tex, samp, f33 * inv_tex_size).rgb;

    let wx = cubic_weight(fxy.x, 0.5);
    let wy = cubic_weight(fxy.y, 0.5);

    let wx0 = 1.0 - wx;
    let wy0 = 1.0 - wy;

    let row0x = (c00 * wx0 + c10 * wx) * wy0 + (c01 * wx0 + c11 * wx) * wy;
    let row1x = (c10 * wx0 + c20 * wx) * wy0 + (c11 * wx0 + c21 * wx) * wy;
    let row2x = (c20 * wx0 + c30 * wx) * wy0 + (c21 * wx0 + c31 * wx) * wy;
    let row3x = (c30 * wx0 + c00 * wx) * wy0 + (c31 * wx0 + c03 * wx) * wy;

    let row0y = (c02 * wx0 + c12 * wx) * wy0 + (c03 * wx0 + c13 * wx) * wy;

    return vec4(row0x, 1.0);
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    switch (sample_mode) {
        case 1u: {
            return sample_bicubic(texture, texture_sampler, in.uv);
        }
        default: {
            return sample_bilinear(texture, texture_sampler, in.uv);
        }
    }
}