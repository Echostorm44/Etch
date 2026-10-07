namespace Etch.Compose;

/// <summary>
/// The WGSL the GPU composer runs. These shaders define the per-pixel semantics of every draw kind;
/// the CPU composer's kernels are ports of exactly this arithmetic, so a change here must be mirrored
/// there (the GPU/CPU parity tests fail otherwise).
/// </summary>
internal static class ComposeShaders
{
    public const string GeometryWgsl = """
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

        struct ShapeInstance {
            bounds_min: vec2<f32>,
            bounds_max: vec2<f32>,
            color0: vec4<f32>,
            color1: vec4<f32>,
            p0: vec2<f32>,
            p1: vec2<f32>,
            shape_type: u32,
            _pad0: u32,
            center: vec2<f32>,
            radius: f32,
            stroke_width: f32,
            expand: f32,
            _pad2: f32,
        };

        @group(0) @binding(0)
        var<uniform> surface: SurfaceSize;

        @group(0) @binding(1)
        var<storage, read> instances: array<ShapeInstance>;

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) @interpolate(flat) instance_idx: u32,
        };

        const QUAD_VERTICES = array<vec2<f32>, 4>(
            vec2<f32>(0.0, 0.0),
            vec2<f32>(1.0, 0.0),
            vec2<f32>(0.0, 1.0),
            vec2<f32>(1.0, 1.0)
        );

        @vertex
        fn vs(@builtin(vertex_index) vertex_idx: u32, @builtin(instance_index) instance_idx: u32) -> VertexOutput {
            var quad = QUAD_VERTICES[vertex_idx];
            var inst = instances[instance_idx];
            var e = vec2<f32>(inst.expand, inst.expand);
            var pixel_pos = mix(inst.bounds_min - e, inst.bounds_max + e, quad) + vec2<f32>(surface.offset_x, surface.offset_y);
            var clip = vec4<f32>(
                pixel_pos.x / surface.width * 2.0 - 1.0,
                1.0 - pixel_pos.y / surface.height * 2.0,
                0.0, 1.0
            );
            return VertexOutput(clip, instance_idx);
        }

        // Compute adaptive antialias width from screen-space derivative of signed distance.
        // This adapts fade width to the local slope: shallow angles get wider fades,
        // steep angles get sharper fades — eliminating jaggies without blurring everything.
        fn adaptive_aa(dist: f32) -> f32 {
            // Use the Euclidean gradient magnitude, not fwidth (= |dFdx| + |dFdy|,
            // the Manhattan sum). For a true distance field |grad| = 1 everywhere,
            // but the Manhattan sum overestimates by up to sqrt(2) where the
            // gradient runs diagonally — i.e. at rounded-rect corners — widening
            // the antialiased band there and making stroked corners look chunkier
            // than the straight edges. The Euclidean length keeps the band uniform.
            let g = vec2<f32>(dpdx(dist), dpdy(dist));
            let fw = length(g);
            // Clamp to avoid excessive blur at glancing angles and ensure a minimum fade
            return clamp(fw, 0.35, 1.5);
        }

        // Drop shadow: coverage of a rounded rect blurred by a Gaussian (σ). Mirrors Etch's
        // ShadowShape.Coverage (the CPU fallback) — exact across x via erf, four Gaussian-weighted
        // samples over y (Evan Wallace, "Fast Rounded Rectangle Shadows").
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

        @fragment
        fn fs(in: VertexOutput) -> @location(0) vec4<f32> {
            if (surface.dissolve > 0.0) {
                let dn = fract(sin(dot(floor(in.position.xy), vec2<f32>(12.9898, 78.233))) * 43758.5453);
                if (dn < surface.dissolve) { discard; }
            }
            var inst = instances[in.instance_idx];
            if (inst.shape_type == 1u) {
                // Circle fill with adaptive antialiasing
                let dx = in.position.x - inst.center.x;
                let dy = in.position.y - inst.center.y;
                let dist = sqrt(dx * dx + dy * dy) - inst.radius;
                let aa = adaptive_aa(dist);
                if (dist > aa) {
                    discard;
                }
                let coverage = 1.0 - smoothstep(0.0, aa, dist);
                return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
            }
            if (inst.shape_type == 2u) {
                let pos = in.position.xy;
                let v = inst.p1 - inst.p0;
                let len_sq = dot(v, v);
                let t = select(0.0, dot(pos - inst.p0, v) / len_sq, len_sq > 0.0);
                let clamped_t = clamp(t, 0.0, 1.0);
                return mix(inst.color0, inst.color1, clamped_t);
            }
            if (inst.shape_type == 3u) {
                // Ring (stroked circle) with adaptive antialiasing
                let dx = in.position.x - inst.center.x;
                let dy = in.position.y - inst.center.y;
                let dist = sqrt(dx * dx + dy * dy);
                let outer_dist = dist - inst.radius;
                let inner_dist = (inst.radius - inst.stroke_width) - dist;
                let outer_aa = adaptive_aa(outer_dist);
                let inner_aa = adaptive_aa(inner_dist);
                if (outer_dist > outer_aa || inner_dist > inner_aa) {
                    discard;
                }
                let outer_coverage = 1.0 - smoothstep(0.0, outer_aa, outer_dist);
                let inner_coverage = 1.0 - smoothstep(0.0, inner_aa, inner_dist);
                let coverage = min(outer_coverage, inner_coverage);
                return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
            }
            if (inst.shape_type == 4u) {
                // Line segment stroke with adaptive antialiasing
                let p = in.position.xy;
                let a = inst.p0;
                let b = inst.p1;
                let pa = p - a;
                let ba = b - a;
                let h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                let dist = length(pa - ba * h);
                let half_sw = inst.stroke_width * 0.5;
                let aa = adaptive_aa(dist - half_sw);
                if (dist > half_sw + aa) {
                    discard;
                }
                let coverage = 1.0 - smoothstep(half_sw, half_sw + aa, dist);
                return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
            }
            if (inst.shape_type == 5u || inst.shape_type == 6u || inst.shape_type == 7u) {
                // Rounded rect (5=fill, 6=stroke, 7=gradient) with adaptive antialiasing
                let center = (inst.bounds_min + inst.bounds_max) * 0.5;
                let half_size = (inst.bounds_max - inst.bounds_min) * 0.5;
                let d = abs(in.position.xy - center) - half_size + vec2<f32>(inst.radius, inst.radius);
                let dist = length(max(d, vec2<f32>(0.0))) + min(max(d.x, d.y), 0.0) - inst.radius;
                let aa = adaptive_aa(dist);
                if (inst.shape_type == 5u || inst.shape_type == 7u) {
                    // Fill
                    if (dist > aa) {
                        discard;
                    }
                    let coverage = 1.0 - smoothstep(0.0, aa, dist);
                    if (inst.shape_type == 7u) {
                        let pos = in.position.xy;
                        let v = inst.p1 - inst.p0;
                        let len_sq = dot(v, v);
                        let t = select(0.0, dot(pos - inst.p0, v) / len_sq, len_sq > 0.0);
                        let clamped_t = clamp(t, 0.0, 1.0);
                        let grad = mix(inst.color0, inst.color1, clamped_t);
                        return vec4<f32>(grad.rgb, grad.a * coverage);
                    }
                    return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
                } else {
                    // Stroke
                    let abs_dist = abs(dist);
                    let half_sw = inst.stroke_width * 0.5;
                    let stroke_aa = adaptive_aa(abs_dist - half_sw);
                    if (abs_dist > half_sw + stroke_aa) {
                        discard;
                    }
                    let coverage = 1.0 - smoothstep(half_sw, half_sw + stroke_aa, abs_dist);
                    return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
                }
            }
            if (inst.shape_type == 9u) {
                // Drop shadow: p0..p1 = shadow rect, radius = corner, stroke_width = σ. The quad
                // (bounds) is the rect grown by 3σ and already clipped.
                let coverage = rounded_box_shadow(inst.p0, inst.p1, in.position.xy, inst.stroke_width, inst.radius);
                if (coverage <= 0.0) {
                    discard;
                }
                return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
            }
            if (inst.shape_type == 8u) {
                // Annular sector (pie/donut slice) with radial and angular antialiasing
                let dx = in.position.x - inst.center.x;
                let dy = in.position.y - inst.center.y;
                let dist = sqrt(dx * dx + dy * dy);
                let outer_r = inst.radius;
                let inner_r = inst.stroke_width;
                let outer_dist = dist - outer_r;
                let inner_dist = inner_r - dist;
                let outer_aa = adaptive_aa(outer_dist);
                let inner_aa = adaptive_aa(inner_dist);
                if (outer_dist > outer_aa || inner_dist > inner_aa) {
                    discard;
                }
                let outer_coverage = 1.0 - smoothstep(0.0, outer_aa, outer_dist);
                let inner_coverage = 1.0 - smoothstep(0.0, inner_aa, inner_dist);
                var coverage = min(outer_coverage, inner_coverage);
                // Angular test: p0.x = startAngle, p0.y = sweepAngle (both in radians)
                let angle = atan2(dy, dx);
                let start_angle = inst.p0.x;
                let sweep_angle = inst.p0.y;
                let end_angle = start_angle + sweep_angle;
                // Normalize the test angle into [start_angle, start_angle + 2π] range
                var test_angle = angle;
                if (sweep_angle > 0.0) {
                    while (test_angle < start_angle) {
                        test_angle = test_angle + 6.28318530718;
                    }
                } else {
                    while (test_angle > start_angle) {
                        test_angle = test_angle - 6.28318530718;
                    }
                }
                // Determine if inside the angular sweep and compute signed distance
                // to the nearest angular edge (positive = outside, negative = inside)
                var signed_angular_dist = 0.0;
                var in_sweep = false;
                if (sweep_angle > 0.0) {
                    in_sweep = test_angle >= start_angle && test_angle <= end_angle;
                } else {
                    in_sweep = test_angle <= start_angle && test_angle >= end_angle;
                }
                if (in_sweep) {
                    let dist_to_start = abs(test_angle - start_angle);
                    let dist_to_end = abs(test_angle - end_angle);
                    signed_angular_dist = -min(dist_to_start, dist_to_end);
                } else {
                    if (sweep_angle > 0.0) {
                        signed_angular_dist = max(start_angle - test_angle, test_angle - end_angle);
                    } else {
                        signed_angular_dist = max(test_angle - start_angle, end_angle - test_angle);
                    }
                }
                let signed_angular_dist_px = signed_angular_dist * dist;
                let angular_aa = adaptive_aa(signed_angular_dist_px);
                let angular_coverage = 1.0 - smoothstep(0.0, angular_aa, signed_angular_dist_px);
                if (angular_coverage <= 0.0) {
                    discard;
                }
                coverage = min(coverage, angular_coverage);
                return vec4<f32>(inst.color0.rgb, inst.color0.a * coverage);
            }
            return inst.color0;
        }
        """;

