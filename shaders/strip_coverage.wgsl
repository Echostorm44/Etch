struct PerFrame {
    surface_size: vec2<f32>,
    tile_width: u32,
    tile_height: u32,
    grid_width: u32,
    _pad: u32,
}

struct Paint {
    color: vec4<f32>,
    kind: u32,
    gradient_start: u32,
    gradient_count: u32,
    _pad: u32,
    p0: vec2<f32>,
    p1: vec2<f32>,
}

struct Strip {
    tile_index: u32,
    row_mask: u32,
    x0: u32,
    x1: u32,
    coverage_offset: u32,
    paint_id: u32,
}

@group(0) @binding(0) var<uniform> per_frame: PerFrame;
@group(1) @binding(0) var<storage, read> strips: array<Strip>;
@group(1) @binding(1) var<storage, read> coverage: array<u32>;
@group(2) @binding(0) var<storage, read> paints: array<Paint>;
@group(2) @binding(1) var<storage, read> gradient_stops: array<f32>;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) @interpolate(flat) strip_idx: u32,
    @location(1) local_xy: vec2<f32>,
    @location(2) world_pos: vec2<f32>,
}

@vertex
fn vs_main(@location(0) xy: vec2<f32>, @builtin(instance_index) instance_idx: u32) -> VsOut {
    var out: VsOut;
    let strip = strips[instance_idx];
    let tile_x = strip.tile_index % per_frame.grid_width;
    let tile_y = strip.tile_index / per_frame.grid_width;
    let tw = f32(per_frame.tile_width);
    let th = f32(per_frame.tile_height);
    let world_xy = vec2<f32>(f32(tile_x) * tw, f32(tile_y) * th) + xy * vec2<f32>(tw, th);
    let ndc = world_xy / per_frame.surface_size;
    out.pos = vec4<f32>(ndc * vec2<f32>(2.0, -2.0) + vec2<f32>(-1.0, 1.0), 0.0, 1.0);
    out.strip_idx = instance_idx;
    out.local_xy = xy * vec2<f32>(tw, th);
    out.world_pos = world_xy;
    return out;
}

fn sample_gradient(paint: Paint, world_pos: vec2<f32>) -> vec4<f32> {
    var t: f32;
    if (paint.kind == 1u) {
        // Linear gradient
        let v = paint.p1 - paint.p0;
        let len_sq = dot(v, v);
        if (len_sq < 0.0001) {
            t = 0.0;
        } else {
            t = dot(world_pos - paint.p0, v) / len_sq;
        }
    } else {
        // Radial gradient
        let radius = paint.p1.x;
        if (radius < 0.0001) {
            t = 0.0;
        } else {
            t = distance(world_pos, paint.p0) / radius;
        }
    }
    t = clamp(t, 0.0, 1.0);

    let count = paint.gradient_count;
    if (count == 0u) {
        return vec4<f32>(0.0, 0.0, 0.0, 1.0);
    }
    if (count == 1u) {
        let base = paint.gradient_start;
        return vec4<f32>(
            gradient_stops[base + 1u],
            gradient_stops[base + 2u],
            gradient_stops[base + 3u],
            gradient_stops[base + 4u]
        );
    }

    // Find bracketing stops
    var lower_idx = 0u;
    var upper_idx = 1u;
    for (var i = 1u; i < count; i = i + 1u) {
        let offset = gradient_stops[paint.gradient_start + i * 5u];
        if (offset <= t) {
            lower_idx = i;
            upper_idx = i + 1u;
        } else {
            break;
        }
    }
    if (upper_idx >= count) {
        upper_idx = count - 1u;
    }

    let lower_base = paint.gradient_start + lower_idx * 5u;
    let upper_base = paint.gradient_start + upper_idx * 5u;
    let t0 = gradient_stops[lower_base];
    let t1 = gradient_stops[upper_base];
    let stop_t = select(0.0, (t - t0) / (t1 - t0), t1 > t0);

    let c0 = vec4<f32>(
        gradient_stops[lower_base + 1u],
        gradient_stops[lower_base + 2u],
        gradient_stops[lower_base + 3u],
        gradient_stops[lower_base + 4u]
    );
    let c1 = vec4<f32>(
        gradient_stops[upper_base + 1u],
        gradient_stops[upper_base + 2u],
        gradient_stops[upper_base + 3u],
        gradient_stops[upper_base + 4u]
    );
    return mix(c0, c1, stop_t);
}

@fragment
fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let strip = strips[in.strip_idx];
    let frag_y = u32(in.local_xy.y);
    let frag_x = u32(in.local_xy.x);

    if ((strip.row_mask & (1u << frag_y)) == 0u) {
        discard;
    }

    if (frag_x < strip.x0 || frag_x > strip.x1) {
        discard;
    }

    let x_offset = frag_x - strip.x0;
    let rows_before = bitcount(strip.row_mask & ((1u << frag_y) - 1u));
    let row_width = strip.x1 - strip.x0 + 1u;
    let byte_idx = strip.coverage_offset + rows_before * row_width + x_offset;

    let packed_idx = byte_idx / 4u;
    let byte_in_packed = byte_idx % 4u;
    let packed_u32 = coverage[packed_idx];
    let cov_byte = (packed_u32 >> (byte_in_packed * 8u)) & 0xFFu;
    let cov = f32(cov_byte) / 255.0;

    if (cov == 0.0) {
        discard;
    }

    let paint = paints[strip.paint_id];
    var color: vec4<f32>;
    if (paint.kind == 0u) {
        color = paint.color;
    } else {
        color = sample_gradient(paint, in.world_pos);
    }

    return vec4<f32>(color.rgb * cov, color.a * cov);
}

fn bitcount(x: u32) -> u32 {
    var count = 0u;
    var v = x;
    while (v != 0u) {
        count = count + 1u;
        v = v & (v - 1u);
    }
    return count;
}
