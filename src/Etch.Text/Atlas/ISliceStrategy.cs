namespace Etch.Text.Atlas;

public interface ISliceStrategy
{
    bool TryInsert(int w, int h, out int u, out int v);
    void Reset();
}