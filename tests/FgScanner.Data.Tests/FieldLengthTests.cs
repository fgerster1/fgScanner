using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Text fields can carry a length limit and a memo flag (SPEC-2026-002 §07). Both change how a value
/// is entered and validated, so they are part of the field layout and version it like any other field
/// edit.
/// </summary>
public sealed class FieldLengthTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ProfileService _profiles;

    public FieldLengthTests() => _profiles = new ProfileService(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Length_and_memo_save_and_reload()
    {
        var profile = await _profiles.CreateAsync("Letters", Ct);
        await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = 40 },
            new FieldDefinition { Name = "Notes", Type = FieldType.Text, Memo = true, MaxLength = 2000 },
        ], Ct);

        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        var title = schema.Fields.Single(f => f.Name == "Title");
        var notes = schema.Fields.Single(f => f.Name == "Notes");
        Assert.Equal(40, title.MaxLength);
        Assert.False(title.Memo);
        Assert.Equal(2000, notes.MaxLength);
        Assert.True(notes.Memo);
    }

    /// <summary>
    /// Length is a Text setting. SaveSchemaAsync clears it on the other types, but a definition can
    /// be built without passing through it, and the validator would then count a date's characters
    /// against a limit meant for text. One rule, applied where the definition is converted.
    /// </summary>
    [Fact]
    public void A_non_text_field_carries_no_length_into_validation()
    {
        var due = new FieldDefinition { Name = "Due", Type = FieldType.Date, MaxLength = 5, Memo = true };

        var definition = due.ToIndexFieldDef();

        Assert.Null(definition.MaxLength);
        Assert.Null(FgScanner.Core.Index.FieldValidator.Validate(definition, "2026-09-16", null));
    }

    /// <summary>
    /// A length changes what validates, so groups must not pick one up behind the operator's back; and
    /// pressing Save twice must not leave every group a version behind for nothing.
    /// </summary>
    [Fact]
    public async Task A_length_change_mints_a_version_and_an_identical_save_does_not()
    {
        var profile = await _profiles.CreateAsync("Letters", Ct);
        var plain = await _profiles.SaveSchemaAsync(
            profile.Id, [new FieldDefinition { Name = "Title", Type = FieldType.Text }], Ct);

        var limited = await _profiles.SaveSchemaAsync(
            profile.Id, [new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = 40 }], Ct);
        var again = await _profiles.SaveSchemaAsync(
            profile.Id, [new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = 40 }], Ct);
        var memo = await _profiles.SaveSchemaAsync(
            profile.Id, [new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = 40, Memo = true }], Ct);

        Assert.Equal(plain.Version + 1, limited.Version);
        Assert.Equal(limited.Version, again.Version);
        Assert.Equal(limited.Version + 1, memo.Version);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    [InlineData(2001, true)]
    public async Task An_out_of_range_length_is_refused_with_the_field_named(int length, bool memo)
    {
        var profile = await _profiles.CreateAsync("Letters", Ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _profiles.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = length, Memo = memo }],
            Ct));

        Assert.Contains("Title", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(100, false)]
    [InlineData(2000, true)]
    public async Task A_length_at_either_end_of_the_range_is_accepted(int length, bool memo)
    {
        var profile = await _profiles.CreateAsync("Letters", Ct);

        await _profiles.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = length, Memo = memo }],
            Ct);

        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        Assert.Equal(length, Assert.Single(schema.Fields).MaxLength);
    }

    [Fact]
    public async Task A_non_text_field_drops_length_and_memo()
    {
        var profile = await _profiles.CreateAsync("Letters", Ct);
        FieldDefinition[] submitted = [new FieldDefinition { Name = "Due", Type = FieldType.Date, MaxLength = 5000, Memo = true }];

        var saved = await _profiles.SaveSchemaAsync(profile.Id, submitted, Ct);
        var again = await _profiles.SaveSchemaAsync(profile.Id, submitted, Ct);

        var due = Assert.Single((await _profiles.GetLatestSchemaAsync(profile.Id, Ct)).Fields);
        Assert.Null(due.MaxLength);
        Assert.False(due.Memo);
        // Once cleared, the same submission is the same layout, so it mints nothing.
        Assert.Equal(saved.Version, again.Version);
    }
}
