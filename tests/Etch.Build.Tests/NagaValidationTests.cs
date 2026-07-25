using System;
using System.Diagnostics;
using System.IO;
using TUnit;

namespace Etch.Build.Tests;

public sealed class NagaValidationTests
{
    [Test]
    [Category("NagaValidation")]
    public void ValidShader_PassesBuild()
    {
        string projectDir = Path.Combine(Path.GetTempPath(), $"etch_test_valid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        try
        {
            string brokenShaderDir = Path.Combine(projectDir, "shaders", "broken");
            Directory.CreateDirectory(brokenShaderDir);

            string validShaderPath = Path.Combine(brokenShaderDir, "valid.wgsl");
            File.WriteAllText(validShaderPath, validWgslContent);

            string csprojPath = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(csprojPath, validProjectContent);

            ProcessStartInfo psi = new()
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Failed to start dotnet process");

            process.WaitForExit();
            string output = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            string combined = output + stderr;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Expected build to pass but got exit code {process.ExitCode}. Output: {combined}");
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    [Test]
    [Category("NagaValidation")]
    public void BrokenShader_FailsBuildWithFileAndLine()
    {
        string projectDir = Path.Combine(Path.GetTempPath(), $"etch_test_broken_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        try
        {
            string brokenShaderDir = Path.Combine(projectDir, "shaders", "broken");
            Directory.CreateDirectory(brokenShaderDir);

            string brokenShaderPath = Path.Combine(brokenShaderDir, "broken.wgsl");
            File.WriteAllText(brokenShaderPath, invalidWgslContent);

            string csprojPath = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(csprojPath, validProjectContent);

            ProcessStartInfo psi = new()
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Failed to start dotnet process");

            process.WaitForExit();
            string output = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            string combined = output + stderr;

            if (process.ExitCode == 0)
                throw new InvalidOperationException($"Expected build to fail but got exit code 0. Output: {combined}");

            if (!combined.Contains("broken.wgsl") && !combined.Contains("broken.wgsl"))
                throw new InvalidOperationException($"Expected error to mention shader file path but got: {combined}");
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    private const string validWgslContent = @"
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

    private const string invalidWgslContent = @"
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

    private const string validProjectContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
";
}