    public const string TextWgsl = """
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
            return textureSample(tex, samp, in.uv);
        }
        """;

    public const string GlyphAtlasWgsl = """
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

        struct GlyphInstance {
            pos: vec2<f32>,
            size: vec2<f32>,
            atlas_uv0: vec2<f32>,
            atlas_uv1: vec2<f32>,
            color: vec4<f32>,
            clip_min: vec2<f32>,
            clip_max: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var atlas: texture_2d<f32>;
        @group(0) @binding(2) var atlas_sampler: sampler;
        @group(0) @binding(3) var bg_tex: texture_2d<f32>;
        @group(1) @binding(0) var<storage, read> instances: array<GlyphInstance>;

        fn lin_to_srgb(c: vec3<f32>) -> vec3<f32> {
            let lo = c * 12.92;
            let hi = 1.055 * pow(max(c, vec3<f32>(0.0)), vec3<f32>(1.0 / 2.4)) - 0.055;
            return select(lo, hi, c > vec3<f32>(0.0031308));
        }

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) @interpolate(flat) color: vec4<f32>,
            @location(2) @interpolate(flat) clip_min: vec2<f32>,
            @location(3) @interpolate(flat) clip_max: vec2<f32>,
        };

        const QUAD = array<vec2<f32>, 4>(
            vec2<f32>(0.0, 0.0),
            vec2<f32>(1.0, 0.0),
            vec2<f32>(0.0, 1.0),
            vec2<f32>(1.0, 1.0)
        );

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let quad = QUAD[vi];
            let inst = instances[ii];
            let screen_pos = inst.pos + quad * inst.size;
            let ndc = vec2<f32>(
                screen_pos.x / surface.width * 2.0 - 1.0,
                1.0 - screen_pos.y / surface.height * 2.0
            );
            let uv = mix(inst.atlas_uv0, inst.atlas_uv1, quad);
            return VsOut(
                vec4<f32>(ndc, 0.0, 1.0),
                uv,
                inst.color,
                inst.clip_min,
                inst.clip_max
            );
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            if (surface.dissolve > 0.0) {
                let dn = fract(sin(dot(floor(in.position.xy), vec2<f32>(12.9898, 78.233))) * 43758.5453);
                if (dn < surface.dissolve) { discard; }
            }
            if (in.clip_max.x > in.clip_min.x || in.clip_max.y > in.clip_min.y) {
                let screen_pos = in.position.xy;
                if (screen_pos.x < in.clip_min.x || screen_pos.x >= in.clip_max.x ||
                    screen_pos.y < in.clip_min.y || screen_pos.y >= in.clip_max.y) {
                    discard;
                }
            }
            // The swapchain is sRGB, so the hardware blends in linear light and
            // coverage IS the linear-correct blend factor (text_gamma == 0).
            // But linear-correct blending renders black-on-white text too LIGHT
            // (coverage 0.5 -> sRGB ~0.74), which reads washed-out at small
            // sizes. text_gamma > 0 selects a perceptual weight: we pre-distort
            // the coverage so the *displayed* black-on-white luminance becomes
            // (1-coverage)^text_gamma (1.0 ≈ sRGB weight, 1.5 ≈ macOS smoothing).
            // Unlike a contrast preblend this is monotonic, so no partial pixel
            // is pushed to fully on/off — it adds weight without adding aliasing.
            let cov = textureSample(atlas, atlas_sampler, in.uv).r;
            var weighted = cov;
            if (surface.text_gamma > 0.0) {
                // Perceptual ink fraction: darken the antialiased coverage so text
                // reads with correct weight (WP-3519).
                let pc = 1.0 - pow(max(1.0 - cov, 0.0), surface.text_gamma);

                // WP-3537: adapt the weight to the ACTUAL contrast between the glyph
                // and the local background (read from the framebuffer copy), so text
                // is correctly weighted on any background with no tuned constant. The
                // luminance comparison is in sRGB; bg_tex is sRGB-format so textureLoad
                // returns linear — convert it back.
                let bg_srgb = lin_to_srgb(textureLoad(bg_tex, vec2<i32>(i32(in.position.x), i32(in.position.y)), 0).rgb);
                let fg_lum = dot(in.color.rgb, vec3<f32>(0.2126, 0.7152, 0.0722));
                let bg_lum = dot(bg_srgb, vec3<f32>(0.2126, 0.7152, 0.0722));

                // Dark-on-light (fg darker than bg) keeps the full perceptual weight
                // (light-theme output unchanged). Light-on-dark blooms, so scale its
                // displayed weight toward the un-weighted linear coverage as contrast
                // grows. surface.light_weight is the adaptive strength (1 = full
                // adaptive, 0 = legacy symmetric weight).
                let rel = clamp(fg_lum - bg_lum, 0.0, 1.0);
                let w_light = mix(cov, pc, 1.0 - surface.light_weight * rel);

                // Map the target displayed weight to the HW linear-blend alpha per
                // polarity, then blend by the true (bg-aware) polarity.
                let dDark = 1.0 - pc;
                let aDark = 1.0 - select(dDark / 12.92, pow((dDark + 0.055) / 1.055, 2.4), dDark > 0.04045);
                let aLight = select(w_light / 12.92, pow((w_light + 0.055) / 1.055, 2.4), w_light > 0.04045);
                let polarity = smoothstep(-0.1, 0.1, fg_lum - bg_lum);
                weighted = mix(aDark, aLight, polarity);
            }
            let finalAlpha = in.color.a * weighted;
            return vec4<f32>(in.color.rgb * finalAlpha, finalAlpha);
        }
        """;

