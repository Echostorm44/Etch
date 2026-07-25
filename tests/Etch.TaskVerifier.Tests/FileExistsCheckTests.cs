using System.Collections.Generic;
using System.IO;
using Etch.TaskVerifier;
using TUnit;

namespace Etch.TaskVerifier.Tests;

internal sealed class FileExistsCheckTests
{
    private readonly FileExistsCheck _check = new();
    private const string TestFile = @"F:\Code\Etch\tests\Etch.TaskVerifier.Tests\FileExistsCheckTests.cs";

    [Test]
    public async Task RunExistingFileReturnsPass()
    {
        if (!File.Exists(TestFile))
        {
            return;
        }

        var args = new Dictionary<string, string> { { "path", TestFile } };
        var result = _check.Run("file-exists", args, TestFile, 1);

        await Assert.That(result.Status).IsEqualTo(CheckStatus.Pass);
    }

    [Test]
    public async Task RunNonExistingFileReturnsFail()
    {
        var args = new Dictionary<string, string> { { "path", "/nonexistent/path/file.txt" } };
        var result = _check.Run("file-exists", args, TestFile, 1);

        await Assert.That(result.Status).IsEqualTo(CheckStatus.Fail);
    }

    [Test]
    public async Task RunMissingPathArgReturnsFail()
    {
        var args = new Dictionary<string, string>();
        var result = _check.Run("file-exists", args, TestFile, 1);

        await Assert.That(result.Status).IsEqualTo(CheckStatus.Fail);
        await Assert.That(result.Detail).Contains("Missing required");
    }
}
