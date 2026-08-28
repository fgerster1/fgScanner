using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class ProfileImportExportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ProfileService _profiles;

    public ProfileImportExportTests() => _profiles = new ProfileService(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Profile> CreateRichProfileAsync()
    {
        var profile = await _profiles.CreateAsync("Accounting", Ct);
        await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Required = true, Sticky = true },
            new FieldDefinition
            {
                Name = "Category", Type = FieldType.List,
                ListChoicesJson = """["Utilities","Rent"]""", DefaultValue = "Rent",
            },
        ], Ct);
        await _profiles.UpdateExportSettingsAsync(profile.Id, csv: true, xlsx: true, xml: false, json: true, ";", Ct);
        await _profiles.UpdateOcrEnabledAsync(profile.Id, true, Ct);
        await _profiles.UpdateCapturePolicyAsync(
            profile.Id, separatorDetection: true, keepSeparators: false,
            FgScanner.Core.Capture.BlankPagePolicy.Flag, Ct);
        return profile;
    }

    [Fact]
    public async Task Export_import_round_trips_everything()
    {
        var original = await CreateRichProfileAsync();

        var json = await _profiles.ExportProfileJsonAsync(original.Id, Ct);
        Assert.Contains("\"FormatVersion\": 2", json);
        var imported = await _profiles.ImportProfileJsonAsync(json, Ct);

        Assert.Equal("Accounting (2)", imported.Name); // name collision → suffix
        var reloaded = (await _profiles.ListAsync(Ct)).First(p => p.Id == imported.Id);
        Assert.True(reloaded.OcrEnabled);
        Assert.True(reloaded.ExportXlsx);
        Assert.False(reloaded.ExportXml);
        Assert.True(reloaded.SeparatorDetectionEnabled);
        Assert.False(reloaded.KeepSeparatorPages);
        Assert.Equal(FgScanner.Core.Capture.BlankPagePolicy.Flag, reloaded.BlankPolicy);
        Assert.Equal(";", reloaded.CsvDelimiter);

        var schema = await _profiles.GetLatestSchemaAsync(imported.Id, Ct);
        Assert.Equal(2, schema.Fields.Count);
        var vendor = schema.Fields.First(f => f.Name == "Vendor");
        Assert.True(vendor.Required);
        Assert.True(vendor.Sticky);
        var category = schema.Fields.First(f => f.Name == "Category");
        Assert.Equal(FieldType.List, category.Type);
        Assert.Equal(["Utilities", "Rent"], IndexingService.ParseChoices(category.ListChoicesJson));
        Assert.Equal("Rent", category.DefaultValue);
    }

    [Fact]
    public async Task Unsupported_format_version_is_refused()
    {
        var json = """{"FormatVersion": 99, "Name": "X", "Fields": []}""";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _profiles.ImportProfileJsonAsync(json, Ct));
    }

    [Fact]
    public async Task Garbage_json_is_refused() =>
        await Assert.ThrowsAnyAsync<Exception>(() => _profiles.ImportProfileJsonAsync("not json", Ct));

    [Fact]
    public async Task A_batch_field_round_trips()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Scope = FieldScope.Batch },
        ], Ct);

        var json = await _profiles.ExportProfileJsonAsync(profile.Id, Ct);
        Assert.Contains("\"FormatVersion\": 2", json, StringComparison.Ordinal);

        var imported = await _profiles.ImportProfileJsonAsync(json, Ct);
        var schema = await _profiles.GetLatestSchemaAsync(imported.Id, Ct);

        Assert.Equal(FieldScope.Batch, schema.Fields.Single(f => f.Name == "Box").Scope);
    }

    /// <summary>
    /// Profiles already exported onto the hand-off USB stick are version 1. Refusing them would
    /// strand the operator mid-box with a file that worked yesterday.
    /// </summary>
    [Fact]
    public async Task A_version_1_file_still_imports_with_row_scope()
    {
        const string v1 = """
            {
              "FormatVersion": 1,
              "Name": "Legacy",
              "OcrEnabled": false,
              "ExportCsv": true,
              "ExportXlsx": false,
              "ExportXml": false,
              "ExportJson": false,
              "CsvDelimiter": ",",
              "Fields": [
                { "Name": "Box", "Type": "Text", "Required": true, "Sticky": true,
                  "DefaultValue": null, "ListChoicesJson": null }
              ]
            }
            """;

        var imported = await _profiles.ImportProfileJsonAsync(v1, Ct);
        var schema = await _profiles.GetLatestSchemaAsync(imported.Id, Ct);

        Assert.Equal(FieldScope.Row, schema.Fields.Single().Scope);
    }
}
