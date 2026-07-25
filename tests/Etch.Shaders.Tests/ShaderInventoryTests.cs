using System.Reflection;

namespace Etch.Shaders.Tests;

internal sealed class ShaderInventoryTests
{
    [Test]
    public async Task EveryWgslFileAppearsInGeneratedShaderResources()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslSlugs = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories)
            .Select(f => ToSlug(Path.GetFileNameWithoutExtension(f)))
            .ToList();

        await Assert.That(wgslSlugs.Count).IsGreaterThan(0);

        var generated = GeneratedResourceSlugs();

        foreach (var slug in wgslSlugs)
        {
            await Assert.That(generated.Contains(slug)).IsTrue();
        }
    }

    [Test]
    public async Task GeneratedShaderResourcesHasNoExtraEntries()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslSlugs = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories)
            .Select(f => ToSlug(Path.GetFileNameWithoutExtension(f)))
            .ToHashSet(StringComparer.Ordinal);

        var extra = GeneratedResourceSlugs()
            .Where(slug => !wgslSlugs.Contains(slug))
            .ToList();

        await Assert.That(extra.Count).IsEqualTo(0);
    }

    // The ShaderResourceGenerator emits one `public static ReadOnlySpan<Byte> <slug>` property
    // per .wgsl file into Etch.Shaders.ShaderResources (in the Etch.Shaders assembly, not this
    // test assembly). The slug is ToSlug(fileNameWithoutExtension).
    private static HashSet<string> GeneratedResourceSlugs()
    {
        return typeof(global::Etch.Shaders.ShaderResources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Etch.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }

    private static string ToSlug(string fileName)
    {
        var sb = new System.Text.StringBuilder();
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
