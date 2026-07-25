using Etch.Shaders;

namespace Etch.Shaders.Tests;

internal sealed class ShaderErrorParserTests
{
    [Test]
    public async Task ParseExtractsLineAndColumnFromCanonicalWgpuError()
    {
        string wgpuError = "ShaderModule \"strip_coverage\" validation error: : line 14 column 8: expected `;`, found `return`";
        string shaderSource = "fn coverage(strip: Strip) -> f32 {\n    let mask = strip.row_mask;\n    let row = (mask >> strip.y) & 1u\n    return f32(row) * strip.alpha;\n}";

        var ex = ShaderErrorParser.Parse(wgpuError, "strip_coverage", "shaders/strip_coverage.wgsl", shaderSource);

        await Assert.That(ex.Line).IsEqualTo(14);
        await Assert.That(ex.Column).IsEqualTo(8);
        await Assert.That(ex.ErrorMessage).IsEqualTo("expected `;`, found `return`");
    }

    [Test]
    public async Task ParseExtractsFromNagaStyleError()
    {
        string nagaError = "error: expected ')', found ';'  ┌─ /path/shaders/test.wgsl:3:40";
        string shaderSource = "struct Foo {\n    x: f32;\n}\nfn main() { return; }";

        var ex = ShaderErrorParser.Parse(nagaError, "test", "/path/shaders/test.wgsl", shaderSource);

        await Assert.That(ex.Line).IsEqualTo(3);
        await Assert.That(ex.Column).IsEqualTo(40);
    }

    [Test]
    public async Task ContextSnippetIncludesLinesAroundErrorWithCaret()
    {
        string wgpuError = "ShaderModule validation error: : line 3 column 5: expected identifier";
        string shaderSource = "line 1 content\nline 2 content\nline 3 with error\nline 4 content\nline 5 content";

        var ex = ShaderErrorParser.Parse(wgpuError, "test", "test.wgsl", shaderSource);

        string snippet = ex.ContextSnippet;
        await Assert.That(snippet.Contains('|', StringComparison.Ordinal)).IsTrue();
        await Assert.That(snippet.Contains('^', StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingSourceReturnsUnavailableSnippet()
    {
        string wgpuError = "ShaderModule validation error: : line 5 column 1: something went wrong";

        var ex = ShaderErrorParser.Parse(wgpuError, "test", "test.wgsl", null);

        await Assert.That(ex.ContextSnippet).IsEqualTo("<source unavailable>");
        await Assert.That(ex.Line).IsEqualTo(5);
    }

    [Test]
    public async Task MalformedErrorFallsBackToMessageOnly()
    {
        string malformedError = "this is not a parseable error message at all";

        var ex = ShaderErrorParser.Parse(malformedError, "test", "test.wgsl", "some source");

        await Assert.That(ex.Line).IsEqualTo(0);
        await Assert.That(ex.Column).IsEqualTo(0);
        await Assert.That(ex.RawWgpuText).IsEqualTo(malformedError);
        await Assert.That(ex.ErrorMessage).IsEqualTo(malformedError);
    }

    [Test]
    public async Task EmptyErrorFallsBackGracefully()
    {
        var ex = ShaderErrorParser.Parse("", "test");

        await Assert.That(ex.ErrorMessage).IsEqualTo("<empty error>");
        await Assert.That(ex.ContextSnippet).IsEqualTo("<source unavailable>");
    }

    [Test]
    public async Task ShaderCompileExceptionHasCorrectPanicCode()
    {
        var ex = ShaderErrorParser.Parse("error at line 1", "test");

        await Assert.That(ShaderCompileException.Code).IsEqualTo(PanicCodes.ShaderCompileError);
    }

    [Test]
    public async Task ToStringProducesMultiLineDiagnostic()
    {
        string wgpuError = "ShaderModule validation error: : line 2 column 3: found invalid token";
        string shaderSource = "line one\nline two error\nline three";

        var ex = ShaderErrorParser.Parse(wgpuError, "my_shader", "shaders/my_shader.wgsl", shaderSource);
        string output = ex.ToString();

        await Assert.That(output.Contains("my_shader", StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains("line 2", StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains("column 3", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ToEtchExceptionCreatesCorrectException()
    {
        var ex = ShaderErrorParser.Parse("error", "test");

        var etchEx = ex.ToEtchException();

        await Assert.That(etchEx.Code).IsEqualTo(PanicCodes.ShaderCompileError);
        await Assert.That(etchEx.Message.Contains("ShaderCompileException", StringComparison.Ordinal)).IsTrue();
    }
}