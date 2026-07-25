using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Etch.Shader.Generator;

[Generator]
public partial class ShaderLayoutGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<AdditionalText>> wgslFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".wgsl", StringComparison.OrdinalIgnoreCase))
            .Collect();

        context.RegisterSourceOutput(wgslFiles, static (sourceContext, files) =>
        {
            foreach (AdditionalText file in files)
            {
                string path = file.Path;
                string fileName = Path.GetFileNameWithoutExtension(path);
                string slug = ToSlug(fileName);
                string wgslContent = file.GetText()?.ToString() ?? string.Empty;

                var layout = WgslTokenizer.Parse(wgslContent, slug);

                ValidateBindings(layout, sourceContext, path);
                ValidateEntryPoints(layout, sourceContext, path);

                EmitLayoutClass(layout, sourceContext);
            }
        });
    }

    private static void ValidateBindings(WgslTokenizer.ShaderLayout layout, SourceProductionContext context, string path)
    {
        var seen = new System.Collections.Generic.Dictionary<(uint Group, uint Binding), string>();
        foreach (var binding in layout.Bindings)
        {
            var key = ((uint)binding.Group, (uint)binding.Binding);
            if (seen.TryGetValue(key, out var existing))
            {
                var descriptor = new DiagnosticDescriptor(
                    "ET0602",
                    "DuplicateBinding",
                    $"Duplicate binding (@group({binding.Group}) @binding({binding.Binding})) in shader '{layout.ShaderName}' — first declared as '{existing}', now also as '{binding.Name}'",
                    "Etch.Shader.Generator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
            }
            else
            {
                seen[key] = binding.Name;
            }
        }
    }

    private static void ValidateEntryPoints(WgslTokenizer.ShaderLayout layout, SourceProductionContext context, string path)
    {
        bool hasVertex = false;
        bool hasFragment = false;

        foreach (var ep in layout.EntryPoints)
        {
            if (ep.Stage.Equals("vertex", StringComparison.OrdinalIgnoreCase))
                hasVertex = true;
            else if (ep.Stage.Equals("fragment", StringComparison.OrdinalIgnoreCase))
                hasFragment = true;
        }

        if (!hasVertex && !hasFragment)
        {
            var descriptor = new DiagnosticDescriptor(
                "ET0603",
                "MissingEntryPoint",
                $"Shader '{layout.ShaderName}' lacks both @vertex and @fragment entry points",
                "Etch.Shader.Generator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
        }
    }

    private static void EmitLayoutClass(WgslTokenizer.ShaderLayout layout, SourceProductionContext context)
    {
        var source = new StringBuilder();
        source.AppendLine("namespace Etch.Shaders;");
        source.AppendLine();
        source.AppendLine("public static partial class ShaderResources");
        source.AppendLine("{");

        string className = char.ToUpper(layout.ShaderName[0], System.Globalization.CultureInfo.InvariantCulture).ToString();
        if (layout.ShaderName.Length > 1)
            className += layout.ShaderName.Substring(1);
        className += "Layout";

        source.AppendLine($"    public static class {className}");
        source.AppendLine("    {");

        bool hasVertex = false;
        bool hasFragment = false;

        foreach (var ep in layout.EntryPoints)
        {
            if (ep.Stage.Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                source.AppendLine($"        public const string VertexEntryPoint = \"{ep.Name}\";");
                hasVertex = true;
            }
            else if (ep.Stage.Equals("fragment", StringComparison.OrdinalIgnoreCase))
            {
                source.AppendLine($"        public const string FragmentEntryPoint = \"{ep.Name}\";");
                hasFragment = true;
            }
        }

        if (!hasVertex)
            source.AppendLine("        public const string VertexEntryPoint = \"\";");
        if (!hasFragment)
            source.AppendLine("        public const string FragmentEntryPoint = \"\";");

        var groupBindings = new System.Collections.Generic.SortedDictionary<uint, System.Collections.Generic.List<(uint Binding, string Name, string ResourceType)>>();
        foreach (var binding in layout.Bindings)
        {
            if (!groupBindings.TryGetValue(binding.Group, out var list))
            {
                list = new System.Collections.Generic.List<(uint, string, string)>();
                groupBindings[binding.Group] = list;
            }
            list.Add((binding.Binding, binding.Name, binding.ResourceType));
        }

        foreach (var group in groupBindings)
        {
            source.AppendLine($"        public const uint Group{group.Key} = {group.Key};");
            for (int i = 0; i < group.Value.Count; i++)
            {
                var b = group.Value[i];
                string bindingFieldName = $"Binding{group.Key}_{i}_{b.Name}";
                source.AppendLine($"        public const uint {bindingFieldName} = {b.Binding};");
            }
        }

        if (layout.Overrides.Count > 0)
        {
            source.AppendLine();
            string specKeyName = className.Replace("Layout", "SpecKey", System.StringComparison.Ordinal);
            source.AppendLine($"        public readonly struct {specKeyName} : global::Etch.Shaders.IShaderSpecKey");
            source.AppendLine("        {");
            foreach (var ov in layout.Overrides)
            {
                string fieldType = ov.Type switch
                {
                    "u32" => "uint",
                    "f32" => "float",
                    "i32" => "int",
                    _ => "uint"
                };
                source.AppendLine($"            public readonly {fieldType} {CapitalizeFirst(ov.Name)};");
            }
            source.AppendLine();
            var hashFields = new System.Collections.Generic.List<string>();
            foreach (var ov in layout.Overrides)
            {
                hashFields.Add(CapitalizeFirst(ov.Name));
            }
            source.AppendLine($"            public int Hash => global::System.HashCode.Combine({string.Join(", ", hashFields)});");
            source.AppendLine($"            public global::System.ReadOnlySpan<global::Etch.Shaders.ConstantEntry> ToEntries() => new global::Etch.Shaders.ConstantEntry[]");
            source.AppendLine("            {");
            for (int i = 0; i < layout.Overrides.Count; i++)
            {
                var ov = layout.Overrides[i];
                source.AppendLine($"                new global::Etch.Shaders.ConstantEntry(\"{ov.Name}\", {CapitalizeFirst(ov.Name)}),");
            }
            source.AppendLine("            };");
            source.AppendLine("        }");
        }

        if (layout.Structs.Count > 0)
        {
            source.AppendLine();
            EmitStructs(layout, source);
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        string hintName = "Shaders." + layout.ShaderName + ".Layout.g.cs";
        context.AddSource(hintName, Microsoft.CodeAnalysis.Text.SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void EmitStructs(WgslTokenizer.ShaderLayout layout, StringBuilder source)
    {
        foreach (var structDecl in layout.Structs)
        {
            string structName = CapitalizeFirst(structDecl.Name);
            string fullName = $"{layout.ShaderName}_{structName}";
            var offsets = Std140Packer.ComputeFieldOffsets(structDecl.Fields);
            int totalSize = Std140Packer.ComputeStructSize(structDecl.Fields);

            source.AppendLine($"        [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = {totalSize})]");
            source.AppendLine($"        public struct {fullName}");
            source.AppendLine("        {");

            foreach (var field in structDecl.Fields)
            {
                int offset = offsets[field.Name];
                string csType = Std140Packer.GetCsType(field.Type);
                string fieldName = CapitalizeFirst(field.Name);
                source.AppendLine($"            [global::System.Runtime.InteropServices.FieldOffset({offset})] public {csType} {fieldName};");
            }

            source.AppendLine($"            public static readonly int SizeBytes = {totalSize};");
            source.AppendLine("        }");
            source.AppendLine();
        }
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return char.ToUpper(s[0], System.Globalization.CultureInfo.InvariantCulture).ToString() + (s.Length > 1 ? s.Substring(1) : string.Empty);
    }

    private static string ToSlug(string fileName)
    {
        var sb = new StringBuilder();
        foreach (char c in fileName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}