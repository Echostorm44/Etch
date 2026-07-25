using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Geometry;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class SceneCommandLayoutTests
{
    [Test]
    public void SceneCommand_Is40Bytes()
    {
        if (Unsafe.SizeOf<SceneCommand>() != 40)
            throw new InvalidOperationException($"sizeof(SceneCommand) = {Unsafe.SizeOf<SceneCommand>()}, expected 40");
    }

    [Test]
    public void FillPathPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<FillPathPayload>() != 32)
            throw new InvalidOperationException($"sizeof(FillPathPayload) = {Unsafe.SizeOf<FillPathPayload>()}, expected 32");
    }

    [Test]
    public void StrokePathPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<StrokePathPayload>() != 32)
            throw new InvalidOperationException($"sizeof(StrokePathPayload) = {Unsafe.SizeOf<StrokePathPayload>()}, expected 32");
    }

    [Test]
    public void FillRectPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<FillRectPayload>() != 32)
            throw new InvalidOperationException($"sizeof(FillRectPayload) = {Unsafe.SizeOf<FillRectPayload>()}, expected 32");
    }

    [Test]
    public void SetTransformPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<SetTransformPayload>() != 32)
            throw new InvalidOperationException($"sizeof(SetTransformPayload) = {Unsafe.SizeOf<SetTransformPayload>()}, expected 32");
    }

    [Test]
    public void PushClipPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<PushClipPayload>() != 32)
            throw new InvalidOperationException($"sizeof(PushClipPayload) = {Unsafe.SizeOf<PushClipPayload>()}, expected 32");
    }

    [Test]
    public void PopClipPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<PopClipPayload>() != 32)
            throw new InvalidOperationException($"sizeof(PopClipPayload) = {Unsafe.SizeOf<PopClipPayload>()}, expected 32");
    }

    [Test]
    public void DrawImagePayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<DrawImagePayload>() != 32)
            throw new InvalidOperationException($"sizeof(DrawImagePayload) = {Unsafe.SizeOf<DrawImagePayload>()}, expected 32");
    }

    [Test]
    public void DrawGlyphRunPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<DrawGlyphRunPayload>() != 32)
            throw new InvalidOperationException($"sizeof(DrawGlyphRunPayload) = {Unsafe.SizeOf<DrawGlyphRunPayload>()}, expected 32");
    }

    [Test]
    public void SetBlendModePayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<SetBlendModePayload>() != 32)
            throw new InvalidOperationException($"sizeof(SetBlendModePayload) = {Unsafe.SizeOf<SetBlendModePayload>()}, expected 32");
    }

    [Test]
    public void PushLayerPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<PushLayerPayload>() != 32)
            throw new InvalidOperationException($"sizeof(PushLayerPayload) = {Unsafe.SizeOf<PushLayerPayload>()}, expected 32");
    }

    [Test]
    public void PopLayerPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<PopLayerPayload>() != 32)
            throw new InvalidOperationException($"sizeof(PopLayerPayload) = {Unsafe.SizeOf<PopLayerPayload>()}, expected 32");
    }

    [Test]
    public void BeginFramePayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<BeginFramePayload>() != 32)
            throw new InvalidOperationException($"sizeof(BeginFramePayload) = {Unsafe.SizeOf<BeginFramePayload>()}, expected 32");
    }

    [Test]
    public void EndFramePayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<EndFramePayload>() != 32)
            throw new InvalidOperationException($"sizeof(EndFramePayload) = {Unsafe.SizeOf<EndFramePayload>()}, expected 32");
    }

    [Test]
    public void NoopPayload_Is32Bytes()
    {
        if (Unsafe.SizeOf<NoopPayload>() != 32)
            throw new InvalidOperationException($"sizeof(NoopPayload) = {Unsafe.SizeOf<NoopPayload>()}, expected 32");
    }

    [Test]
    public void OpcodeValues_AreExplicit()
    {
        if ((byte)SceneOpcode.Noop != 0) throw new InvalidOperationException("Noop must be 0");
        if ((byte)SceneOpcode.PushLayer != 1) throw new InvalidOperationException("PushLayer must be 1");
        if ((byte)SceneOpcode.PopLayer != 2) throw new InvalidOperationException("PopLayer must be 2");
        if ((byte)SceneOpcode.PushClip != 3) throw new InvalidOperationException("PushClip must be 3");
        if ((byte)SceneOpcode.PopClip != 4) throw new InvalidOperationException("PopClip must be 4");
        if ((byte)SceneOpcode.SetTransform != 5) throw new InvalidOperationException("SetTransform must be 5");
        if ((byte)SceneOpcode.FillPath != 6) throw new InvalidOperationException("FillPath must be 6");
        if ((byte)SceneOpcode.StrokePath != 7) throw new InvalidOperationException("StrokePath must be 7");
        if ((byte)SceneOpcode.FillRect != 8) throw new InvalidOperationException("FillRect must be 8");
        if ((byte)SceneOpcode.DrawImage != 9) throw new InvalidOperationException("DrawImage must be 9");
        if ((byte)SceneOpcode.DrawGlyphRun != 10) throw new InvalidOperationException("DrawGlyphRun must be 10");
        if ((byte)SceneOpcode.SetBlendMode != 11) throw new InvalidOperationException("SetBlendMode must be 11");
        if ((byte)SceneOpcode.BeginFrame != 12) throw new InvalidOperationException("BeginFrame must be 12");
        if ((byte)SceneOpcode.EndFrame != 13) throw new InvalidOperationException("EndFrame must be 13");
    }

    [Test]
    public void SceneCommand_ByteEquality()
    {
        var a = SceneCommand.CreateNoop();
        var b = SceneCommand.CreateNoop();

        var aSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref a, 1));
        var bSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref b, 1));

        for (int i = 0; i < 40; i++)
        {
            if (aSpan[i] != bSpan[i])
                throw new InvalidOperationException($"Byte mismatch at {i}");
        }
    }

    [Test]
    public void SceneCommand_Noop_CreatesCorrectly()
    {
        var cmd = SceneCommand.CreateNoop();
        if (cmd.Op != SceneOpcode.Noop)
            throw new InvalidOperationException("Noop opcode mismatch");
    }
}
