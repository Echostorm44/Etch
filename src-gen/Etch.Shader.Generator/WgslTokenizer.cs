using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Etch.Shader.Generator;

internal static partial class WgslTokenizer
{
    public readonly struct BindingDeclaration
    {
        public readonly uint Group;
        public readonly uint Binding;
        public readonly string ResourceType;
        public readonly string Name;

        public BindingDeclaration(uint group, uint binding, string resourceType, string name)
        {
            Group = group;
            Binding = binding;
            ResourceType = resourceType;
            Name = name;
        }
    }

    public readonly struct EntryPointDeclaration
    {
        public readonly string Stage;
        public readonly string Name;

        public EntryPointDeclaration(string stage, string name)
        {
            Stage = stage;
            Name = name;
        }
    }

    public readonly struct ShaderLayout
    {
        public readonly string ShaderName;
        public readonly List<BindingDeclaration> Bindings;
        public readonly List<EntryPointDeclaration> EntryPoints;
        public readonly List<OverrideDeclaration> Overrides;
        public readonly List<StructDeclaration> Structs;

        public ShaderLayout(string shaderName)
        {
            ShaderName = shaderName;
            Bindings = new List<BindingDeclaration>();
            EntryPoints = new List<EntryPointDeclaration>();
            Overrides = new List<OverrideDeclaration>();
            Structs = new List<StructDeclaration>();
        }
    }

    public readonly struct OverrideDeclaration
    {
        public readonly string Name;
        public readonly string Type;
        public readonly string DefaultValue;

        public OverrideDeclaration(string name, string type, string defaultValue)
        {
            Name = name;
            Type = type;
            DefaultValue = defaultValue;
        }
    }

    public readonly struct StructDeclaration
    {
        public readonly string Name;
        public readonly List<StructField> Fields;

        public StructDeclaration(string name)
        {
            Name = name;
            Fields = new List<StructField>();
        }
    }

    public readonly struct StructField
    {
        public readonly string Name;
        public readonly string Type;
        public readonly uint ArrayCount;

        public StructField(string name, string type, uint arrayCount = 0)
        {
            Name = name;
            Type = type;
            ArrayCount = arrayCount;
        }
    }

    private static readonly Regex GroupBindingRegex = new(
        @"@group\((\d+)\)\s+@binding\((\d+)\)\s+var<([^>]+)>\s+(\w+)\s*:",
        RegexOptions.Compiled);

    private static readonly Regex GroupBindingVarLtRegex = new(
        @"@group\((\d+)\)\s+@binding\((\d+)\)\s+var<\s*(storage|uniform|texture|sampler)[^>]*>\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex EntryPointRegex = new(
        @"@(\w+)\s+fn\s+(\w+)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex OverrideRegex = new(
        @"override\s+(\w+)\s*:\s*(\w+)\s*(?:=\s*(\w+))?;",
        RegexOptions.Compiled);

    private static readonly Regex StructRegex = new(
        @"struct\s+(\w+)\s*\{([^}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex StructFieldRegex = new(
        @"(\w+)\s*:\s*([\w<>]+)(?:\[\s*(\d+)\s*\])?;",
        RegexOptions.Compiled);

    public static ShaderLayout Parse(string wgslContent, string shaderSlug)
    {
        var layout = new ShaderLayout(shaderSlug);

        ParseBindings(wgslContent, layout);
        ParseEntryPoints(wgslContent, layout);
        ParseOverrides(wgslContent, layout);
        ParseStructs(wgslContent, layout);

        return layout;
    }

    private static void ParseBindings(string wgsl, ShaderLayout layout)
    {
        var seen = new System.Collections.Generic.HashSet<(uint Group, uint Binding)>();

        var matches = GroupBindingRegex.Matches(wgsl);

        foreach (Match match in matches)
        {
            uint group = uint.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            uint binding = uint.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            string resourceType = match.Groups[3].Value;
            string name = match.Groups[4].Value;

            var key = (group, binding);
            if (seen.Add(key))
            {
                layout.Bindings.Add(new BindingDeclaration(group, binding, resourceType, name));
            }
        }

        var varLtMatches = GroupBindingVarLtRegex.Matches(wgsl);
        foreach (Match match in varLtMatches)
        {
            uint group = uint.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            uint binding = uint.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            string resourceType = match.Groups[3].Value;
            string name = match.Groups[4].Value;

            var key = (group, binding);
            if (seen.Add(key))
            {
                layout.Bindings.Add(new BindingDeclaration(group, binding, resourceType, name));
            }
        }
    }

    private static void ParseEntryPoints(string wgsl, ShaderLayout layout)
    {
        var matches = EntryPointRegex.Matches(wgsl);

        foreach (Match match in matches)
        {
            string stage = match.Groups[1].Value;
            string name = match.Groups[2].Value;

            layout.EntryPoints.Add(new EntryPointDeclaration(stage, name));
        }
    }

    private static void ParseOverrides(string wgsl, ShaderLayout layout)
    {
        var matches = OverrideRegex.Matches(wgsl);

        foreach (Match match in matches)
        {
            string name = match.Groups[1].Value;
            string type = match.Groups[2].Value;
            string defaultValue = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

            layout.Overrides.Add(new OverrideDeclaration(name, type, defaultValue));
        }
    }

    private static void ParseStructs(string wgsl, ShaderLayout layout)
    {
        var structMatches = StructRegex.Matches(wgsl);

        foreach (Match structMatch in structMatches)
        {
            string structName = structMatch.Groups[1].Value;
            string fieldsContent = structMatch.Groups[2].Value;
            var structDecl = new StructDeclaration(structName);

            var fieldMatches = StructFieldRegex.Matches(fieldsContent);
            foreach (Match fieldMatch in fieldMatches)
            {
                string fieldName = fieldMatch.Groups[1].Value;
                string fieldType = fieldMatch.Groups[2].Value;
                uint arrayCount = fieldMatch.Groups[3].Success ? uint.Parse(fieldMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

                structDecl.Fields.Add(new StructField(fieldName, fieldType, arrayCount));
            }

            layout.Structs.Add(structDecl);
        }
    }
}