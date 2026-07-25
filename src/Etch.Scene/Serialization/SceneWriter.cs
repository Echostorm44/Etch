#pragma warning disable CA1062

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Scene.Serialization;

public static class SceneWriter
{
    private const byte Magic0 = 0x45;
    private const byte Magic1 = 0x54;
    private const byte Magic2 = 0x53;
    private const byte Magic3 = 0x43;
    private const ushort CurrentMajorVersion = 1;
    private const ushort CurrentMinorVersion = 2;
    private static readonly int ColorFilterSize = Unsafe.SizeOf<ColorFilter>();

    private const int HeaderSize = 64;
    private const int SizeOfInt32 = 4;
    private static readonly int SizeOfGradientStops = Unsafe.SizeOf<GradientStops>();
    private const int SizeOfMeshVertex = 80;
    private const int SizeOfMeshGradientHeader = 12;
    private static readonly int NoiseSpecSize = Unsafe.SizeOf<NoiseSpec>();

    public static int GetRequiredSize(SceneBuffer scene)
    {
        int pathArenaSize = scene.PathCount > 0 ? SizeOfInt32 + scene.PathArenaLength : 0;
        int paintTableSize = scene.PaintCount * (int)Unsafe.SizeOf<Paint>();
        int transformTableSize = scene.TransformCount * (int)Unsafe.SizeOf<Geometry.Affine>();
        int rectTableSize = scene.RectCount * (int)Unsafe.SizeOf<Geometry.Rect>();
        int gradientStopsTableSize = scene.GradientStopsCount * SizeOfGradientStops;
        int meshGradientTableSize = GetMeshGradientTableSize(scene);
        int commandsSize = scene.CommandCount * (int)Unsafe.SizeOf<SceneCommand>();

        return HeaderSize + pathArenaSize + paintTableSize + transformTableSize + rectTableSize + gradientStopsTableSize + meshGradientTableSize + scene.NoiseSpecCount * NoiseSpecSize + 4 + scene.ColorFilterCount * ColorFilterSize + commandsSize;
    }

    public static int Write(SceneBuffer scene, Span<byte> dst)
    {
        int requiredSize = GetRequiredSize(scene);
        if (dst.Length < requiredSize)
            Etch.Panic.Invariant(Etch.PanicCodes.BufferOverflow, "Destination buffer too small for scene serialization");

        int offset = 0;

        dst[offset++] = Magic0;
        dst[offset++] = Magic1;
        dst[offset++] = Magic2;
        dst[offset++] = Magic3;

        WriteUInt16(dst[offset..], CurrentMajorVersion);
        offset += 2;
        WriteUInt16(dst[offset..], CurrentMinorVersion);
        offset += 2;

        WriteUInt32(dst[offset..], (uint)scene.NoiseSpecCount);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.CommandCount);
        offset += 4;

        int headerEnd = HeaderSize;

        int pathArenaOffset = scene.PathCount > 0 ? headerEnd : 0;
        WriteUInt32(dst[offset..], (uint)pathArenaOffset);
        offset += 4;

        int pathArenaDataOffset = headerEnd + SizeOfInt32;
        int pathArenaSize = scene.PathCount > 0 ? SizeOfInt32 + scene.PathArenaLength : 0;

        int paintTableOffset = headerEnd + pathArenaSize;
        WriteUInt32(dst[offset..], (uint)paintTableOffset);
        offset += 4;

