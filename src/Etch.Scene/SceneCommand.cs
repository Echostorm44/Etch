using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Scene;

[StructLayout(LayoutKind.Explicit, Size = 40)]
public readonly struct SceneCommand
{
    [FieldOffset(0)]
    public readonly SceneOpcode Op;

    [FieldOffset(8)]
    public readonly FillPathPayload FillPath;

    [FieldOffset(8)]
    public readonly StrokePathPayload StrokePath;

    [FieldOffset(8)]
    public readonly FillRectPayload FillRect;

    [FieldOffset(8)]
    public readonly SetTransformPayload SetTransform;

    [FieldOffset(8)]
    public readonly PushClipPayload PushClip;

    [FieldOffset(8)]
    public readonly PopClipPayload PopClip;

    [FieldOffset(8)]
    public readonly DrawImagePayload DrawImage;

    [FieldOffset(8)]
    public readonly DrawGlyphRunPayload DrawGlyphRun;

    [FieldOffset(8)]
    public readonly SetBlendModePayload SetBlendMode;

    [FieldOffset(8)]
    public readonly PushLayerPayload PushLayer;

    [FieldOffset(8)]
    public readonly PopLayerPayload PopLayer;

    [FieldOffset(8)]
    public readonly BeginFramePayload BeginFrame;

    [FieldOffset(8)]
    public readonly EndFramePayload EndFrame;

    [FieldOffset(8)]
    public readonly NoopPayload Noop;

    [FieldOffset(8)]
    public readonly DrawShadowPayload DrawShadow;

    [FieldOffset(8)]
    public readonly DrawMaterialRegionPayload DrawMaterialRegion;

    [FieldOffset(8)]
    public readonly PushColorFilterPayload PushColorFilter;

    [FieldOffset(8)]
    public readonly PopColorFilterPayload PopColorFilter;

    [FieldOffset(8)]
    public readonly FillSectorPayload FillSector;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, FillPathPayload payload)
    {
        Op = op;
        FillPath = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, StrokePathPayload payload)
    {
        Op = op;
        StrokePath = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, FillRectPayload payload)
    {
        Op = op;
        FillRect = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, SetTransformPayload payload)
    {
        Op = op;
        SetTransform = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PushClipPayload payload)
    {
        Op = op;
        PushClip = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PopClipPayload payload)
    {
        Op = op;
        PopClip = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, DrawImagePayload payload)
    {
        Op = op;
        DrawImage = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, DrawGlyphRunPayload payload)
    {
        Op = op;
        DrawGlyphRun = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, SetBlendModePayload payload)
    {
        Op = op;
        SetBlendMode = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PushLayerPayload payload)
    {
        Op = op;
        PushLayer = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PopLayerPayload payload)
    {
        Op = op;
        PopLayer = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, BeginFramePayload payload)
    {
        Op = op;
        BeginFrame = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, EndFramePayload payload)
    {
        Op = op;
        EndFrame = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, NoopPayload payload)
    {
        Op = op;
        Noop = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, DrawShadowPayload payload)
    {
        Op = op;
        DrawShadow = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, DrawMaterialRegionPayload payload)
    {
        Op = op;
        DrawMaterialRegion = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PushColorFilterPayload payload)
    {
        Op = op;
        PushColorFilter = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, PopColorFilterPayload payload)
    {
        Op = op;
        PopColorFilter = payload;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SceneCommand(SceneOpcode op, FillSectorPayload payload)
    {
        Op = op;
        FillSector = payload;
    }

    public static SceneCommand CreateNoop() => new(SceneOpcode.Noop, new NoopPayload());
}