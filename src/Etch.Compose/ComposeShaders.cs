namespace Etch.Compose;

/// <summary>
/// The WGSL the GPU composer runs. These shaders define the per-pixel semantics of every draw kind;
/// the CPU composer's kernels are ports of exactly this arithmetic, so a change here must be
/// mirrored there (the GPU/CPU parity tests fail otherwise).
/// </summary>
/// <remarks>
/// Conventions shared by every kind: pixel centres are sampled (<c>position.xy</c> = x + 0.5);
/// antialiasing is one device pixel wide, centred on the shape's edge (<c>clamp(0.5 − distance, 0, 1)</c>,
/// the pixel area inside a straight edge); colours are linear-light with straight alpha; the target
/// is sRGB-encoded, so the fixed-function blend runs in linear light and every draw is re-quantized
/// to 8 bits per channel when written.
/// </remarks>
internal static class ComposeShaders
{
    private const string Surface = """
        struct SurfaceSize {
            width: f32,
            height: f32,
            offset_x: f32,
            offset_y: f32,
            text_gamma: f32,
            light_weight: f32,
            dissolve: f32,
            pad2: f32,
        };

        struct ClipEntry {
            rect: vec4<f32>,
            round_rect: vec4<f32>,
            round_radius: f32,
            has_round: u32,
            has_mask: u32,
            pad0: u32,
            mask_origin: vec2<i32>,
            mask_uv: vec2<i32>,
            mask_size: vec2<i32>,
            pad1: vec2<i32>,
        };

        const QUAD = array<vec2<f32>, 4>(
            vec2<f32>(0.0, 0.0),
            vec2<f32>(1.0, 0.0),
            vec2<f32>(0.0, 1.0),
            vec2<f32>(1.0, 1.0)
        );
        """;

    // Needs `surface`, `clips` and `mask_tex` declared by the including shader.
    private const string Common = """
        fn to_clip_space(p: vec2<f32>) -> vec4<f32> {
            return vec4<f32>(p.x / surface.width * 2.0 - 1.0, 1.0 - p.y / surface.height * 2.0, 0.0, 1.0);
        }

        fn dissolved(position: vec2<f32>) -> bool {
            if (surface.dissolve <= 0.0) {
                return false;
            }
            let dn = fract(sin(dot(floor(position), vec2<f32>(12.9898, 78.233))) * 43758.5453);
            return dn < surface.dissolve;
        }

        // Signed distance to a rounded rect (negative inside).
        fn sdf_round_rect(p: vec2<f32>, lo: vec2<f32>, hi: vec2<f32>, r: f32) -> f32 {
            let center = (lo + hi) * 0.5;
            let half_size = (hi - lo) * 0.5;
            let d = abs(p - center) - half_size + vec2<f32>(r, r);
            return length(max(d, vec2<f32>(0.0))) + min(max(d.x, d.y), 0.0) - r;
        }

        // Coverage of a pixel whose centre is `dist` device pixels outside an edge: the area of the
        // pixel on the inside of a straight edge through it (exact for axis-aligned edges), so
        // analytic shapes have the same weight as rasterized masks of the same geometry.
        fn edge_coverage(dist: f32) -> f32 {
            return clamp(0.5 - dist, 0.0, 1.0);
        }

        fn mask_texel(p: vec2<f32>, origin: vec2<i32>, uv: vec2<i32>, size: vec2<i32>) -> f32 {
            let t = vec2<i32>(floor(p)) - origin;
            if (t.x < 0 || t.y < 0 || t.x >= size.x || t.y >= size.y) {
                return 0.0;
            }
            return textureLoad(mask_tex, t + uv, 0).r;
        }

        fn clip_coverage(p: vec2<f32>, index: u32) -> f32 {
            let c = clips[index];
            if (p.x < c.rect.x || p.x >= c.rect.z || p.y < c.rect.y || p.y >= c.rect.w) {
                return 0.0;
            }
            var cov = 1.0;
            if (c.has_round != 0u) {
                cov = edge_coverage(sdf_round_rect(p, c.round_rect.xy, c.round_rect.zw, c.round_radius));
            }
            if (c.has_mask != 0u) {
                cov = cov * mask_texel(p, c.mask_origin, c.mask_uv, c.mask_size);
            }
            return cov;
        }
        """;

