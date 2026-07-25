using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Scene.Serialization;

public static class SceneReader
{
    private const byte Magic0 = 0x45;
    private const byte Magic1 = 0x54;
    private const byte Magic2 = 0x53;
    private const byte Magic3 = 0x43;
    private const ushort SupportedMajorVersion = 1;
    private const int HeaderSize = 64;
    private static readonly int SizeOfGradientStops = Unsafe.SizeOf<GradientStops>();
    private const int SizeOfMeshVertex = 80;
    private const int SizeOfMeshGradientHeader = 12;
    private static readonly int NoiseSpecSize = Unsafe.SizeOf<NoiseSpec>();
    private static readonly int ColorFilterSize = Unsafe.SizeOf<ColorFilter>();

    public static SceneBuffer Read(ReadOnlySpan<byte> src)
    {
        if (src.Length < HeaderSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for header");

        if (src[0] != Magic0 || src[1] != Magic1 || src[2] != Magic2 || src[3] != Magic3)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatBadMagic, "Scene buffer missing ETSC magic");

        ushort majorVersion = ReadUInt16(src[4..6]);
        ushort minorVersion = ReadUInt16(src[6..8]);

        if (majorVersion > SupportedMajorVersion)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatVersionTooNew, $"Scene format major version {majorVersion} is too new");

        uint flags = ReadUInt32(src[8..12]);
        uint noiseSpecCount = flags;

        uint commandCount = ReadUInt32(src[12..16]);
        uint pathArenaOffset = ReadUInt32(src[16..20]);
        uint paintTableOffset = ReadUInt32(src[20..24]);
        uint transformTableOffset = ReadUInt32(src[24..28]);
        uint meshGradientCount = ReadUInt32(src[28..32]);
        uint rectTableOffset = ReadUInt32(src[32..36]);
        uint gradientStopsTableOffset = ReadUInt32(src[36..40]);
        uint pathArenaLength = ReadUInt32(src[40..44]);
        uint paintCount = ReadUInt32(src[44..48]);
        uint transformCount = ReadUInt32(src[48..52]);
        uint meshGradientTableOffset = ReadUInt32(src[52..56]);
        uint rectCount = ReadUInt32(src[56..60]);
        uint gradientStopsCount = ReadUInt32(src[60..64]);

        if (minorVersion == 0)
        {
            meshGradientCount = 0;
            meshGradientTableOffset = 0;
        }

        PathEntry[] pathTable;
        byte[] pathArena;

        if (pathArenaLength > 0)
        {
            int pathArenaStart = (int)pathArenaOffset;
            int pathArenaEnd = pathArenaStart + (int)pathArenaLength;
            if (src.Length < pathArenaEnd)
                Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for path arena");

            pathArena = new byte[pathArenaLength];
            src.Slice(pathArenaStart, (int)pathArenaLength).CopyTo(pathArena);

            pathTable = ParsePathTable(pathArena);
        }
        else
        {
            pathArena = Array.Empty<byte>();
            pathTable = Array.Empty<PathEntry>();
        }

