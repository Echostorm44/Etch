using System;
using System.Reflection;

namespace Etch.Tests;

internal sealed class LoggingTests
{
    [Test]
    public async Task NullEtchLogger_IsEnabled_ReturnsFalseForAllLevels()
    {
        NullEtchLogger logger = NullEtchLogger.Instance;
        await Assert.That(logger.IsEnabled(EtchLogLevel.Trace)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Debug)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Info)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Warn)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Error)).IsFalse();
    }

    [Test]
    public async Task NullEtchLogger_Log_IsNoOp()
    {
        NullEtchLogger logger = NullEtchLogger.Instance;
        logger.Log(EtchLogLevel.Error, 999, "template", []);
        logger.Log(EtchLogLevel.Info, 0, "", []);
    }

    [Test]
    public async Task NullEtchLogger_Instance_IsSingleton()
    {
        await Assert.That(ReferenceEquals(NullEtchLogger.Instance, NullEtchLogger.Instance)).IsTrue();
    }

    [Test]
    public async Task IEtchLogger_HasCorrectSurface()
    {
        Type iface = typeof(IEtchLogger);
        await Assert.That(iface.IsInterface).IsTrue();

        MethodInfo? logMethod = iface.GetMethod("Log");
        await Assert.That(logMethod).IsNotNull();
        await Assert.That(logMethod!.GetParameters().Length).IsEqualTo(4);

        MethodInfo? isEnabledMethod = iface.GetMethod("IsEnabled");
        await Assert.That(isEnabledMethod).IsNotNull();
    }

    [Test]
    public async Task EtchLogLevel_HasExpectedValues()
    {
        int traceVal = (int)EtchLogLevel.Trace;
        int debugVal = (int)EtchLogLevel.Debug;
        int infoVal = (int)EtchLogLevel.Info;
        int warnVal = (int)EtchLogLevel.Warn;
        int errorVal = (int)EtchLogLevel.Error;
        await Assert.That(traceVal).IsEqualTo(0);
        await Assert.That(debugVal).IsEqualTo(1);
        await Assert.That(infoVal).IsEqualTo(2);
        await Assert.That(warnVal).IsEqualTo(3);
        await Assert.That(errorVal).IsEqualTo(4);
    }
}
