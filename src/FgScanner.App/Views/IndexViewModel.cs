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
            OnPropertyChanged(nameof(StagedAnswers));
            OnPropertyChanged(nameof(DeleteEnabled));
            SaveDraft();
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
                "This subject was decided on the portal; removing it is done there (phase 5).";
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
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.Date,
                SelectedDateQualifier, DateText.Trim());
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

        if (value)
        {
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null,
                IndexAnswerVocabulary.KeyFlagTrue);
        }
        else
        {
            // Unchecking un-stages this batch's answer; withdrawing a
            // PORTAL-decided key flag is the explicit Withdraw button.
            Staging.Unstage(document.AnchorPageId, IndexAnswerVocabulary.KeyFlag, null,
                IndexAnswerVocabulary.KeyFlagTrue);
        }
    }

    [ObservableProperty]
    private PackagePerson? _personToAdd;

    [ObservableProperty]
    private string _personQualifierToAdd = "mentioned";

    [RelayCommand]
    public void AddPerson()
    {
        if (SelectedDocument is { } document && PersonToAdd is { } person)
        {
            TryStage(document.AnchorPageId, IndexAnswerVocabulary.Person,
                PersonQualifierToAdd, person.Id);
        }
    }

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
        if (SelectedDocument is { } document)
        {
            TryStage(document.AnchorPageId, field, null, "");
        }
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

    [ObservableProperty]
    private string? _exportMessage;

    private string? _lastExportPath;

    private string? _exportedCoverage;

    /// <summary>What an export would have to cover to count as current:
    /// every staged answer plus who signs them.</summary>
    private string CurrentCoverage() =>
        System.Text.Json.JsonSerializer.Serialize(Staging.Snapshot())
        + "|" + DeciderNameProvider().Trim();

    public bool DeleteEnabled =>
        Package is not null
        && _exportedCoverage is not null
        && _exportedCoverage == CurrentCoverage();

    [RelayCommand]
    public void ExportResults()
    {
        if (Package is not { } package || _packageDirectory is not { } packageDir)
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
                answers.Add(new IndexAnswer(
                    document.AnchorPageId, staged.Field, staged.Qualifier, staged.Value,
                    decider,
                    DateTimeOffset.ParseExact(staged.DecidedAt, "yyyy-MM-dd'T'HH:mm:ss'Z'",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal)));
            }
        }

        if (answers.Count == 0)
        {
            ExportMessage = "There are no answers to export yet.";
            return;
        }

        var output = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(packageDir))!,
            package.PackageId + "-results.json");
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
        _exportedCoverage = CurrentCoverage();
        OnPropertyChanged(nameof(DeleteEnabled));
        ExportMessage = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Exported {0} answer(s) across {1} document(s) to {2}",
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

        var outcome = share(new Core.Sharing.ShareRequest(
            [path], Package?.PackageId + " index answers", MailPathProvider()));
        ExportMessage = outcome.Message;
        // Route and count only — never a recipient, never case content
        // (SPEC-2026-007 logging rule).
        Log.Information(
            "Index results share: surface Index, 1 file, json, route {Route}", outcome.Route);
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

        Directory.Delete(packageDir, recursive: true);
        _drafts.Delete(package.PackageId);
        Log.Information("Index package {PackageId} removed after export", package.PackageId);
        PendingDeleteText = null;
        Package = null;
        _packageDirectory = null;
        SelectedDocument = null;
        PackageSummary = null;
        _exportedCoverage = null;
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
            var package = await Task.Run(
                () => PackageReader.Open(packageDirectory, AppVersion));
            _packageDirectory = packageDirectory;
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
            var restored = _drafts.Load(
                package.PackageId, package.PackageChecksum, out var draftNotice);
            Staging.Restore(restored
                ?? new Dictionary<string, IReadOnlyList<StagedAnswer>>());
            DraftNotice = draftNotice;
            OnPropertyChanged(nameof(StagedAnswers));
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
            Package = null;
            _packageDirectory = null;
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