    public const string ShapeWgsl = Surface + """

        struct ShapeInstance {
            quad: vec4<f32>,
            color: vec4<f32>,
            frame: vec4<f32>,
            frame_t: vec4<f32>,
            p: vec4<f32>,
            q: vec4<f32>,
            shape_type: u32,
            clip_index: u32,
            paint_index: u32,
            pad0: u32,
        };

        struct GradientEntry {
            m: vec4<f32>,
            t: vec4<f32>,
            kind: u32,
            stop_start: u32,
            stop_count: u32,
            pad0: u32,
        };

        struct GradientStop {
            offset: f32,
            pad0: f32,
            pad1: f32,
            pad2: f32,
            color: vec4<f32>,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var<storage, read> instances: array<ShapeInstance>;
        @group(0) @binding(2) var<storage, read> clips: array<ClipEntry>;
        @group(0) @binding(3) var<storage, read> gradients: array<GradientEntry>;
        @group(0) @binding(4) var<storage, read> stops: array<GradientStop>;
        @group(0) @binding(5) var mask_tex: texture_2d<f32>;

        """ + Common + """

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) @interpolate(flat) instance_idx: u32,
        };

        @vertex
        fn vs(@builtin(vertex_index) vertex_idx: u32, @builtin(instance_index) instance_idx: u32) -> VertexOutput {
            let inst = instances[instance_idx];
            let pixel_pos = mix(inst.quad.xy, inst.quad.zw, QUAD[vertex_idx]);
            return VertexOutput(to_clip_space(pixel_pos), instance_idx);
        }

        // Signed distance outside an annular sector's angular extent, in device pixels.
        fn sector_angular_distance(d: vec2<f32>, dist: f32, start_angle: f32, sweep_angle: f32) -> f32 {
            let angle = atan2(d.y, d.x);
            let end_angle = start_angle + sweep_angle;
            var test_angle = angle;
            if (sweep_angle > 0.0) {
                while (test_angle < start_angle) {
                    test_angle = test_angle + 6.28318530718;
                }
                while (test_angle > start_angle + 6.28318530718) {
                    test_angle = test_angle - 6.28318530718;
                }
            } else {
                while (test_angle > start_angle) {
                    test_angle = test_angle - 6.28318530718;
                }
                while (test_angle < start_angle - 6.28318530718) {
                    test_angle = test_angle + 6.28318530718;
                }
            }
            var signed_angular = 0.0;
            if (sweep_angle > 0.0) {
                if (test_angle <= end_angle) {
                    signed_angular = -min(test_angle - start_angle, end_angle - test_angle);
                } else {
                    signed_angular = min(test_angle - end_angle, start_angle + 6.28318530718 - test_angle);
                }
            } else {
                if (test_angle >= end_angle) {
                    signed_angular = -min(start_angle - test_angle, test_angle - end_angle);
                } else {
                    signed_angular = min(end_angle - test_angle, test_angle - (start_angle - 6.28318530718));
                }
            }
            return signed_angular * dist;
        }

        // Drop shadow: coverage of a rounded rect blurred by a Gaussian (σ). Exact across x via erf,
        // four Gaussian-weighted samples over y (Evan Wallace, "Fast Rounded Rectangle Shadows").
        fn shadow_erf(x: vec2<f32>) -> vec2<f32> {
            let s = sign(x);
            let a = abs(x);
            var r = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
            r = r * r;
            return s - s / (r * r);
        }

        fn shadow_x(x: f32, y: f32, sigma: f32, corner: f32, half_size: vec2<f32>) -> f32 {
            let delta = min(half_size.y - corner - abs(y), 0.0);
            let curved = half_size.x - corner + sqrt(max(0.0, corner * corner - delta * delta));
            let integral = 0.5 + 0.5 * shadow_erf((vec2<f32>(x, x) + vec2<f32>(-curved, curved)) * (0.70710678 / sigma));
            return integral.y - integral.x;
        }

        fn rounded_box_shadow(lower: vec2<f32>, upper: vec2<f32>, point: vec2<f32>, sigma: f32, corner_in: f32) -> f32 {
            let center = (lower + upper) * 0.5;
            let half_size = (upper - lower) * 0.5;
            let corner = min(corner_in, min(half_size.x, half_size.y));
            let p = point - center;
            let low = p.y - half_size.y;
            let high = p.y + half_size.y;
            let start = clamp(-3.0 * sigma, low, high);
            let end = clamp(3.0 * sigma, low, high);
            let step = (end - start) / 4.0;
            var y = start + step * 0.5;
            var value = 0.0;
            for (var i = 0; i < 4; i = i + 1) {
                let g = exp(-(y * y) / (2.0 * sigma * sigma)) / (2.50662827 * sigma);
                value = value + shadow_x(p.x, p.y - y, sigma, corner, half_size) * g * step;
                y = y + step;
            }
            return clamp(value, 0.0, 1.0);
        }

        fn shape_coverage(inst: ShapeInstance, p: vec2<f32>) -> f32 {
            let lp = vec2<f32>(
                inst.frame.x * p.x + inst.frame.y * p.y + inst.frame_t.x,
                inst.frame.z * p.x + inst.frame.w * p.y + inst.frame_t.y);
            let s = inst.frame_t.z;
            switch (inst.shape_type) {
                case 0u: {
                    if (lp.x >= inst.p.x && lp.x < inst.p.z && lp.y >= inst.p.y && lp.y < inst.p.w) {
                        return 1.0;
                    }
                    return 0.0;
                }
                case 1u: {
                    return edge_coverage((length(lp - inst.p.xy) - inst.p.z) * s);
                }
                case 3u: {
                    let d = length(lp - inst.p.xy);
                    let outer = edge_coverage((d - inst.p.z) * s);
                    let inner = edge_coverage(((inst.p.z - inst.p.w) - d) * s);
                    return min(outer, inner);
                }
                case 4u: {
                    let a = inst.p.xy;
                    let ba = inst.p.zw - a;
                    let len = length(ba);
                    let dir = ba / len;
                    let rel = lp - a;
                    let hw = inst.q.x;
                    var dist = 0.0;
                    if (inst.q.y == 1.0) {
                        let h = clamp(dot(rel, ba) / (len * len), 0.0, 1.0);
                        dist = length(rel - ba * h) - hw;
                    } else {
                        let ext = select(0.0, hw, inst.q.y == 2.0);
                        let along = dot(rel, dir);
                        let perp = dot(rel, vec2<f32>(-dir.y, dir.x));
                        let c = vec2<f32>(abs(along - len * 0.5) - (len * 0.5 + ext), abs(perp) - hw);
                        dist = length(max(c, vec2<f32>(0.0))) + min(max(c.x, c.y), 0.0);
                    }
                    return edge_coverage(dist * s);
                }
                case 5u: {
                    return edge_coverage(sdf_round_rect(lp, inst.p.xy, inst.p.zw, inst.q.x) * s);
                }
                case 6u: {
                    let outer = edge_coverage(sdf_round_rect(lp, inst.p.xy, inst.p.zw, inst.q.x) * s);
                    let inset = vec2<f32>(inst.q.y, inst.q.y);
                    let inner = edge_coverage(-sdf_round_rect(lp, inst.p.xy + inset, inst.p.zw - inset, inst.q.z) * s);
                    return min(outer, inner);
                }
                case 8u: {
                    let d = lp - inst.p.xy;
                    let dist = length(d);
                    var cov = edge_coverage((dist - inst.p.z) * s);
                    if (inst.p.w > 0.0) {
                        cov = min(cov, edge_coverage((inst.p.w - dist) * s));
                    }
                    cov = min(cov, edge_coverage(sector_angular_distance(d, dist, inst.q.x, inst.q.y) * s));
                    if (inst.q.z == 1.0) {
                        let mid = (inst.p.z + inst.p.w) * 0.5;
                        let cap_r = (inst.p.z - inst.p.w) * 0.5;
                        let end_angle = inst.q.x + inst.q.y;
                        let cs = inst.p.xy + mid * vec2<f32>(cos(inst.q.x), sin(inst.q.x));
                        let ce = inst.p.xy + mid * vec2<f32>(cos(end_angle), sin(end_angle));
                        cov = max(cov, edge_coverage((length(lp - cs) - cap_r) * s));
                        cov = max(cov, edge_coverage((length(lp - ce) - cap_r) * s));
                    }
                    return cov;
                }
                case 9u: {
                    return rounded_box_shadow(inst.p.xy, inst.p.zw, lp, inst.q.y, inst.q.x);
                }
                case 10u: {
                    return mask_texel(p, vec2<i32>(inst.p.xy), vec2<i32>(inst.p.zw), vec2<i32>(inst.q.xy));
                }
                default: {
                    return 0.0;
                }
            }
        }

        // Premultiplied colour of a gradient at parameter t.
        fn gradient_color(g: GradientEntry, t_in: f32) -> vec4<f32> {
            let t = clamp(t_in, 0.0, 1.0);
            var prev = stops[g.stop_start];
            if (g.stop_count == 1u || t <= prev.offset) {
                return prev.color;
            }
            for (var i = 1u; i < g.stop_count; i = i + 1u) {
                let cur = stops[g.stop_start + i];
                if (t < cur.offset) {
                    let span = cur.offset - prev.offset;
                    let f = select(1.0, (t - prev.offset) / span, span > 0.0);
                    return mix(prev.color, cur.color, f);
                }
                prev = cur;
            }
            return prev.color;
        }

        fn paint_color(inst: ShapeInstance, p: vec2<f32>) -> vec4<f32> {
            if (inst.paint_index == 0u) {
                return inst.color;
            }
            let g = gradients[inst.paint_index];
            let gp = vec2<f32>(g.m.x * p.x + g.m.y * p.y + g.t.x, g.m.z * p.x + g.m.w * p.y + g.t.y);
            var t = gp.x;
            if (g.kind == 2u) {
                t = length(gp);
            } else if (g.kind == 3u) {
                t = fract((atan2(gp.y, gp.x) - g.t.z) / 6.28318530718);
            }
            let c = gradient_color(g, t);
            var rgb = vec3<f32>(0.0);
            if (c.a > 0.0) {
                rgb = c.rgb / c.a;
            }
            return vec4<f32>(rgb, c.a * inst.color.a);
        }

        @fragment
        fn fs(in: VertexOutput) -> @location(0) vec4<f32> {
            let p = in.position.xy;
            if (dissolved(p)) {
                discard;
            }
            let inst = instances[in.instance_idx];
            let cov = shape_coverage(inst, p) * clip_coverage(p, inst.clip_index);
            if (cov <= 0.0) {
                discard;
            }
            let color = paint_color(inst, p);
            return vec4<f32>(color.rgb, color.a * cov);
        }
        """;

