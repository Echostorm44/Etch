using System;
using System.IO;
using Etch.Fuzz.Shared;
using Etch.Scene;
using Etch.Scene.Serialization;
using SharpFuzz;

namespace Etch.Scene.Fuzz;

public static class Program
{
    public static void Main(string[] args)
        => Fuzzer.Run(Fuzz);

    private static void Fuzz(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        FuzzGuard.Run(bytes, static input =>
        {
            try
            {
                var scene = SceneReader.Read(input);

                var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
                SceneValidator.ValidateAccumulated(scene, ref report);
            }
            catch (EtchException ex)
            {
                if (!IsExpectedScenePanic(ex.Code))
                {
                    throw new InvalidOperationException($"Unexpected panic: {ex.Code}");
                }
            }
        });
    }

    private static bool IsExpectedScenePanic(Etch.PanicCode code)
    {
        var value = code.Value;
        return value != null && (
            value == "ET-P-0404" ||
            value == "ET-P-0405" ||
            value == "ET-P-0406" ||
            value == "ET-P-0420" ||
            value == "ET-P-0421" ||
            value == "ET-P-0422" ||
            value == "ET-P-0423" ||
            value == "ET-P-0424" ||
            value == "ET-P-0425" ||
            value == "ET-P-0426" ||
            value == "ET-P-0427" ||
            value == "ET-P-0428" ||
            value == "ET-P-0429");
    }
}
