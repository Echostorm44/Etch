using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// CA2255: registering the resolver from a module initializer is intentional — it installs the
// resolver before the first native P/Invoke, with no ordering burden on callers.
#pragma warning disable CA2255

namespace Etch.Text;

/// <summary>
/// The single <see cref="NativeLibrary.SetDllImportResolver"/> for Etch.Text's native dependencies
/// (FreeType and HarfBuzz). Registered once from a module initializer — before any P/Invoke — so
/// the load path is ours and deterministic: it probes an optional caller-set directory, the app
/// base directory, then <c>runtimes/&lt;rid&gt;/native</c> under the base dir. This is what makes
/// single-file / self-extract / AOT layouts "just work", unlike the upstream binding packages whose
/// loaders searched only exe-relative paths and crashed the single-file installer (INSTALL-001).
///
/// A resolver may be set only once per assembly, so both libraries share this one.
/// </summary>
internal static class NativeLibraryResolver
{
    private static string? s_overrideDirectory;

    /// <summary>
    /// Overrides the directory searched first for every Etch.Text native library. Lets a host with
    /// a custom on-disk layout (e.g. a self-extract dir) point the loader at the natives explicitly.
    /// Must be set before the first native call.
    /// </summary>
    internal static void SetNativeSearchDirectory(string? directory)
    {
        s_overrideDirectory = directory;
    }

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        string? fileName = NativeFileName(libraryName);
        if (fileName is null)
        {
            // Not one of ours — defer to the default resolver.
            return 0;
        }

        // TryLoad returns false (never throws) for a path that does not exist, so probing the
        // candidates directly doubles as the existence check — no filesystem stat needed.
        foreach (string candidate in CandidatePaths(fileName))
        {
            if (candidate.Length > 0 && NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return handle;
            }
        }

        // Last resort: bare-name load from the safe OS search directories (never the CWD, which
        // would invite DLL planting) — covers a system-installed copy.
        DllImportSearchPath safePaths = searchPath ?? DllImportSearchPath.SafeDirectories;
        if (NativeLibrary.TryLoad(fileName, assembly, safePaths, out nint fallback))
        {
            return fallback;
        }
        return 0;
    }

    /// <summary>
    /// Maps a logical <c>[LibraryImport]</c> name to the neutral platform file name we ship (Windows
    /// has no <c>lib</c> prefix; Unix does), spelled out per library.
    /// </summary>
    private static string? NativeFileName(string libraryName)
    {
        bool win = OperatingSystem.IsWindows();
        bool mac = OperatingSystem.IsMacOS();
        return libraryName switch
        {
            "freetype" => win ? "freetype.dll" : mac ? "libfreetype.dylib" : "libfreetype.so",
            "harfbuzz" => win ? "harfbuzz.dll" : mac ? "libharfbuzz.dylib" : "libharfbuzz.so",
            _ => null,
        };
    }

    private static IEnumerable<string> CandidatePaths(string fileName)
    {
        string rid = RidFolder;

        if (s_overrideDirectory is { Length: > 0 } dir)
        {
            yield return Path.Combine(dir, fileName);
            yield return Path.Combine(dir, "runtimes", rid, "native", fileName);
        }

        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, fileName);
        yield return Path.Combine(baseDir, "runtimes", rid, "native", fileName);
    }

    private static string RidFolder
    {
        get
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };
            if (OperatingSystem.IsWindows())
            {
                return $"win-{arch}";
            }
            if (OperatingSystem.IsMacOS())
            {
                return "osx";
            }
            return $"linux-{arch}";
        }
    }
}
