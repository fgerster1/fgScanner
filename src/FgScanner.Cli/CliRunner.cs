using System.CommandLine;
using System.Globalization;
using FgScanner.Core.Station;
using FgScanner.Data;
using FgScanner.Ocr;
using FgScanner.Scanning;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Cli;

/// <summary>Test seams: scanner and database are injectable so integration tests run hardware-free.</summary>
public sealed record CliOverrides(
    IScanService? ScanService = null,
    string? DbPath = null,
    string? TessdataDir = null,
    string? StationRoot = null);

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
        var dataDirOption = new Option<string?>("--data-dir")
        {
            Description = "A second station's folder (database, trash, languages). Default: this Windows account's own station.",
            Recursive = true,
        };

        // Read before parsing so every subcommand opens the same station; a flag with no folder
        // is refused rather than falling back to the default station's evidence.
        string? dataDir;
        try
        {
            dataDir = StationPaths.DataDirFromArgs(args);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 2;
        }

        if (dataDir is not null)
        {
            if (args.Contains("--db"))
            {
                await Console.Error.WriteLineAsync("--db and --data-dir name two databases; give one.")
                    .ConfigureAwait(false);
                return 2;
            }

            var station = StationPaths.Resolve(dataDir);
            overrides = (overrides ?? new CliOverrides()) with
            {
                DbPath = overrides?.DbPath ?? station.Database,
                TessdataDir = overrides?.TessdataDir ?? station.Tessdata,
                StationRoot = station.Root,
            };
        }

        var root = new RootCommand("FG Scanner command line — scan, process, and export without the UI.");
        root.Options.Add(verboseOption);
        root.Options.Add(fakeOption);
        root.Options.Add(dbOption);
        root.Options.Add(dataDirOption);
        root.Subcommands.Add(BuildScanCommand(verboseOption, fakeOption, dbOption, overrides));
        root.Subcommands.Add(BuildProcessCommand(verboseOption, dbOption, overrides));
        root.Subcommands.Add(BuildExportCommand(verboseOption, dbOption, overrides));
        root.Subcommands.Add(BuildListDevicesCommand(fakeOption, overrides));
        root.Subcommands.Add(BuildStationCommand(overrides));

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

                        var triage = await services.Triage
                            .TriageAsync(group, scanned, cancellationToken: cancellationToken)
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
                        services, report.GroupId, overrides?.StationRoot, parseResult.GetValue(verbose),
                        cancellationToken)
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
            new Scanning.Editing.ImageEditorPageRotator(new Scanning.Editing.ImageEditor(
                preserveOriginals: ct => FeatureFlags.IsEnabledAsync(settings, FeatureFlags.PreserveOriginals, ct))),
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
                job.ImagePath, 300, "eng", job.AutoOrient, cancellationToken).ConfigureAwait(false);
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
        Services services, Guid groupId, string? stationRoot, bool verbose, CancellationToken cancellationToken)
    {
        var credentials = new Ai.CredentialStore(fallbackDirectory: stationRoot);
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

    // ---- station (SPEC-SCANNER-2026-010) ----
    private static Command BuildStationCommand(CliOverrides? overrides)
    {
        var packArgument = new Argument<string>("pack-folder") { Description = "The pack folder holding station-manifest.json." };
        var verify = new Command("verify", "Check a station pack against its manifest.") { packArgument };
        verify.SetAction(parseResult =>
        {
            var pack = parseResult.GetValue(packArgument)!;
            if (!Directory.Exists(pack))
            {
                Console.Error.WriteLine($"NOT OK — the folder \"{pack}\" does not exist.");
                return 1;
            }

            StationVerifyResult result;
            try
            {
                result = StationManifestVerifier.Verify(pack);
            }
            catch (Exception ex) when (NotOkRather(ex))
            {
                Console.WriteLine($"NOT OK — {ex.Message}");
                return 1;
            }

            if (result.Status != StationVerifyStatus.StillArriving)
            {
                StationManifestVerifier.WriteLog(pack, result, DateTimeOffset.UtcNow);
            }

            Console.WriteLine(StationManifestVerifier.Summary(result));
            return result.Status switch
            {
                StationVerifyStatus.Ok => 0,
                StationVerifyStatus.StillArriving => 3,
                _ => 1,
            };
        });

        var toOption = new Option<string>("--to") { Description = "The transport folder; the pack becomes a new folder inside it.", Required = true };
        var basedOnOption = new Option<string?>("--based-on") { Description = "The manifest of the last pack the receiver holds; only what it lacks is shipped." };
        var kindOption = new Option<string>("--kind") { Description = "send | return.", DefaultValueFactory = _ => "send" };
        var stationOption = new Option<string?>("--station") { Description = "Name on the pack folder (default: the workspace's, else this computer's)." };
        var evidenceOption = new Option<string?>("--evidence") { Description = "The evidence folder (default: the one the profiles name)." };
        var byOption = new Option<string?>("--by") { Description = "Whose work a return records in changes.jsonl (default: this Windows account)." };
        var pack = new Command("pack", "Pack this station for the other side; prints OK and the code to read out.")
        {
            toOption, basedOnOption, kindOption, stationOption, evidenceOption, byOption,
        };
        pack.SetAction(parseResult => HoldingTheStation(() => PackStation(
            parseResult.GetValue(toOption)!, parseResult.GetValue(basedOnOption), parseResult.GetValue(kindOption)!,
            parseResult.GetValue(stationOption), parseResult.GetValue(evidenceOption),
            parseResult.GetValue(byOption) ?? Environment.UserName, overrides)));

        var applyArgument = new Argument<string?>("pack-folder")
        {
            Description = "The pack folder to install (not with --undo).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var undoOption = new Option<bool>("--undo") { Description = "Put the station back as it was before the last apply." };
        var applyEvidenceOption = new Option<string?>("--evidence") { Description = "The evidence folder (default: the one the profiles name; required for an empty station). Undo uses the folder the last apply used." };
        var expectCodeOption = new Option<string?>("--expect-code") { Description = "The code the operator confirmed; the pack must still have it." };
        var apply = new Command("apply", "Install a pack into this station; prints OK and the code to read out.")
        {
            applyArgument, undoOption, applyEvidenceOption, expectCodeOption,
        };
        apply.SetAction(parseResult => HoldingTheStation(
            () => ApplyStation(
                parseResult.GetValue(applyArgument), parseResult.GetValue(undoOption),
                parseResult.GetValue(applyEvidenceOption), parseResult.GetValue(expectCodeOption), overrides),
            (overrides?.StationRoot is { } stationRoot ? StationPaths.Resolve(stationRoot) : StationPaths.Default).Root));

        return new Command("station", "Move a whole station between machines as verified packs.") { verify, pack, apply };
    }

    private static readonly SqliteStationDatabase StationDatabase = new();

    /// <summary>
    /// What the receiver's database looked like when it last matched <paramref name="basedOn"/>:
    /// read from the pack that manifest came with (Franz's return, based on Jim's send), or kept
    /// in this station's freeze record (a send based on the last return).
    /// </summary>
    private static StationContent? ContentBehind(string basedOn, string stationRoot)
    {
        var bytes = File.ReadAllBytes(basedOn);
        if (PackedDatabaseBeside(basedOn) is { } packedDb)
        {
            return StationDatabase.Content(packedDb);
        }

        var freeze = StationFreezeRecord.Read(stationRoot);
        return freeze?.ManifestSha256 == StationManifest.HashOf(bytes) ? freeze.Content : null;
    }

    /// <summary>The database a pack carried, when <paramref name="manifest"/> sits in that pack and
    /// the database is the one it names.</summary>
    private static string? PackedDatabaseBeside(string manifest)
    {
        var packedDb = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, StationManifest.DatabaseFileName);
        return File.Exists(packedDb)
            && StationManifestBuilder.Sha256Of(packedDb) == StationManifest.Parse(File.ReadAllBytes(manifest)).DbSha256
            ? packedDb
            : null;
    }

    private static int ApplyStation(string? packFolder, bool undo, string? evidence, string? expectCode, CliOverrides? overrides)
    {
        var paths = overrides?.StationRoot is { } root ? StationPaths.Resolve(root) : StationPaths.Default;
        var stationRoot = paths.Root;
        var database = Path.Combine(stationRoot, StationManifest.DatabaseFileName);
        var applier = new StationApplier(() => false, StationDatabase);
        // An empty station's evidence folder is typed by the operator, never taken from the pack:
        // the pack arrives through a shared folder and would otherwise choose where files land.
        if (!undo && File.Exists(database))
        {
            evidence ??= StationFacts.Read(database).BaseDirectories is [var only] ? only : null;
        }

        if (evidence is not null && NotLocal(evidence))
        {
            Console.WriteLine($"NOT OK — the evidence folder must be on this computer, not {evidence}");
            return 1;
        }

        StationApplyResult result;
        if (undo)
        {
            result = applier.Undo(stationRoot);
        }
        else
        {
            if (packFolder is null || evidence is null)
            {
                Console.WriteLine(packFolder is null
                    ? "NOT OK — name the pack folder to install"
                    : "NOT OK — say which folder holds the evidence: --evidence <folder>");
                return packFolder is null ? 2 : 1;
            }

            using var migrations = new FgScannerDbContext(DbBootstrapper.BuildOptions(database));
            KeepAwake(true);
            try
            {
                result = applier.Apply(new StationApplyRequest(
                    packFolder, stationRoot, evidence,
                    typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                    migrations.Database.GetMigrations().Last(),
                    DateTimeOffset.Now,
                    File.Exists(database) ? StationFacts.Read(database).LastMigration : null,
                    expectCode));
            }
            finally
            {
                KeepAwake(false);
            }
        }

        var line = result.Problem is not null
            ? (result.StillArriving ? "NOT YET — " : "NOT OK — ") + result.Problem
            : undo ? "OK — put back as it was before the last update" : $"OK {result.Code}";
        Console.WriteLine(line);
        if (Directory.Exists(stationRoot))
        {
            File.AppendAllText(Path.Combine(stationRoot, "station-apply.log"),
                $"{StationManifest.FormatSentAt(DateTimeOffset.UtcNow)} {(undo ? "undo" : packFolder)}: {line}{Environment.NewLine}");
        }

        return result.Problem is null ? 0 : result.StillArriving ? 3 : 1;
    }

    /// <summary>A share, a network drive or a device path (UNC, <c>\\?\</c>, <c>\\.\</c>), judged after
    /// normalising: evidence is only ever written to a folder on this computer.</summary>
    private static bool NotLocal(string folder)
    {
        var full = Path.GetFullPath(folder);

        // A drive letter mapped (or SUBSTed) to a share is a network drive too (Revision D, P10-11).
        return full.StartsWith(@"\\", StringComparison.Ordinal)
            || new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network;
    }

    /// <summary>
    /// Runs a verb holding FG Scanner's own single-instance lock (Revision D, D5): packing and
    /// installing take minutes, and an app opened meanwhile would scan into a station being
    /// copied or swapped. While the lock is held a launched app exits as a second instance does.
    /// </summary>
    /// <param name="logFolder">Where apply and undo keep <c>station-apply.log</c>; a crash is
    /// logged there too (re-review R-6). Null for pack.</param>
    private static int HoldingTheStation(Func<int> verb, string? logFolder = null)
    {
        using var hold = new Mutex(initiallyOwned: true, "FGScanner.SingleInstance", out var ours);
        if (!ours)
        {
            Console.WriteLine("NOT OK — FG Scanner is open — close it, then try again");
            return 1;
        }

        try
        {
            return verb();
        }
        catch (Exception ex) when (NotOkRather(ex))
        {
            Console.WriteLine($"NOT OK — {ex.Message}");
            if (logFolder is not null && Directory.Exists(logFolder))
            {
                File.AppendAllText(Path.Combine(logFolder, "station-apply.log"),
                    $"{StationManifest.FormatSentAt(DateTimeOffset.UtcNow)} NOT OK — {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }

            return 1;
        }
        finally
        {
            hold.ReleaseMutex();
        }
    }

    /// <summary>A pack arrives from a shared folder and may be damaged or crafted: what it can
    /// cause is a NOT OK line Jim can read out, never a stack trace (Revision D, P10-10).</summary>
    private static bool NotOkRather(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException
            or System.Text.Json.JsonException or InvalidOperationException;

    private static int PackStation(
        string to, string? basedOn, string kind, string? stationName, string? evidence, string by, CliOverrides? overrides)
    {
        if (kind is not ("send" or "return"))
        {
            Console.Error.WriteLine($"--kind is send or return, not \"{kind}\".");
            return 2;
        }

        var paths = overrides?.StationRoot is { } root ? StationPaths.Resolve(root) : StationPaths.Default;
        var database = overrides?.DbPath ?? paths.Database;
        if (!File.Exists(database))
        {
            Console.WriteLine($"NOT OK — this station has no database at {database}");
            return 1;
        }

        StationFacts facts;
        try
        {
            facts = StationFacts.Read(database);
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"NOT OK — the database could not be read: {ex.Message}");
            return 1;
        }

        evidence ??= facts.BaseDirectories.Count == 1 ? facts.BaseDirectories[0] : null;
        if (evidence is null)
        {
            Console.WriteLine("NOT OK — say which folder holds the evidence: --evidence <folder>");
            return 1;
        }

        if (NotLocal(evidence))
        {
            Console.WriteLine($"NOT OK — the evidence folder must be on this computer, not {evidence}");
            return 1;
        }

        // A group outside the evidence folder would not travel, and the other side's database
        // would point at a folder that is not there.
        var evidenceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidence)) + Path.DirectorySeparatorChar;
        var outside = facts.GroupDirectories.FirstOrDefault(d =>
            !(Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)) + Path.DirectorySeparatorChar)
                .StartsWith(evidenceRoot, StringComparison.OrdinalIgnoreCase));
        if (outside is not null)
        {
            Console.WriteLine($"NOT OK — a group lives outside the evidence folder {evidence}: {outside}");
            return 1;
        }

        // A return answers one send: its change log is a diff against the database that send
        // carried, so without that database there is nothing to account for Franz's work against.
        var received = basedOn is null ? null : PackedDatabaseBeside(basedOn);
        if (kind == "return" && received is null)
        {
            Console.WriteLine("NOT OK — a return must be based on the pack it answers: --based-on <that pack>/station-manifest.json");
            return 1;
        }

        StationContent? basedOnContent = null;
        if (basedOn is not null)
        {
            basedOnContent = ContentBehind(basedOn, Path.GetDirectoryName(Path.GetFullPath(database))!);
            if (basedOnContent is null)
            {
                Console.WriteLine($"NOT OK — cannot tell what the other side holds: {basedOn} is neither a verified pack nor this station's last one");
                return 1;
            }
        }

        var packer = new StationPacker(
            isAppOpen: () => false,
            copyDatabase: BackUpDatabase,
            freeBytes: folder => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace);
        var request = new StationPackRequest(
            new StationSource(
                stationName ?? paths.Name ?? Environment.MachineName.ToLowerInvariant(),
                database,
                evidence,
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, "trash"),
                typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                facts.LastMigration,
                null,
                DateTimeOffset.Now),
            Path.GetDirectoryName(Path.GetFullPath(database))!,
            to,
            basedOn,
            kind,
            facts.PendingReadingJobs,
            basedOnContent,
            StationDatabase.Content(database),
            received is null ? null :
            [
                new StationPackReport(StationChangeLog.FileName,
                    StationChangeLog.ToBytes(StationChangeLog.DiffSinceReceived(received, database, by, DateTimeOffset.Now))),
                new StationPackReport(NeedsJimSheet.FileName,
                    System.Text.Encoding.UTF8.GetBytes(NeedsJimSheet.Write(database, DateTimeOffset.Now))),
            ]);

        StationPackResult result;
        KeepAwake(true);
        try
        {
            result = packer.Pack(request);
        }
        finally
        {
            KeepAwake(false);
        }

        if (result.Problem is not null)
        {
            Console.WriteLine($"NOT OK — {result.Problem}");
            return 1;
        }

        var packedBytes = new FileInfo(Path.Combine(result.PackFolder!, StationManifest.DatabaseFileName)).Length
            + result.ShippedBytes;
        Console.WriteLine(StationManifestVerifier.Summary(new StationVerifyResult(
            StationVerifyStatus.Ok, result.Code, result.ShippedFiles + 1, packedBytes, [])));
        Console.WriteLine(result.PackFolder);
        return 0;
    }

    /// <summary>SQLite's backup API: a consistent copy even of a database another reader has open.</summary>
    private static void BackUpDatabase(string from, string to)
    {
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = from,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString()))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = to,
            Pooling = false,
        }.ToString()))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
    }

    /// <summary>Jim's laptop sleeps on its own; a pack interrupted by sleep is a pack to redo.</summary>
    private static void KeepAwake(bool on) =>
        _ = NativeMethods.SetThreadExecutionState(on
            ? NativeMethods.EsContinuous | NativeMethods.EsSystemRequired
            : NativeMethods.EsContinuous);

    /// <summary>What packing needs from the database, read without migrating it: pack must never
    /// change the station it is backing up.</summary>
    private sealed record StationFacts(
        string? LastMigration,
        int PendingReadingJobs,
        IReadOnlyList<string> BaseDirectories,
        IReadOnlyList<string> GroupDirectories)
    {
        public static StationFacts Read(string database)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            return new StationFacts(
                Strings(connection, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1")
                    .FirstOrDefault(),
                // JobType.Ocr = 0; JobState Pending = 0, InFlight = 1 (stored as integers).
                int.Parse(Strings(connection, "SELECT count(*) FROM Jobs WHERE Type = 0 AND State IN (0, 1)")[0],
                    CultureInfo.InvariantCulture),
                Strings(connection, "SELECT DISTINCT BaseDirectory FROM Profiles WHERE BaseDirectory <> ''"),
                Strings(connection, "SELECT DirectoryPath FROM Groups"));
        }

        private static List<string> Strings(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
            }

            return values;
        }
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
