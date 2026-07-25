using System;

namespace Etch.Text;

internal static class FreeTypeLibrary
{
    private static readonly nint s_library = InitializeLibrary();

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

    public static nint Instance => s_library;
}