        int paintTableStart = (int)paintTableOffset;
        int paintTableSize = (int)paintCount * (int)Unsafe.SizeOf<Paint>();
        if (src.Length < paintTableStart + paintTableSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for paint table");

        Paint[] paints = new Paint[paintCount];
        for (uint i = 0; i < paintCount; i++)
        {
            paints[i] = ReadPaint(src.Slice(paintTableStart + (int)i * (int)Unsafe.SizeOf<Paint>(), (int)Unsafe.SizeOf<Paint>()));
        }

        int transformTableStart = (int)transformTableOffset;
        int transformTableSize = (int)transformCount * (int)Unsafe.SizeOf<Geometry.Affine>();
        if (src.Length < transformTableStart + transformTableSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for transform table");

        Geometry.Affine[] transforms = new Geometry.Affine[transformCount];
        for (uint i = 0; i < transformCount; i++)
        {
            transforms[i] = ReadAffine(src.Slice(transformTableStart + (int)i * (int)Unsafe.SizeOf<Geometry.Affine>(), (int)Unsafe.SizeOf<Geometry.Affine>()));
        }

        int rectTableStart = (int)rectTableOffset;
        int rectTableSize = (int)rectCount * (int)Unsafe.SizeOf<Geometry.Rect>();
        if (src.Length < rectTableStart + rectTableSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for rect table");

        Geometry.Rect[] rects = new Geometry.Rect[rectCount];
        for (uint i = 0; i < rectCount; i++)
        {
            rects[i] = ReadRect(src.Slice(rectTableStart + (int)i * (int)Unsafe.SizeOf<Geometry.Rect>(), (int)Unsafe.SizeOf<Geometry.Rect>()));
        }

        int gradientStopsTableStart = (int)gradientStopsTableOffset;
        int gradientStopsTableSize = (int)gradientStopsCount * SizeOfGradientStops;
        if (src.Length < gradientStopsTableStart + gradientStopsTableSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for gradient stops table");

        GradientStops[] gradientStops = new GradientStops[gradientStopsCount];
        for (uint i = 0; i < gradientStopsCount; i++)
        {
            gradientStops[i] = ReadGradientStops(src.Slice(gradientStopsTableStart + (int)i * SizeOfGradientStops, SizeOfGradientStops));
        }

        int meshGradientsCount = (int)meshGradientCount;
        List<MeshGradient>? meshGradients = null;
        int meshGradientTableSize = 0;
        if (meshGradientsCount > 0 && meshGradientTableOffset > 0)
        {
            int meshOffset = (int)meshGradientTableOffset;
            var meshDataSlice = src.Slice(meshOffset);
            meshGradients = new List<MeshGradient>(meshGradientsCount);
            meshGradientTableSize = ReadMeshGradientTable(meshDataSlice, meshGradientsCount, meshGradients);
        }

        int noiseSpecsCount = (int)noiseSpecCount;
        int noiseTableStart = (int)meshGradientTableOffset + meshGradientTableSize;
        int noiseTableSize = noiseSpecsCount * NoiseSpecSize;
        NoiseSpec[] noiseSpecs = new NoiseSpec[noiseSpecsCount];
        for (uint i = 0; i < noiseSpecsCount; i++)
        {
            noiseSpecs[i] = ReadNoiseSpec(src.Slice(noiseTableStart + (int)i * NoiseSpecSize, NoiseSpecSize));
        }

        int colorFilterTableStart = noiseTableStart + noiseTableSize;
        int colorFilterCount = 0;
        if (src.Length > colorFilterTableStart + 4)
        {
            colorFilterCount = ReadInt32(src.Slice(colorFilterTableStart, 4));
        }
        int colorFilterTableSize = 4 + colorFilterCount * ColorFilterSize;
        ColorFilter[] colorFilters = new ColorFilter[colorFilterCount];
        for (int i = 0; i < colorFilterCount; i++)
        {
            colorFilters[i] = ReadColorFilter(src.Slice(colorFilterTableStart + 4 + i * ColorFilterSize, ColorFilterSize));
        }

        int commandsStart = gradientStopsTableStart + gradientStopsTableSize + meshGradientTableSize + noiseTableSize + colorFilterTableSize;
        int commandsSize = (int)commandCount * (int)Unsafe.SizeOf<SceneCommand>();
        if (src.Length < commandsStart + commandsSize)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneFormatTruncated, "Scene buffer too short for commands");

        SceneCommand[] commands = new SceneCommand[commandCount];
        for (uint i = 0; i < commandCount; i++)
        {
            commands[i] = ReadSceneCommand(src.Slice(commandsStart + (int)i * (int)Unsafe.SizeOf<SceneCommand>(), (int)Unsafe.SizeOf<SceneCommand>()));
        }

        return new SceneBuffer(commands, pathTable, pathArena, paints, transforms, rects, gradientStops,
            meshGradients?.ToArray() ?? Array.Empty<MeshGradient>(),
            noiseSpecs,
            colorFilters);
    }

    private static PathEntry[] ParsePathTable(byte[] pathArena)
    {
        int count = 0;
        int offset = 0;
        while (offset < pathArena.Length)
        {
            int verbCount = ReadInt32(pathArena.AsSpan(offset, 4));
            int coordCount = ReadInt32(pathArena.AsSpan(offset + 4, 4));
            int entrySize = 8 + verbCount + coordCount * 8;
            offset += entrySize;
            count++;
        }

        PathEntry[] entries = new PathEntry[count];
        offset = 0;
        for (int i = 0; i < count; i++)
        {
            int verbCount = ReadInt32(pathArena.AsSpan(offset, 4));
            int coordCount = ReadInt32(pathArena.AsSpan(offset + 4, 4));
            int entrySize = 8 + verbCount + coordCount * 8;
            entries[i] = new PathEntry(offset, entrySize, verbCount, coordCount);
            offset += entrySize;
        }
        return entries;
    }

    private static Paint ReadPaint(ReadOnlySpan<byte> src)
    {
        PaintKind kind = (PaintKind)src[0];
        uint color = ReadUInt32(src[4..8]);
        return new Paint(kind, color);
    }

    private static Geometry.Affine ReadAffine(ReadOnlySpan<byte> src)
    {
        double m00 = ReadDouble(src[0..8]);
        double m01 = ReadDouble(src[8..16]);
        double m10 = ReadDouble(src[16..24]);
        double m11 = ReadDouble(src[24..32]);
        double m02 = ReadDouble(src[32..40]);
        double m12 = ReadDouble(src[40..48]);
        return new Geometry.Affine(m00, m01, m10, m11, m02, m12);
    }

    private static Geometry.Rect ReadRect(ReadOnlySpan<byte> src)
    {
        double minX = ReadDouble(src[0..8]);
        double minY = ReadDouble(src[8..16]);
        double maxX = ReadDouble(src[16..24]);
        double maxY = ReadDouble(src[24..32]);
        return new Geometry.Rect(minX, minY, maxX, maxY);
    }

    private static GradientStops ReadGradientStops(ReadOnlySpan<byte> src)
    {
        int count = ReadInt32(src[0..4]);
        var gradientStops = new GradientStops { Count = count };
        for (int i = 0; i < count; i++)
        {
            float offset = ReadFloat(src[(4 + i * 8)..(4 + i * 8 + 4)]);
            uint argb = ReadUInt32(src[(4 + i * 8 + 4)..(4 + i * 8 + 8)]);
            gradientStops.SetStop(i, offset, argb);
        }
        return gradientStops;
    }

    private static SceneCommand ReadSceneCommand(ReadOnlySpan<byte> src)
    {
        SceneOpcode op = (SceneOpcode)src[0];

        switch (op)
        {
            case SceneOpcode.FillPath:
                return new SceneCommand(op, ReadFillPathPayload(src[8..]));
            case SceneOpcode.StrokePath:
                return new SceneCommand(op, ReadStrokePathPayload(src[8..]));
            case SceneOpcode.FillRect:
                return new SceneCommand(op, ReadFillRectPayload(src[8..]));
            case SceneOpcode.SetTransform:
                return new SceneCommand(op, ReadSetTransformPayload(src[8..]));
            case SceneOpcode.PushClip:
                return new SceneCommand(op, ReadPushClipPayload(src[8..]));
            case SceneOpcode.PopClip:
                return new SceneCommand(op, new PopClipPayload());
            case SceneOpcode.DrawImage:
                return new SceneCommand(op, ReadDrawImagePayload(src[8..]));
            case SceneOpcode.DrawGlyphRun:
                return new SceneCommand(op, ReadDrawGlyphRunPayload(src[8..]));
            case SceneOpcode.DrawShadow:
                return new SceneCommand(op, ReadDrawShadowPayload(src[8..]));
            case SceneOpcode.DrawMaterialRegion:
                return new SceneCommand(op, ReadDrawMaterialRegionPayload(src[8..]));
            case SceneOpcode.PushColorFilter:
                return new SceneCommand(op, ReadPushColorFilterPayload(src[8..]));
            case SceneOpcode.PopColorFilter:
                return new SceneCommand(op, new PopColorFilterPayload());
            case SceneOpcode.SetBlendMode:
                return new SceneCommand(op, ReadSetBlendModePayload(src[8..]));
            case SceneOpcode.PushLayer:
                return new SceneCommand(op, ReadPushLayerPayload(src[8..]));
            case SceneOpcode.PopLayer:
                return new SceneCommand(op, new PopLayerPayload());
            case SceneOpcode.BeginFrame:
                return new SceneCommand(op, new BeginFramePayload());
            case SceneOpcode.EndFrame:
                return new SceneCommand(op, new EndFramePayload());
            case SceneOpcode.Noop:
                return new SceneCommand(op, new NoopPayload());
            default:
                return new SceneCommand(SceneOpcode.Noop, new NoopPayload());
        }
    }

    private static FillPathPayload ReadFillPathPayload(ReadOnlySpan<byte> src)
    {
        return new FillPathPayload
        {
            PathId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12]),
            FillRule = src[12]
        };
    }

