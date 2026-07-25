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

fn lum(c: vec3<f32>) -> f32 {
    return dot(c, vec3<f32>(0.30, 0.59, 0.11));
}

fn sat(c: vec3<f32>) -> f32 {
    return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
}

fn clip_color(c: vec3<f32>) -> vec3<f32> {
    let l = lum(c);
    let n = min(c.r, min(c.g, c.b));
    let x = max(c.r, max(c.g, c.b));

    var result = c;

    if (n < 0.0) {
        let denom = l - n;
        if (denom > 0.0) {
            result = l + (result - l) * l / denom;
        }
    }

    if (x > 1.0) {
        let denom = x - l;
        if (denom > 0.0) {
            result = l + (result - l) * (1.0 - l) / denom;
        }
    }

    return result;
}

fn set_lum(c: vec3<f32>, l: f32) -> vec3<f32> {
    let d = l - lum(c);
    return clip_color(c + d);
}

fn set_sat(c: vec3<f32>, s: f32) -> vec3<f32> {
    // Sort channels to find min, mid, max
    var c0 = c.r;
    var c1 = c.g;
    var c2 = c.b;
    var i0 = 0u;
    var i1 = 1u;
    var i2 = 2u;

    if (c0 > c1) {
        let t = c0; c0 = c1; c1 = t;
        let ti = i0; i0 = i1; i1 = ti;
    }
    if (c1 > c2) {
        let t = c1; c1 = c2; c2 = t;
        let ti = i1; i1 = i2; i2 = ti;
    }
    if (c0 > c1) {
        let t = c0; c0 = c1; c1 = t;
        let ti = i0; i0 = i1; i1 = ti;
    }

    var min_val = c0;
    var mid_val = c1;
    var max_val = c2;

    if (max_val > min_val) {
        mid_val = ((mid_val - min_val) * s) / (max_val - min_val);
        max_val = s;
        min_val = 0.0;
    } else {
        mid_val = 0.0;
        max_val = 0.0;
        min_val = 0.0;
    }

    var arr = vec3<f32>(0.0, 0.0, 0.0);
    if (i0 == 0u) { arr.r = min_val; }
    else if (i0 == 1u) { arr.g = min_val; }
    else { arr.b = min_val; }

    if (i1 == 0u) { arr.r = mid_val; }
    else if (i1 == 1u) { arr.g = mid_val; }
    else { arr.b = mid_val; }

    if (i2 == 0u) { arr.r = max_val; }
    else if (i2 == 1u) { arr.g = max_val; }
    else { arr.b = max_val; }

    return arr;
}

fn blend_hue(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    // Hue: SetLum(SetSat(Cs, Sat(Cb)), Lum(Cb))
    return set_lum(set_sat(s, sat(d)), lum(d));
}

fn blend_saturation(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    // Saturation: SetLum(SetSat(Cb, Sat(Cs)), Lum(Cb))
    return set_lum(set_sat(d, sat(s)), lum(d));
}

fn blend_color(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    // Color: SetLum(Cs, Lum(Cb))
    return set_lum(s, lum(d));
}

fn blend_luminosity(s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    // Luminosity: SetLum(Cb, Lum(Cs))
    return set_lum(d, lum(s));
}

fn blend_nonseparable(mode: u32, s: vec3<f32>, d: vec3<f32>) -> vec3<f32> {
    switch (mode) {
        case 12u { return blend_hue(s, d); }
        case 13u { return blend_saturation(s, d); }
        case 14u { return blend_color(s, d); }
        case 15u { return blend_luminosity(s, d); }
        default { return s; }
    }
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let src = per_draw.color0.rgb;
    let dst = per_draw.color1.rgb;
    let blended = blend_nonseparable(per_draw.blend_mode, src, dst);
    let alpha = per_draw.color0.a;
    return vec4(blended, alpha);
}
