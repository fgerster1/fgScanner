using System.CommandLine;
using System.Globalization;
using FgScanner.Data;
using FgScanner.Ocr;
using FgScanner.Scanning;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Cli;

/// <summary>Test seams: scanner and database are injectable so integration tests run hardware-free.</summary>
public sealed record CliOverrides(
    IScanService? ScanService = null,
    string? DbPath = null,
    string? TessdataDir = null);

/// <summary>
/// Headless pipeline (PLAN §5.8): scan / process / export / list-devices with exit codes, so a
/// scheduled task can run scan→OCR→index with no UI. No WPF anywhere in this assembly.
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, CliOverrides? overrides = null)
    {
        var verboseOption = new Option<bool>("--verbose", "-v") { Description = "Detailed progress output." };
        var fakeOption = new Option<bool>("--fake")
        {
            Description = "Use the built-in fake scanner (demo/testing).",
        };
        var dbOption = new Option<string?>("--db") { Description = "Database file override.", Hidden = true };

        var root = new RootCommand("FG Scanner command line — scan, process, and export without the UI.");
        root.Options.Add(verboseOption);
        root.Options.Add(fakeOption);
        root.Options.Add(dbOption);
        root.Subcommands.Add(BuildScanCommand(verboseOption, fakeOption, dbOption, overrides));
        root.Subcommands.Add(BuildProcessCommand(verboseOption, dbOption, overrides));
        root.Subcommands.Add(BuildExportCommand(verboseOption, dbOption, overrides));
        root.Subcommands.Add(BuildListDevicesCommand(fakeOption, overrides));

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    private sealed record Services(
        IDbContextFactory<FgScannerDbContext> Factory,
        GroupService Groups,
        ProfileService Profiles,
        IndexingService Indexing,
        TrashService Trash,
        CaptureTriageService Triage);

    private sealed class Factory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private static Services OpenDatabase(string? dbOverride, CliOverrides? overrides)
    {
        var dbPath = overrides?.DbPath ?? dbOverride ?? DbBootstrapper.DefaultDbPath;
        var appVersion = typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        IndexingService.AppVersion = appVersion;
        DbBootstrapper.MigrateWithBackup(dbPath, appVersion);
        var factory = new Factory(dbPath);
        var groups = new GroupService(factory);
        var profiles = new ProfileService(factory);
        var trash = new TrashService(factory, Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "trash"));
        var settings = new AppSettingsService(factory);
        var hooks = new CommitHookRunner(settings, new Core.Hooks.CommitHookService());
        var triage = new CaptureTriageService(factory, settings, new Scanning.Capture.PageClassifier());
        return new Services(
            factory, groups, profiles,
            new IndexingService(factory, profiles, new Core.Index.IndexExporter(), hooks), trash, triage);
    }

    private static IScanService CreateScanService(bool fake, CliOverrides? overrides) =>
        overrides?.ScanService ?? (fake ? new FakeScanService() : new Naps2ScanService());

    private static async Task<(Guid ProfileId, int SchemaVersion)?> ResolveProfileAsync(
        Services services, string? profileName)
    {
        var all = await services.Profiles.ListAsync().ConfigureAwait(false);
        var profile = profileName is null
            ? await services.Profiles.EnsureDefaultAsync().ConfigureAwait(false)
            : all.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Profile \"{profileName}\" not found. Available: {string.Join(", ", all.Select(p => p.Name))}");
        var schema = await services.Profiles.GetLatestSchemaAsync(profile.Id).ConfigureAwait(false);
        return (profile.Id, schema.Version);
    }

    // ---- scan ----

    private static Command BuildScanCommand(
        Option<bool> verbose, Option<bool> fake, Option<string?> db, CliOverrides? overrides)
    {
        var profileOption = new Option<string?>("--profile", "-p") { Description = "Profile name (default: Default)." };
        var groupOption = new Option<string>("--group") { Description = "Group directory.", Required = true };
        var driverOption = new Option<string>("--driver") { Description = "wia | twain | escl." };
        driverOption.DefaultValueFactory = _ => "wia";
        var deviceOption = new Option<string?>("--device") { Description = "Device name substring (default: first found)." };
        var sourceOption = new Option<string>("--source") { Description = "glass | feeder | duplex." };
        sourceOption.DefaultValueFactory = _ => "glass";
        var dpiOption = new Option<int>("--dpi") { Description = "Resolution." };
        dpiOption.DefaultValueFactory = _ => 300;
        var bitDepthOption = new Option<string>("--bitdepth") { Description = "c (color) | g (gray) | bw." };
        bitDepthOption.DefaultValueFactory = _ => "c";
        var countOption = new Option<int>("--count", "-n") { Description = "Number of scan passes." };
        countOption.DefaultValueFactory = _ => 1;
        var delayOption = new Option<int>("--delay") { Description = "Seconds between passes." };

        var command = new Command("scan", "Scan pages into a group directory.")
        {
            profileOption, groupOption, driverOption, deviceOption,
            sourceOption, dpiOption, bitDepthOption, countOption, delayOption,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var services = OpenDatabase(parseResult.GetValue(db), overrides);
                var scanService = CreateScanService(parseResult.GetValue(fake), overrides);
                var driver = ParseDriver(parseResult.GetValue(driverOption)!);
                var devices = await scanService.ListDevicesAsync(driver, cancellationToken).ConfigureAwait(false);
                var deviceFilter = parseResult.GetValue(deviceOption);
                var device = deviceFilter is null
                    ? (devices.Count > 0 ? devices[0] : null)
                    : devices.FirstOrDefault(d => d.Name.Contains(deviceFilter, StringComparison.OrdinalIgnoreCase));
                if (device is null)
                {
                    Console.Error.WriteLine($"No {driver} device found" +
                        (deviceFilter is null ? "." : $" matching \"{deviceFilter}\"."));
                    return 1;
                }

                var profileRef = await ResolveProfileAsync(services, parseResult.GetValue(profileOption))
                    .ConfigureAwait(false);
                var group = await services.Groups.AdoptDirectoryAsync(
                    parseResult.GetValue(groupOption)!, profileRef, cancellationToken).ConfigureAwait(false);
                var options = new ScanProfileOptions
                {
                    Device = device,
                    Source = parseResult.GetValue(sourceOption) switch
                    {
                        "feeder" => ScanSource.Feeder,
                        "duplex" => ScanSource.Duplex,
                        _ => ScanSource.Flatbed,
                    },
                    Dpi = parseResult.GetValue(dpiOption),
                    BitDepth = parseResult.GetValue(bitDepthOption) switch
                    {
                        "g" => ScanBitDepth.Grayscale,
                        "bw" => ScanBitDepth.BlackWhite,
                        _ => ScanBitDepth.Color,
                    },
                };

                var totalAdopted = 0;
                var passes = Math.Max(1, parseResult.GetValue(countOption));
                for (var pass = 1; pass <= passes; pass++)
                {
                    if (pass > 1 && parseResult.GetValue(delayOption) > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(parseResult.GetValue(delayOption)), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var workDir = Directory.CreateTempSubdirectory("fgscanner-cli-scan").FullName;
                    try
                    {
                        var storage = new DirectoryPageStorage(workDir);
                        var scanned = new List<string>();
                        await foreach (var page in scanService.ScanAsync(options, storage, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            scanned.Add(page.FilePath);
                            if (parseResult.GetValue(verbose))
                            {
                                Console.WriteLine($"  page {scanned.Count} scanned");
                            }
                        }

                        var triage = await services.Triage.TriageAsync(group, scanned, cancellationToken)
                            .ConfigureAwait(false);
                        if (triage.DroppedCount > 0 && parseResult.GetValue(verbose))
                        {
                            Console.WriteLine(
                                $"  {triage.DroppedSeparators.Count} separator(s), {triage.DroppedBlanks.Count} blank(s) dropped (journal.txt)");
                        }

                        var result = await services.Groups.AdoptPagesAsync(
                            group.Id, triage.FilesToAdopt, triage.IsBlankFlagged, cancellationToken)
                            .ConfigureAwait(false);
                        await services.Indexing.ApplyInitialValuesAsync(
                            group.Id, [.. result.Adopted.Select(p => p.DocumentId)], null, cancellationToken)
                            .ConfigureAwait(false);
                        totalAdopted += result.Adopted.Count;
                        Console.WriteLine($"Pass {pass}/{passes}: {result.Adopted.Count} page(s) into \"{group.Name}\".");
                    }
                    finally
                    {
                        TryDelete(workDir);
                    }
                }

                Console.WriteLine($"Done: {totalAdopted} page(s) total in {group.DirectoryPath}.");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"scan failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }

    // ---- process ----

    private static Command BuildProcessCommand(
        Option<bool> verbose, Option<string?> db, CliOverrides? overrides)
    {
        var dirArgument = new Argument<string>("directory") { Description = "Folder to process as a group." };
        var profileOption = new Option<string?>("--profile", "-p") { Description = "Profile name for new groups." };
        var ocrOption = new Option<bool>("--ocr") { Description = "Run OCR on pages not yet OCRed." };
        var aiOption = new Option<bool>("--ai") { Description = "Generate AI descriptions (needs a stored key)." };
        var writeIndexOption = new Option<bool>("--write-index") { Description = "Write index files in every enabled format." };

        var command = new Command("process", "Register a folder's images/PDFs and run the pipeline.")
        {
            dirArgument, profileOption, ocrOption, aiOption, writeIndexOption,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var services = OpenDatabase(parseResult.GetValue(db), overrides);
                using var fileImport = new FileImportService();
                var retro = new RetroProcessService(
                    services.Factory, services.Groups, services.Trash, new Naps2PdfRenderer(fileImport));
                var profileRef = await ResolveProfileAsync(services, parseResult.GetValue(profileOption))
                    .ConfigureAwait(false);
                var report = await retro.ProcessFolderAsync(
                    parseResult.GetValue(dirArgument)!, profileRef, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(
                    $"Registered: {report.AdoptedImages} image(s), {report.AdoptedPdfPages} PDF page(s); " +
                    $"{report.DuplicateFiles.Count} duplicate(s) skipped.");
                foreach (var foreign in report.ForeignIndexFiles)
                {
                    Console.WriteLine($"WARNING: {foreign} was not written by FG Scanner and will be replaced on export.");
                }

                if (parseResult.GetValue(ocrOption))
                {
                    var failures = await RunOcrAsync(
                        services, report.GroupId, overrides, parseResult.GetValue(verbose), cancellationToken)
                        .ConfigureAwait(false);
                    if (failures > 0)
                    {
                        Console.Error.WriteLine($"OCR finished with {failures} failed page(s).");
                    }
                }

                if (parseResult.GetValue(aiOption))
                {
                    var exit = await RunAiAsync(
                        services, report.GroupId, parseResult.GetValue(verbose), cancellationToken)
                        .ConfigureAwait(false);
                    if (exit != 0)
                    {
                        return exit;
                    }
                }

                if (parseResult.GetValue(writeIndexOption))
                {
                    var export = await services.Indexing.ReexportAsync(report.GroupId, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine("Index files: " + string.Join(
                        ", ", export.Results.Select(r => Path.GetFileName(r.Path))));
                }

                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"process failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }

    private static async Task<int> RunOcrAsync(
        Services services, Guid groupId, CliOverrides? overrides, bool verbose,
        CancellationToken cancellationToken)
    {
        var tessdataDir = overrides?.TessdataDir ?? TesseractPaths.DefaultUserTessdataDir;
        new LanguageManager(tessdataDir).EnsureBundledData();
        using var runner = new TesseractRunner(tessdataDir: tessdataDir);
        var settings = new AppSettingsService(services.Factory);
        var pipeline = new OcrPipeline(
            runner,
            new Scanning.Editing.ImageEditorPageRotator(new Scanning.Editing.ImageEditor()),
            ct => FeatureFlags.IsEnabledAsync(settings, FeatureFlags.AutoOrient, ct));
        var reorder = new ReorderService(services.Factory);
        var queue = new OcrQueueService(services.Factory);
        await queue.ResetInFlightAsync(cancellationToken).ConfigureAwait(false);
        var queued = await queue.EnqueueGroupAsync(groupId, cancellationToken: cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"OCR: {queued} page(s) queued.");

        var failures = 0;
        while (await queue.ClaimNextAsync(cancellationToken).ConfigureAwait(false) is { } job)
        {
            var outcome = await pipeline.ProcessPageAsync(
                job.ImagePath, languages: "eng", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (outcome.RotatedClockwiseDegrees != 0)
            {
                // Uprighting rewrote the file, so the stored checksum and perceptual hash are stale.
                await reorder.RefreshChecksumAsync(job.PageId, cancellationToken).ConfigureAwait(false);
                if (verbose)
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {Path.GetFileName(job.ImagePath)}: rotated {outcome.RotatedClockwiseDegrees}° upright"));
                }
            }

            if (outcome.Success)
            {
                await queue.CompleteAsync(job.JobId, outcome.PlainText ?? "", outcome.MeanConfidence, cancellationToken)
                    .ConfigureAwait(false);
                if (verbose)
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {Path.GetFileName(job.ImagePath)}: {outcome.MeanConfidence:0}% in {outcome.Duration.TotalMilliseconds:0}ms"));
                }
            }
            else
            {
                await queue.FailAsync(job.JobId, outcome.Error ?? "unknown", cancellationToken).ConfigureAwait(false);
                failures++;
            }
        }

        return failures;
    }

    private static async Task<int> RunAiAsync(
        Services services, Guid groupId, bool verbose, CancellationToken cancellationToken)
    {
        var credentials = new Ai.CredentialStore();
        if (credentials.GetKey() is not { } key)
        {
            Console.Error.WriteLine("--ai needs a Gemini API key; add one in the FG Scanner app (Settings → AI).");
            return 1;
        }

        var settings = new AppSettingsService(services.Factory);
        var model = await settings.GetAsync("Ai.Model", Ai.GeminiDescriptionProvider.DefaultModel, cancellationToken)
            .ConfigureAwait(false);
        using var provider = new Ai.GeminiDescriptionProvider(key, model);
        var queue = new AiQueueService(services.Factory);
        await queue.ResetInFlightAsync(cancellationToken).ConfigureAwait(false);
        var queued = await queue.EnqueueGroupAsync(groupId, cancellationToken: cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"AI: {queued} page(s) queued ({model}).");

        while (await queue.ClaimNextAsync(cancellationToken).ConfigureAwait(false) is { } job)
        {
            if (job.OcrStatus == OcrStatus.Yes && Ai.AiBackoffPolicy.IsBlankByOcr(job.OcrText))
            {
                await queue.SkipAsync(job.JobId, Ai.DescriptionPrompt.BlankPageSentinel, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var result = await provider.DescribeAsync(job.ImagePath, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                await queue.CompleteAsync(job.JobId, result.Description!, cancellationToken).ConfigureAwait(false);
                if (verbose)
                {
                    Console.WriteLine($"  {Path.GetFileName(job.ImagePath)}: described");
                }
            }
            else
            {
                if (result.Retryable)
                {
                    await Task.Delay(Ai.AiBackoffPolicy.DelayFor(job.Attempt), cancellationToken).ConfigureAwait(false);
                }

                await queue.FailAsync(job.JobId, result.FailureReason ?? "unknown", result.Retryable, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return 0;
    }

    // ---- export ----

    private static Command BuildExportCommand(
        Option<bool> verbose, Option<string?> db, CliOverrides? overrides)
    {
        var groupOption = new Option<string>("--group") { Description = "Group directory.", Required = true };
        var outputOption = new Option<string>("--output", "-o") { Description = "Output PDF path.", Required = true };
        var compatOption = new Option<string?>("--pdfcompat") { Description = "A1-b | A2-b | A3-b | A3-u." };
        var ocrOption = new Option<bool>("--ocr") { Description = "Embed a searchable text layer." };
        var titleOption = new Option<string?>("--title") { Description = "PDF title metadata." };

        var command = new Command("export", "Export a group's pages as a PDF.")
        {
            groupOption, outputOption, compatOption, ocrOption, titleOption,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var services = OpenDatabase(parseResult.GetValue(db), overrides);
                var group = await services.Groups.AdoptDirectoryAsync(
                    parseResult.GetValue(groupOption)!, null, cancellationToken).ConfigureAwait(false);
                var pages = await services.Groups.GetPagesAsync(group.Id, cancellationToken).ConfigureAwait(false);
                if (pages.Count == 0)
                {
                    Console.Error.WriteLine("The group has no pages.");
                    return 1;
                }

                PdfOcrSettings? ocrSettings = null;
                if (parseResult.GetValue(ocrOption))
                {
                    var tessdataDir = overrides?.TessdataDir ?? TesseractPaths.DefaultUserTessdataDir;
                    new LanguageManager(tessdataDir).EnsureBundledData();
                    ocrSettings = new PdfOcrSettings(TesseractPaths.DefaultExePath, tessdataDir);
                }

                using var pdfExport = new PdfExportService();
                await pdfExport.ExportAsync(
                    [.. pages.Select(p => Path.Combine(group.DirectoryPath, p.FileName))],
                    parseResult.GetValue(outputOption)!,
                    new PdfExportOptions
                    {
                        Title = parseResult.GetValue(titleOption) ?? group.Name,
                        Compat = parseResult.GetValue(compatOption)?.ToUpperInvariant() switch
                        {
                            "A1-B" => PdfCompatLevel.PdfA1B,
                            "A2-B" => PdfCompatLevel.PdfA2B,
                            "A3-B" => PdfCompatLevel.PdfA3B,
                            "A3-U" => PdfCompatLevel.PdfA3U,
                            null => PdfCompatLevel.Default,
                            var other => throw new InvalidOperationException($"Unknown --pdfcompat \"{other}\"."),
                        },
                        Ocr = ocrSettings,
                    },
                    cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"Exported {pages.Count} page(s) to {parseResult.GetValue(outputOption)}.");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"export failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }

    // ---- list-devices ----

    private static Command BuildListDevicesCommand(Option<bool> fake, CliOverrides? overrides)
    {
        var driverOption = new Option<string?>("--driver") { Description = "wia | twain | escl (default: all available)." };
        var command = new Command("list-devices", "List connected scanners.") { driverOption };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var scanService = CreateScanService(parseResult.GetValue(fake), overrides);
                var drivers = parseResult.GetValue(driverOption) is { } filter
                    ? [ParseDriver(filter)]
                    : scanService.AvailableDrivers;
                foreach (var driver in drivers)
                {
                    foreach (var device in await scanService.ListDevicesAsync(driver, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        Console.WriteLine($"{driver.ToString().ToLowerInvariant()}\t{device.Name}");
                    }
                }

                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"list-devices failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }

    private static ScanDriver ParseDriver(string value) => value.ToUpperInvariant() switch
    {
        "TWAIN" => ScanDriver.Twain,
        "ESCL" => ScanDriver.Escl,
        "WIA" => ScanDriver.Wia,
        var other => throw new InvalidOperationException($"Unknown --driver \"{other}\"."),
    };

    private sealed class DirectoryPageStorage(string directory) : IPageStorage
    {
        private int _next;

        public string ReserveNextPagePath(string extension)
        {
            Directory.CreateDirectory(directory);
            _next++;
            return Path.Combine(
                directory, $"page-{_next.ToString("00000", CultureInfo.InvariantCulture)}.{extension}");
        }

        public void CommitPage(ScannedPage page)
        {
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
