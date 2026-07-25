using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Etch.ClipBlendGradient;

public static class ClipComposer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte IntersectAlpha(byte a, byte b)
    {
        int product = a * b + 128;
        return (byte)((product + (product >> 8)) >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte DifferenceAlpha(byte bg, byte fg)
    {
        return (byte)((bg * (255 - fg) + 127) >> 8);
    }

    public static ClipMaskBuffer Intersect(ReadOnlySpan<ClipMaskBuffer> stack)
    {
        if (stack.Length == 0)
            return new ClipMaskBuffer(Array.Empty<ClipStrip>(), [0], Array.Empty<byte>(), 0);

        if (stack.Length == 1)
            return stack[0];

        var result = stack[0];
        for (int i = 1; i < stack.Length; i++)
        {
            result = ComposeIntersect(result, stack[i]);
        }
        return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static ClipMaskBuffer ApplyDifference(ClipMaskBuffer bg, ClipMaskBuffer fg)
    {
        if (bg.StripCount == 0)
            return bg;
        if (fg.StripCount == 0)
            return bg;

        if (HasSameLayout(bg, fg))
            return ComposeDifferenceSameLayout(bg, fg);

        return ComposeDifferenceGeneral(bg, fg);
    }

    private static ClipMaskBuffer ComposeIntersect(ClipMaskBuffer a, ClipMaskBuffer b)
    {
        if (a.StripCount == 0)
            return a;
        if (b.StripCount == 0)
            return b;

        if (HasSameLayout(a, b))
            return ComposeIntersectSameLayout(a, b);

        return ComposeIntersectGeneral(a, b);
    }

    private static bool HasSameLayout(ClipMaskBuffer a, ClipMaskBuffer b)
    {
        if (a.StripCount != b.StripCount)
            return false;
        if (a.TileCount != b.TileCount)
            return false;

        var aStrips = a.Strips;
        var bStrips = b.Strips;
        for (int i = 0; i < aStrips.Length; i++)
        {
            if (aStrips[i].RowMask != bStrips[i].RowMask ||
                aStrips[i].X0 != bStrips[i].X0 ||
                aStrips[i].X1 != bStrips[i].X1)
                return false;
        }
        return true;
    }

    private static ClipMaskBuffer ComposeIntersectSameLayout(ClipMaskBuffer a, ClipMaskBuffer b)
    {
        int tileCount = a.TileCount;
        int totalStrips = a.StripCount;
        var resultStrips = new ClipStrip[totalStrips];
        var resultCoverage = new byte[Math.Max(a.CoverageBytes.Length, b.CoverageBytes.Length)];
        int resultCoverageUsed = 0;

        var tileOffsets = new int[tileCount + 1];
        int stripIndex = 0;

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            tileOffsets[tileIndex] = stripIndex;
            var aStripsForTile = a.StripsForTile(tileIndex);
            var bStripsForTile = b.StripsForTile(tileIndex);

            int localStripCount = aStripsForTile.Length;
            for (int i = 0; i < localStripCount; i++)
            {
                ref readonly var aStrip = ref aStripsForTile[i];
                ref readonly var bStrip = ref bStripsForTile[i];
                var aCov = a.CoverageForStrip(in aStrip);
                var bCov = b.CoverageForStrip(in bStrip);

                int length = aCov.Length;
                int offset = resultCoverageUsed;
                resultCoverageUsed += length;

                for (int j = 0; j < length; j++)
                {
                    resultCoverage[offset + j] = IntersectAlpha(aCov[j], bCov[j]);
                }

                resultStrips[stripIndex] = new ClipStrip(aStrip.RowMask, aStrip.X0, aStrip.X1, (uint)offset);
                stripIndex++;
            }
        }

        tileOffsets[tileCount] = stripIndex;

        Array.Resize(ref resultStrips, stripIndex);
        Array.Resize(ref resultCoverage, resultCoverageUsed);

        return new ClipMaskBuffer(resultStrips, tileOffsets, resultCoverage, tileCount);
    }

    private static ClipMaskBuffer ComposeIntersectGeneral(ClipMaskBuffer a, ClipMaskBuffer b)
    {
        int tileCount = Math.Max(a.TileCount, b.TileCount);
        var resultCoverage = new byte[Math.Max(a.CoverageBytes.Length, b.CoverageBytes.Length) / 2];
        int resultCoverageUsed = 0;

        var resultStrips = new ClipStrip[Math.Max(a.StripCount, b.StripCount)];
        var tileOffsets = new int[tileCount + 1];
        int stripIndex = 0;

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            tileOffsets[tileIndex] = stripIndex;

            var aStripsForTile = a.StripsForTile(tileIndex);
            var bStripsForTile = b.StripsForTile(tileIndex);

            int aIdx = 0, bIdx = 0;
            while (aIdx < aStripsForTile.Length && bIdx < bStripsForTile.Length)
            {
                ref readonly var aStrip = ref aStripsForTile[aIdx];
                ref readonly var bStrip = ref bStripsForTile[bIdx];

                if (aStrip.RowMask < bStrip.RowMask || (aStrip.RowMask == bStrip.RowMask && aStrip.X0 < bStrip.X0))
                {
                    var cov = a.CoverageForStrip(in aStrip);
                    int offset = resultCoverageUsed;
                    resultCoverageUsed += cov.Length;
                    for (int i = 0; i < cov.Length; i++)
                        resultCoverage[offset + i] = cov[i];
                    resultStrips[stripIndex++] = new ClipStrip(aStrip.RowMask, aStrip.X0, aStrip.X1, (uint)offset);
                    aIdx++;
                }
                else if (bStrip.RowMask < aStrip.RowMask || (bStrip.RowMask == aStrip.RowMask && bStrip.X0 < aStrip.X0))
                {
                    var cov = b.CoverageForStrip(in bStrip);
                    int offset = resultCoverageUsed;
                    resultCoverageUsed += cov.Length;
                    for (int i = 0; i < cov.Length; i++)
                        resultCoverage[offset + i] = cov[i];
                    resultStrips[stripIndex++] = new ClipStrip(bStrip.RowMask, bStrip.X0, bStrip.X1, (uint)offset);
                    bIdx++;
                }
                else
                {
                    var aCov = a.CoverageForStrip(in aStrip);
                    var bCov = b.CoverageForStrip(in bStrip);
                    int length = Math.Min(aCov.Length, bCov.Length);
                    int offset = resultCoverageUsed;
                    resultCoverageUsed += length;
                    for (int i = 0; i < length; i++)
                        resultCoverage[offset + i] = IntersectAlpha(aCov[i], bCov[i]);
                    resultStrips[stripIndex++] = new ClipStrip(aStrip.RowMask, aStrip.X0, aStrip.X1, (uint)offset);
                    aIdx++;
                    bIdx++;
                }
            }

            while (aIdx < aStripsForTile.Length)
            {
                ref readonly var aStrip = ref aStripsForTile[aIdx++];
                var cov = a.CoverageForStrip(in aStrip);
                int offset = resultCoverageUsed;
                resultCoverageUsed += cov.Length;
                for (int i = 0; i < cov.Length; i++)
                    resultCoverage[offset + i] = cov[i];
                resultStrips[stripIndex++] = new ClipStrip(aStrip.RowMask, aStrip.X0, aStrip.X1, (uint)offset);
            }

            while (bIdx < bStripsForTile.Length)
            {
                ref readonly var bStrip = ref bStripsForTile[bIdx++];
                var cov = b.CoverageForStrip(in bStrip);
                int offset = resultCoverageUsed;
                resultCoverageUsed += cov.Length;
                for (int i = 0; i < cov.Length; i++)
                    resultCoverage[offset + i] = cov[i];
                resultStrips[stripIndex++] = new ClipStrip(bStrip.RowMask, bStrip.X0, bStrip.X1, (uint)offset);
            }
        }

        tileOffsets[tileCount] = stripIndex;

        Array.Resize(ref resultStrips, stripIndex);
        Array.Resize(ref resultCoverage, resultCoverageUsed);

        return new ClipMaskBuffer(resultStrips, tileOffsets, resultCoverage, tileCount);
    }

    private static ClipMaskBuffer ComposeDifferenceSameLayout(ClipMaskBuffer bg, ClipMaskBuffer fg)
    {
        int tileCount = bg.TileCount;
        var bgStrips = bg.Strips;
        var fgStrips = fg.Strips;

        var resultCoverage = new byte[bg.CoverageBytes.Length];
        int resultCoverageUsed = 0;

        var resultStrips = new ClipStrip[bg.StripCount];
        var tileOffsets = new int[tileCount + 1];
        int stripIndex = 0;

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            tileOffsets[tileIndex] = stripIndex;
            var stripsForTile = bg.StripsForTile(tileIndex);

            for (int i = 0; i < stripsForTile.Length; i++)
            {
                ref readonly var strip = ref stripsForTile[i];
                var bgCov = bg.CoverageForStrip(in bgStrips[stripIndex]);
                var fgCov = fg.CoverageForStrip(in fgStrips[stripIndex]);

                int length = bgCov.Length;
                int offset = resultCoverageUsed;
                resultCoverageUsed += length;

                for (int j = 0; j < length; j++)
                {
                    resultCoverage[offset + j] = DifferenceAlpha(bgCov[j], fgCov[j]);
                }

                resultStrips[stripIndex] = new ClipStrip(strip.RowMask, strip.X0, strip.X1, (uint)offset);
                stripIndex++;
            }
        }

        tileOffsets[tileCount] = stripIndex;

        Array.Resize(ref resultStrips, stripIndex);
        Array.Resize(ref resultCoverage, resultCoverageUsed);

        return new ClipMaskBuffer(resultStrips, tileOffsets, resultCoverage, tileCount);
    }

    private static ClipMaskBuffer ComposeDifferenceGeneral(ClipMaskBuffer bg, ClipMaskBuffer fg)
    {
        int tileCount = Math.Max(bg.TileCount, fg.TileCount);
        var resultCoverage = new byte[bg.CoverageBytes.Length];
        int resultCoverageUsed = 0;

        var resultStrips = new ClipStrip[bg.StripCount];
        var tileOffsets = new int[tileCount + 1];
        int stripIndex = 0;

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            tileOffsets[tileIndex] = stripIndex;

            var bgStripsForTile = bg.StripsForTile(tileIndex);
            var fgStripsForTile = fg.StripsForTile(tileIndex);

            int bgIdx = 0, fgIdx = 0;
            while (bgIdx < bgStripsForTile.Length)
            {
                ref readonly var bgStrip = ref bgStripsForTile[bgIdx];
                var bgCov = bg.CoverageForStrip(in bgStrip);

                byte fgAlpha = 0;
                if (fgIdx < fgStripsForTile.Length)
                {
                    ref readonly var fgStrip = ref fgStripsForTile[fgIdx];
                    if (fgStrip.RowMask == bgStrip.RowMask && fgStrip.X0 == bgStrip.X0 && fgStrip.X1 == bgStrip.X1)
                    {
                        var fgCov = fg.CoverageForStrip(in fgStrip);
                        fgAlpha = fgCov.Length > 0 ? fgCov[0] : (byte)0;
                        fgIdx++;
                    }
                }

                int offset = resultCoverageUsed;
                resultCoverageUsed += bgCov.Length;
                for (int i = 0; i < bgCov.Length; i++)
                    resultCoverage[offset + i] = DifferenceAlpha(bgCov[i], fgAlpha);

                resultStrips[stripIndex++] = new ClipStrip(bgStrip.RowMask, bgStrip.X0, bgStrip.X1, (uint)offset);
                bgIdx++;
            }
        }

        tileOffsets[tileCount] = stripIndex;

        Array.Resize(ref resultStrips, stripIndex);
        Array.Resize(ref resultCoverage, resultCoverageUsed);

        return new ClipMaskBuffer(resultStrips, tileOffsets, resultCoverage, tileCount);
    }
}
