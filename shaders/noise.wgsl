struct NoiseUniforms {
    scale: f32,
    octaves: u32,
    persistence: f32,
    seed: u32,
    opacity: f32,
    _pad0: f32,
    _pad1: f32,
    _pad2: f32,
}

@group(0) @binding(0) var<uniform> per_frame: NoiseUniforms;

fn grad3(hash: u32) -> vec3<f32> {
    let h = hash & 11u;
    let u = select(0.0, 1.0, h < 4u);
    let v = select(1.0, 2.0, h < 4u || h == 12u || h == 14u);
    let g = vec3<f32>(u, v, 0.0);
    return select(g, -g, bool(h & 1u)) + select(g, -g, bool(h & 2u));
}

fn hash_perm(x: u32, seed: u32) -> u32 {
    var h = x + seed;
    h = ((h >> 16u) ^ h) * 0x45d9f3bu;
    h = ((h >> 16u) ^ h) * 0x45d9f3bu;
    h = (h >> 16u) ^ h;
    return h % 12u;
}

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

const F2: f32 = 0.366025403784439;
const G2: f32 = 0.211324865405187;

fn simplex_noise_2d(p: vec2<f32>, seed: u32) -> f32 {
    let s = (p.x + p.y) * F2;
    let i = floor(p.x + s);
    let j = floor(p.y + s);
    let t = (i + j) * G2;
    let X0 = i - t;
    let Y0 = j - t;
    let x0 = p.x - X0;
    let y0 = p.y - Y0;

    let i1 = select(0u, 1u, x0 > y0);
    let j1 = 1u - i1;

    let x1 = x0 - f32(i1) + G2;
    let y1 = y0 - f32(j1) + G2;
    let x2 = x0 - 1.0 + 2.0 * G2;
    let y2 = y0 - 1.0 + 2.0 * G2;

    let ii = u32(i) & 255u;
    let jj = u32(j) & 255u;

    let gi0 = hash_perm(ii + hash_perm(jj, seed), seed);
    let gi1 = hash_perm(ii + i1 + hash_perm(jj + j1, seed), seed);
    let gi2 = hash_perm(ii + 1u + hash_perm(jj + 1u, seed), seed);

    var n = 0.0;

    var corner = vec2(x0, y0);
    var t_val = 0.5 - corner.x * corner.x - corner.y * corner.y;
    if (t_val >= 0.0) {
        t_val = t_val * t_val;
        n += t_val * t_val * dot(grad3(gi0), vec3(corner.x, corner.y, 0.0));
    }

    corner = vec2(x1, y1);
    t_val = 0.5 - corner.x * corner.x - corner.y * corner.y;
    if (t_val >= 0.0) {
        t_val = t_val * t_val;
        n += t_val * t_val * dot(grad3(gi1), vec3(corner.x, corner.y, 0.0));
    }

    corner = vec2(x2, y2);
    t_val = 0.5 - corner.x * corner.x - corner.y * corner.y;
    if (t_val >= 0.0) {
        t_val = t_val * t_val;
        n += t_val * t_val * dot(grad3(gi2), vec3(corner.x, corner.y, 0.0));
    }

    return 70.0 * n;
}

fn fbm_2d(p: vec2<f32>, octaves: u32, persistence: f32, seed: u32) -> f32 {
    var value = 0.0;
    var amplitude = 1.0;
    var frequency = 1.0;
    var maxValue = 0.0;

    for (var i = 0u; i < octaves; i++) {
        value += amplitude * simplex_noise_2d(p * frequency, seed + i);
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= 2.0;
    }

    return value / maxValue;
}

@fragment fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let scaled = in.uv * per_frame.scale;
    let n = fbm_2d(scaled, per_frame.octaves, per_frame.persistence, per_frame.seed);
    let noise = n * 0.5 + 0.5;
    return vec4(noise, noise, noise, per_frame.opacity);
}