        int transformTableOffset = paintTableOffset + scene.PaintCount * (int)Unsafe.SizeOf<Paint>();
        WriteUInt32(dst[offset..], (uint)transformTableOffset);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.MeshGradientCount);
        offset += 4;

        int rectTableOffset = transformTableOffset + scene.TransformCount * (int)Unsafe.SizeOf<Geometry.Affine>();
        WriteUInt32(dst[offset..], (uint)rectTableOffset);
        offset += 4;

        int gradientStopsTableOffset = rectTableOffset + scene.RectCount * (int)Unsafe.SizeOf<Geometry.Rect>();
        WriteUInt32(dst[offset..], (uint)gradientStopsTableOffset);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.PathArenaLength);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.PaintCount);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.TransformCount);
        offset += 4;

        int meshGradientTableOffset = gradientStopsTableOffset + scene.GradientStopsCount * SizeOfGradientStops;
        WriteUInt32(dst[offset..], (uint)meshGradientTableOffset);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.RectCount);
        offset += 4;

        WriteUInt32(dst[offset..], (uint)scene.GradientStopsCount);
        offset += 4;

        if (scene.PathCount > 0)
        {
            WriteUInt32(dst[pathArenaOffset..], (uint)scene.PathArenaLength);
            // PathArenaBytes is the over-allocated backing array; copy only the used prefix
            // (PathArenaLength), not the full capacity, or the destination slice is too short.
            scene.PathArenaBytes.AsSpan(0, scene.PathArenaLength).CopyTo(dst.Slice(pathArenaOffset + 4, scene.PathArenaLength));
        }

        WritePaintTable(scene, dst, paintTableOffset);
        WriteTransformTable(scene, dst, transformTableOffset);
        WriteRectTable(scene, dst, rectTableOffset);
        WriteGradientStopsTable(scene, dst, gradientStopsTableOffset);
        WriteMeshGradientTable(scene, dst, meshGradientTableOffset);
        int meshGradientTableSize = GetMeshGradientTableSize(scene);
        int noiseSpecTableOffset = meshGradientTableOffset + meshGradientTableSize;
        WriteNoiseSpecTable(scene, dst, noiseSpecTableOffset);
        int colorFilterTableOffset = noiseSpecTableOffset + scene.NoiseSpecCount * NoiseSpecSize;
        int colorFilterTableSize = WriteColorFilterTable(scene, dst, colorFilterTableOffset);
        int commandsOffset = colorFilterTableOffset + colorFilterTableSize;

        WriteCommands(scene, dst, commandsOffset);

        return commandsOffset + scene.CommandCount * (int)Unsafe.SizeOf<SceneCommand>();
    }

    private static void WritePaintTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        int paintSize = (int)Unsafe.SizeOf<Paint>();
        for (int i = 0; i < scene.PaintCount; i++)
        {
            var paint = scene.GetPaint(i);
            var paintSpan = dst.Slice(offset + i * paintSize, paintSize);
            WritePaint(paintSpan, paint);
        }
    }

    private static void WritePaint(Span<byte> dst, Paint paint)
    {
        dst[0] = (byte)paint.Kind;
        WriteUInt32(dst[4..], paint.ColorOrGradientId);
    }

    private static void WriteTransformTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        int transformSize = (int)Unsafe.SizeOf<Geometry.Affine>();
        for (int i = 0; i < scene.TransformCount; i++)
        {
            var transform = scene.GetTransform(i);
            WriteAffine(dst.Slice(offset + i * transformSize), transform);
        }
    }

    private static void WriteAffine(Span<byte> dst, Geometry.Affine a)
    {
        WriteDouble(dst, a.M00);
        WriteDouble(dst[8..], a.M01);
        WriteDouble(dst[16..], a.M10);
        WriteDouble(dst[24..], a.M11);
        WriteDouble(dst[32..], a.M02);
        WriteDouble(dst[40..], a.M12);
    }

    private static void WriteRectTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        int rectSize = (int)Unsafe.SizeOf<Geometry.Rect>();
        for (int i = 0; i < scene.RectCount; i++)
        {
            var rect = scene.GetRect(i);
            WriteRect(dst.Slice(offset + i * rectSize), rect);
        }
    }

    private static void WriteRect(Span<byte> dst, Geometry.Rect r)
    {
        WriteDouble(dst, r.MinX);
        WriteDouble(dst[8..], r.MinY);
        WriteDouble(dst[16..], r.MaxX);
        WriteDouble(dst[24..], r.MaxY);
    }

    private static void WriteGradientStopsTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        for (int i = 0; i < scene.GradientStopsCount; i++)
        {
            var gradientStops = scene.GetGradientStops(i);
            WriteGradientStops(dst.Slice(offset + i * SizeOfGradientStops), gradientStops);
        }
    }

    private static void WriteGradientStops(Span<byte> dst, GradientStops gradientStops)
    {
        WriteInt32(dst, gradientStops.Count);
        for (int i = 0; i < gradientStops.Count; i++)
        {
            var (offset, argb) = gradientStops.GetStop(i);
            WriteFloat(dst[(4 + i * 8)..], offset);
            WriteUInt32(dst[(4 + i * 8 + 4)..], argb);
        }
    }

    private static void WriteCommands(SceneBuffer scene, Span<byte> dst, int offset)
    {
        int commandSize = (int)Unsafe.SizeOf<SceneCommand>();
        var commands = scene.Commands;
        for (int i = 0; i < commands.Length; i++)
        {
            WriteSceneCommand(dst.Slice(offset + i * commandSize), commands[i]);
        }
    }

    private static void WriteSceneCommand(Span<byte> dst, SceneCommand cmd)
    {
        dst[0] = (byte)cmd.Op;
        dst[1] = 0;
        dst[2] = 0;
        dst[3] = 0;
        dst[4] = 0;
        dst[5] = 0;
        dst[6] = 0;
        dst[7] = 0;

        switch (cmd.Op)
        {
            case SceneOpcode.FillPath:
                WriteFillPathPayload(dst[8..], cmd.FillPath);
                break;
            case SceneOpcode.StrokePath:
                WriteStrokePathPayload(dst[8..], cmd.StrokePath);
                break;
            case SceneOpcode.FillRect:
                WriteFillRectPayload(dst[8..], cmd.FillRect);
                break;
            case SceneOpcode.SetTransform:
                WriteSetTransformPayload(dst[8..], cmd.SetTransform);
                break;
            case SceneOpcode.PushClip:
                WritePushClipPayload(dst[8..], cmd.PushClip);
                break;
            case SceneOpcode.PopClip:
                break;
            case SceneOpcode.DrawImage:
                WriteDrawImagePayload(dst[8..], cmd.DrawImage);
                break;
            case SceneOpcode.DrawGlyphRun:
                WriteDrawGlyphRunPayload(dst[8..], cmd.DrawGlyphRun);
                break;
            case SceneOpcode.DrawShadow:
                WriteDrawShadowPayload(dst[8..], cmd.DrawShadow);
                break;
            case SceneOpcode.DrawMaterialRegion:
                WriteDrawMaterialRegionPayload(dst[8..], cmd.DrawMaterialRegion);
                break;
            case SceneOpcode.PushColorFilter:
                WritePushColorFilterPayload(dst[8..], cmd.PushColorFilter);
                break;
            case SceneOpcode.PopColorFilter:
                break;
            case SceneOpcode.SetBlendMode:
                WriteSetBlendModePayload(dst[8..], cmd.SetBlendMode);
                break;
            case SceneOpcode.PushLayer:
                WritePushLayerPayload(dst[8..], cmd.PushLayer);
                break;
            case SceneOpcode.PopLayer:
                break;
            case SceneOpcode.BeginFrame:
                break;
            case SceneOpcode.EndFrame:
                break;
            case SceneOpcode.Noop:
                break;
        }
    }

    private static void WriteFillPathPayload(Span<byte> dst, FillPathPayload p)
    {
        WriteInt32(dst, p.PathId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
        dst[12] = p.FillRule;
    }

    private static void WriteStrokePathPayload(Span<byte> dst, StrokePathPayload p)
    {
        WriteInt32(dst, p.PathId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
        WriteFloat(dst[12..], p.StrokeWidth);
    }

    private static void WriteFillRectPayload(Span<byte> dst, FillRectPayload p)
    {
        WriteInt32(dst, p.RectId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
    }

    private static void WriteSetTransformPayload(Span<byte> dst, SetTransformPayload p)
    {
        WriteInt32(dst, p.TransformId);
    }

    private static void WritePushClipPayload(Span<byte> dst, PushClipPayload p)
    {
        WriteInt32(dst, p.ClipId);
        dst[4] = p.FillRule;
    }

    private static void WriteDrawImagePayload(Span<byte> dst, DrawImagePayload p)
    {
        WriteInt32(dst, p.ImageId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
    }

    private static void WriteDrawGlyphRunPayload(Span<byte> dst, DrawGlyphRunPayload p)
    {
        WriteInt32(dst, p.GlyphRunId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
    }

    private static void WriteDrawShadowPayload(Span<byte> dst, DrawShadowPayload p)
    {
        WriteInt32(dst, p.PathId);
        WriteInt32(dst[4..], p.PaintId);
        WriteInt32(dst[8..], p.TransformId);
        WriteFloat(dst[12..], p.ShadowOffsetX);
        WriteFloat(dst[16..], p.ShadowOffsetY);
        WriteFloat(dst[20..], p.BlurRadius);
        WriteUInt32(dst[24..], p.ShadowColor);
    }

    private static void WriteDrawMaterialRegionPayload(Span<byte> dst, DrawMaterialRegionPayload p)
    {
        WriteInt32(dst, p.RectId);
        WriteInt32(dst[4..], p.TransformId);
        WriteFloat(dst[8..], p.Radius);
    }

    private static void WriteSetBlendModePayload(Span<byte> dst, SetBlendModePayload p)
    {
        dst[0] = p.BlendMode;
    }

    private static void WritePushLayerPayload(Span<byte> dst, PushLayerPayload p)
    {
        WriteInt32(dst, p.LayerId);
        WriteFloat(dst[4..], p.Opacity);
        dst[8] = p.BlendMode;
        dst[9] = p.Flags;
    }

    private static void WriteInt32(Span<byte> dst, int value)
    {
        dst[0] = (byte)value;
        dst[1] = (byte)(value >> 8);
        dst[2] = (byte)(value >> 16);
        dst[3] = (byte)(value >> 24);
    }

    private static void WriteUInt32(Span<byte> dst, uint value)
    {
        dst[0] = (byte)value;
        dst[1] = (byte)(value >> 8);
        dst[2] = (byte)(value >> 16);
        dst[3] = (byte)(value >> 24);
    }

    private static void WriteUInt16(Span<byte> dst, ushort value)
    {
        dst[0] = (byte)value;
        dst[1] = (byte)(value >> 8);
    }

    private static void WriteFloat(Span<byte> dst, float value)
    {
        BitConverter.TryWriteBytes(dst, value);
    }

    private static void WriteDouble(Span<byte> dst, double value)
    {
        BitConverter.TryWriteBytes(dst, value);
    }

    private static void WritePushColorFilterPayload(Span<byte> dst, PushColorFilterPayload p)
    {
        WriteInt32(dst, p.ColorFilterId);
    }

    private static int WriteColorFilterTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        WriteInt32(dst[offset..], scene.ColorFilterCount);
        int dataOffset = offset + 4;
        for (int i = 0; i < scene.ColorFilterCount; i++)
        {
            var filter = scene.GetColorFilter(i);
            var entry = dst.Slice(dataOffset + i * ColorFilterSize, ColorFilterSize);
            WriteFilterMatrix(entry, filter);
        }
        return 4 + scene.ColorFilterCount * ColorFilterSize;
    }

    private static void WriteFilterMatrix(Span<byte> dst, ColorFilter f)
    {
        WriteFloat(dst, f.M11); WriteFloat(dst[4..], f.M12); WriteFloat(dst[8..], f.M13); WriteFloat(dst[12..], f.M14); WriteFloat(dst[16..], f.M15);
        WriteFloat(dst[20..], f.M21); WriteFloat(dst[24..], f.M22); WriteFloat(dst[28..], f.M23); WriteFloat(dst[32..], f.M24); WriteFloat(dst[36..], f.M25);
        WriteFloat(dst[40..], f.M31); WriteFloat(dst[44..], f.M32); WriteFloat(dst[48..], f.M33); WriteFloat(dst[52..], f.M34); WriteFloat(dst[56..], f.M35);
        WriteFloat(dst[60..], f.M41); WriteFloat(dst[64..], f.M42); WriteFloat(dst[68..], f.M43); WriteFloat(dst[72..], f.M44); WriteFloat(dst[76..], f.M45);
    }

    private static void WriteNoiseSpecTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        for (int i = 0; i < scene.NoiseSpecCount; i++)
        {
            var spec = scene.GetNoiseSpec(i);
            var entry = dst.Slice(offset + i * NoiseSpecSize, NoiseSpecSize);
            WriteFloat(entry, spec.Scale);
            WriteInt32(entry[4..], spec.Octaves);
            WriteFloat(entry[8..], spec.Persistence);
            WriteUInt32(entry[12..], spec.Seed);
            WriteFloat(entry[16..], spec.Opacity);
        }
    }

    private static int GetMeshGradientTableSize(SceneBuffer scene)
    {
        int size = 0;
        for (int i = 0; i < scene.MeshGradientCount; i++)
        {
            var mesh = scene.GetMeshGradient(i);
            size += SizeOfMeshGradientHeader + mesh.VertexCount * SizeOfMeshVertex;
        }
        return size;
    }

    private static void WriteMeshGradientTable(SceneBuffer scene, Span<byte> dst, int offset)
    {
        for (int i = 0; i < scene.MeshGradientCount; i++)
        {
            var mesh = scene.GetMeshGradient(i);
            int entrySize = SizeOfMeshGradientHeader + mesh.VertexCount * SizeOfMeshVertex;
            WriteMeshGradient(dst.Slice(offset, entrySize), mesh);
            offset += entrySize;
        }
    }

    private static void WriteMeshGradient(Span<byte> dst, MeshGradient mesh)
    {
        WriteInt32(dst, mesh.Rows);
        WriteInt32(dst[4..], mesh.Cols);
        WriteInt32(dst[8..], mesh.VertexCount);

        int vertexOffset = SizeOfMeshGradientHeader;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.Vertices[i];
            var vDst = dst.Slice(vertexOffset, SizeOfMeshVertex);
            WriteFloat(vDst, v.Color.R);
            WriteFloat(vDst[4..], v.Color.G);
            WriteFloat(vDst[8..], v.Color.B);
            WriteFloat(vDst[12..], v.Color.A);
            WriteDouble(vDst[16..], v.DuIn.X);
            WriteDouble(vDst[24..], v.DuIn.Y);
            WriteDouble(vDst[32..], v.DuOut.X);
            WriteDouble(vDst[40..], v.DuOut.Y);
            WriteDouble(vDst[48..], v.DvIn.X);
            WriteDouble(vDst[56..], v.DvIn.Y);
            WriteDouble(vDst[64..], v.DvOut.X);
            WriteDouble(vDst[72..], v.DvOut.Y);
            vertexOffset += SizeOfMeshVertex;
        }
    }
}