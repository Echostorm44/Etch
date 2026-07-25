struct PerDraw {
    blend_mode: u32,
    color0: vec4<f32>,
    color1: vec4<f32>,
}

@group(1) @binding(0) var<uniform> per_draw: PerDraw;

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

fn blend_multiply(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return s * d;
}

fn blend_screen(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return 1.0 - (1.0 - s) * (1.0 - d);
}

fn blend_overlay(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    var result: vec3<f32>;
    for (var i = 0u; i < 3u; i++) {
        if (d[i] < 0.5) {
            result[i] = 2.0 * s[i] * d[i];
        } else {
            result[i] = 1.0 - 2.0 * (1.0 - s[i]) * (1.0 - d[i]);
        }
    }
    return result;
}

fn blend_darken(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return min(s, d);
}

fn blend_lighten(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return max(s, d);
}

fn blend_color_dodge(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    var result: vec3<f32>;
    for (var i = 0u; i < 3u; i++) {
        if (s[i] >= 1.0) {
            result[i] = 1.0;
        } else {
            let denom = 1.0 - s[i];
            if (denom < 0.00001) {
                result[i] = 1.0;
            } else {
                result[i] = min(1.0, d[i] / denom);
            }
        }
    }
    return result;
}

fn blend_color_burn(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    var result: vec3<f32>;
    for (var i = 0u; i < 3u; i++) {
        if (s[i] <= 0.0) {
            result[i] = 0.0;
        } else {
            let denom = s[i];
            if (denom < 0.00001) {
                result[i] = 0.0;
            } else {
                result[i] = max(0.0, 1.0 - (1.0 - d[i]) / denom);
            }
        }
    }
    return result;
}

fn blend_hard_light(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    var result: vec3<f32>;
    for (var i = 0u; i < 3u; i++) {
        if (s[i] < 0.5) {
            result[i] = 2.0 * s[i] * d[i];
        } else {
            result[i] = 1.0 - 2.0 * (1.0 - s[i]) * (1.0 - d[i]);
        }
    }
    return result;
}

fn blend_soft_light(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    var result: vec3<f32>;
    for (var i = 0u; i < 3u; i++) {
        let s2 = s[i];
        let d2 = d[i];
        if (d2 < 0.25) {
            result[i] = d2 - (1.0 - 2.0 * s2) * d2 * (1.0 - d2);
        } else if (s2 < 0.5) {
            result[i] = d2 + (2.0 * s2 - 1.0) * (sqrt(d2) - d2);
        } else {
            if (d2 < 0.75) {
                result[i] = d2 + (2.0 * s2 - 1.0) * ((4.0 * d2 - 3.0) * d2 + 0.75);
            } else {
                result[i] = d2 - (2.0 * s2 - 1.0) * (1.0 - d2) * (4.0 * d2 - 1.0);
            }
        }
    }
    return result;
}

fn blend_difference(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return abs(s - d);
}

fn blend_exclusion(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    return s + d - 2.0 * s * d;
}

fn blend_separable(mode: u32, s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    switch (mode) {
        case 1u { return blend_multiply(s, d); }
        case 2u { return blend_screen(s, d); }
        case 3u { return blend_overlay(s, d); }
        case 4u { return blend_darken(s, d); }
        case 5u { return blend_lighten(s, d); }
        case 6u { return blend_color_dodge(s, d); }
        case 7u { return blend_color_burn(s, d); }
        case 8u { return blend_hard_light(s, d); }
        case 9u { return blend_soft_light(s, d); }
        case 10u { return blend_difference(s, d); }
        case 11u { return blend_exclusion(s, d); }
        default { return s; }
    }
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let src = per_draw.color0.rgb;
    let dst = per_draw.color1.rgb;
    let blended = blend_separable(per_draw.blend_mode, src, dst);
    let alpha = per_draw.color0.a;
    return vec4(blended, alpha);
}
