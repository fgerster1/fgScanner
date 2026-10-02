using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.Core.IndexPackages;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>
/// The Index section (SPEC-2026-008): opens an index package through the
/// production <see cref="PackageReader"/> and surfaces its refusals
/// VERBATIM — the refusal texts are the contract's own words, written for
/// the operator, and the newer-version update message in particular must
/// arrive unrephrased. A refusal leaves the section usable; a later good
/// open clears it.
/// </summary>
public sealed partial class IndexViewModel : ObservableObject
{
    /// <summary>Pinned into every open for results provenance; set at
    /// startup beside IndexingService.AppVersion, overridable in tests.</summary>
    public string AppVersion { get; init; } =
        typeof(IndexViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    [ObservableProperty]
    private IndexPackage? _package;

    [ObservableProperty]
    private string? _refusalMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private bool _busy;

    public bool CanOpen => !Busy;

    /// <summary>One line under the toolbar; null (hidden) until a package is open.</summary>
    [ObservableProperty]
    private string? _packageSummary;

    /// <summary>Where the open package lives on disk; image paths in the
    /// seed are relative to it.</summary>
    private string? _packageDirectory;

    /// <summary>Where results.json is written: beside the folder Jim opened,
    /// or — for a downloaded zip — beside the zip, because the folder it was
    /// extracted to is one he never sees (ADR-0015). Null when a folder sits
    /// at a drive root, which has no "beside".</summary>
    private string? _resultsDirectory;

    /// <summary>Where a downloaded batch zip is extracted. Out of Jim's way
    /// on purpose: he works with the zip and the answers file, never this
    /// folder. Settable for tests.</summary>
    public string ExtractDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FGScanner", "index-packages");

    /// <summary>The seed's documents in the seed's own order — the portal
    /// exported them by priority, and reordering here would silently
    /// defeat the planner.</summary>
    public IReadOnlyList<SeedDocument> Documents =>
        Package?.Documents ?? [];

    [ObservableProperty]
    private SeedDocument? _selectedDocument;

    private int _pageIndex;

    private readonly IndexDraftStore _drafts;

    public IndexViewModel(string? draftDirectory = null)
    {
        _drafts = new IndexDraftStore(draftDirectory ?? IndexDraftStore.DefaultDirectory);
        Staging.Changed += () =>
        {
            SaveDraft();
            // Every control re-reads staging, whichever control caused the
            // change: a chip's remove button or an Accept must move the combo
            // and the boxes too, or the screen shows what is not staged.
            if (!_refreshingPanel)
            {
                RefreshAnswerPanel();
            }

            OnPropertyChanged(nameof(DeleteEnabled));
        };
    }

    /// <summary>Something worth knowing about the draft (a mismatched or
    /// damaged one left untouched) — informational, not a refusal.</summary>
    [ObservableProperty]
    private string? _draftNotice;

    /// <summary>A draft save FAILED: the answer on screen is not on disk.
    /// Surfaced the moment it happens (SPEC-2026-008 §14).</summary>
    [ObservableProperty]
    private string? _draftError;

    private void SaveDraft()
    {
        if (Package is not { } package)
        {
            return;
        }

        try
        {
            _drafts.Save(package.PackageId, package.PackageChecksum, Staging.Snapshot());
            DraftError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DraftError = "This answer is NOT saved: " + ex.Message;
            Log.Error(ex, "Index draft save failed for {PackageId}", package.PackageId);
        }
    }

    partial void OnSelectedDocumentChanged(SeedDocument? value)
    {
        _pageIndex = 0;
        RaisePageChanged();
        RefreshAnswerPanel();
    }

    /// <summary>Absolute path of the page on screen; null with nothing open.</summary>
    public string? CurrentPageImagePath =>
        SelectedDocument is { } document && _packageDirectory is { } root
            ? System.IO.Path.Combine(
                root, document.Pages[_pageIndex].Image.Replace('/', System.IO.Path.DirectorySeparatorChar))
            : null;

    public string? PagePositionText => SelectedDocument is { } document
        ? string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} of {1}", _pageIndex + 1, document.Pages.Count)
        : null;

