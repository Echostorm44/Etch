namespace Etch.Strokes;

public enum JoinStyle : byte
{
    Miter = 0,
    Round = 1,
    Bevel = 2,
}

public enum CapStyle : byte
{
    Butt = 0,
    Round = 1,
    Square = 2,
}

public readonly struct StrokeStyle
{
    public readonly float Width;
    public readonly JoinStyle Join;
    public readonly CapStyle Cap;
    public readonly float MiterLimit;
    public readonly float[]? DashPattern;
    public readonly float DashOffset;

    public StrokeStyle(float width, JoinStyle join = JoinStyle.Miter, CapStyle cap = CapStyle.Butt, float miterLimit = 4f, float[]? dashPattern = null, float dashOffset = 0f)
    {
        Width = width;
        Join = join;
        Cap = cap;
        MiterLimit = miterLimit;
        DashPattern = dashPattern;
        DashOffset = dashOffset;
    }

    public static StrokeStyle Default => new(1f);
}