    public const string ImageWgsl = Surface + """

        struct ImageInstance {
            quad: vec4<f32>,
            u_row: vec4<f32>,
            v_row: vec4<f32>,
            opacity: f32,
            clip_index: u32,
            handle: i32,
            edge_aa: f32,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var<storage, read> images: array<ImageInstance>;
        @group(0) @binding(2) var<storage, read> clips: array<ClipEntry>;
        @group(0) @binding(3) var mask_tex: texture_2d<f32>;
        @group(1) @binding(0) var image_tex: texture_2d<f32>;
        @group(1) @binding(1) var image_sampler: sampler;

        """ + Common + """

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) @interpolate(flat) instance_idx: u32,
        };

        @vertex
        fn vs(@builtin(vertex_index) vertex_idx: u32, @builtin(instance_index) instance_idx: u32) -> VertexOutput {
            let inst = images[instance_idx];
            let pixel_pos = mix(inst.quad.xy, inst.quad.zw, QUAD[vertex_idx]);
            return VertexOutput(to_clip_space(pixel_pos), instance_idx);
        }

        @fragment
        fn fs(in: VertexOutput) -> @location(0) vec4<f32> {
            let p = in.position.xy;
            let inst = images[in.instance_idx];
            let u = inst.u_row.x * p.x + inst.u_row.y * p.y + inst.u_row.z;
            let v = inst.v_row.x * p.x + inst.v_row.y * p.y + inst.v_row.z;
            var edge = 0.0;
            if (inst.edge_aa != 0.0) {
                // Device-space distance inside each pair of edges; half a pixel either side of an
                // edge antialiases it.
                let du = min(u, 1.0 - u) / length(inst.u_row.xy);
                let dv = min(v, 1.0 - v) / length(inst.v_row.xy);
                edge = clamp(du + 0.5, 0.0, 1.0) * clamp(dv + 0.5, 0.0, 1.0);
            } else if (u >= 0.0 && u < 1.0 && v >= 0.0 && v < 1.0) {
                edge = 1.0;
            }
            let cov = edge * clip_coverage(p, inst.clip_index) * inst.opacity;
            if (cov <= 0.0) {
                discard;
            }
            let texel = textureSampleLevel(image_tex, image_sampler, vec2<f32>(u, v), 0.0);
            return vec4<f32>(texel.rgb, texel.a * cov);
        }
        """;

