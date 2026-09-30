using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// The one place this project knows how to find the repo from a test binary.
/// The anchor is FgScanner.slnx; renaming or moving it is one edit here, not
/// a hunt through every test class that reads committed files.
/// </summary>
internal static class TestPaths
{
    internal static string RepoRoot()
    {
        // Walk up from bin/ to the repo root (folder containing FgScanner.slnx).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
