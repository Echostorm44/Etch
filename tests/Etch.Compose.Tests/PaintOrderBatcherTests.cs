namespace Etch.Compose.Tests;

/// <summary>
/// The batcher never reorders overlapping draws, keeps an ordinary UI frame to one batch per kind,
/// and orders each kind's elements stably by batch.
/// </summary>
internal sealed class PaintOrderBatcherTests
{
    private static readonly int[] ExpectedScatter = [1, 3, 2];

    [Test]
    public async Task DrawOverText_GoesAfterIt()
    {
        var batcher = new PaintOrderBatcher();
        int background = batcher.Place(DrawKind.Shape, 0, 0, 500, 500);
        int text = batcher.Place(DrawKind.Glyph, 10, 10, 100, 30);
        int panel = batcher.Place(DrawKind.Shape, 50, 0, 300, 300);
        int panelText = batcher.Place(DrawKind.Glyph, 60, 10, 200, 30);

        await Assert.That(background).IsEqualTo(0);
        await Assert.That(text).IsEqualTo(1);
        await Assert.That(panel).IsEqualTo(2);
        await Assert.That(panelText).IsEqualTo(3);
    }

    [Test]
    public async Task DrawsThatDoNotOverlapEarlierText_JoinTheFirstBatches()
    {
        // Backgrounds, then text and icons on them, interleaved row by row: still one batch of each.
        var batcher = new PaintOrderBatcher();
        batcher.Place(DrawKind.Shape, 0, 0, 500, 500);
        for (int row = 0; row < 10; row++)
        {
            float y = row * 40;
            await Assert.That(batcher.Place(DrawKind.Shape, 0, y, 500, y + 36)).IsEqualTo(0);
            await Assert.That(batcher.Place(DrawKind.Image, 4, y + 4, 32, y + 32)).IsEqualTo(1);
            await Assert.That(batcher.Place(DrawKind.Glyph, 40, y + 8, 300, y + 30)).IsEqualTo(2);
        }
        await Assert.That(batcher.Count).IsEqualTo(3);
    }

    [Test]
    public async Task FootprintIsNotOneUnion()
    {
        // A sidebar label and a header label: their union covers the content area, but a card
        // drawn there overlaps neither, so it must still join the first shape batch.
        var batcher = new PaintOrderBatcher();
        batcher.Place(DrawKind.Shape, 0, 0, 1000, 800);
        batcher.Place(DrawKind.Glyph, 10, 700, 150, 720);
        batcher.Place(DrawKind.Glyph, 800, 10, 990, 30);

        int card = batcher.Place(DrawKind.Shape, 300, 200, 600, 400);

        await Assert.That(card).IsEqualTo(0);
    }

    [Test]
    public async Task BlurNeverJoinsABatchItOverlaps()
    {
        // A blur samples a copy taken before its batch; joining the batch holding an earlier blur
        // it overlaps would hide that blur from it.
        var batcher = new PaintOrderBatcher();
        batcher.Place(DrawKind.Shape, 0, 0, 500, 500);
        int first = batcher.Place(DrawKind.Blur, 0, 0, 200, 200);
        int second = batcher.Place(DrawKind.Blur, 100, 100, 300, 300);
        int apart = batcher.Place(DrawKind.Blur, 400, 400, 450, 450);

        await Assert.That(second).IsGreaterThan(first);
        await Assert.That(apart).IsEqualTo(first);
    }

    [Test]
    public async Task Order_ReturnsSourceWhenAlreadyInOrder()
    {
        var batcher = new PaintOrderBatcher();
        var items = new List<DrawItem>();
        var source = new List<int> { 10, 11, 12 };
        var ordered = new List<int>();
        for (int i = 0; i < source.Count; i++)
        {
            batcher.Record(DrawKind.Shape, items, batcher.Place(DrawKind.Shape, i * 10, 0, i * 10 + 5, 5), i, 1);
        }
        batcher.AssignStarts();

        var result = batcher.Order(DrawKind.Shape, source, items, ordered);

        await Assert.That(ReferenceEquals(result, source)).IsTrue();
        await Assert.That(items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Order_ScattersStablyByBatch()
    {
        var batcher = new PaintOrderBatcher();
        var shapeItems = new List<DrawItem>();
        var glyphItems = new List<DrawItem>();
        var shapes = new List<int>();

        // shape 1 (batch 0), text over it (batch 1), shape 2 over the text (batch 2),
        // then shape 3 elsewhere — it joins batch 0, so the shape list is out of batch order.
        Add(DrawKind.Shape, shapeItems, shapes, 1, 0, 0, 100, 100);
        batcher.Record(DrawKind.Glyph, glyphItems, batcher.Place(DrawKind.Glyph, 10, 10, 50, 30), 0, 1);
        Add(DrawKind.Shape, shapeItems, shapes, 2, 0, 0, 60, 60);
        Add(DrawKind.Shape, shapeItems, shapes, 3, 200, 200, 300, 300);
        batcher.AssignStarts();

        var ordered = batcher.Order(DrawKind.Shape, shapes, shapeItems, new List<int>());

        await Assert.That(ordered.SequenceEqual(ExpectedScatter)).IsTrue();
        await Assert.That(batcher.Batches[2].Start).IsEqualTo(2);

        void Add(DrawKind kind, List<DrawItem> items, List<int> list, int name, float x0, float y0, float x1, float y1)
        {
            batcher.Record(kind, items, batcher.Place(kind, x0, y0, x1, y1), list.Count, 1);
            list.Add(name);
        }
    }

    [Test]
    public async Task DrawList_OnlyShapesFastPath_MatchesPerInstancePlacement()
    {
        // The fast path places a run of leading shapes as one item; it must produce the same batch
        // as placing them one by one would.
        var shapes = new ShapeInstance[]
        {
            new() { MinX = 0, MinY = 0, MaxX = 100, MaxY = 100 },
            new() { MinX = 10, MinY = 10, MaxX = 50, MaxY = 50, Expand = 1.5f },
        };
        var list = new DrawList();
        list.Reset(200, 200);
        list.AddShapes(shapes);
        list.Finish();

        await Assert.That(list.BatchCount).IsEqualTo(1);
        await Assert.That(list.Batches[0].Count).IsEqualTo(2);
        await Assert.That(list.Batches[0].MinX).IsEqualTo(0f);
        await Assert.That(list.Batches[0].MaxX).IsEqualTo(100f);
    }
}