    public const string FullFrameWgsl = """
        @group(0) @binding(0) var tex: texture_2d<f32>;
        @group(0) @binding(1) var samp: sampler;

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
        };

        @vertex
        fn vs(@location(0) pos: vec2<f32>, @location(1) uv: vec2<f32>) -> VertexOutput {
            return VertexOutput(vec4<f32>(pos, 0.0, 1.0), uv);
        }

        @fragment
        fn fs(in: VertexOutput) -> @location(0) vec4<f32> {
            return textureSampleLevel(tex, samp, in.uv, 0.0);
        }
        """;

    public const string GlyphWgsl = Surface + """

        struct GlyphInstance {
            pos: vec2<f32>,
            size: vec2<f32>,
            atlas_uv0: vec2<f32>,
            atlas_uv1: vec2<f32>,
            color: vec4<f32>,
            fg_lum: f32,
            clip_index: u32,
            pad0: f32,
            pad1: f32,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var atlas: texture_2d<f32>;
        @group(0) @binding(2) var atlas_sampler: sampler;
        @group(0) @binding(3) var bg_tex: texture_2d<f32>;
        @group(0) @binding(4) var<storage, read> clips: array<ClipEntry>;
        @group(0) @binding(5) var mask_tex: texture_2d<f32>;
        @group(1) @binding(0) var<storage, read> instances: array<GlyphInstance>;

        """ + Common + """

        fn lin_to_srgb(c: vec3<f32>) -> vec3<f32> {
            let lo = c * 12.92;
            let hi = 1.055 * pow(max(c, vec3<f32>(0.0)), vec3<f32>(1.0 / 2.4)) - 0.055;
            return select(lo, hi, c > vec3<f32>(0.0031308));
        }

        fn srgb_to_lin1(c: f32) -> f32 {
            return select(c / 12.92, pow((c + 0.055) / 1.055, 2.4), c > 0.04045);
        }

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) @interpolate(flat) instance_idx: u32,
        };

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let quad = QUAD[vi];
            let inst = instances[ii];
            return VsOut(to_clip_space(inst.pos + quad * inst.size), mix(inst.atlas_uv0, inst.atlas_uv1, quad), ii);
        }

        // The text-weight curve. Linear-correct coverage blending renders black-on-white text too
        // light, so `text_gamma` > 0 pre-distorts coverage: dark-on-light gets the displayed weight
        // 1 − (1 − c)^γ; light-on-dark (which blooms) is scaled toward linear coverage as its
        // contrast grows. Both luminances are of sRGB-encoded colour.
        fn weighted_coverage(cov: f32, fg_lum: f32, bg_lum: f32) -> f32 {
            if (surface.text_gamma <= 0.0) {
                return cov;
            }
            let pc = 1.0 - pow(max(1.0 - cov, 0.0), surface.text_gamma);
            let rel = clamp(fg_lum - bg_lum, 0.0, 1.0);
            let w_light = mix(cov, pc, 1.0 - surface.light_weight * rel);
            // Map the target displayed weight to the linear-light blend alpha per polarity.
            let a_dark = 1.0 - srgb_to_lin1(1.0 - pc);
            let a_light = srgb_to_lin1(w_light);
            let polarity = smoothstep(-0.1, 0.1, fg_lum - bg_lum);
            return mix(a_dark, a_light, polarity);
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            let p = in.position.xy;
            if (dissolved(p)) {
                discard;
            }
            let inst = instances[in.instance_idx];
            let clip = clip_coverage(p, inst.clip_index);
            if (clip <= 0.0) {
                discard;
            }
            let cov = textureSampleLevel(atlas, atlas_sampler, in.uv, 0.0).r;
            let bg = lin_to_srgb(textureLoad(bg_tex, vec2<i32>(i32(p.x), i32(p.y)), 0).rgb);
            let bg_lum = dot(bg, vec3<f32>(0.2126, 0.7152, 0.0722));
            let alpha = inst.color.a * weighted_coverage(cov, inst.fg_lum, bg_lum) * clip;
            return vec4<f32>(inst.color.rgb * alpha, alpha);
        }
        """;

