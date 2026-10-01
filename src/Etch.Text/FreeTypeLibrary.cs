using System;
using System.Threading;

namespace Etch.Text;

internal static class FreeTypeLibrary
{
    private static readonly nint s_library = InitializeLibrary();

    // One FT_Library is shared by every face. FreeType allows that across threads only if
    // FT_New_Face / FT_Done_Face are serialized, because both edit the library's face list
    // (FreeType API reference, FT_Library). Unserialized, parallel tests crashed with an access
    // violation in FT_Done_Face (CI run 36811554062, ShaperTests.Dispose on windows-2022).
    private static readonly Lock s_faceListLock = new();

    private static nint InitializeLibrary()
    {
        FT_Error err = FreeTypeNative.FT_Init_FreeType(out nint lib);
        if (err != FT_Error.FT_Err_Ok)
        {
            Panic.Invariant(PanicCodes.InvariantViolation,
                $"Failed to initialize FreeType: {err}");
        }
        return lib;
    }

    public static FT_Error NewMemoryFace(nint fileBase, nint fileSize, nint faceIndex, out nint face)
    {
        lock (s_faceListLock)
        {
            return FreeTypeNative.FT_New_Memory_Face(s_library, fileBase, fileSize, faceIndex, out face);
        }
    }

    public static void DoneFace(nint face)
    {
        lock (s_faceListLock)
        {
            FreeTypeNative.FT_Done_Face(face);
        }
    }
}
