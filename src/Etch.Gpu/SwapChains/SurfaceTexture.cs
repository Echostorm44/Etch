using Etch.Gpu.Native;

namespace Etch.Gpu.SwapChains;

public readonly struct SurfaceTexture : IDisposable
{
    private readonly TextureHandle _texture;
    private readonly TextureViewHandle _view;

    internal SurfaceTexture(TextureHandle texture, TextureViewHandle view)
    {
        _texture = texture;
        _view = view;
    }

    public TextureHandle Texture => _texture;

    public TextureViewHandle View => _view;

    public bool IsValid => !_texture.IsInvalid;

    public void Dispose()
    {
        if (!_texture.IsInvalid)
        {
            WebGPU.TextureViewRelease(_view);
            WebGPU.TextureRelease(_texture);
        }
    }
}