using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>Typed access to the key/value Settings table.</summary>
public sealed class AppSettingsService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    public const string OcrLanguagesKey = "Ocr.Languages";

    public async Task<string> GetAsync(
        string key, string defaultValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings.FindAsync([key], cancellationToken).ConfigureAwait(false);
        return setting?.Value ?? defaultValue;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings.FindAsync([key], cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