    public const string ColorGlyphAtlasWgsl = """
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

        struct GlyphInstance {
            pos: vec2<f32>,
            size: vec2<f32>,
            atlas_uv0: vec2<f32>,
            atlas_uv1: vec2<f32>,
            color: vec4<f32>,
            clip_min: vec2<f32>,
            clip_max: vec2<f32>,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var atlas: texture_2d<f32>;
        @group(0) @binding(2) var atlas_sampler: sampler;
        @group(1) @binding(0) var<storage, read> instances: array<GlyphInstance>;

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
            @location(1) @interpolate(flat) clip_min: vec2<f32>,
            @location(2) @interpolate(flat) clip_max: vec2<f32>,
        };

        const QUAD = array<vec2<f32>, 4>(
            vec2<f32>(0.0, 0.0),
            vec2<f32>(1.0, 0.0),
            vec2<f32>(0.0, 1.0),
            vec2<f32>(1.0, 1.0)
        );

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let quad = QUAD[vi];
            let inst = instances[ii];
            let screen_pos = inst.pos + quad * inst.size;
            let ndc = vec2<f32>(
                screen_pos.x / surface.width * 2.0 - 1.0,
                1.0 - screen_pos.y / surface.height * 2.0
            );
            let uv = mix(inst.atlas_uv0, inst.atlas_uv1, quad);
            return VsOut(
                vec4<f32>(ndc, 0.0, 1.0),
                uv,
                inst.clip_min,
                inst.clip_max
            );
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            if (surface.dissolve > 0.0) {
                let dn = fract(sin(dot(floor(in.position.xy), vec2<f32>(12.9898, 78.233))) * 43758.5453);
                if (dn < surface.dissolve) { discard; }
            }
            if (in.clip_max.x > in.clip_min.x || in.clip_max.y > in.clip_min.y) {
                let screen_pos = in.position.xy;
                if (screen_pos.x < in.clip_min.x || screen_pos.x >= in.clip_max.x ||
                    screen_pos.y < in.clip_min.y || screen_pos.y >= in.clip_max.y) {
                    discard;
                }
            }
            let texColor = textureSample(atlas, atlas_sampler, in.uv);
            // Atlas stores straight RGBA; pipeline blend is premultiplied (One, OneMinusSrcAlpha)
            return vec4<f32>(texColor.rgb * texColor.a, texColor.a);
        }
        """;

