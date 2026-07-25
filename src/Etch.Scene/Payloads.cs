using System.Runtime.InteropServices;

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct FillPathPayload
{
    public int PathId;
    public int PaintId;
    public int TransformId;
    public byte FillRule;
    private byte _align0, _align1, _align2, _align3, _align4, _align5, _align6;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct StrokePathPayload
{
    public int PathId;
    public int PaintId;
    public int TransformId;
    public float StrokeWidth;
    private byte _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct FillRectPayload
{
    public int RectId;
    public int PaintId;
    public int TransformId;
    private int _align0, _align1, _align2, _align3, _align4;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct SetTransformPayload
{
    public int TransformId;
    private int _align0, _align1, _align2, _align3, _align4, _align5, _align6;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PushClipPayload
{
    public int ClipId;
    public byte FillRule;
    public byte ClipMode;
    private byte _align0, _align1, _align2, _align3, _align4, _align5;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PopClipPayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct DrawImagePayload
{
    public int ImageId;
    public int PaintId;
    public int TransformId;
    private int _align;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct DrawGlyphRunPayload
{
    public int GlyphRunId;
    public int PaintId;
    public int TransformId;
    private int _align;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct SetBlendModePayload
{
    public byte BlendMode;
    private byte _align0, _align1, _align2, _align3, _align4, _align5, _align6;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PushLayerPayload
{
    public int LayerId;
    public float Opacity;
    public byte BlendMode;
    public byte Flags;
    private byte _align0, _align1, _align2, _align3, _align4;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PopLayerPayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct BeginFramePayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct EndFramePayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct DrawShadowPayload
{
    public int PathId;
    public int PaintId;
    public int TransformId;
    public float ShadowOffsetX;
    public float ShadowOffsetY;
    public float BlurRadius;
    public uint ShadowColor;
    private byte _align0;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct NoopPayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct DrawMaterialRegionPayload
{
    public int RectId;
    public int TransformId;
    public float Radius;
    private int _align0, _align1, _align2, _align3;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PushColorFilterPayload
{
    public int ColorFilterId;
    private int _align0, _align1, _align2, _align3, _align4, _align5, _align6;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PopColorFilterPayload
{
    private long _align0, _align1, _align2;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct FillSectorPayload
{
    public float CenterX;
    public float CenterY;
    public float OuterRadius;
    public float InnerRadius;
    public float StartRad;
    public float SweepRad;
    public int PaintId;
    public int TransformId;
}
