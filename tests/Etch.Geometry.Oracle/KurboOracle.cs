using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Etch.Geometry;
using Etch.Primitives;

namespace Etch.Geometry.Oracle;

public static unsafe partial class KurboOracle
{
    private const string LibName = "etch_kurbo_oracle";

    [LibraryImport(LibName, EntryPoint = "affine_compose")]
    private static partial void AffineComposeNative(double* a, double* b, double* outArr);

    [LibraryImport(LibName, EntryPoint = "affine_inverse")]
    private static partial void AffineInverseNative(double* a, double* outArr);

    [LibraryImport(LibName, EntryPoint = "point_transform")]
    private static partial void PointTransformNative(double* affine, double* pts, nuint count, double* outPts);

    [LibraryImport(LibName, EntryPoint = "cubic_eval")]
    private static partial void CubicEvalNative(double* cubic, double t, double startX, double startY, double* outPt);

    [LibraryImport(LibName, EntryPoint = "cubic_subdivide")]
    private static partial void CubicSubdivideNative(double* cubic, double t, double startX, double startY, double* left, double* right);

    [LibraryImport(LibName, EntryPoint = "cubic_aabb")]
    private static partial void CubicAabbNative(double* cubic, double startX, double startY, double* rect);

    [LibraryImport(LibName, EntryPoint = "cubic_flatten")]
    private static partial int CubicFlattenNative(double* cubic, double startX, double startY, double tolerance, double* output, nuint maxOutput, nuint* outCount);

    [LibraryImport(LibName, EntryPoint = "quad_flatten")]
    private static partial int QuadFlattenNative(double* quad, double startX, double startY, double tolerance, double* output, nuint maxOutput, nuint* outCount);

    private static readonly object s_loadLock = new();
    private static string? _loadError;
    private static nint s_libraryHandle;

    public static string? LastLoadError => _loadError;

    /// <summary>
    /// Loads the oracle cdylib from <c>runtimes/&lt;rid&gt;/native</c> under the app base directory
    /// and binds every <c>[LibraryImport]</c> in this assembly to that exact handle. Loading by path
    /// alone is not enough: the P/Invoke stubs resolve "etch_kurbo_oracle" through the default
    /// probing, which searches only the native-asset folders named in deps.json. On win-x64 and
    /// linux-x64 other packages (SkiaSharp, code coverage) happen to put runtimes/&lt;rid&gt;/native
    /// on that list; on osx-arm64 the only one is runtimes/osx/native, so the import failed with
    /// DllNotFoundException even though the load by path had succeeded.
    /// </summary>
    public static bool TryLoad()
    {
        lock (s_loadLock)
        {
            if (s_libraryHandle != 0)
                return true;

            if (_loadError != null)
                return false;

            string rid;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                rid = "win-x64";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64)
                rid = "linux-x64";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64)
                rid = "osx-arm64";
            else
            {
                _loadError = $"Unsupported platform: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
                return false;
            }

            var libFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? LibName + ".dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "lib" + LibName + ".dylib"
                : "lib" + LibName + ".so";

            var basePath = AppContext.BaseDirectory;
            var nativePath = Path.Combine(basePath, "runtimes", rid, "native", libFileName);

            if (!NativeLibrary.TryLoad(nativePath, out nint handle))
            {
                _loadError = $"Oracle native library not found at {nativePath} — run tools/ci/build-oracle.ps1";
                return false;
            }

