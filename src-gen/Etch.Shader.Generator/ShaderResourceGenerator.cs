using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Etch.Shader.Generator;

[Generator]
public partial class ShaderResourceGenerator : IIncrementalGenerator
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
                string slug = ToSlug(path);
                string relativePath = MakeRelativePath(path);

                string wgslContent = file.GetText()?.ToString() ?? string.Empty;
                string escaped = EscapeWgsl(wgslContent);

                var source = new StringBuilder();
                source.AppendLine("namespace Etch.Shaders;");
                source.AppendLine();
                source.AppendLine("public static partial class ShaderResources");
                source.AppendLine("{");
                source.Append("    /// <summary>Source: ");
                source.Append(relativePath);
                source.AppendLine("</summary>");
                source.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Etch.Shader.Generator\", \"1.0\")]");
                source.AppendLine("    [global::System.Diagnostics.DebuggerNonUserCode]");
                source.AppendLine("public static global::System.ReadOnlySpan<global::System.Byte> ");
                source.Append(slug);
                source.Append(" => \"");
                source.Append(escaped);
                source.AppendLine("\"u8;");
                source.AppendLine("}");

                string hintName = "Shaders." + slug + ".g.cs";
                sourceContext.AddSource(hintName, SourceText.From(source.ToString(), Encoding.UTF8));
            }
        });
    }

    private static string ToSlug(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
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

    private static string MakeRelativePath(string path)
    {
        int idx = path.IndexOf("/shaders/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            idx = path.IndexOf("\\shaders\\", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return path.Substring(idx + 1);
        return Path.GetFileName(path);
    }

    private static string EscapeWgsl(string wgsl)
    {
        return wgsl
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}