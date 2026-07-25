using System;

namespace Etch.Strokes;

public readonly struct DashPattern
{
    public readonly float[] Segments;
    public readonly float Phase;
    public readonly bool ResetOnMove;

    public DashPattern(float[] segments, float phase = 0f, bool resetOnMove = true)
    {
        if (segments == null) Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "segments must not be null");
        if (segments.Length == 0) Etch.Panic.Invariant(Etch.PanicCodes.DegenerateDashPattern, "DashPattern Segments cannot be empty");

        if (segments.Length % 2 == 1)
        {
            float[] doubled = new float[segments.Length * 2];
            Array.Copy(segments, doubled, segments.Length);
            Array.Copy(segments, 0, doubled, segments.Length, segments.Length);
            Segments = doubled;
        }
        else
        {
            Segments = segments;
        }

        float sum = 0f;
        foreach (float seg in Segments)
        {
            sum += seg;
        }

        if (sum < 1e-10f)
        {
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateDashPattern, "DashPattern Segments sum cannot be zero");
        }

        Phase = phase;
        ResetOnMove = resetOnMove;
    }

    public int SegmentCount => Segments.Length;

    public float TotalLength()
    {
        float sum = 0f;
        foreach (float seg in Segments)
        {
            sum += seg;
        }
        return sum;
    }

    public float PhasePosition(float position)
    {
        float total = TotalLength();
        if (total < 1e-10f) return 0f;

        float p = position % total;
        if (p < 0) p += total;

        float offset = Phase % total;
        if (offset < 0) offset += total;

        float adjusted = p + offset;
        if (adjusted > total) adjusted -= total;

        return adjusted;
    }

    public bool IsOnSegment(float positionInCycle)
    {
        float pos = positionInCycle;
        for (int i = 0; i < Segments.Length; i++)
        {
            if (pos < Segments[i]) return i % 2 == 0;
            pos -= Segments[i];
        }
        return false;
    }
}