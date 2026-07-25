struct PerDraw {
    transform: mat3x3<f32>,
    color: vec4<f32>,
    clip_index: u32,
    blend_mode: u32,
}