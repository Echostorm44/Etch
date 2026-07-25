using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class DasherTests
{
    [Test]
    public async Task EmptyPatternPanics()
    {
        bool threw = false;
        try
        {
            var pattern = new DashPattern(Array.Empty<float>());
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.DegenerateDashPattern)
        {
            threw = true;
        }

        if (!threw)
            throw new InvalidOperationException("Empty dash pattern should panic with DegenerateDashPattern");
    }

    [Test]
    public async Task OddLengthPatternGetsDoubled()
    {
        var pattern = new DashPattern(new float[] { 10f, 5f, 3f });
        if (pattern.SegmentCount != 6)
            throw new InvalidOperationException("Odd-length pattern should be doubled to even length");
    }

    [Test]
    public async Task IsOnSegmentAlternates()
    {
        var pattern = new DashPattern(new float[] { 10f, 5f });
        if (!pattern.IsOnSegment(0f)) throw new InvalidOperationException("Position 0 should be in dash");
        if (!pattern.IsOnSegment(5f)) throw new InvalidOperationException("Position 5 should be in dash");
        if (pattern.IsOnSegment(10f)) throw new InvalidOperationException("Position 10 should be in gap");
        if (pattern.IsOnSegment(14f)) throw new InvalidOperationException("Position 14 should be in gap");
        if (pattern.IsOnSegment(15f)) throw new InvalidOperationException("Position 15 is beyond pattern (in gap)");
    }

    [Test]
    public async Task TotalLengthSum()
    {
        var pattern = new DashPattern(new float[] { 10f, 5f, 3f, 2f });
        if (Math.Abs(pattern.TotalLength() - 20f) > 0.001f)
            throw new InvalidOperationException("Total length should be 20 (doubled from odd input)");
    }

    [Test]
    public async Task PhasePositionWraps()
    {
        var pattern = new DashPattern(new float[] { 10f, 10f });
        float pos = pattern.PhasePosition(25f);
        if (Math.Abs(pos - 5f) > 0.001f)
            throw new InvalidOperationException("Phase position should wrap to 5");
    }

    [Test]
    public async Task PhaseShiftsDashes()
    {
        var pattern1 = new DashPattern(new float[] { 10f, 5f }, phase: 0f);
        var pattern2 = new DashPattern(new float[] { 10f, 5f }, phase: 5f);

        if (pattern1.IsOnSegment(0f) != pattern2.IsOnSegment(5f))
            throw new InvalidOperationException("Phase offset should shift dash positions");
    }
}