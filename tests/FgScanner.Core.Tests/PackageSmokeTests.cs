using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// The real-package smoke: point FG_OPEN_PACKAGE at any exported package
/// folder and this opens it with the production reader — the "scratch
/// reader call" JimsStuff SPEC-2026-006's prompt 6 asks for, kept as a
/// permanent env-gated check instead of throwaway code. Unset, it skips.
/// </summary>
public sealed class PackageSmokeTests
{
    [Fact]
    public void OpensThePackageNamedByTheEnvironment()
    {
        var dir = Environment.GetEnvironmentVariable("FG_OPEN_PACKAGE");
        Assert.SkipWhen(string.IsNullOrEmpty(dir),
            "set FG_OPEN_PACKAGE to an exported package folder to smoke-open it");
        var package = PackageReader.Open(dir!, appVersion: "0.0.0-smoke");
        Assert.NotEmpty(package.Documents);
        Assert.All(package.Documents, d => Assert.NotEmpty(d.Pages));
        Assert.NotEmpty(package.Subjects);
        Assert.NotEmpty(package.DocTypes);
        Console.WriteLine(
            $"opened {package.PackageId}: {package.Documents.Count} documents, " +
            $"{package.Documents.Sum(d => d.Pages.Count)} pages, " +
            $"{package.Documents.Sum(d => d.Suggestions.Count)} suggestions, " +
            $"vocabularyVersion {package.VocabularyVersion}");
    }
}
