using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public class SchemaDocTests
{
    private static string RepoDocPath
    {
        get
        {
            // Walk up from bin/ to the repo root (folder containing FgScanner.slnx).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "docs", "db-schema.md");
        }
    }

    /// <summary>
    /// CI drift gate (PLAN §5.1): docs/db-schema.md must match the live model.
    /// To update after a schema change: FGSCANNER_UPDATE_SCHEMA_DOC=1 dotnet test --project tests/FgScanner.Data.Tests
    /// </summary>
    [Fact]
    public void Schema_doc_matches_the_model()
    {
        var generated = SchemaDocGenerator.Generate().ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("FGSCANNER_UPDATE_SCHEMA_DOC") == "1")
        {
            File.WriteAllText(RepoDocPath, generated);
        }

        Assert.True(File.Exists(RepoDocPath), $"docs/db-schema.md is missing. Regenerate: set FGSCANNER_UPDATE_SCHEMA_DOC=1 and run this test.");
        var committed = File.ReadAllText(RepoDocPath).ReplaceLineEndings("\n");
        Assert.True(
            committed == generated,
            "docs/db-schema.md is out of date with the EF model. Regenerate: set FGSCANNER_UPDATE_SCHEMA_DOC=1 and run this test project.");
    }
}
