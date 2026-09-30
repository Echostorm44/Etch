using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TUnit;

namespace Etch.Build.Tests;

/// <summary>
/// Builds a throwaway project that imports the repo's build/Shaders.targets with ValidateShaders=true,
/// so these tests exercise the real naga validation gate rather than an unrelated plain build.
/// </summary>
public sealed class NagaValidationTests
{
    [Test]
    [Category("NagaValidation")]
    public void ValidShader_PassesBuild()
    {
        var (exitCode, output) = BuildProjectWithShader("valid.wgsl", ValidWgslContent);

        if (exitCode != 0)
            throw new InvalidOperationException($"Expected build to pass but got exit code {exitCode}. Output: {output}");
        if (!output.Contains("valid.wgsl", StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected the shader to be validated but it was never mentioned. Output: {output}");
    }

    [Test]
    [Category("NagaValidation")]
    public void BrokenShader_FailsBuildWithFileAndLine()
    {
        var (exitCode, output) = BuildProjectWithShader("broken.wgsl", InvalidWgslContent);

        if (exitCode == 0)
            throw new InvalidOperationException($"Expected build to fail but got exit code 0. Output: {output}");

        // naga reports errors as "┌─ <path>:<line>:<column>".
        if (!Regex.IsMatch(output, @"broken\.wgsl:\d+:\d+", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"Expected the error to name the shader file and line but got: {output}");
    }

    private static (int ExitCode, string Output) BuildProjectWithShader(string shaderFileName, string shaderContent)
    {
        string projectDir = Path.Combine(Path.GetTempPath(), $"etch_naga_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        try
        {
            string shaderDir = Path.Combine(projectDir, "shaders");
            Directory.CreateDirectory(shaderDir);
            File.WriteAllText(Path.Combine(shaderDir, shaderFileName), shaderContent);

            string shadersTargetsPath = Path.Combine(FindRepoRoot(), "build", "Shaders.targets");
            File.WriteAllText(Path.Combine(projectDir, "TestProject.csproj"), CreateProjectContent(shadersTargetsPath));

            ProcessStartInfo psi = new()
            {
                FileName = "dotnet",
                Arguments = "build -nodeReuse:false",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Failed to start dotnet process");

            // Drain both pipes before waiting so a chatty build cannot block on a full pipe buffer.
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = standardErrorTask.GetAwaiter().GetResult();
            process.WaitForExit();

            return (process.ExitCode, standardOutput + standardError);
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Etch.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find Etch.sln above {AppContext.BaseDirectory}");
    }

    // Shaders.targets globs the repo's own shaders; the project swaps those for its local shader so
    // only the shader under test is validated.
    private static string CreateProjectContent(string shadersTargetsPath) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ValidateShaders>true</ValidateShaders>
          </PropertyGroup>
          <Import Project="{shadersTargetsPath}" />
          <ItemGroup>
            <WgslShader Remove="@(WgslShader)" />
            <WgslShader Include="shaders/**/*.wgsl" />
          </ItemGroup>
        </Project>
        """;

    private const string ValidWgslContent = @"
@vertex
fn vs(@builtin(vertex_index) idx: u32) -> @builtin(position) vec4<f32> {
    var p = array<vec2<f32>, 3>(
        vec2(-0.5, -0.5),
        vec2( 0.5, -0.5),
        vec2( 0.0,  0.5));
    return vec4<f32>(p[idx], 0.0, 1.0);
}

@fragment
fn fs() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
";

    private const string InvalidWgslContent = @"
@vertex
fn vs(@builtin(vertex_index) idx: u32) -> @builtin(position) vec4<f32> {
    var p = array<vec2<f32>, 3>(
        vec2(-0.5, -0.5),
        vec2( 0.5, -0.5),
        vec2( 0.0,  0.5));
    return vec4<f32>(p[idx], 0.0, 1.0);

@fragment
fn fs() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
";
}
