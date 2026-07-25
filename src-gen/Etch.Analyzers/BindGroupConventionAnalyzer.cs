using System;

namespace Etch.Analyzers;

public static class BindGroupConventions
{
    public static readonly (uint Group, uint Binding, string ExpectedType)[] Group0Conventions =
    {
        (0, 0, "per_frame"),
    };

    public static readonly (uint Group, uint Binding, string ExpectedType)[] Group1Conventions =
    {
        (1, 0, "strips"),
        (1, 1, "coverage"),
        (1, 2, "atlas"),
        (1, 3, "atlas_sampler"),
        (1, 4, "clip_stack"),
    };

    public static readonly (uint Group, uint Binding, string ExpectedType)[] Group2Conventions =
    {
        (2, 0, "per_draw"),
    };

    public static bool IsConventionBinding(uint group, uint binding, string typeName)
    {
        foreach (var convention in Group0Conventions)
        {
            if (convention.Group == group && convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        foreach (var convention in Group1Conventions)
        {
            if (convention.Group == group && convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        foreach (var convention in Group2Conventions)
        {
            if (convention.Group == group && convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        return false;
    }

    public static bool IsReservedGroup(uint group)
    {
        return group == 0 || group == 1 || group == 2;
    }

    public static bool IsValidGroup0Binding(uint binding, string typeName)
    {
        foreach (var convention in Group0Conventions)
        {
            if (convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        return false;
    }

    public static bool IsValidGroup1Binding(uint binding, string typeName)
    {
        foreach (var convention in Group1Conventions)
        {
            if (convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        return false;
    }

    public static bool IsValidGroup2Binding(uint binding, string typeName)
    {
        foreach (var convention in Group2Conventions)
        {
            if (convention.Binding == binding)
                return typeName == convention.ExpectedType;
        }
        return false;
    }
}

#pragma warning disable RS1025
public sealed class BindGroupConventionAnalyzer : object
#pragma warning restore RS1025
{
    public const string DiagnosticId = "ET0604";
    public const string OutOfConventionMessage = "Shader '{0}' binding @group({1}) @binding({2}) '{3}' does not match bind-group convention.";
    public const string ReservedGroupMessage = "Shader '{0}' uses reserved group {1}. Group 3 is reserved for future use.";
}