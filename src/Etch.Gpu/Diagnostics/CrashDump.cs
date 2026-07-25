using System;
using System.IO;
using Etch.Abstractions.Diagnostics;

namespace Etch.Gpu.Diagnostics;

// ═══════════════════════════════════════════════════════════════════════════
// CrashDump — best-effort writer for .etrp crash artefacts. Centralises the
// path policy (ETCH_CRASH_DUMP_DIR env var → %TEMP%/etch/) and the file-naming
// convention (crash-<pid>-<utc>.etrp). Writes are best-effort: failure to
// create the dump is logged but never hides the original panic.
//
// Lives in the GPU assembly rather than in Abstractions because the primary
// consumer is the GPU panic bridge; other subsystems would call straight
// into SceneReproWriter when they need to dump something else.
// ═══════════════════════════════════════════════════════════════════════════

public static class CrashDump
{
    public const string DirectoryEnvVar = "ETCH_CRASH_DUMP_DIR";
    private const string FallbackSubdir = "etch";

    /// <summary>
    /// Resolves the directory into which crash dumps should be written. The env var
    /// takes precedence; otherwise falls back to <c>Path.GetTempPath()/etch</c>. The
    /// returned directory is created if missing (best-effort).
    /// </summary>
    public static string ResolveDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable(DirectoryEnvVar);
        string chosen = string.IsNullOrWhiteSpace(envDir)
            ? Path.Combine(Path.GetTempPath(), FallbackSubdir)
            : envDir;

        try
        {
            Directory.CreateDirectory(chosen);
        }
        catch
        {
            // Best-effort; caller will get the failure from File.WriteAllBytes.
        }

        return chosen;
    }

    /// <summary>
    /// Builds the canonical crash-dump filename for the current process using the supplied
    /// UTC timestamp string (typically formatted <c>yyyyMMddHHmmssfff</c>). The timestamp
    /// is supplied by the caller rather than read from <c>DateTime.UtcNow</c> directly so
    /// this type stays free of non-deterministic API calls (see ET0105).
    /// Format: <c>crash-&lt;pid&gt;-&lt;utcStamp&gt;.etrp</c>.
    /// </summary>
    public static string BuildFileName(string utcStamp)
    {
        if (utcStamp is null)
        {
            Panic.ArgumentNull(nameof(utcStamp));
        }

        int pid = Environment.ProcessId;
        return $"crash-{pid}-{utcStamp}.etrp";
    }

    /// <summary>
    /// Captures the supplied reproducer and writes the bytes to the resolved crash directory
    /// under <c>crash-&lt;pid&gt;-&lt;utcStamp&gt;.etrp</c>. Returns the full path on success,
    /// or <see langword="null"/> if the write failed (disk full, permissions, etc.). Never
    /// throws — callers typically invoke from a top-level panic handler that must not observe
    /// a secondary failure. When <paramref name="directoryOverride"/> is non-null the env
    /// var / temp fallback is bypassed — useful for tests that need isolation from the
    /// process-global environment.
    /// </summary>
    public static string? TryWrite(
        ISceneReproducer reproducer,
        int upperBoundBytes,
        string utcStamp,
        string? directoryOverride = null)
    {
        if (reproducer is null) return null;
        if (upperBoundBytes <= 0) return null;

        string directory;
        string path;
        try
        {
            directory = directoryOverride ?? ResolveDirectory();
            if (directoryOverride is not null)
            {
                Directory.CreateDirectory(directory);
            }
            path = Path.Combine(directory, BuildFileName(utcStamp));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CrashDump] path build failed: {ex}");
            return null;
        }

        byte[] scratch;
        int written;
        try
        {
            scratch = new byte[upperBoundBytes];
            written = reproducer.CaptureTo(scratch);
            if (written <= 0) { Console.Error.WriteLine("[CrashDump] CaptureTo returned 0"); return null; }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CrashDump] capture failed: {ex}");
            return null;
        }

        try
        {
            // Using a FileStream (not WriteAllBytes) so we can specify the slice length
            // without allocating a trimmed copy.
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            fs.Write(scratch, 0, written);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CrashDump] write failed: {ex}");
            return null;
        }

        return path;
    }
}
