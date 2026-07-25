using System;

namespace Etch.Scene;

public ref struct SceneValidationReport
{
    private readonly Span<SceneValidationError> _errors;
    private int _count;

    public SceneValidationReport(Span<SceneValidationError> buffer)
    {
        _errors = buffer;
        _count = 0;
    }

    public int Count => _count;

    public bool IsFull => _count >= _errors.Length;

    public void Add(Etch.PanicCode code, int commandIndex)
    {
        if (_count < _errors.Length)
        {
            _errors[_count] = new SceneValidationError(ToErrorCode(code), commandIndex);
            _count++;
        }
    }

    public ReadOnlySpan<SceneValidationError> Errors => _errors[.._count];

    private static SceneValidationErrorCode ToErrorCode(Etch.PanicCode code)
    {
        var value = code.Value;
        return value switch
        {
            "ET-P-0420" => SceneValidationErrorCode.BadFrameMarkers,
            "ET-P-0421" => SceneValidationErrorCode.UnbalancedLayerStack,
            "ET-P-0422" => SceneValidationErrorCode.LayerStackOverflow,
            "ET-P-0423" => SceneValidationErrorCode.UnbalancedClipStack,
            "ET-P-0424" => SceneValidationErrorCode.InvalidResourceId,
            "ET-P-0425" => SceneValidationErrorCode.NonFiniteGeometry,
            "ET-P-0426" => SceneValidationErrorCode.EmptyPath,
            "ET-P-0427" => SceneValidationErrorCode.BadGradient,
            "ET-P-0428" => SceneValidationErrorCode.BadLayerOpacity,
            "ET-P-0429" => SceneValidationErrorCode.BadStrokeParam,
            _ => SceneValidationErrorCode.BadFrameMarkers,
        };
    }
}
