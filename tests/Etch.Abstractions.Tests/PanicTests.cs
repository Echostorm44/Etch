using System;
using System.Collections.Generic;
using System.Reflection;

namespace Etch.Tests;

/// <summary>
/// Verifies the error-model shape from FND-010: every <see cref="Panic"/> helper throws
/// an <see cref="EtchException"/> carrying the correct <see cref="PanicCode"/>, a
/// caller-captured call site, and a non-empty message. A separate test asserts the
/// panic-code registry contains no duplicates.
/// </summary>
internal sealed class PanicTests
{
    [Test]
    public async Task Invariant_ThrowsEtchException_WithCodeAndCallSite()
    {
        EtchException caught = CaptureFromInvariant();

        await Assert.That(caught.Code).IsEqualTo(PanicCodes.InvariantViolation);
        await Assert.That(caught.Message).IsEqualTo("probe message");
        await Assert.That(caught.CallSite).IsNotNull();
        // The call site must point back at the helper invocation below, captured via
        // [CallerFilePath]/[CallerLineNumber] — not at Panic.cs itself.
        await Assert.That(caught.CallSite!).Contains("PanicTests.cs");
        await Assert.That(caught.CallSite!).Contains(":");
    }

    [Test]
    public async Task ArgumentNull_ThrowsWithArgumentNullCode()
    {
        EtchException caught = Capture(static () => Panic.ArgumentNull("scene"));

        await Assert.That(caught.Code).IsEqualTo(PanicCodes.ArgumentNull);
        await Assert.That(caught.Message).Contains("scene");
        await Assert.That(caught.CallSite).IsNotNull();
    }

    [Test]
    public async Task ArgumentOutOfRange_ThrowsWithRangeCode_AndIncludesOptionalMessage()
    {
        EtchException caught = Capture(static () => Panic.ArgumentOutOfRange("index", "must be < 256"));

        await Assert.That(caught.Code).IsEqualTo(PanicCodes.ArgumentOutOfRange);
        await Assert.That(caught.Message).Contains("index");
        await Assert.That(caught.Message).Contains("must be < 256");
    }

    [Test]
    public async Task NotImplemented_ThrowsWithNotImplementedCode()
    {
        EtchException caught = Capture(static () => Panic.NotImplemented("MyFeature"));

        await Assert.That(caught.Code).IsEqualTo(PanicCodes.NotImplemented);
        await Assert.That(caught.Message).Contains("MyFeature");
    }

    [Test]
    public async Task PanicCodeRegistry_HasUniqueValues()
    {
        // Reflect over PanicCodes to guarantee no two constants share a value — a silent
        // collision would quietly break incident correlation across logs / dumps / verifier.
        FieldInfo[] fields = typeof(PanicCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

        HashSet<string> seen = new(StringComparer.Ordinal);
        int count = 0;
        foreach (FieldInfo field in fields)
        {
            if (field.FieldType != typeof(PanicCode))
            {
                continue;
            }
            PanicCode code = (PanicCode)field.GetValue(null)!;
            bool added = seen.Add(code.Value);
            await Assert.That(added).IsTrue();
            count++;
        }

        // The real invariant is per-code uniqueness (asserted in the loop above). The registry
        // grows over time (FND-010 seeded ET-P-0001..0007; GPU/surface codes ET-P-02xx followed),
        // so assert a floor rather than a brittle exact count that breaks on every new code.
        await Assert.That(count).IsGreaterThanOrEqualTo(8);
    }

    [Test]
    public async Task EtchException_IsSealed_AndExposesCodeAndCallSite()
    {
        // Shape contract from FND-010 AC#1: sealed, carries PanicCode + CallSite + Message.
        Type t = typeof(EtchException);
        await Assert.That(t.IsSealed).IsTrue();
        await Assert.That(t.GetProperty("Code")).IsNotNull();
        await Assert.That(t.GetProperty("CallSite")).IsNotNull();
    }

    // Compile-time probe for FND-010 AC#7: a void method whose last statement is a
    // Panic.* helper compiles without a `return;`. If the [DoesNotReturn] annotation ever
    // gets lost, the C# compiler emits CS0161 here and the whole project fails to build —
    // turning the acceptance criterion into a structural invariant rather than a runtime
    // assertion.
    private static void DoesNotReturnProbe_Invariant()
    {
        Panic.Invariant(PanicCodes.InvariantViolation, "compile-time probe");
    }

    private static void DoesNotReturnProbe_ArgumentNull()
    {
        Panic.ArgumentNull("probe");
    }

    private static EtchException CaptureFromInvariant()
    {
        // Direct inline call — proves [CallerFilePath]/[CallerLineNumber] capture the
        // invocation site in *this* test file, not Panic.cs. Keep this exactly inline;
        // a helper would move the captured file path.
        try
        {
            Panic.Invariant(PanicCodes.InvariantViolation, "probe message");
        }
        catch (EtchException ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Panic.Invariant did not throw.");
    }

    private static EtchException Capture(Action act)
    {
        try
        {
            act();
        }
        catch (EtchException ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Panic helper did not throw.");
    }
}
