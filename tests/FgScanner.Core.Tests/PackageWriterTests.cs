using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-008 AC-5: the widened writer carries the full answer surface
/// (doc_type, date, person, subject, key_flag, withdrawals) and refuses
/// every malformed answer BEFORE any file is written. The phase-2 golden
/// bytes are pinned by ContractGoldenTests and must never change; this
/// file pins the new surface and the delegation equivalence.
/// </summary>
public sealed class PackageWriterTests : IDisposable
{
    private static readonly DateTimeOffset When =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    private readonly IndexPackage _package;

    public PackageWriterTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var file in Directory.EnumerateFiles(
            PackageReaderTests.GoldenPackageDir(), "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(_root, Path.GetRelativePath(
                PackageReaderTests.GoldenPackageDir(), file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        _package = PackageReader.Open(_root, appVersion: "0.6.0-test");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Out() => Path.Combine(_root, "widened-results.json");

    private static IndexAnswer Answer(
        string field, string? qualifier, string value, string anchor = "TOM99001")
        => new(anchor, field, qualifier, value, "jim", When);

    private void Write(params IndexAnswer[] answers)
        => PackageWriter.WriteResults(_package, answers, Out());

    private System.Text.Json.JsonElement[] WrittenAnswers()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Out()));
        return doc.RootElement.GetProperty("answers")
            .EnumerateArray().Select(a => a.Clone()).ToArray();
    }

    [Fact]
    public void AcceptsOneAnswerOfEachFieldAndAssignsSeqInOrder()
    {
        Write(
            Answer("doc_type", null, "letter"),
            Answer("date", "about", "2021-07-18"),
            Answer("person", "from", "P0002"),
            Answer("subject", null, "accounting-distributions"),
            Answer("key_flag", null, "true"));

        var rows = WrittenAnswers();
        Assert.Equal(
            ["doc_type", "date", "person", "subject", "key_flag"],
            rows.Select(r => r.GetProperty("field").GetString()).ToArray());
        Assert.Equal(
            [1, 2, 3, 4, 5],
            rows.Select(r => r.GetProperty("seq").GetInt32()).ToArray());
        Assert.Equal("from", rows[2].GetProperty("qualifier").GetString());
        Assert.True(
            rows[0].GetProperty("qualifier").ValueKind
                == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public void ASingleValueWithdrawalIsAnEmptyValueAndSkipsVocabularyChecks()
    {
        Write(Answer("doc_type", null, ""), Answer("key_flag", null, ""),
            Answer("date", "about", ""));
        var rows = WrittenAnswers();
        Assert.All(rows, r => Assert.Equal("", r.GetProperty("value").GetString()));
    }

    [Fact]
    public void AMultiValueWithdrawalIsRefusedBecauseItCannotNameItsTarget()
    {
        // Review finding 14: the Core gate must hold on its own; a Core-only
        // producer (the CLI may reference Core, never App) gets no App guard.
        Assert.Throws<ArgumentException>(() => Write(Answer("person", "mentioned", "")));
        Assert.Throws<ArgumentException>(() => Write(Answer("subject", null, "")));
        Assert.False(File.Exists(Out()));
    }

    [Fact]
    public void TheDocTypeOverloadStillRefusesAMissingDocType()
    {
        // main refused an empty DocTypeId (not in the vocabulary); the
        // delegating overload must not turn it into a withdrawal.
        Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(_package,
            [new DocTypeAnswer("TOM99001", "", "jim", When)], Out()));
        Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(_package,
            [new DocTypeAnswer("TOM99001", null!, "jim", When)], Out()));
        Assert.Throws<ArgumentException>(() => Write(Answer("doc_type", null, null!)));
    }

    [Fact]
    public void DatesValidateInvariantlyWhateverTheStationCulture()
    {
        // Review finding 15 / CLAUDE.md: dates ISO-8601, invariant. Under a
        // culture whose default calendar is not Gregorian, 2021 is out of
        // range and a culture-bound parse refuses every real date.
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("ar-SA");
            Write(Answer("date", "exact", "2021-07-18"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }

        Assert.Equal("2021-07-18", WrittenAnswers()[0].GetProperty("value").GetString());
    }

    [Fact]
    public void ALockedResultsFileSaysCloseAndRetryNeverThatTheDataIsSafe()
    {
        // Review finding 12: the atomic writer's Locked text was written for
        // the group index ("The data is safe in the database") and is false
        // here — no database holds index answers.
        File.WriteAllText(Out(), "held");
        using var holder = new FileStream(Out(), FileMode.Open, FileAccess.Read, FileShare.None);
        var ex = Assert.Throws<IOException>(() => Write(Answer("doc_type", null, "letter")));
        Assert.DoesNotContain("database", ex.Message);
        Assert.Contains("close", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("widened-results.json", ex.Message);
    }

    [Fact]
    public void RefusesAnUnknownField()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Write(Answer("amount", null, "12.00")));
        Assert.Contains("amount", ex.Message);
        Assert.False(File.Exists(Out()));
    }

    [Fact]
    public void RefusesOffVocabularyValues()
    {
        Assert.Contains("doc type", Assert.Throws<ArgumentException>(
            () => Write(Answer("doc_type", null, "postcard"))).Message);
        Assert.Contains("subject", Assert.Throws<ArgumentException>(
            () => Write(Answer("subject", null, "tractor-repairs"))).Message);
        Assert.Contains("person", Assert.Throws<ArgumentException>(
            () => Write(Answer("person", "from", "P9999"))).Message);
        Assert.False(File.Exists(Out()));
    }

    [Fact]
    public void RefusesABadDateValue()
    {
        Assert.Throws<ArgumentException>(
            () => Write(Answer("date", "exact", "07/18/2021")));
        Assert.Throws<ArgumentException>(
            () => Write(Answer("date", "exact", "2021-13-40")));
        Assert.False(File.Exists(Out()));
    }

    [Fact]
    public void RefusesAQualifierWhereNoneBelongs()
    {
        Assert.Throws<ArgumentException>(
            () => Write(Answer("subject", "from", "accounting-distributions")));
        Assert.Throws<ArgumentException>(
            () => Write(Answer("key_flag", "exact", "true")));
    }

    [Fact]
    public void RefusesAnUnknownOrMissingQualifierWhereOneIsRequired()
    {
        Assert.Throws<ArgumentException>(
            () => Write(Answer("person", "witness", "P0001")));
        Assert.Throws<ArgumentException>(
            () => Write(Answer("person", null, "P0001")));
        Assert.Throws<ArgumentException>(
            () => Write(Answer("date", "maybe", "2021-07-18")));
        Assert.Throws<ArgumentException>(
            () => Write(Answer("date", null, "2021-07-18")));
    }

    [Fact]
    public void RefusesAKeyFlagValueOtherThanTrueOrWithdrawal()
    {
        Assert.Throws<ArgumentException>(
            () => Write(Answer("key_flag", null, "yes")));
    }

    [Fact]
    public void RefusesAnEmptyDeciderOnTheWidenedPath()
    {
        var ex = Assert.Throws<ArgumentException>(() => Write(
            new IndexAnswer("TOM99001", "doc_type", null, "letter", " ", When)));
        Assert.Contains("decidedBy", ex.Message);
    }

    [Fact]
    public void RefusesAnAnchorNotInThePackage()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Write(Answer("doc_type", null, "letter", anchor: "TOM00000")));
        Assert.Contains("TOM00000", ex.Message);
    }

    [Fact]
    public void DocTypeAnswerOverloadAndWidenedPathProduceIdenticalBytes()
    {
        var oldPath = Path.Combine(_root, "old.json");
        var newPath = Path.Combine(_root, "new.json");
        PackageWriter.WriteResults(_package,
            new[]
            {
                new DocTypeAnswer("TOM99005", "card-note", "jim", When),
                new DocTypeAnswer("TOM99001", "letter", "jim", When),
            }, oldPath);
        PackageWriter.WriteResults(_package,
            new[]
            {
                Answer("doc_type", null, "card-note", anchor: "TOM99005"),
                Answer("doc_type", null, "letter"),
            }, newPath);
        Assert.Equal(File.ReadAllBytes(oldPath), File.ReadAllBytes(newPath));
    }
}
