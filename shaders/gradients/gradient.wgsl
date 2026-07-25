override gradient_kind: u32 = 0u;
override extend: u32 = 0u;

struct PerFrame {
    surface_size: vec2<f32>,
    _pad0: vec2<f32>,
}

struct PerDraw {
    p0: vec2<f32>,
    p1: vec2<f32>,
    center: vec2<f32>,
    radius: f32,
    start_angle: f32,
    end_angle: f32,
    color0: vec4<f32>,
    color1: vec4<f32>,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var<uniform> per_draw: PerDraw;
@group(1) @binding(1) var lut: texture_1d<f32>;
@group(1) @binding(2) var lut_sampler: sampler;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) local: vec2<f32>,
}

@vertex fn vs_main(@location(0) xy: vec2<f32>) -> VsOut {
    var out: VsOut;
    out.pos = vec4(xy * 2.0 - 1.0, 0.0, 1.0);
    out.local = xy;
    return out;
}

fn apply_extend(t: f32, extend_mode: u32) -> f32 {
    switch (extend_mode) {
        case 1u: {
            let t2 = f32(1u) - abs(fract(t * 0.5) * 2.0 - 1.0);
            return t2;
        }
        case 2u: {
            return fract(t);
        }
        default: {
            return clamp(t, 0.0, 1.0);
        }
    }
}

fn sample_lut(t: f32) -> vec4<f32> {
    return textureSample(lut, lut_sampler, t).rgba;
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    var t_raw = 0.0;
    switch (gradient_kind) {
        case 0u: {
            let p = in.local;
            let p0 = per_draw.p0;
            let p1 = per_draw.p1;
            let d = p1 - p0;
            let len_sq = dot(d, d);
            if (len_sq < 0.0001) {
                t_raw = 0.0;
            } else {
                t_raw = dot(p - p0, d) / len_sq;
            }
        }
        case 1u: {
            let dist = distance(in.local, per_draw.center);
            t_raw = dist / per_draw.radius;
        }
        case 2u: {
            let dx = in.local.x - per_draw.center.x;
            let dy = in.local.y - per_draw.center.y;
            var angle = atan2(dy, dx);
            t_raw = angle / (2.0 * 3.14159265) + 0.5;
        }
        case 3u: {
            let dx = in.local.x - per_draw.center.x;
            let dy = in.local.y - per_draw.center.y;
            var angle = atan2(dy, dx);
            let start = per_draw.start_angle;
            let end = per_draw.end_angle;
            let range = end - start;
            if (range < 0.0001) {
                t_raw = 0.0;
            } else {
                t_raw = (angle - start) / range;
            }
        }
        default: {
            t_raw = 0.0;
        }
    }

    let t = apply_extend(t_raw, extend);

    return sample_lut(t);
}