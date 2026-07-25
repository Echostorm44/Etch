using System;
using System.Runtime.InteropServices;
using Etch.ClipBlendGradient;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;

namespace Etch.Gpu.Compositor.Pipelines;

public sealed unsafe class BlendModeDispatcher : IDisposable
{
    private readonly Device _device;
    private readonly BlendPipeline _blendPipeline;
    private readonly Buffer _perDrawBuffer;
    private readonly Buffer _vertexBuffer;
    private bool _disposed;

    private static readonly float[] UnitQuadVertices = new float[]
    {
        0.0f, 0.0f,
        1.0f, 0.0f,
        0.0f, 1.0f,
        1.0f, 1.0f
    };

    [StructLayout(LayoutKind.Explicit)]
    private struct PerDrawData
    {
        [FieldOffset(0)]
        public uint BlendMode;
        [FieldOffset(16)]
        public float Color0R;
        [FieldOffset(20)]
        public float Color0G;
        [FieldOffset(24)]
        public float Color0B;
        [FieldOffset(28)]
        public float Color0A;
        [FieldOffset(32)]
        public float Color1R;
        [FieldOffset(36)]
        public float Color1G;
        [FieldOffset(40)]
        public float Color1B;
        [FieldOffset(44)]
        public float Color1A;
    }

    public BlendModeDispatcher(Device device)
    {
        _device = device;
        _blendPipeline = new BlendPipeline(device);
        _perDrawBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerDrawData)
        });
        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 4ul,
            Size = (ulong)(sizeof(float) * UnitQuadVertices.Length)
        });

        var vertexData = new ReadOnlySpan<float>(UnitQuadVertices);
        var vertexBytes = MemoryMarshal.AsBytes(vertexData);
        device.Queue.WriteBuffer(_vertexBuffer, 0, vertexBytes);
    }

    public void Record(
        FrameContext frame,
        RenderPass pass,
        ReadOnlySpan<DrawGroup> drawGroups)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (drawGroups.Length == 0)
        {
            return;
        }

        pass.SetVertexBuffer(0, _vertexBuffer, 0, (ulong)(sizeof(float) * UnitQuadVertices.Length));

        foreach (ref readonly var group in drawGroups)
        {
            RecordGroup(pass, group);
        }
    }

    private void RecordGroup(RenderPass pass, in DrawGroup group)
    {
        SetBlendColors(group.BlendMode, group.SrcColor, group.DstColor);

        pass.SetPipeline(_blendPipeline.GetPipeline(group.BlendMode));
        pass.Draw(4, (uint)group.InstanceCount);
    }

    private void SetBlendColors(BlendMode mode, LinearColor src, LinearColor dst)
    {
        PerDrawData data = default;
        data.BlendMode = (byte)mode;
        data.Color0R = (float)src.R;
        data.Color0G = (float)src.G;
        data.Color0B = (float)src.B;
        data.Color0A = (float)src.A;
        data.Color1R = (float)dst.R;
        data.Color1G = (float)dst.G;
        data.Color1B = (float)dst.B;
        data.Color1A = (float)dst.A;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerDrawData));
        _device.Queue.WriteBuffer(_perDrawBuffer, 0, span);
    }

    public static int GetDistinctBlendModeCount(ReadOnlySpan<DrawGroup> drawGroups)
    {
        int count = 0;
        uint seenModes = 0;
        foreach (ref readonly var group in drawGroups)
        {
            uint mask = 1u << (byte)group.BlendMode;
            if ((seenModes & mask) == 0)
            {
                seenModes |= mask;
                count++;
            }
        }
        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _blendPipeline.Dispose();
        _perDrawBuffer.Dispose();
        _vertexBuffer.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct DrawGroup
{
    public readonly BlendMode BlendMode;
    public readonly LinearColor SrcColor;
    public readonly LinearColor DstColor;
    public readonly int InstanceCount;

    public DrawGroup(BlendMode blendMode, LinearColor srcColor, LinearColor dstColor, int instanceCount)
    {
        BlendMode = blendMode;
        SrcColor = srcColor;
        DstColor = dstColor;
        InstanceCount = instanceCount;
    }
}