    [RelayCommand]
    public void NextPage()
    {
        // Clamped, never wrapped: a viewer that wraps silently is how an
        // operator reads page 1 believing it is page 3.
        if (SelectedDocument is { } document && _pageIndex < document.Pages.Count - 1)
        {
            _pageIndex++;
            RaisePageChanged();
        }
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (SelectedDocument is not null && _pageIndex > 0)
        {
            _pageIndex--;
            RaisePageChanged();
        }
    }

    private void RaisePageChanged()
    {
        OnPropertyChanged(nameof(CurrentPageImagePath));
        OnPropertyChanged(nameof(PagePositionText));
    }

    // ----- answering (SPEC-2026-008 AC-3) ------------------------------

    /// <summary>The staged-but-not-exported answers; the draft store
    /// persists it (Prompt 6) and the export drains it (Prompt 7).</summary>
    public AnswerStaging Staging { get; } = new();

    [ObservableProperty]
    private string? _answerError;

    /// <summary>What is staged for the document on screen, for the
    /// panel's summary and chips.</summary>
    public IReadOnlyList<StagedAnswer> StagedAnswers =>
        SelectedDocument is { } document ? Staging.ForDocument(document.AnchorPageId) : [];

    /// <summary>Doc types with soft-deleted rows hidden — membership stays
    /// valid at the writer; hiding inactive ids is this picker's job.</summary>
    public IReadOnlyList<PackageDocType> ActiveDocTypes =>
        Package?.DocTypes.Where(d => d.Active).ToArray() ?? [];

    public IReadOnlyList<PackagePerson> People => Package?.People ?? [];

    public IReadOnlyList<string> PersonQualifiers { get; } =
        [.. IndexAnswerVocabulary.PersonQualifiers.Order(StringComparer.Ordinal)];

    public IReadOnlyList<string> DateQualifiers { get; } =
        [.. IndexAnswerVocabulary.DateQualifiers.Order(StringComparer.Ordinal)];

    public sealed partial class SubjectChoice(
        IndexViewModel owner, PackageSubject subject) : ObservableObject
    {
        public PackageSubject Subject { get; } = subject;

        public string Label { get; } = subject.ParentId is null
            ? subject.Label
            : "    " + subject.Label;

        [ObservableProperty]
        private bool _isChecked;

        partial void OnIsCheckedChanged(bool value) =>
            owner.OnSubjectToggled(Subject.Id, value);
    }

    public IReadOnlyList<SubjectChoice> SubjectChoices { get; private set; } = [];

    private bool _refreshingPanel;

    private void OnSubjectToggled(string subjectId, bool isChecked)
    {
        if (_refreshingPanel || SelectedDocument is not { } document)
        {
            return;
        }

        var decidedOnPortal = document.Decisions.Any(
            d => d.Field == IndexAnswerVocabulary.Subject && d.Value == subjectId);
        if (isChecked)
        {
            // A subject the portal already decided is already current;
            // re-checking it stages nothing.
            if (!decidedOnPortal)
            {
                TryStage(document.AnchorPageId, IndexAnswerVocabulary.Subject, null, subjectId);
            }
        }
        else if (decidedOnPortal && !Staging.ForDocument(document.AnchorPageId)
            .Any(a => a.Field == IndexAnswerVocabulary.Subject && a.Value == subjectId))
        {
            // Unchecking a portal decision would need a withdrawal that can
            // name its target, which the contract cannot express (§03
            // non-goal) — the box springs back and says why.
            AnswerError =
                "This subject was decided on the portal; remove it there — Case Index → " +
                "the batch → the document → Remove.";
            SubjectChoices.Single(c => c.Subject.Id == subjectId).IsChecked = true;
        }
        else
        {
            Staging.Unstage(
                document.AnchorPageId, IndexAnswerVocabulary.Subject, null, subjectId);
        }
    }

    [ObservableProperty]
    private PackageDocType? _selectedDocType;

