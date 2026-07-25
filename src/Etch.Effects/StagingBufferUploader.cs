using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Effects;

public static class StagingBufferUploader
{
    public static void UploadImage(this Queue queue, Image.ImageSource source, Texture texture, uint mipLevel = 0)
    {
        if (source == null)
        {
            Panic.ArgumentNull(nameof(source));
        }

        if (texture.IsInvalid)
        {
            Panic.Invariant(PanicCodes.InvalidState, "Texture is invalid.");
        }

        ReadOnlySpan<byte> pixelSpan = source.GetPixelSpan();

        uint bytesPerRow = (uint)(source.Width * 4);

        WGPUOrigin3D origin = default;
        origin.X = 0;
        origin.Y = 0;
        origin.Z = 0;

        Extent3D writeSize = default;
        writeSize.Width = (uint)source.Width;
        writeSize.Height = (uint)source.Height;
        writeSize.DepthOrArrayLayers = 1;

        queue.WriteTexture(texture, mipLevel, origin, pixelSpan, bytesPerRow, (uint)source.Height, writeSize);
    }
}
