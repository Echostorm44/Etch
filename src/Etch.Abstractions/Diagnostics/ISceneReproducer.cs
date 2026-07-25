using System;

namespace Etch.Abstractions.Diagnostics;

public interface ISceneReproducer
{
    int CaptureTo(Span<byte> destination);
}