    partial void OnSelectedDocTypeChanged(PackageDocType? value)
    {
        if (!_refreshingPanel && value is not null && SelectedDocument is { } document)
        {
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.DocType, null, value.Id);
        }
    }

    [ObservableProperty]
    private string _dateText = "";

    [ObservableProperty]
    private string _selectedDateQualifier = "about";

    [RelayCommand]
    public void SetDate()
    {
        if (SelectedDocument is { } document)
        {
            // "Undated" is its own answer (ADR-0016): whatever is left in the
            // date box is not part of it.
            var value = SelectedDateQualifier == IndexAnswerVocabulary.Undated
                ? IndexAnswerVocabulary.Undated
                : DateText.Trim();
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.Date,
                SelectedDateQualifier, value);
        }
    }

    [ObservableProperty]
    private bool _keyFlagChecked;

    partial void OnKeyFlagCheckedChanged(bool value)
    {
        if (_refreshingPanel || SelectedDocument is not { } document)
        {
            return;
        }

        var portalFlagged = document.Decisions.Any(d =>
            d.Field == IndexAnswerVocabulary.KeyFlag && d.Value == IndexAnswerVocabulary.KeyFlagTrue);
        if (value)
        {
            if (portalFlagged)
            {
                // Back to the portal's own state: drop a staged withdrawal.
                Staging.Unstage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null, "");
            }
            else
            {
                TryStage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null,
                    IndexAnswerVocabulary.KeyFlagTrue);
            }
        }
        else if (portalFlagged)
        {
            // Unchecking a flag the portal decided is a withdrawal, and it
            // must reach the portal: a silent no-op here left the document
            // flagged while the screen said it was not.
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null, "");
        }
        else
        {
            Staging.Unstage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null,
                IndexAnswerVocabulary.KeyFlagTrue);
        }
    }

    [ObservableProperty]
    private PackagePerson? _personToAdd;

    [ObservableProperty]
    private string _personQualifierToAdd = "mentioned";

    /// <summary>What is typed in the person box (it is editable). A name on
    /// the list — display name or any spelling, case and punctuation ignored,
    /// the portal's own rule — becomes that person; any other name travels as
    /// typed, and the portal holds it as a proposal for Franz (ADR-0016).</summary>
    [ObservableProperty]
    private string _personText = "";

    [RelayCommand]
    public void AddPerson()
    {
        if (SelectedDocument is not { } document)
        {
            return;
        }

        var typed = PersonText.Trim();
        string value;
        if (typed.Length > 0)
        {
            value = MatchPerson(typed)?.Id ?? typed;
        }
        else if (PersonToAdd is { } person)
        {
            value = person.Id;
        }
        else
        {
            return;
        }

        TryStage(document.AnchorPageId, IndexAnswerVocabulary.Person,
            PersonQualifierToAdd, value);
    }

    private PackagePerson? MatchPerson(string typed)
    {
        var wanted = NormaliseName(typed);
        return People.FirstOrDefault(p => NormaliseName(p.DisplayName) == wanted
            || p.Aliases.Any(a => NormaliseName(a) == wanted));
    }

    /// <summary>JimsStuff app/persons.py normalise_alias: punctuation to
    /// spaces, spaces collapsed, lower-cased. A rare mismatch only means the
    /// name travels as typed and the portal matches it itself.</summary>
    private static string NormaliseName(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"[^\w\s]", " "),
            @"\s+", " ").Trim().ToLowerInvariant();

    [RelayCommand]
    public void RemoveStagedAnswer(StagedAnswer answer)
    {
        if (SelectedDocument is { } document)
        {
            Staging.Unstage(
                document.AnchorPageId, answer.Field, answer.Qualifier, answer.Value);
        }
    }

    /// <summary>Withdraw the portal's earlier single-value decision —
    /// legal only for single-value fields (an empty person/subject cannot
    /// name its target; AnswerStaging refuses it).</summary>
    [RelayCommand]
    public void Withdraw(string field)
    {
        if (SelectedDocument is not { } document)
        {
            return;
        }

        // A date's portal slot includes its qualifier, so its withdrawal must
        // name the qualifier the portal's date carries.
        var qualifier = field == IndexAnswerVocabulary.Date
            ? document.Decisions.FirstOrDefault(d => d.Field == IndexAnswerVocabulary.Date)?.Qualifier
                ?? SelectedDateQualifier
            : null;
        TryStage(document.AnchorPageId, field, qualifier, "");
    }

    [RelayCommand]
    public void AcceptSuggestion(SeedSuggestion suggestion)
    {
        if (SelectedDocument is { } document)
        {
            TryStage(document.AnchorPageId, suggestion.Field,
                suggestion.Qualifier, suggestion.Value);
        }
    }

    // ----- export, share, delete (SPEC-2026-008 AC-7/AC-8) -------------

    /// <summary>Who signs the answers — wired at startup to the
    /// Index.DeciderName setting (Q1); overridable in tests.</summary>
    public Func<string> DeciderNameProvider { get; set; } =
        () => Environment.UserName;

    /// <summary>Opens the operator's mail path with a file; never sends
    /// (SPEC-2026-007). Null hides the email button.</summary>
    public Func<Core.Sharing.ShareRequest, Core.Sharing.ShareOutcome>? Share { get; set; }

    public Func<Core.Sharing.MailPath> MailPathProvider { get; set; } =
        () => Core.Sharing.MailPath.MailApp;

    public Func<string> WebmailAccountProvider { get; set; } = () => "";

    [ObservableProperty]
    private string? _exportMessage;

    private string? _lastExportPath;

    /// <summary>Staging's version at the last successful export; delete
    /// arms only while nothing has changed since.</summary>
    private int? _exportedVersion;

    public bool DeleteEnabled =>
        Package is not null && _exportedVersion == Staging.Version;

    /// <summary>Everything that belongs to the package on screen and must
    /// not survive opening another: a stale export path would email one
    /// package's answers under the next one's name, and a stale delete
    /// banner would delete the next package's folder.</summary>
    private void ResetPackageState()
    {
        _lastExportPath = null;
        _exportedVersion = null;
        ExportMessage = null;
        PendingDeleteText = null;
        DraftError = null;
        DraftNotice = null;
        AnswerError = null;
        OnPropertyChanged(nameof(DeleteEnabled));
    }

    [RelayCommand]
    public void ExportResults()
    {
        if (Package is not { } package || _packageDirectory is null)
        {
            return;
        }

        var decider = DeciderNameProvider().Trim();
        if (decider.Length == 0)
        {
            ExportMessage =
                "The answers need a decider: set \"Indexer name\" in Settings first.";
            return;
        }

        var answers = new List<IndexAnswer>();
        foreach (var document in Documents)
        {
            foreach (var staged in Staging.ForDocument(document.AnchorPageId))
            {
                if (!DateTimeOffset.TryParseExact(staged.DecidedAt, "yyyy-MM-dd'T'HH:mm:ss'Z'",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var decidedAt))
                {
                    ExportMessage = $"The saved {staged.Field} answer for {document.AnchorPageId} " +
                        "has no valid decision time. Remove it and enter it again, then export.";
                    return;
                }

                // The portal's date slot includes the qualifier, so an answer in
                // a new qualifier would sit BESIDE the old date: withdraw the
                // old slot first.
                if (staged.Field == IndexAnswerVocabulary.Date
                    && document.Decisions.FirstOrDefault(d => d.Field == IndexAnswerVocabulary.Date)
                        is { Qualifier: { } oldQualifier }
                    && oldQualifier != staged.Qualifier)
                {
                    answers.Add(new IndexAnswer(
                        document.AnchorPageId, staged.Field, oldQualifier, "", decider, decidedAt));
                }

                answers.Add(new IndexAnswer(
                    document.AnchorPageId, staged.Field, staged.Qualifier, staged.Value,
                    decider, decidedAt));
            }
        }

        if (answers.Count == 0)
        {
            ExportMessage = "There are no answers to export yet.";
            return;
        }

        if (_resultsDirectory is not { } parent)
        {
            // A package folder at a drive root has no "beside".
            ExportMessage = "Move the package folder into a folder of its own " +
                "(not the top of a drive), open it again, and export.";
            return;
        }

        var output = Path.Combine(parent, package.PackageId + "-results.json");
        try
        {
            PackageWriter.WriteResults(package, answers, output);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            ExportMessage = ex.Message;
            return;
        }

        _lastExportPath = output;
        _exportedVersion = Staging.Version;
        OnPropertyChanged(nameof(DeleteEnabled));
        ExportMessage = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Exported {0} answer(s) across {1} document(s) to {2}. Next: upload this " +
            "file on the portal — Case Index → your batch → Upload answers.",
            answers.Count, Staging.AnsweredDocumentCount, output);
        Log.Information(
            "Index results exported: {PackageId}, {Answers} answer(s), app {AppVersion}",
            package.PackageId, answers.Count, AppVersion);
    }

    [RelayCommand]
    public void EmailResults()
    {
        if (Share is not { } share || _lastExportPath is not { } path || !File.Exists(path))
        {
            ExportMessage = "Export the results first, then email the file.";
            return;
        }

        // The account matters on webmail: with several signed in, the compose
        // page otherwise opens in whichever the browser holds first.
        var outcome = share(new Core.Sharing.ShareRequest(
            [path], Package?.PackageId + " index answers", MailPathProvider(),
            WebmailAccountProvider()));
        ExportMessage = outcome.Message;
        // The SPEC-2026-007 section-14 template: never a recipient, never content.
        Log.Information("Email: {Count} page(s) from {Surface} as {Format} via {Route}",
            1, "Index", "json", outcome.Route);
    }

    [ObservableProperty]
    private string? _pendingDeleteText;

    [RelayCommand]
    public void RequestDelete()
    {
        if (Package is not { } package || _packageDirectory is not { } packageDir
            || !DeleteEnabled)
        {
            return;
        }

        var imagesDir = Path.Combine(packageDir, "images");
        var images = Directory.Exists(imagesDir)
            ? Directory.EnumerateFiles(imagesDir).Count() : 0;
        PendingDeleteText = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}: {1} document(s), {2} image(s), results exported — remove the " +
            "package folder from this computer? The exported results file stays.",
            package.PackageId, package.Documents.Count, images);
    }

    [RelayCommand]
    public void CancelDelete() => PendingDeleteText = null;

    [RelayCommand]
    public void ConfirmDelete()
    {
        if (PendingDeleteText is null || Package is not { } package
            || _packageDirectory is not { } packageDir || !DeleteEnabled)
        {
            return;
        }

        PendingDeleteText = null;
        try
        {
            Directory.Delete(packageDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Part of the folder may be gone; the draft and the exported
            // results are kept, so nothing Jim decided is lost.
            ExportMessage = "The package folder could not be removed completely " +
                $"({ex.Message}). Your answers and the exported results are kept. Close " +
                "any program showing a page from it and remove it again.";
            Log.Warning(ex, "Index package {PackageId} delete incomplete", package.PackageId);
            return;
        }

        _drafts.Delete(package.PackageId);
        Log.Information("Index package {PackageId} removed after export", package.PackageId);
        Package = null;
        _packageDirectory = null;
        _resultsDirectory = null;
        SelectedDocument = null;
        PackageSummary = null;
        ResetPackageState();
        Staging.Restore(new Dictionary<string, IReadOnlyList<StagedAnswer>>());
        OnPropertyChanged(nameof(Documents));
        OnPropertyChanged(nameof(StagedAnswers));
        OnPropertyChanged(nameof(DeleteEnabled));
    }

    private void TryStage(string anchor, string field, string? qualifier, string value)
    {
        try
        {
            Staging.Stage(anchor, field, qualifier, value);
            AnswerError = null;
        }
        catch (ArgumentException ex)
        {
            AnswerError = ex.Message;
        }
    }

    /// <summary>Re-reads the panel's controls from staging + the seed's
    /// current decisions, without those setters staging anything back.</summary>
    private void RefreshAnswerPanel()
    {
        _refreshingPanel = true;
        try
        {
            var staged = StagedAnswers;
            var decisions = SelectedDocument?.Decisions ?? [];

            string? CurrentSingle(string field) =>
                staged.FirstOrDefault(a => a.Field == field)?.Value
                ?? decisions.FirstOrDefault(d => d.Field == field)?.Value;

            var docTypeId = CurrentSingle(IndexAnswerVocabulary.DocType);
            SelectedDocType = ActiveDocTypes.FirstOrDefault(d => d.Id == docTypeId);

            var stagedDate = staged.FirstOrDefault(a => a.Field == IndexAnswerVocabulary.Date);
            var seedDate = decisions.FirstOrDefault(d => d.Field == IndexAnswerVocabulary.Date);
            DateText = stagedDate?.Value ?? seedDate?.Value ?? "";
            SelectedDateQualifier =
                stagedDate?.Qualifier ?? seedDate?.Qualifier ?? "about";

            KeyFlagChecked = CurrentSingle(IndexAnswerVocabulary.KeyFlag)
                == IndexAnswerVocabulary.KeyFlagTrue;

            var subjects = staged
                .Where(a => a.Field == IndexAnswerVocabulary.Subject)
                .Select(a => a.Value)
                .Concat(decisions
                    .Where(d => d.Field == IndexAnswerVocabulary.Subject)
                    .Select(d => d.Value))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var choice in SubjectChoices)
            {
                choice.IsChecked = subjects.Contains(choice.Subject.Id);
            }

            OnPropertyChanged(nameof(StagedAnswers));
        }
        finally
        {
            _refreshingPanel = false;
        }
    }

    [RelayCommand]
    public async Task OpenPackageAsync(string packageDirectory)
    {
        Busy = true;
        try
        {
            // The reader hashes every file in the package; off the UI thread.
            // A downloaded zip is extracted first, then opened by the same
            // reader (ADR-0015).
            IndexPackage package;
            if (packageDirectory.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && File.Exists(packageDirectory))
            {
                var zipPath = Path.GetFullPath(packageDirectory);
                var opened = await Task.Run(
                    () => ZipPackageOpener.Open(zipPath, ExtractDirectory, AppVersion));
                package = opened.Package;
                _packageDirectory = opened.PackageDirectory;
                _resultsDirectory = Path.GetDirectoryName(zipPath);
            }
            else
            {
                package = await Task.Run(
                    () => PackageReader.Open(packageDirectory, AppVersion));
                _packageDirectory = packageDirectory;
                // The results go BESIDE the package, never inside it (the
                // package is checksummed law).
                _resultsDirectory = Path.GetDirectoryName(Path.GetFullPath(packageDirectory));
            }
            SelectedDocument = null;
            Package = package;
            SubjectChoices = package.Subjects
                .Where(s => s.Active)
                .OrderBy(s => s.ParentId ?? s.Id, StringComparer.Ordinal)
                .ThenBy(s => s.ParentId is null ? 0 : 1)
                .ThenBy(s => s.Label, StringComparer.Ordinal)
                .Select(s => new SubjectChoice(this, s))
                .ToArray();
            OnPropertyChanged(nameof(SubjectChoices));
            OnPropertyChanged(nameof(Documents));
            OnPropertyChanged(nameof(ActiveDocTypes));
            OnPropertyChanged(nameof(People));
            ResetPackageState();
            var restored = _drafts.Load(
                package.PackageId, package.PackageChecksum, out var loaded, out var draftNotice);
            Staging.Validator = new PackageWriter.AnswerValidator(package);
            Staging.BlockedReason = loaded == IndexDraftStore.LoadResult.Unreadable
                ? "Answering is paused: the saved draft could not be read. Close any " +
                  "program holding it and open the package again."
                : null;
            Staging.Restore(restored
                ?? new Dictionary<string, IReadOnlyList<StagedAnswer>>());
            DraftNotice = draftNotice;
            OnPropertyChanged(nameof(StagedAnswers));
            OnPropertyChanged(nameof(DeleteEnabled));
            RefusalMessage = null;
            PackageSummary = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} - {1} document(s).", package.PackageId, package.Documents.Count);
            Log.Information(
                "Index package {PackageId} opened: {Documents} document(s), app {AppVersion}",
                package.PackageId, package.Documents.Count, AppVersion);
        }
        catch (PackageRefusedException ex)
        {
            ResetPackageState();
            Staging.Validator = null;
            Package = null;
            _packageDirectory = null;
            _resultsDirectory = null;
            SelectedDocument = null;
            OnPropertyChanged(nameof(Documents));
            PackageSummary = null;
            RefusalMessage = ex.Message;
            Log.Information("Index package refused: {Reason}", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