    private static StrokePathPayload ReadStrokePathPayload(ReadOnlySpan<byte> src)
    {
        return new StrokePathPayload
        {
            PathId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12]),
            StrokeWidth = ReadFloat(src[12..16])
        };
    }

    private static FillRectPayload ReadFillRectPayload(ReadOnlySpan<byte> src)
    {
        return new FillRectPayload
        {
            RectId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12])
        };
    }

    private static SetTransformPayload ReadSetTransformPayload(ReadOnlySpan<byte> src)
    {
        return new SetTransformPayload
        {
            TransformId = ReadInt32(src[0..4])
        };
    }

    private static PushClipPayload ReadPushClipPayload(ReadOnlySpan<byte> src)
    {
        return new PushClipPayload
        {
            ClipId = ReadInt32(src[0..4]),
            FillRule = src[4]
        };
    }

    private static DrawImagePayload ReadDrawImagePayload(ReadOnlySpan<byte> src)
    {
        return new DrawImagePayload
        {
            ImageId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12])
        };
    }

    private static DrawGlyphRunPayload ReadDrawGlyphRunPayload(ReadOnlySpan<byte> src)
    {
        return new DrawGlyphRunPayload
        {
            GlyphRunId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12])
        };
    }

    private static DrawShadowPayload ReadDrawShadowPayload(ReadOnlySpan<byte> src)
    {
        return new DrawShadowPayload
        {
            PathId = ReadInt32(src[0..4]),
            PaintId = ReadInt32(src[4..8]),
            TransformId = ReadInt32(src[8..12]),
            ShadowOffsetX = ReadFloat(src[12..16]),
            ShadowOffsetY = ReadFloat(src[16..20]),
            BlurRadius = ReadFloat(src[20..24]),
            ShadowColor = ReadUInt32(src[24..28])
        };
    }

    private static DrawMaterialRegionPayload ReadDrawMaterialRegionPayload(ReadOnlySpan<byte> src)
    {
        return new DrawMaterialRegionPayload
        {
            RectId = ReadInt32(src[0..4]),
            TransformId = ReadInt32(src[4..8]),
            Radius = ReadFloat(src[8..12])
        };
    }

    private static PushColorFilterPayload ReadPushColorFilterPayload(ReadOnlySpan<byte> src)
    {
        return new PushColorFilterPayload
        {
            ColorFilterId = ReadInt32(src[0..4])
        };
    }

    private static ColorFilter ReadColorFilter(ReadOnlySpan<byte> src)
    {
        return new ColorFilter(
            ReadFloat(src[0..4]), ReadFloat(src[4..8]), ReadFloat(src[8..12]), ReadFloat(src[12..16]), ReadFloat(src[16..20]),
            ReadFloat(src[20..24]), ReadFloat(src[24..28]), ReadFloat(src[28..32]), ReadFloat(src[32..36]), ReadFloat(src[36..40]),
            ReadFloat(src[40..44]), ReadFloat(src[44..48]), ReadFloat(src[48..52]), ReadFloat(src[52..56]), ReadFloat(src[56..60]),
            ReadFloat(src[60..64]), ReadFloat(src[64..68]), ReadFloat(src[68..72]), ReadFloat(src[72..76]), ReadFloat(src[76..80]));
    }

    private static SetBlendModePayload ReadSetBlendModePayload(ReadOnlySpan<byte> src)
    {
        return new SetBlendModePayload
        {
            BlendMode = src[0]
        };
    }

    private static PushLayerPayload ReadPushLayerPayload(ReadOnlySpan<byte> src)
    {
        return new PushLayerPayload
        {
            LayerId = ReadInt32(src[0..4]),
            Opacity = ReadFloat(src[4..8]),
            BlendMode = src[8],
            Flags = src[9]
        };
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data)
    {
        return (ushort)(data[0] | (data[1] << 8));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data)
    {
        return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
    }

    private static int ReadInt32(ReadOnlySpan<byte> data)
    {
        return data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    }

    private static float ReadFloat(ReadOnlySpan<byte> data)
    {
        return BitConverter.ToSingle(data);
    }

    private static double ReadDouble(ReadOnlySpan<byte> data)
    {
        return BitConverter.ToDouble(data);
    }

    private static NoiseSpec ReadNoiseSpec(ReadOnlySpan<byte> src)
    {
        float scale = ReadFloat(src[0..4]);
        int octaves = ReadInt32(src[4..8]);
        float persistence = ReadFloat(src[8..12]);
        uint seed = ReadUInt32(src[12..16]);
        float opacity = ReadFloat(src[16..20]);
        return new NoiseSpec(scale, octaves, persistence, seed, opacity);
    }

    private static int ReadMeshGradientTable(ReadOnlySpan<byte> src, int count, List<MeshGradient> meshGradients)
    {
        int offset = 0;
        for (int entryIdx = 0; entryIdx < count; entryIdx++)
        {
            var header = src.Slice(offset, SizeOfMeshGradientHeader);
            int rows = ReadInt32(header[0..4]);
            int cols = ReadInt32(header[4..8]);
            int vertexCount = ReadInt32(header[8..12]);

            int verticesSize = vertexCount * SizeOfMeshVertex;
            var verticesSlice = src.Slice(offset + SizeOfMeshGradientHeader, verticesSize);

            var vertices = new MeshVertex[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var vSrc = verticesSlice.Slice(i * SizeOfMeshVertex, SizeOfMeshVertex);
                var color = new RgbaFloat(
                    ReadFloat(vSrc[0..4]),
                    ReadFloat(vSrc[4..8]),
                    ReadFloat(vSrc[8..12]),
                    ReadFloat(vSrc[12..16]));
                var duIn = new Geometry.Vec2(
                    ReadDouble(vSrc[16..24]),
                    ReadDouble(vSrc[24..32]));
                var duOut = new Geometry.Vec2(
                    ReadDouble(vSrc[32..40]),
                    ReadDouble(vSrc[40..48]));
                var dvIn = new Geometry.Vec2(
                    ReadDouble(vSrc[48..56]),
                    ReadDouble(vSrc[56..64]));
                var dvOut = new Geometry.Vec2(
                    ReadDouble(vSrc[64..72]),
                    ReadDouble(vSrc[72..80]));
                vertices[i] = new MeshVertex(color, duIn, duOut, dvIn, dvOut);
            }

            meshGradients.Add(new MeshGradient(rows, cols, vertices));
            offset += SizeOfMeshGradientHeader + verticesSize;
        }
        return offset;
    }
}