    // Frosted-glass backdrop blur: for each rounded-rect panel, Gaussian-blur the
    // framebuffer copy (bg_tex) behind it and tint the result. bg_tex is sRGB so
    // sampling returns linear; we blur in linear (correct) and output linear (the
    // sRGB target re-encodes on write, reproducing the backdrop). Premultiplied.
    public const string BackdropBlurWgsl = """
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
        struct BlurInstance {
            bounds_min: vec2<f32>,
            bounds_max: vec2<f32>,
            tint: vec4<f32>,
            radius: f32,
            sigma: f32,
            pad0: f32,
            pad1: f32,
        };

        @group(0) @binding(0) var<uniform> surface: SurfaceSize;
        @group(0) @binding(1) var bg_tex: texture_2d<f32>;
        @group(0) @binding(2) var bg_sampler: sampler;
        @group(1) @binding(0) var<storage, read> instances: array<BlurInstance>;

        var<private> QUAD: array<vec2<f32>, 4> = array<vec2<f32>, 4>(
            vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 0.0), vec2<f32>(0.0, 1.0), vec2<f32>(1.0, 1.0)
        );

        struct VsOut {
            @builtin(position) position: vec4<f32>,
            @location(0) @interpolate(flat) idx: u32,
        };

        fn srgb_to_lin(c: vec3<f32>) -> vec3<f32> {
            let lo = c / 12.92;
            let hi = pow((c + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4));
            return select(lo, hi, c > vec3<f32>(0.04045));
        }

        @vertex
        fn vs(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VsOut {
            let inst = instances[ii];
            let p = mix(inst.bounds_min, inst.bounds_max, QUAD[vi]);
            let ndc = vec2<f32>(p.x / surface.width * 2.0 - 1.0, 1.0 - p.y / surface.height * 2.0);
            return VsOut(vec4<f32>(ndc, 0.0, 1.0), ii);
        }

        @fragment
        fn fs(in: VsOut) -> @location(0) vec4<f32> {
            let inst = instances[in.idx];
            let pos = in.position.xy;

            // Rounded-rect coverage (SDF), like shape_type 5 in the geometry shader.
            let center = (inst.bounds_min + inst.bounds_max) * 0.5;
            let half_size = (inst.bounds_max - inst.bounds_min) * 0.5;
            let q = abs(pos - center) - half_size + vec2<f32>(inst.radius, inst.radius);
            let dist = length(max(q, vec2<f32>(0.0))) + min(max(q.x, q.y), 0.0) - inst.radius;
            if (dist > 1.0) {
                discard;
            }
            let coverage = 1.0 - smoothstep(-1.0, 1.0, dist);

            // 7x7 Gaussian tap of the framebuffer copy behind the panel.
            let sigma = max(inst.sigma, 0.5);
            let texel = vec2<f32>(1.0 / surface.width, 1.0 / surface.height);
            let step = sigma * 0.5;
            var acc = vec3<f32>(0.0);
            var wsum = 0.0;
            for (var j: i32 = -3; j <= 3; j = j + 1) {
                for (var i: i32 = -3; i <= 3; i = i + 1) {
                    let off = vec2<f32>(f32(i), f32(j)) * step;
                    let w = exp(-(off.x * off.x + off.y * off.y) / (2.0 * sigma * sigma));
                    let uv = (pos + off) * texel;
                    acc = acc + textureSampleLevel(bg_tex, bg_sampler, uv, 0.0).rgb * w;
                    wsum = wsum + w;
                }
            }
            let blurred = acc / max(wsum, 0.0001);

            // Tint over the blur (convert the sRGB tint to linear to match).
            let outc = mix(blurred, srgb_to_lin(inst.tint.rgb), inst.tint.a);
            return vec4<f32>(outc * coverage, coverage);
        }
        """;
}