            s_libraryHandle = handle;
            NativeLibrary.SetDllImportResolver(typeof(KurboOracle).Assembly, ResolveOracleImport);
            return true;
        }
    }

    private static nint ResolveOracleImport(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName == LibName ? s_libraryHandle : 0;
    }

    private static void EnsureLoaded()
    {
        if (!TryLoad())
            throw new DllNotFoundException(_loadError ?? "Unknown error loading kurbo oracle");
    }

    // kurbo stores an affine column-major as [a, b, c, d, e, f] with x' = a*x + c*y + e and
    // y' = b*x + d*y + f, so its b and c are Etch's M10 and M01 respectively.
    private static void WriteKurboCoefficients(Affine affine, Span<double> coefficients)
    {
        coefficients[0] = affine.M00;
        coefficients[1] = affine.M10;
        coefficients[2] = affine.M01;
        coefficients[3] = affine.M11;
        coefficients[4] = affine.M02;
        coefficients[5] = affine.M12;
    }

    private static Affine ReadKurboCoefficients(ReadOnlySpan<double> coefficients)
    {
        return new Affine(coefficients[0], coefficients[2], coefficients[1], coefficients[3], coefficients[4], coefficients[5]);
    }

    public static Affine Compose(Affine a, Affine b)
    {
        EnsureLoaded();
        Span<double> aa = stackalloc double[6];
        Span<double> bb = stackalloc double[6];
        WriteKurboCoefficients(a, aa);
        WriteKurboCoefficients(b, bb);
        Span<double> oo = stackalloc double[6];
        fixed (double* pa = aa, pb = bb, po = oo)
            AffineComposeNative(pa, pb, po);
        return ReadKurboCoefficients(oo);
    }

    public static Affine Inverse(Affine a)
    {
        EnsureLoaded();
        Span<double> aa = stackalloc double[6];
        WriteKurboCoefficients(a, aa);
        Span<double> oo = stackalloc double[6];
        fixed (double* pa = aa, po = oo)
            AffineInverseNative(pa, po);
        return ReadKurboCoefficients(oo);
    }

    public static void TransformPoints(Affine a, ReadOnlySpan<Point> src, Span<Point> dst)
    {
        EnsureLoaded();
        if (src.Length != dst.Length)
            throw new ArgumentException("src and dst must have same length");

        Span<double> srcBuf = stackalloc double[src.Length * 2];
        for (int i = 0; i < src.Length; i++)
        {
            srcBuf[i * 2] = src[i].X;
            srcBuf[i * 2 + 1] = src[i].Y;
        }

        Span<double> dstBuf = stackalloc double[src.Length * 2];
        Span<double> affineBuf = stackalloc double[6];
        WriteKurboCoefficients(a, affineBuf);

        fixed (double* pa = affineBuf, ps = srcBuf, pd = dstBuf)
            PointTransformNative(pa, ps, (nuint)src.Length, pd);

        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = new Point(dstBuf[i * 2], dstBuf[i * 2 + 1]);
        }
    }

    public static Point CubicEval(CubicBez cubic, double t)
    {
        EnsureLoaded();
        Span<double> cubicBuf = stackalloc double[]
        {
            cubic.P1.X, cubic.P1.Y,
            cubic.P2.X, cubic.P2.Y,
            cubic.P3.X, cubic.P3.Y,
        };
        Span<double> outBuf = stackalloc double[2];
        fixed (double* pc = cubicBuf, po = outBuf)
            CubicEvalNative(pc, t, cubic.P0.X, cubic.P0.Y, po);
        return new Point(outBuf[0], outBuf[1]);
    }

    public static (CubicBez Left, CubicBez Right) CubicSubdivide(CubicBez cubic, double t)
    {
        EnsureLoaded();
        Span<double> cubicBuf = stackalloc double[]
        {
            cubic.P1.X, cubic.P1.Y,
            cubic.P2.X, cubic.P2.Y,
            cubic.P3.X, cubic.P3.Y,
        };
        Span<double> leftBuf = stackalloc double[6];
        Span<double> rightBuf = stackalloc double[6];
        fixed (double* pc = cubicBuf, pl = leftBuf, pr = rightBuf)
            CubicSubdivideNative(pc, t, cubic.P0.X, cubic.P0.Y, pl, pr);

        return (
            new CubicBez(cubic.P0,
                new Point(leftBuf[0], leftBuf[1]),
                new Point(leftBuf[2], leftBuf[3]),
                new Point(leftBuf[4], leftBuf[5])),
            new CubicBez(
                new Point(rightBuf[0], rightBuf[1]),
                new Point(rightBuf[2], rightBuf[3]),
                new Point(rightBuf[4], rightBuf[5]),
                cubic.P3));
    }

    public static Rect CubicAabb(CubicBez cubic)
    {
        EnsureLoaded();
        Span<double> cubicBuf = stackalloc double[]
        {
            cubic.P1.X, cubic.P1.Y,
            cubic.P2.X, cubic.P2.Y,
            cubic.P3.X, cubic.P3.Y,
        };
        Span<double> rectBuf = stackalloc double[4];
        fixed (double* pc = cubicBuf, pr = rectBuf)
            CubicAabbNative(pc, cubic.P0.X, cubic.P0.Y, pr);
        return new Rect(rectBuf[0], rectBuf[1], rectBuf[2], rectBuf[3]);
    }

    public static IReadOnlyList<Point> CubicFlatten(CubicBez cubic, double tolerance)
    {
        EnsureLoaded();
        const int maxPoints = 8192;
        Span<double> output = stackalloc double[maxPoints * 2];
        Span<double> cubicBuf = stackalloc double[]
        {
            cubic.P1.X, cubic.P1.Y,
            cubic.P2.X, cubic.P2.Y,
            cubic.P3.X, cubic.P3.Y,
        };
        nuint count;
        fixed (double* pc = cubicBuf, po = output)
        {
            nuint outCount;
            int ok = CubicFlattenNative(pc, cubic.P0.X, cubic.P0.Y, tolerance, po, maxPoints, &outCount);
            count = outCount;
            if (ok == 0)
                return [];
        }
        var result = new List<Point>((int)count);
        for (int i = 0; i < (int)count; i++)
        {
            result.Add(new Point(output[i * 2], output[i * 2 + 1]));
        }
        return result;
    }

    public static IReadOnlyList<Point> QuadFlatten(QuadBez quad, double tolerance)
    {
        EnsureLoaded();
        const int maxPoints = 8192;
        Span<double> output = stackalloc double[maxPoints * 2];
        Span<double> quadBuf = stackalloc double[]
        {
            quad.P1.X, quad.P1.Y,
            quad.P2.X, quad.P2.Y,
        };
        nuint count;
        fixed (double* pq = quadBuf, po = output)
        {
            nuint outCount;
            int ok = QuadFlattenNative(pq, quad.P0.X, quad.P0.Y, tolerance, po, maxPoints, &outCount);
            count = outCount;
            if (ok == 0)
                return [];
        }
        var result = new List<Point>((int)count);
        for (int i = 0; i < (int)count; i++)
        {
            result.Add(new Point(output[i * 2], output[i * 2 + 1]));
        }
        return result;
    }
}
