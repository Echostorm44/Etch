using System;
using System.Collections.Generic;
using System.Globalization;
using ICU4N.Text;

namespace Etch.Text.Unicode;

#pragma warning disable CA1028
/// <summary>
/// The kind of line break returned by <see cref="LineBreakIterator"/>.
/// </summary>
public enum LineBreakKind : byte
{
    /// <summary>A soft break — text may wrap here.</summary>
    Soft = 0,
    /// <summary>A hard break — forced line ending (\n, \r\n, LS, PS).</summary>
    Hard = 1,
}
#pragma warning restore CA1028

/// <summary>
/// Streaming, zero-allocation-per-iteration line-break enumerator.
/// Wraps ICU4N <see cref="BreakIterator.GetLineInstance(CultureInfo)"/> with per-locale,
/// thread-local instance caching.
/// </summary>
public ref struct LineBreakIterator
{
    // Thread-local cache: one BreakIterator per locale per thread.
    // BreakIterator is NOT thread-safe, so ThreadLocal is required.
    private static readonly ThreadLocal<Dictionary<string, BreakIterator>> s_iterators = new(
        () => new Dictionary<string, BreakIterator>(capacity: 4),
        trackAllValues: false);

    private readonly BreakIterator _iterator;
    private readonly string _text;
    private bool _started;

    /// <summary>
    /// Create an iterator over <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Text to scan for line-break opportunities.</param>
    /// <param name="locale">BCP-47 locale tag (e.g. "en", "ja").</param>
    public LineBreakIterator(ReadOnlySpan<char> text, string locale = "en")
    {
        _text = text.ToString();
        _iterator = GetOrCreateIterator(locale);
        _iterator.SetText(_text);
        _started = false;
    }

    /// <summary>
    /// Advance to the next break boundary.
    /// </summary>
    /// <param name="index">Character index of the boundary (0..Length).</param>
    /// <param name="kind">Soft or hard break.</param>
    /// <returns><c>true</c> if a boundary was found; <c>false</c> when exhausted.</returns>
    public bool MoveNext(out int index, out LineBreakKind kind)
    {
        int boundary;
        if (!_started)
        {
            // First() initialises the iterator and returns the first boundary
            // (always 0). We discard it because the start of text is not a
            // meaningful break point.
            _ = _iterator.First();
            boundary = _iterator.Next();
            _started = true;
        }
        else
        {
            boundary = _iterator.Next();
        }

        if (boundary == BreakIterator.Done)
        {
            index = 0;
            kind = default;
            return false;
        }

        index = boundary;
        kind = IsHardBreak(_text, boundary) ? LineBreakKind.Hard : LineBreakKind.Soft;
        return true;
    }

    private static BreakIterator GetOrCreateIterator(string locale)
    {
        var dict = s_iterators.Value!;
        if (!dict.TryGetValue(locale, out var iter))
        {
            var ci = new CultureInfo(locale);
            iter = BreakIterator.GetLineInstance(ci);
            dict[locale] = iter;
        }
        return iter;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsHardBreak(string text, int index)
    {
        if (index <= 0 || index > text.Length)
            return false;

        char c = text[index - 1];
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }
}
