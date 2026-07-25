namespace Etch.Shaders;

public static class ShaderSource
{
    private static IShaderSource? _default;

    public static IShaderSource Default
    {
        get
        {
            if (_default == null)
            {
                string? envValue = Environment.GetEnvironmentVariable("ETCH_HOT_RELOAD");
                if (envValue == "1")
                {
#if DEBUG && ETCH_HOT_RELOAD
                    _default = new HotReloadShaderSource();
#else
                    _default = new EmbeddedShaderSource();
#endif
                }
                else
                {
                    _default = new EmbeddedShaderSource();
                }
            }
            return _default;
        }
    }

#if DEBUG && ETCH_HOT_RELOAD
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void Reset()
    {
        if (_default is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _default = null;
    }
#endif
}