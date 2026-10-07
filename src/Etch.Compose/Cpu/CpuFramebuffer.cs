namespace Etch.Compose.Cpu;

/// <summary>
/// The CPU composer's render target: 8-bit sRGB-encoded BGRA, one <see cref="uint"/> per pixel
/// (<c>0xAARRGGBB</c>: B in the lowest byte, the layout Windows bitmaps and <c>Bgra8UnormSrgb</c>
/// textures use, so presenting needs no conversion). Like the GPU swapchain format it stands in for,
/// every draw is re-quantized to 8 bits per channel when written.
/// </summary>
public sealed class CpuFramebuffer
{
    private uint[] pixels = Array.Empty<uint>();

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>The pixels, row-major, stride <see cref="Width"/>.</summary>
    public Span<uint> Pixels => pixels.AsSpan(0, Width * Height);

    /// <summary>The backing array (length may exceed Width × Height after a shrink).</summary>
    public uint[] Buffer => pixels;

    /// <summary>
    /// Resizes the target. Contents are kept when the size is unchanged; otherwise the buffer is
    /// reallocated only when it must grow. Returns true when the size changed.
    /// </summary>
    public bool Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Panic.Invariant(PanicCodes.InvalidSurfaceSize, "Framebuffer dimensions must be positive");
        }
        if (width == Width && height == Height)
        {
            return false;
        }
        int needed = width * height;
        if (pixels.Length < needed)
        {
            pixels = new uint[needed];
        }
        Width = width;
        Height = height;
        return true;
    }

    /// <summary>Releases the pixel memory (the next <see cref="Resize"/> reallocates).</summary>
    public void Release()
    {
        pixels = Array.Empty<uint>();
        Width = 0;
        Height = 0;
    }

    /// <summary>Copies the frame out as tightly packed RGBA8 (screenshot order).</summary>
    public void CopyToRgba(Span<byte> rgba)
    {
        if (rgba.Length < Width * Height * 4)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, "RGBA destination smaller than the frame");
        }
        var src = Pixels;
        for (int i = 0; i < src.Length; i++)
        {
            uint p = src[i];
            rgba[i * 4] = (byte)(p >> 16);
            rgba[i * 4 + 1] = (byte)(p >> 8);
            rgba[i * 4 + 2] = (byte)p;
            rgba[i * 4 + 3] = (byte)(p >> 24);
        }
    }
}
