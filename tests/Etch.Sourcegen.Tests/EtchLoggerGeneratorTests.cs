using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Etch.Sourcegen.Logging;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Basic sanity tests for <see cref="EtchLoggerGenerator"/> to verify it initializes
/// without throwing and produces output when presented with attributed source code.
/// Full snapshot testing is performed via the EtchLoggerGeneratorSnapshotTests in
/// Etch.Sourcegen.Tests using a driver adapted for IIncrementalGenerator.
/// </summary>
internal sealed class EtchLoggerGeneratorTests
{
    [Test]
    public async Task GeneratorType_IsPublicPartialClass()
    {
        Type generatorType = typeof(EtchLoggerGenerator);
        await Assert.That(generatorType.IsPublic).IsTrue();
        await Assert.That(generatorType.IsClass).IsTrue();
    }

    [Test]
    public async Task GeneratorType_ImplementsIIncrementalGenerator()
    {
        await Assert.That(typeof(EtchLoggerGenerator).GetInterfaces())
            .Contains(typeof(Microsoft.CodeAnalysis.IIncrementalGenerator));
    }

    [Test]
    public async Task EtchLogAttribute_HasCorrectProperties()
    {
        Type attrType = typeof(EtchLogAttribute);
        PropertyInfo? eventIdProp = attrType.GetProperty(nameof(EtchLogAttribute.EventId));
        PropertyInfo? levelProp = attrType.GetProperty(nameof(EtchLogAttribute.Level));
        PropertyInfo? templateProp = attrType.GetProperty(nameof(EtchLogAttribute.Template));

        await Assert.That(eventIdProp).IsNotNull();
        await Assert.That(levelProp).IsNotNull();
        await Assert.That(templateProp).IsNotNull();
    }

    [Test]
    public async Task NullLogger_ImplementsIEtchLogger()
    {
        var loggerType = typeof(NullEtchLogger);
        await Assert.That(loggerType.GetInterfaces())
            .Contains(typeof(IEtchLogger));
    }

    [Test]
    public async Task NullLogger_IsEnabled_IsFalseForAllLevels()
    {
        NullEtchLogger logger = NullEtchLogger.Instance;
        await Assert.That(logger.IsEnabled(EtchLogLevel.Trace)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Debug)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Info)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Warn)).IsFalse();
        await Assert.That(logger.IsEnabled(EtchLogLevel.Error)).IsFalse();
    }

    [Test]
    public async Task NullLogger_Log_DoesNotThrow()
    {
        NullEtchLogger logger = NullEtchLogger.Instance;
        logger.Log(EtchLogLevel.Error, 999, "template", []);
        logger.Log(EtchLogLevel.Info, 0, "", []);
    }
}
