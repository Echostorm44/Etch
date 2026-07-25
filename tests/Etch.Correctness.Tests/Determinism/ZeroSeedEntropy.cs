namespace Etch.Correctness.Tests.Determinism;

public static class ZeroSeedEntropy
{
    public const int DeterministicSeed = 0;

    public static int Next() => 0;
}
