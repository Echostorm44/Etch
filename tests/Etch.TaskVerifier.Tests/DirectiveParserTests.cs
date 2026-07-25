using System.Collections.Generic;
using System.Linq;
using Etch.TaskVerifier;
using TUnit;

namespace Etch.TaskVerifier.Tests;

internal sealed class DirectiveParserTests
{
    [Test]
    public async Task ParseLineSingleDirectiveParsesCorrectly()
    {
        var directives = DirectiveParser.ParseLine(
            "Some text <!-- verify: file-exists path=foo.txt --> more text",
            10).ToList();

        await Assert.That(directives.Count).IsEqualTo(1);
        await Assert.That(directives[0].Verb).IsEqualTo("file-exists");
        await Assert.That(directives[0].Args["path"]).IsEqualTo("foo.txt");
        await Assert.That(directives[0].LineNumber).IsEqualTo(10);
    }

    [Test]
    public async Task ParseLineMultipleDirectivesParsesAll()
    {
        var line = "<!-- verify: file-exists path=a --> <!-- verify: tunit class=Foo -->";
        var directives = DirectiveParser.ParseLine(line, 1).ToList();

        await Assert.That(directives.Count).IsEqualTo(2);
        await Assert.That(directives[0].Verb).IsEqualTo("file-exists");
        await Assert.That(directives[1].Verb).IsEqualTo("tunit");
    }

    [Test]
    public async Task ParseLineQuotedValueParsesWithSpaces()
    {
        var directives = DirectiveParser.ParseLine(
            @"<!-- verify: symbol-shape assembly=""my assembly.dll"" type=Foo -->",
            1).ToList();

        await Assert.That(directives.Count).IsEqualTo(1);
        await Assert.That(directives[0].Verb).IsEqualTo("symbol-shape");
        await Assert.That(directives[0].Args["assembly"]).IsEqualTo("my assembly.dll");
    }

    [Test]
    public async Task ParseLineNoDirectiveReturnsEmpty()
    {
        var directives = DirectiveParser.ParseLine("No directive here", 1).ToList();
        await Assert.That(directives.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParseLineHyphenatedVerbParsesCorrectly()
    {
        var directives = DirectiveParser.ParseLine(
            "<!-- verify: file-exists path=foo.txt -->",
            1).ToList();

        await Assert.That(directives.Count).IsEqualTo(1);
        await Assert.That(directives[0].Verb).IsEqualTo("file-exists");
    }

    [Test]
    public async Task ParseLineMultipleArgsParsesAll()
    {
        var directives = DirectiveParser.ParseLine(
            "<!-- verify: aot-publish rid=win-x64 project=src/Foo/Foo.csproj -->",
            1).ToList();

        await Assert.That(directives.Count).IsEqualTo(1);
        await Assert.That(directives[0].Verb).IsEqualTo("aot-publish");
        await Assert.That(directives[0].Args["rid"]).IsEqualTo("win-x64");
        await Assert.That(directives[0].Args["project"]).IsEqualTo("src/Foo/Foo.csproj");
    }
}
