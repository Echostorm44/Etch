namespace Etch.Compose;

/// <summary>
/// The GPU objects a <see cref="GpuComposer"/> holds and their bytes (see
/// <see cref="GpuComposer.ResourceUsage"/>).
/// </summary>
/// <param name="ShaderModules">Shader modules.</param>
/// <param name="RenderPipelines">Render pipelines.</param>
/// <param name="Buffers">Buffers.</param>
/// <param name="BufferBytes">Bytes of all buffers.</param>
/// <param name="Textures">Textures: glyph atlases, the mask pages, the framebuffer copy, the CPU-frame texture and images.</param>
/// <param name="TextureViews">Texture views (one per texture).</param>
/// <param name="TextureBytes">Bytes of all textures.</param>
/// <param name="BindGroups">Bind groups.</param>
public readonly record struct GpuResourceUsage(
    int ShaderModules,
    int RenderPipelines,
    int Buffers,
    long BufferBytes,
    int Textures,
    int TextureViews,
    long TextureBytes,
    int BindGroups);