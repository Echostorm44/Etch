using Microsoft.CodeAnalysis;

namespace Etch.Sourcegen.Pooled;

public static class EtchDiagnostics
{
    public const string EtPrefix = "ET";

    public static readonly DiagnosticDescriptor ET0101 = new(
        id: $"{EtPrefix}0101",
        title: "NoReflection",
        messageFormat: "Reflection is prohibited in Etch production code",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits System.Reflection in production assemblies.");

    public static readonly DiagnosticDescriptor ET0103 = new(
        id: $"{EtPrefix}0103",
        title: "NoDiContainer",
        messageFormat: "Dependency injection containers are prohibited in Etch production code",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits IServiceProvider and constructor-injection DI containers.");

    public static readonly DiagnosticDescriptor ET0104 = new(
        id: $"{EtPrefix}0104",
        title: "NoMvvm",
        messageFormat: "MVVM pattern is prohibited in Etch production code",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits INotifyPropertyChanged and view-model layers.");

    public static readonly DiagnosticDescriptor ET0105 = new(
        id: $"{EtPrefix}0105",
        title: "NoNonDeterministicApi",
        messageFormat: "Non-deterministic APIs are prohibited in Etch production code",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits wall-clock, RNG, and other non-deterministic APIs.");

    public static readonly DiagnosticDescriptor ET0106 = new(
        id: $"{EtPrefix}0106",
        title: "NoInterpolatedLog",
        messageFormat: "Interpolated string logging is prohibited in Etch production code",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits interpolated strings in logging calls.");

    public static readonly DiagnosticDescriptor ET0107 = new(
        id: $"{EtPrefix}0107",
        title: "NoSingleImplInterface",
        messageFormat: "Interface with a single implementor is prohibited",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Etch prohibits interfaces without a documented reason.");
}
