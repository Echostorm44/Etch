using ICU4N.Text;
using Etch.Text.Unicode.Minimal;

namespace Etch.Text.Unicode;

/// <summary>
/// Process-global ICU4N initialisation and health checks.
/// Call <see cref="Ensure"/> once before using any ICU4N-dependent API
/// (line-breaking, BiDi, etc.).
/// </summary>
public static class UnicodeData
{
    private static bool _initialized;

    /// <summary>
    /// Verifies that ICU4N data is loaded and functional.
    /// Panics with <see cref="PanicCodes.IcuDataMissing"/> if not.
    /// Safe to call from multiple threads; idempotent.
    /// </summary>
    public static void Ensure()
    {
        if (_initialized)
            return;

        try
        {
            // Touch a data-backed API to force-load the ICU data tables.
            // BreakIterator is the lightest-weight probe that still exercises
            // the full data-loading path.
            var probe = BreakIterator.GetLineInstance(System.Globalization.CultureInfo.InvariantCulture);
            probe.SetText("hello world");
            probe.First();
        }
        catch (System.TypeInitializationException ex)
        {
            Panic.Invariant(PanicCodes.IcuDataMissing,
                "ICU4N data could not be loaded: " + ex.Message);
        }

        _initialized = true;
    }

    /// <summary>
    /// Analyse a single paragraph and return visual-order BiDi runs.
    /// Uses our own UAX #9 implementation because ICU4N does not expose
    /// a public BiDi class.
    /// </summary>
    /// <param name="text">Paragraph text.</param>
    /// <param name="paragraphLevel">0 = LTR, 1 = RTL, -1 = auto-detect.</param>
    public static BidiParagraphResult GetParagraphRuns(ReadOnlySpan<char> text, sbyte paragraphLevel = -1)
        => BidiAlgorithm.Analyze(text, paragraphLevel);
}