    public const string ColorGlyphWgsl = Surface + """

        struct GlyphInstance {
            pos: vec2<f32>,
            size: vec2<f32>,
            atlas_uv0: vec2<f32>,
            atlas_uv1: vec2<f32>,
            color: vec4<f32>,
            fg_lum: f32,
            clip_index: u32,
            pad0: f32,
            pad1: f32,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var atlas: texture_2d<f32>;
        @group(0) @binding(2) var atlas_sampler: sampler;
        @group(0) @binding(3) var bg_tex: texture_2d<f32>;
        @group(0) @binding(4) var<storage, read> clips: array<ClipEntry>;
        @group(0) @binding(5) var mask_tex: texture_2d<f32>;
        @group(1) @binding(0) var<storage, read> instances: array<GlyphInstance>;

        """ + Common + """

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) @interpolate(flat) instance_idx: u32,
        };

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let quad = QUAD[vi];
            let inst = instances[ii];
            return VsOut(to_clip_space(inst.pos + quad * inst.size), mix(inst.atlas_uv0, inst.atlas_uv1, quad), ii);
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            let p = in.position.xy;
            if (dissolved(p)) {
                discard;
            }
            let inst = instances[in.instance_idx];
            let clip = clip_coverage(p, inst.clip_index);
            if (clip <= 0.0) {
                discard;
            }
            // The atlas holds straight RGBA; the colour glyph keeps its own colours and takes only
            // the text's opacity.
            let texel = textureSampleLevel(atlas, atlas_sampler, in.uv, 0.0);
            let alpha = texel.a * inst.color.a * clip;
            return vec4<f32>(texel.rgb * alpha, alpha);
        }
        """;

