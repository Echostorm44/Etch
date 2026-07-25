namespace Etch.Text.Atlas;

[Etch.Abstractions.EtchExtensionPoint]  // pluggable atlas slice-packing strategy
public interface ISliceStrategy
{
    bool TryInsert(int w, int h, out int u, out int v);
    void Reset();
}