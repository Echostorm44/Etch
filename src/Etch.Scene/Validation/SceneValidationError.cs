using System.Runtime.InteropServices;

namespace Etch.Scene;

public enum SceneValidationErrorCode : int
{
    BadFrameMarkers = 0,
    UnbalancedLayerStack = 1,
    LayerStackOverflow = 2,
    UnbalancedClipStack = 3,
    InvalidResourceId = 4,
    NonFiniteGeometry = 5,
    EmptyPath = 6,
    BadGradient = 7,
    BadLayerOpacity = 8,
    BadStrokeParam = 9,
}

[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly struct SceneValidationError
{
    public readonly SceneValidationErrorCode ErrorCode;
    public readonly int CommandIndex;
    private readonly int _padding;

    public SceneValidationError(SceneValidationErrorCode errorCode, int commandIndex)
    {
        ErrorCode = errorCode;
        CommandIndex = commandIndex;
        _padding = 0;
    }
}
