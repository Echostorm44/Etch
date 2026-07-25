using Etch.Text.Unicode.Minimal;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class BidiAlgorithmTests
{
    // ------------------------------------------------------------------
    // Empty & single-script
    // ------------------------------------------------------------------
    [Test]
    public async Task EmptyString_ReturnsSingleLtrRun()
    {
        var result = BidiAlgorithm.Analyze(string.Empty);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        await Assert.That(result.Runs.Length).IsEqualTo(0);
    }

    [Test]
    public async Task PureLtr_ProducesOneRun()
    {
        var result = BidiAlgorithm.Analyze("Hello");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Start).IsEqualTo(0);
        await Assert.That(result.Runs[0].Length).IsEqualTo(5);
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)0);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
    }

    [Test]
    public async Task PureHebrew_ProducesOneRtlRun()
    {
        var result = BidiAlgorithm.Analyze("\u05E9\u05DC\u05D5\u05DD"); // "shalom"
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Start).IsEqualTo(0);
        await Assert.That(result.Runs[0].Length).IsEqualTo(4);
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)1);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    [Test]
    public async Task PureArabic_ProducesOneRtlRun()
    {
        var result = BidiAlgorithm.Analyze("\u0645\u0631\u062D\u0628\u0627"); // "marhaba"
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)1);
    }

    // ------------------------------------------------------------------
    // Auto paragraph level
    // ------------------------------------------------------------------
    [Test]
    public async Task AutoLevel_LeadingLtr_ReturnsLtr()
    {
        var result = BidiAlgorithm.Analyze("abc \u05D0");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
    }

    [Test]
    public async Task AutoLevel_LeadingRtl_ReturnsRtl()
    {
        var result = BidiAlgorithm.Analyze("\u05D0 abc");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
    }

    [Test]
    public async Task AutoLevel_OnlyNeutral_ReturnsLtr()
    {
        var result = BidiAlgorithm.Analyze("123 + =");
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
    }

    // ------------------------------------------------------------------
    // Explicit paragraph level
    // ------------------------------------------------------------------
    [Test]
    public async Task ExplicitRtl_LtrText_ReturnsRtlLevel()
    {
        var result = BidiAlgorithm.Analyze("Hello", paragraphLevel: 1);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)2); // L inside RTL → even stays even
    }

    [Test]
    public async Task ExplicitLtr_RtlText_ReturnsLtrLevel()
    {
        var result = BidiAlgorithm.Analyze("\u05E9\u05DC\u05D5\u05DD", paragraphLevel: 0);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)1); // R inside LTR → odd
    }

    // ------------------------------------------------------------------
    // Mixed LTR + RTL
    // ------------------------------------------------------------------
    [Test]
    public async Task LtrThenRtl_ProducesTwoRuns()
    {
        var result = BidiAlgorithm.Analyze("abc \u05D0\u05D1");
        await Assert.That(result.Runs.Length).IsEqualTo(2);
        // Visual order in LTR para: LTR run first, then RTL run
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    [Test]
    public async Task RtlThenLtr_ProducesTwoRuns()
    {
        var result = BidiAlgorithm.Analyze("\u05D0\u05D1 abc");
        await Assert.That(result.Runs.Length).IsEqualTo(2);
        // Visual order in RTL para: LTR text appears before RTL text visually
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    [Test]
    public async Task LtrRtlLtr_ProducesThreeRuns()
    {
        var result = BidiAlgorithm.Analyze("abc \u05D0\u05D1 def");
        await Assert.That(result.Runs.Length).IsEqualTo(3);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr); // "abc "
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Rtl); // "\u05D0\u05D1"
        await Assert.That(result.Runs[2].Direction).IsEqualTo(BidiRunDirection.Ltr); // " def"
    }

    [Test]
    public async Task RtlLtrRtl_ProducesThreeRuns()
    {
        var result = BidiAlgorithm.Analyze("\u05D0\u05D1 abc \u05D2\u05D3");
        await Assert.That(result.Runs.Length).IsEqualTo(3);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Rtl); // last RTL
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Ltr); // " abc "
        await Assert.That(result.Runs[2].Direction).IsEqualTo(BidiRunDirection.Rtl); // first RTL
    }

    // ------------------------------------------------------------------
    // L2 Visual reordering
    // ------------------------------------------------------------------
    [Test]
    public async Task RtlRun_ReordersToFront()
    {
        var text = "abc \u05D0\u05D1";
        var result = BidiAlgorithm.Analyze(text);
        // In LTR para, visual order is: LTR run (starts 0, len 4), RTL run (starts 4, len 2)
        await Assert.That(result.Runs[0].Start).IsEqualTo(0);
        await Assert.That(result.Runs[0].Length).IsEqualTo(4);
        await Assert.That(result.Runs[1].Start).IsEqualTo(4);
        await Assert.That(result.Runs[1].Length).IsEqualTo(2);
    }

    [Test]
    public async Task RtlPara_ReordersLtrToBack()
    {
        var text = "\u05D0\u05D1 abc";
        var result = BidiAlgorithm.Analyze(text);
        // In RTL para, visual order: LTR text first (reordered to front), then RTL text
        // The neutral space is resolved to RTL, so it joins the RTL run.
        await Assert.That(result.Runs[0].Start).IsEqualTo(3);
        await Assert.That(result.Runs[0].Length).IsEqualTo(3);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[1].Start).IsEqualTo(0);
        await Assert.That(result.Runs[1].Length).IsEqualTo(3);
        await Assert.That(result.Runs[1].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    // ------------------------------------------------------------------
    // L1 Trailing whitespace
    // ------------------------------------------------------------------
    [Test]
    public async Task TrailingSpace_InLtrPara_GetsLevel0()
    {
        var result = BidiAlgorithm.Analyze("abc ");
        var lastRun = result.Runs[^1];
        await Assert.That(lastRun.Level).IsEqualTo((byte)0);
    }

    [Test]
    public async Task TrailingSpace_InRtlPara_GetsLevel1()
    {
        var result = BidiAlgorithm.Analyze("\u05D0\u05D1 ");
        var lastRun = result.Runs[^1];
        await Assert.That(lastRun.Level).IsEqualTo((byte)1);
    }

    // ------------------------------------------------------------------
    // Explicit embeddings
    // ------------------------------------------------------------------
    [Test]
    public async Task LreInsideRtl_ProducesEmbeddedLtrRun()
    {
        // RLE + LRE + abc + PDF + PDF
        // No strong character at start, so paragraph level auto-detects as 0.
        var text = "\u202B\u202Aabc\u202C\u202C";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        // The embedded LTR block should have an even level >= 2
        var ltrRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr);
        await Assert.That(ltrRun.Level).IsEqualTo((byte)2);
    }

    [Test]
    public async Task RleInsideLtr_ProducesEmbeddedRtlRun()
    {
        // LRE + RLE + Hebrew + PDF + PDF
        // Hebrew is strong RTL, so paragraph level auto-detects as 1.
        var text = "\u202A\u202B\u05D0\u05D1\u202C\u202C";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        var rtlRun = result.Runs.First(r => r.Direction == BidiRunDirection.Rtl);
        await Assert.That(rtlRun.Level).IsGreaterThanOrEqualTo((byte)1);
    }

    // ------------------------------------------------------------------
    // Explicit overrides
    // ------------------------------------------------------------------
    [Test]
    public async Task LroInsideRtl_ForcesLtrDirection()
    {
        // RLE + LRO + abc + PDF + PDF
        // No strong character at start, so paragraph level auto-detects as 0.
        var text = "\u202B\u202Dabc\u202C\u202C";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        var ltrRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr);
        await Assert.That(ltrRun.Level).IsEqualTo((byte)2);
    }

    // ------------------------------------------------------------------
    // Weak types — numbers
    // ------------------------------------------------------------------
    [Test]
    public async Task EuropeanNumbers_InLtr_AreLtr()
    {
        var result = BidiAlgorithm.Analyze("123");
        // EN on even level gets +2 per I1, but direction stays LTR (even level)
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)2);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
    }

    [Test]
    public async Task EuropeanNumbers_InRtl_AreRtl()
    {
        var result = BidiAlgorithm.Analyze("\u05D0 123");
        var numRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr && r.Length == 3);
        // EN on odd level gets +1 per I2
        await Assert.That(numRun.Level).IsEqualTo((byte)2);
    }

    [Test]
    public async Task ArabicNumbers_TreatedAsWeak()
    {
        var result = BidiAlgorithm.Analyze("\u0661\u0662\u0663"); // Arabic-Indic digits
        // AN on even level gets +2 per I1, direction stays LTR (even)
        await Assert.That(result.Runs[0].Level).IsEqualTo((byte)2);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
    }

    [Test]
    public async Task ArabicNumbers_InRtl_AreRtl()
    {
        var result = BidiAlgorithm.Analyze("\u05D0 \u0661\u0662");
        var numRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr && r.Length == 2);
        await Assert.That(numRun.Level).IsEqualTo((byte)2);
    }

    // ------------------------------------------------------------------
    // Neutrals between strongs
    // ------------------------------------------------------------------
    [Test]
    public async Task NeutralBetweenLtr_AreLtr()
    {
        var result = BidiAlgorithm.Analyze("a-b");
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
    }

    [Test]
    public async Task NeutralBetweenRtl_AreRtl()
    {
        var result = BidiAlgorithm.Analyze("\u05D0-\u05D1");
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }

    [Test]
    public async Task NeutralBetweenLtrAndRtl_TakesEmbeddingDir()
    {
        // In LTR para, space between L and R → resolved to L (N2)
        // The space is merged into the leading LTR run.
        var result = BidiAlgorithm.Analyze("a \u05D0");
        await Assert.That(result.Runs.Length).IsEqualTo(2);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[0].Start).IsEqualTo(0);
        await Assert.That(result.Runs[0].Length).IsEqualTo(2);
    }

    // ------------------------------------------------------------------
    // Isolates
    // ------------------------------------------------------------------
    [Test]
    public async Task Lri_IsolatesLtrInsideRtl()
    {
        // Hebrew + LRI + abc + PDI
        var text = "\u05D0\u2066abc\u2069";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        var ltrRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr);
        // LRI itself is included in the LTR run
        await Assert.That(ltrRun.Level).IsEqualTo((byte)2);
        await Assert.That(ltrRun.Length).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task Fsi_AutoLtrInsideRtl()
    {
        // FSI containing LTR text inside RTL para → should be LTR isolate
        var text = "\u05D0\u2068abc\u2069";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)1);
        var ltrRun = result.Runs.First(r => r.Direction == BidiRunDirection.Ltr);
        await Assert.That(ltrRun.Level).IsEqualTo((byte)2);
    }

    [Test]
    public async Task Fsi_AutoRtlInsideLtr()
    {
        // FSI containing RTL text inside LTR para → should be RTL isolate
        var text = "a\u2068\u05D0\u05D1\u2069";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        var rtlRun = result.Runs.First(r => r.Direction == BidiRunDirection.Rtl);
        await Assert.That(rtlRun.Level).IsEqualTo((byte)1);
    }

    // ------------------------------------------------------------------
    // Stress / edge cases
    // ------------------------------------------------------------------
    [Test]
    public async Task LongLtrString_SingleRun()
    {
        var text = new string('x', 500);
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.Runs.Length).IsEqualTo(1);
        await Assert.That(result.Runs[0].Length).IsEqualTo(500);
    }

    [Test]
    public async Task MixedLtrRtlHebrewArabic()
    {
        // "hello שלום مرحبا"
        var text = "hello \u05E9\u05DC\u05D5\u05DD \u0645\u0631\u062D\u0628\u0627";
        var result = BidiAlgorithm.Analyze(text);
        await Assert.That(result.ParagraphLevel).IsEqualTo((byte)0);
        // Hebrew and Arabic are both RTL; they merge into one reversed block
        await Assert.That(result.Runs.Length).IsEqualTo(2);
        await Assert.That(result.Runs[0].Direction).IsEqualTo(BidiRunDirection.Ltr);
        await Assert.That(result.Runs[^1].Direction).IsEqualTo(BidiRunDirection.Rtl);
    }
}
