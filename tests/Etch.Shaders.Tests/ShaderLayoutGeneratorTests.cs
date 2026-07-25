using Etch.Shader.Generator;

namespace Etch.Shaders.Tests;

internal sealed class ShaderLayoutGeneratorTests
{
    [Test]
    public async Task ParseExtractsBindingsAndEntryPoints()
    {
        string wgsl = @"
@group(0) @binding(0) var<uniform> transform: mat4x4<f32>;
@group(1) @binding(0) var<uniform> color: vec4<f32>;

@vertex
fn vs_main() -> @builtin(position) vec4<f32> { return vec4<f32>(0.0, 0.0, 0.0, 1.0); }

@fragment
fn fs_main() -> @location(0) vec4<f32> { return vec4<f32>(1.0, 0.0, 0.0, 1.0); }
";

        var layout = WgslTokenizer.Parse(wgsl, "solid_fill");

        await Assert.That(layout.Bindings.Count).IsEqualTo(2);
        await Assert.That(layout.Bindings[0].Group).IsEqualTo(0u);
        await Assert.That(layout.Bindings[0].Binding).IsEqualTo(0u);
        await Assert.That(layout.Bindings[0].Name).IsEqualTo("transform");
        await Assert.That(layout.Bindings[1].Group).IsEqualTo(1u);
        await Assert.That(layout.Bindings[1].Binding).IsEqualTo(0u);
        await Assert.That(layout.Bindings[1].Name).IsEqualTo("color");

        await Assert.That(layout.EntryPoints.Count).IsEqualTo(2);
        await Assert.That(layout.EntryPoints[0].Stage).IsEqualTo("vertex");
        await Assert.That(layout.EntryPoints[0].Name).IsEqualTo("vs_main");
        await Assert.That(layout.EntryPoints[1].Stage).IsEqualTo("fragment");
        await Assert.That(layout.EntryPoints[1].Name).IsEqualTo("fs_main");
    }

    [Test]
    public async Task ParseHandlesNoBindings()
    {
        string wgsl = @"
@vertex
fn vs_main() -> @builtin(position) vec4<f32> { return vec4<f32>(0.0, 0.0, 0.0, 1.0); }

@fragment
fn fs_main() -> @location(0) vec4<f32> { return vec4<f32>(1.0, 0.0, 0.0, 1.0); }
";

        var layout = WgslTokenizer.Parse(wgsl, "test_shader");

        await Assert.That(layout.Bindings.Count).IsEqualTo(0);
        await Assert.That(layout.EntryPoints.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ParseHandlesMultipleBindingsPerGroup()
    {
        string wgsl = @"
@group(0) @binding(0) var<uniform> transform: mat4x4<f32>;
@group(0) @binding(1) var<uniform> transform2: mat4x4<f32>;
@group(0) @binding(2) var<uniform> transform3: mat4x4<f32>;

@vertex
fn vs_main() -> @builtin(position) vec4<f32> { return vec4<f32>(0.0, 0.0, 0.0, 1.0); }

@fragment
fn fs_main() -> @location(0) vec4<f32> { return vec4<f32>(1.0, 0.0, 0.0, 1.0); }
";

        var layout = WgslTokenizer.Parse(wgsl, "multi_binding");

        await Assert.That(layout.Bindings.Count).IsEqualTo(3);
        await Assert.That(layout.Bindings[0].Binding).IsEqualTo(0u);
        await Assert.That(layout.Bindings[1].Binding).IsEqualTo(1u);
        await Assert.That(layout.Bindings[2].Binding).IsEqualTo(2u);
    }

    [Test]
    public async Task ShaderNameIsPreserved()
    {
        string wgsl = @"
@group(0) @binding(0) var<uniform> transform: mat4x4<f32>;

@vertex
fn vs_main() -> @builtin(position) vec4<f32> { return vec4<f32>(0.0, 0.0, 0.0, 1.0); }

@fragment
fn fs_main() -> @location(0) vec4<f32> { return vec4<f32>(1.0, 0.0, 0.0, 1.0); }
";

        var layout = WgslTokenizer.Parse(wgsl, "my_test_shader");

        await Assert.That(layout.ShaderName).IsEqualTo("my_test_shader");
    }
}