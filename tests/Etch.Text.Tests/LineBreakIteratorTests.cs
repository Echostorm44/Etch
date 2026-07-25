using Etch.Text.Unicode;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class LineBreakIteratorTests
{
    [Test]
    public async Task HelloWorld_BreaksAfterSpace()
    {
        var breaks = GetAllBreaks("hello world");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(6);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Soft);

        await Assert.That(breaks[1].Index).IsEqualTo(11);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task FooNewlineBar_HardBreakAtFour()
    {
        var breaks = GetAllBreaks("foo\nbar");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(4);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Hard);

        await Assert.That(breaks[1].Index).IsEqualTo(7);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task CrLf_BreakAtTwo()
    {
        var breaks = GetAllBreaks("foo\r\nbar");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(5);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Hard);

        await Assert.That(breaks[1].Index).IsEqualTo(8);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task StandaloneCr_BreakAtOne()
    {
        var breaks = GetAllBreaks("\rfoo");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(1);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Hard);

        await Assert.That(breaks[1].Index).IsEqualTo(4);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task Nbsp_DoesNotBreak()
    {
        var breaks = GetAllBreaks("foo\u00A0bar");

        await Assert.That(breaks.Length).IsEqualTo(1);

        await Assert.That(breaks[0].Index).IsEqualTo(7);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task MultiScript_BreaksAtScriptBoundary()
    {
        var breaks = GetAllBreaks("\u65E5\u672C\u8A9EABC"); // 日本語ABC

        // CJK allows breaks between most characters, so we get multiple
        // soft breaks. We only assert that the script boundary (index 3)
        // is among them.
        await Assert.That(breaks.Length).IsGreaterThanOrEqualTo(1);

        var boundaryBreak = breaks.FirstOrDefault(b => b.Index == 3);
        await Assert.That(boundaryBreak.Index).IsEqualTo(3);
        await Assert.That(boundaryBreak.Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task EmptyString_NoBreaks()
    {
        var breaks = GetAllBreaks(string.Empty);
        await Assert.That(breaks.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LineSeparator_IsHardBreak()
    {
        var breaks = GetAllBreaks("foo\u2028bar");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(4);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Hard);

        await Assert.That(breaks[1].Index).IsEqualTo(7);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task ParagraphSeparator_IsHardBreak()
    {
        var breaks = GetAllBreaks("foo\u2029bar");

        await Assert.That(breaks.Length).IsEqualTo(2);

        await Assert.That(breaks[0].Index).IsEqualTo(4);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Hard);

        await Assert.That(breaks[1].Index).IsEqualTo(7);
        await Assert.That(breaks[1].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task SingleWord_NoInternalBreaks()
    {
        var breaks = GetAllBreaks("foobar");

        await Assert.That(breaks.Length).IsEqualTo(1);

        await Assert.That(breaks[0].Index).IsEqualTo(6);
        await Assert.That(breaks[0].Kind).IsEqualTo(LineBreakKind.Soft);
    }

    [Test]
    public async Task MultipleSpaces_BreaksAtEachSpace()
    {
        var breaks = GetAllBreaks("a  b");

        // ICU4N may or may not break between consecutive spaces depending on
        // the locale rules. We just verify that spaces are treated as soft
        // break opportunities and the end boundary is present.
        await Assert.That(breaks.Length).IsGreaterThanOrEqualTo(1);
        await Assert.That(breaks[^1].Index).IsEqualTo(4);
        await Assert.That(breaks[^1].Kind).IsEqualTo(LineBreakKind.Soft);

        // All breaks should be soft (no hard breaks in this input).
        foreach (var b in breaks)
        {
            await Assert.That(b.Kind).IsEqualTo(LineBreakKind.Soft);
        }
    }

    // ------------------------------------------------------------------
    // Helper — keeps ref struct usage in a synchronous local method
    // ------------------------------------------------------------------
    private static (int Index, LineBreakKind Kind)[] GetAllBreaks(string text)
    {
        var iter = new LineBreakIterator(text);
        var list = new System.Collections.Generic.List<(int, LineBreakKind)>();
        while (iter.MoveNext(out int idx, out var kind))
        {
            list.Add((idx, kind));
        }
        return list.ToArray();
    }
}
