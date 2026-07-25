namespace Etch.Text.Unicode.Minimal;

/// <summary>
/// The resolved direction of a BiDi run.
/// </summary>
#pragma warning disable CA1028
public enum BidiRunDirection : byte
{
    /// <summary>Left-to-right (even embedding level).</summary>
    Ltr = 0,
    /// <summary>Right-to-left (odd embedding level).</summary>
    Rtl = 1,
}
#pragma warning restore CA1028

/// <summary>
/// A single run of text with uniform direction.
/// </summary>
public readonly struct BidiRun
{
    /// <summary>Start index in the original string (inclusive).</summary>
    public int Start { get; }

    /// <summary>Length in characters.</summary>
    public int Length { get; }

    /// <summary>Embedding level (even = LTR, odd = RTL).</summary>
    public byte Level { get; }

    /// <summary>Resolved direction derived from level parity.</summary>
    public BidiRunDirection Direction => (Level & 1) == 0 ? BidiRunDirection.Ltr : BidiRunDirection.Rtl;

    public BidiRun(int start, int length, byte level)
    {
        Start = start;
        Length = length;
        Level = level;
    }

    public override string ToString() => $"Run[{Start}..{Start + Length}) level={Level}";
}

/// <summary>
/// Result of BiDi analysis on a paragraph.
/// </summary>
public readonly struct BidiParagraphResult
{
    /// <summary>Resolved paragraph embedding level (0 = LTR, 1 = RTL).</summary>
    public byte ParagraphLevel { get; }

    /// <summary>Visual-order runs, left-to-right in display order.</summary>
    public BidiRun[] Runs { get; }

    public BidiParagraphResult(byte paragraphLevel, BidiRun[] runs)
    {
        ParagraphLevel = paragraphLevel;
        Runs = runs;
    }
}
