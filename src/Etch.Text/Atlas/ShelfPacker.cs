namespace Etch.Text.Atlas;

public sealed class ShelfPacker : ISliceStrategy
{
    private Shelf? _head;
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowHeight;

    public ShelfPacker(int width, int height, int rowHeight)
    {
        _width = width;
        _height = height;
        _rowHeight = rowHeight;
        _head = new Shelf(0, rowHeight);
    }

    public bool TryInsert(int w, int h, out int u, out int v)
    {
        u = 0;
        v = 0;

        // Add 1 pixel padding on all sides to prevent atlas bleeding with linear filtering
        const int pad = 1;
        int paddedW = w + pad;
        int paddedH = h + pad;

        if (paddedH > _rowHeight)
        {
            return false;
        }

        for (var shelf = _head; shelf != null; shelf = shelf.Next)
        {
            if (paddedH > shelf.Height)
                continue;
            if (shelf.X + paddedW <= _width)
            {
                u = shelf.X;
                v = shelf.Y;
                shelf.X += paddedW;
                return true;
            }
        }

        int newY = GetLastShelfY() + _rowHeight;
        if (newY + _rowHeight > _height)
            return false;

        var newShelf = new Shelf(newY, _rowHeight);
        AddShelf(newShelf);
        u = 0;
        v = newY;
        newShelf.X = paddedW;
        return true;
    }

    public void Reset()
    {
        _head = new Shelf(0, _rowHeight);
    }

    private int GetLastShelfY()
    {
        var shelf = _head;
        while (shelf?.Next != null)
            shelf = shelf.Next;
        return shelf?.Y ?? 0;
    }

    private void AddShelf(Shelf newShelf)
    {
        var shelf = _head;
        while (shelf?.Next != null)
            shelf = shelf.Next;
        if (shelf != null)
            shelf.Next = newShelf;
        else
            _head = newShelf;
    }
}