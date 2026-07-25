using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Etch.Geometry.Differential;

internal ref struct DifferentialRunner
{
    private readonly string _propertyName;
    private readonly string? _rejectReason;
    private string? _failMessage;

    public DifferentialRunner(string propertyName, string? rejectReason = null)
    {
        _propertyName = propertyName;
        _rejectReason = rejectReason;
    }

    public bool Run<T>(
        int iterations,
        Func<System.Random, T> generate,
        Func<T, bool> property,
        Action<T, Exception>? onError = null)
    {
        var seed = ComputeSeed(_propertyName);
        var rng = new System.Random(seed);

        for (int i = 0; i < iterations; i++)
        {
            T input = generate(rng);
            if (_rejectReason != null && IsRejectable<T>(input, _rejectReason))
                continue;

            try
            {
                if (!property(input))
                {
                    var (shrunk, attempts) = Shrink(input, property, 5);
                    _failMessage = $"Property failed after {i + 1} attempts (shrunk in {attempts} steps). Input: {Format(input)}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _failMessage = $"Property threw {ex.GetType().Name}: {ex.Message}";
                onError?.Invoke(input, ex);
                return false;
            }
        }
        return true;
    }

    public string? FailMessage => _failMessage;

    private static bool IsRejectable<T>(T input, string reason)
    {
        return reason.Contains("singular") && IsSingular(input);
    }

    private static bool IsSingular<T>(T input)
    {
        if (input is Etch.Geometry.Affine a)
            return Math.Abs(a.M00 * a.M11 - a.M01 * a.M10) < 1e-10;
        return false;
    }

    private static int ComputeSeed(string name)
    {
        int hash = name.GetHashCode();
        return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
    }

    private static (T Result, int Attempts) Shrink<T>(T input, Func<T, bool> property, int maxAttempts)
    {
        int attempts = 0;
        T current = input;
        for (int i = 0; i < maxAttempts && attempts < maxAttempts; i++)
        {
            T shrunk = HalveMagnitude(input, i + 1);
            if (property(shrunk))
            {
                current = shrunk;
                attempts++;
            }
            else
            {
                break;
            }
        }
        return (current, attempts);
    }

    private static T HalveMagnitude<T>(T input, int depth)
    {
        if (input is Etch.Geometry.Affine a)
        {
            double scale = Math.Pow(0.5, depth);
            return (T)(object)new Etch.Geometry.Affine(
                1 + (a.M00 - 1) * scale,
                a.M01 * scale,
                a.M10 * scale,
                1 + (a.M11 - 1) * scale,
                a.M02 * scale,
                a.M12 * scale);
        }
        return input;
    }

    private static string Format<T>(T input)
    {
        if (input is Etch.Geometry.Affine a)
            return $"Affine({a.M00:F6},{a.M01:F6},{a.M10:F6},{a.M11:F6},{a.M02:F6},{a.M12:F6})";
        return input?.ToString() ?? "null";
    }
}

internal static class RelativeComparer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NearlyEqual(double a, double b, double epsilon)
    {
        if (a == b) return true;
        double diff = Math.Abs(a - b);
        double scale = Math.Max(Math.Abs(a), Math.Abs(b));
        if (scale < 1e-100) scale = 1e-100;
        return diff / scale < epsilon;
    }
}
