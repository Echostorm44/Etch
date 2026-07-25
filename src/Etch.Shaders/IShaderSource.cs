namespace Etch.Shaders;

[Etch.Abstractions.EtchExtensionPoint]
public interface IShaderSource
{
    ReadOnlySpan<byte> GetSource(string name);

    bool TryGetVersion(string name, out ulong version);

event EventHandler<string>? Changed;
}