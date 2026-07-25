namespace Etch.Shaders.Tests;

internal sealed class ShaderInventoryTests
{
    [Test]
    public async Task EveryWgslFileAppearsInGeneratedShaderResources()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "**/*.wgsl", SearchOption.AllDirectories)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        await Assert.That(wgslFiles.Count).IsGreaterThan(0);

        var generatedTypes = GetType().Assembly.GetTypes()
            .Where(t => t.Namespace == "Etch.Shaders" && t.Name.EndsWith("Layout"))
            .Select(t => t.Name.Replace("Layout", "").ToLowerInvariant())
            .ToHashSet();

        foreach (var shaderFile in wgslFiles)
        {
            var shaderSlug = ToSlug(shaderFile);
            var expectedType = $"shader_{shaderSlug}";
            await Assert.That(generatedTypes.Contains(expectedType)).IsTrue();
        }
    }

    [Test]
    public async Task GeneratedShaderResourcesHasNoExtraEntries()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "**/*.wgsl", SearchOption.AllDirectories)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(f => $"shader_{ToSlug(f)}")
            .ToHashSet();

        var generatedTypes = GetType().Assembly.GetTypes()
            .Where(t => t.Namespace == "Etch.Shaders" && t.Name.EndsWith("Layout"))
            .Select(t => t.Name.Replace("Layout", "").ToLowerInvariant())
            .ToList();

        var extraTypes = new List<string>();
        foreach (var g in generatedTypes)
        {
            if (!wgslFiles.Contains(g.ToLowerInvariant()))
            {
                extraTypes.Add(g);
            }
        }

        await Assert.That(extraTypes.Count).IsEqualTo(0);
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