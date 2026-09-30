using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FgScanner.App.Views;
using FgScanner.Core.Sharing;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 /code-review max findings (2026-09-30), one test per
/// finding that a headless test can reach. Findings 1, 12, 14 and 15 are
/// pinned in Core.Tests; finding 2 (startup order) is a composition-root
/// change checked by reading App.OnStartup.
/// </summary>
public sealed class IndexReviewFixTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    private string DraftDir => Path.Combine(_root, "drafts");

    public IndexReviewFixTests()
    {
        Directory.CreateDirectory(_root);
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

    private string CopyOfGolden(string name = "pkg")
    {
        var source = IndexViewModelTests.GoldenDir();
        var target = Path.Combine(_root, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }

    /// <summary>Edit seed.json in a copy and re-hash it in the manifest so
    /// only the edit under test differs (the PackageReaderTests pattern).</summary>
    private static void EditSeed(string package, Action<JsonNode> edit)
    {
        var seedPath = Path.Combine(package, "seed.json");
        var seed = JsonNode.Parse(File.ReadAllText(seedPath))!;
        edit(seed);
        File.WriteAllText(seedPath, seed.ToJsonString());
        var manifestPath = Path.Combine(package, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["sha256"]!["seed.json"] = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(seedPath)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }

    private static JsonNode Document(JsonNode seed, string anchor) =>
        seed["documents"]!.AsArray().Single(d => (string?)d!["anchorPageId"] == anchor)!;

    private IndexViewModel CreateViewModel() => new(draftDirectory: DraftDir)
    {
        AppVersion = "0.6.0-test",
        DeciderNameProvider = () => "jim",
    };

    // --- finding 3 ---------------------------------------------------------

    [Fact]
    public async Task A_refused_draft_is_set_aside_before_anything_saves_over_it()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");
        vm.Staging.Stage("TOM99005", "key_flag", null, "true");
        var draftPath = Path.Combine(DraftDir, "PKG-0001.json");
        File.WriteAllText(draftPath, File.ReadAllText(draftPath)
            .Replace(vm.Package!.PackageChecksum, new string('0', 64)));
        var refusedBytes = File.ReadAllBytes(draftPath);

        var reopened = CreateViewModel();
        await reopened.OpenPackageAsync(package);
        reopened.Staging.Stage("TOM99009", "doc_type", null, "invoice-bill");

        // The refused draft still exists, byte for byte, somewhere the new
        // export's saves cannot reach — and the notice says where.
        var kept = Directory.EnumerateFiles(DraftDir, "PKG-0001*.json")
            .Where(p => p != draftPath)
            .Select(File.ReadAllBytes)
            .ToArray();
        Assert.Contains(kept, bytes => bytes.SequenceEqual(refusedBytes));
        Assert.Contains("set aside", reopened.DraftNotice);
    }

    // --- finding 4 ---------------------------------------------------------

    [Fact]
    public async Task Opening_a_package_resets_the_previous_packages_export_state()
    {
        var shared = new List<ShareRequest>();
        var vm = CreateViewModel();
        vm.Share = r =>
        {
            shared.Add(r);
            return new ShareOutcome(ShareRoute.Explorer, "ok");
        };
        await vm.OpenPackageAsync(CopyOfGolden("first"));
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");
        vm.ExportResultsCommand.Execute(null);
        vm.RequestDeleteCommand.Execute(null);
        Assert.NotNull(vm.PendingDeleteText);

        await vm.OpenPackageAsync(CopyOfGolden("second"));

        Assert.Null(vm.PendingDeleteText);
        Assert.Null(vm.ExportMessage);
        Assert.False(vm.DeleteEnabled);
        // The first package's results file must never leave under the
        // second package's name.
        vm.EmailResultsCommand.Execute(null);
        Assert.Empty(shared);
    }

    // --- finding 5 ---------------------------------------------------------

    [Fact]
    public async Task Staged_answers_is_a_fresh_list_after_every_change()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99009");
        vm.Staging.Stage("TOM99009", "subject", null, "tractor");
        var before = vm.StagedAnswers;

        vm.Staging.Stage("TOM99009", "subject", null, "lawyer-fees");

        // ItemsControl ignores a PropertyChanged that hands back the same
        // List reference, so the chips froze after the first answer.
        Assert.NotSame(before, vm.StagedAnswers);
        Assert.Single(before);
        Assert.Equal(2, vm.StagedAnswers.Count);
    }

    // --- finding 6 ---------------------------------------------------------

    [Fact]
    public async Task Removing_a_chip_moves_the_matching_control_back()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99009");

        vm.KeyFlagChecked = true;
        vm.SelectedDocType = vm.ActiveDocTypes.First(d => d.Id == "invoice-bill");
        Assert.Equal(2, vm.StagedAnswers.Count);

        foreach (var chip in vm.StagedAnswers.ToArray())
        {
            vm.RemoveStagedAnswerCommand.Execute(chip);
        }

        Assert.False(vm.KeyFlagChecked);
        Assert.Null(vm.SelectedDocType);
        // And re-picking the same type stages it again.
        vm.SelectedDocType = vm.ActiveDocTypes.First(d => d.Id == "invoice-bill");
        Assert.Single(vm.StagedAnswers);
    }

    [Fact]
    public async Task Accepting_a_doc_type_suggestion_moves_the_combo()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99005");

        vm.AcceptSuggestionCommand.Execute(vm.SelectedDocument.Suggestions[0]);

        Assert.Equal("card-note", vm.SelectedDocType?.Id);
    }

    // --- finding 7 ---------------------------------------------------------

    [Fact]
    public async Task Unchecking_a_portal_decided_key_flag_stages_a_withdrawal()
    {
        var package = CopyOfGolden();
        EditSeed(package, seed => Document(seed, "TOM99005")["decisions"]!.AsArray().Add(
            new JsonObject { ["field"] = "key_flag", ["qualifier"] = null, ["value"] = "true" }));
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99005");
        Assert.True(vm.KeyFlagChecked);

        vm.KeyFlagChecked = false;

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("key_flag", (string?)null, ""), (staged.Field, staged.Qualifier, staged.Value));
    }

    [Fact]
    public async Task A_date_withdrawal_names_the_portals_date_slot()
    {
        var package = CopyOfGolden();
        EditSeed(package, seed => Document(seed, "TOM99005")["decisions"]!.AsArray().Add(
            new JsonObject { ["field"] = "date", ["qualifier"] = "about", ["value"] = "2021-07-01" }));
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99005");

        vm.WithdrawCommand.Execute("date");

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("date", "about", ""), (staged.Field, staged.Qualifier, staged.Value));
        Assert.Null(vm.AnswerError);
    }

    [Fact]
    public async Task Changing_a_dates_qualifier_withdraws_the_old_portal_slot_at_export()
    {
        // The portal's slot for date includes the qualifier, so an 'exact'
        // answer beside a decided 'about' date would leave TWO current dates.
        var package = CopyOfGolden();
        EditSeed(package, seed => Document(seed, "TOM99005")["decisions"]!.AsArray().Add(
            new JsonObject { ["field"] = "date", ["qualifier"] = "about", ["value"] = "2021-07-01" }));
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99005", "date", "exact", "2021-07-18");

        vm.ExportResultsCommand.Execute(null);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(_root, "PKG-0001-results.json")));
        var rows = doc.RootElement.GetProperty("answers").EnumerateArray()
            .Select(a => (a.GetProperty("qualifier").GetString(), a.GetProperty("value").GetString()))
            .ToArray();
        Assert.Equal([("about", ""), ("exact", "2021-07-18")], rows);
    }

    // --- finding 8 ---------------------------------------------------------

    [Fact]
    public async Task A_draft_answer_with_a_bad_timestamp_refuses_the_export_without_crashing()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.Staging.Restore(new Dictionary<string, IReadOnlyList<StagedAnswer>>
        {
            ["TOM99001"] = [new StagedAnswer("subject", null, "lawyer-fees", "")],
        });

        vm.ExportResultsCommand.Execute(null);

        Assert.NotNull(vm.ExportMessage);
        Assert.False(File.Exists(Path.Combine(_root, "PKG-0001-results.json")));
    }

    // --- finding 9 ---------------------------------------------------------

    [Fact]
    public async Task A_delete_that_fails_part_way_reports_it_and_keeps_the_draft()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");
        vm.ExportResultsCommand.Execute(null);
        vm.RequestDeleteCommand.Execute(null);

        var image = Directory.EnumerateFiles(Path.Combine(package, "images")).Last();
        using (new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm.ConfirmDeleteCommand.Execute(null);
        }

        Assert.NotNull(vm.ExportMessage);
        Assert.Contains("could not", vm.ExportMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(DraftDir, "PKG-0001.json")));
    }

    // --- finding 10 --------------------------------------------------------

    [Fact]
    public async Task Answers_the_writer_would_refuse_are_refused_at_entry()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99009");

        vm.PersonToAdd = vm.People[0];
        vm.PersonQualifierToAdd = "witness";
        vm.AddPersonCommand.Execute(null);
        Assert.NotNull(vm.AnswerError);

        Assert.Throws<ArgumentException>(
            () => vm.Staging.Stage("TOM99009", "subject", null, "not-a-subject"));
        Assert.Throws<ArgumentException>(
            () => vm.Staging.Stage("TOM00000", "subject", null, "tractor"));
        Assert.Empty(vm.StagedAnswers);
    }

    // --- finding 11 --------------------------------------------------------

    [Fact]
    public async Task An_unreadable_draft_is_a_notice_and_blocks_answering_until_reopened()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");

        var reopened = CreateViewModel();
        using (new FileStream(Path.Combine(DraftDir, "PKG-0001.json"),
            FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await reopened.OpenPackageAsync(package);
        }

        Assert.NotNull(reopened.DraftNotice);
        Assert.NotNull(reopened.Package);
        // Answering now would save a fresh draft over the one that could
        // not be read.
        reopened.SelectedDocument = reopened.Documents[4];
        reopened.KeyFlagChecked = true;
        Assert.NotNull(reopened.AnswerError);
        Assert.Empty(reopened.Staging.ForDocument(reopened.Documents[4].AnchorPageId));
    }

    // --- finding 13 --------------------------------------------------------

    [Fact]
    public async Task The_results_email_carries_the_webmail_account()
    {
        ShareRequest? sent = null;
        var vm = CreateViewModel();
        vm.Share = r =>
        {
            sent = r;
            return new ShareOutcome(ShareRoute.Webmail, "ok");
        };
        vm.MailPathProvider = () => MailPath.Yahoo;
        vm.WebmailAccountProvider = () => "jim@example.com";
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");
        vm.ExportResultsCommand.Execute(null);

        vm.EmailResultsCommand.Execute(null);

        Assert.NotNull(sent);
        Assert.Equal(MailPath.Yahoo, sent!.Via);
        Assert.Equal("jim@example.com", sent.Account);
    }

    // --- cut-list: per-answer cost ------------------------------------------

    [Fact]
    public async Task Staging_an_answer_does_not_read_settings()
    {
        var reads = 0;
        var vm = new IndexViewModel(draftDirectory: DraftDir)
        {
            AppVersion = "0.6.0-test",
            DeciderNameProvider = () =>
            {
                reads++;
                return "jim";
            },
        };
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.Staging.Stage("TOM99001", "subject", null, "lawyer-fees");
        vm.ExportResultsCommand.Execute(null);
        var afterExport = reads;

        vm.Staging.Stage("TOM99005", "key_flag", null, "true");
        vm.Staging.Stage("TOM99009", "subject", null, "tractor");
        _ = vm.DeleteEnabled;

        Assert.Equal(afterExport, reads);
    }
}
