using Etch.Text.Unicode;
using Etch.Text.Unicode.Minimal;
using ICU4N.Text;
using System.Globalization;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class UnicodeDataTests
{
    [Test]
    public async Task Ensure_DoesNotThrow()
    {
        UnicodeData.Ensure();
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task LineBreak_HelloWorld_BreaksAtSpace()
    {
        UnicodeData.Ensure();

        var iterator = BreakIterator.GetLineInstance(CultureInfo.InvariantCulture);
        iterator.SetText("hello world");

        int firstBreak = iterator.First();
        int nextBreak = iterator.Next();

        // Line break occurs at or near the space between "hello" and "world"
        await Assert.That(firstBreak).IsEqualTo(0);
        await Assert.That(nextBreak).IsGreaterThanOrEqualTo(4);
        await Assert.That(nextBreak).IsLessThanOrEqualTo(6);
    }

    [Test]
    public async Task GetParagraphRuns_LtrText_ReturnsOneLtrRun()
    {
        var result = UnicodeData.GetParagraphRuns("Hello");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
    }

    [Test]
    public async Task GetParagraphRuns_RtlText_ReturnsOneRtlRun()
    {
        var result = UnicodeData.GetParagraphRuns("\u05E9\u05DC\u05D5\u05DD");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    [Test]
    public async Task GetParagraphRuns_Mixed_ReturnsMultipleRuns()
    {
        var result = UnicodeData.GetParagraphRuns("abc \u05D0\u05D1");
        await Assert.That(result.Runs.Length).IsEqualTo(2);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }
}