    // Frosted-glass backdrop blur: for each rounded-rect panel, Gaussian-blur the framebuffer copy
    // (bg_tex) behind it and tint the result. bg_tex is sRGB so sampling returns linear; the blur is
    // in linear light, and the output is premultiplied.
    public const string BlurWgsl = Surface + """

        struct BlurInstance {
            bounds_min: vec2<f32>,
            bounds_max: vec2<f32>,
            tint: vec4<f32>,
            radius: f32,
            sigma: f32,
            clip_index: u32,
            opacity: f32,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var bg_tex: texture_2d<f32>;
        @group(0) @binding(2) var bg_sampler: sampler;
        @group(0) @binding(3) var<storage, read> clips: array<ClipEntry>;
        @group(0) @binding(4) var mask_tex: texture_2d<f32>;
        @group(1) @binding(0) var<storage, read> instances: array<BlurInstance>;

        """ + Common + """

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) @interpolate(flat) idx: u32,
        };

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let inst = instances[ii];
            return VsOut(to_clip_space(mix(inst.bounds_min, inst.bounds_max, QUAD[vi])), ii);
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            let inst = instances[in.idx];
            let pos = in.position.xy;

            let dist = sdf_round_rect(pos, inst.bounds_min, inst.bounds_max, inst.radius);
            if (dist > 1.0) {
                discard;
            }
            let coverage = (1.0 - smoothstep(-1.0, 1.0, dist)) * clip_coverage(pos, inst.clip_index) * inst.opacity;
            if (coverage <= 0.0) {
                discard;
            }

            // 7x7 Gaussian taps of the framebuffer copy behind the panel.
            let sigma = max(inst.sigma, 0.5);
            let texel = vec2<f32>(1.0 / surface.width, 1.0 / surface.height);
            let step = sigma * 0.5;
            var acc = vec3<f32>(0.0);
            var wsum = 0.0;
            for (var j: i32 = -3; j <= 3; j = j + 1) {
                for (var i: i32 = -3; i <= 3; i = i + 1) {
                    let off = vec2<f32>(f32(i), f32(j)) * step;
                    let w = exp(-(off.x * off.x + off.y * off.y) / (2.0 * sigma * sigma));
                    acc = acc + textureSampleLevel(bg_tex, bg_sampler, (pos + off) * texel, 0.0).rgb * w;
                    wsum = wsum + w;
                }
            }
            let blurred = acc / max(wsum, 0.0001);
            let outc = mix(blurred, inst.tint.rgb, inst.tint.a);
            return vec4<f32>(outc * coverage, coverage);
        }
        """;
}
