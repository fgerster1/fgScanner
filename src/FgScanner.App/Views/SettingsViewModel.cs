using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Index;
using FgScanner.Data;
using FgScanner.Ocr;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>Profiles + index schema editor (PLAN §5.3) and app settings (trash retention).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ProfileService _profileService;
    private readonly TrashService _trashService;
    private readonly AppSettingsService _appSettings;
    private readonly LanguageManager _languageManager;

    private readonly FgScanner.Ai.CredentialStore _credentials;

    private readonly GroupService _groupService;

    public SettingsViewModel(
        ProfileService profileService,
        TrashService trashService,
        AppSettingsService appSettings,
        LanguageManager languageManager,
        FgScanner.Ai.CredentialStore credentials,
        GroupService groupService)
    {
        _profileService = profileService;
        _groupService = groupService;
        _trashService = trashService;
        _appSettings = appSettings;
        _languageManager = languageManager;
        _credentials = credentials;
        DownloadableLanguages = [.. LanguageManager.KnownLanguages.Where(l => l.Code != "eng")];
        _ = ReloadAsync();
        _ = LoadOcrSettingsAsync();
        _ = LoadAiSettingsAsync();
        _ = LoadShortcutsAsync();
        _ = LoadUpdatePreferenceAsync();
        _ = LoadFeatureSettingsAsync();
        Ready = LoadStoredValuesAsync();
    }

    /// <summary>
    /// Completes once the settings loaded from storage have landed. Save awaits it, so a save made
    /// before the screen finished loading cannot write a default over a stored value.
    /// </summary>
    public Task Ready { get; } = Task.CompletedTask;

    private async Task LoadStoredValuesAsync()
    {
        try
        {
            await LoadRetentionAsync();
            await LoadThemeAsync();
            await LoadSendWithAsync();
        }
        catch (Exception ex)
        {
            // Unobserved, these left the boxes showing defaults that a later save would then
            // write over what is actually stored.
            Log.Error(ex, "Loading stored settings");
        }
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ObservableCollection<FieldRow> Fields { get; } = [];

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = "";

    [ObservableProperty]
    private bool _exportCsv = true;

    [ObservableProperty]
    private bool _exportXlsx;

    [ObservableProperty]
    private bool _exportXml;

    [ObservableProperty]
    private bool _exportJson;

    [ObservableProperty]
    private string _csvDelimiter = ",";

    [ObservableProperty]
    private int _retentionDays = TrashService.DefaultRetentionDays;

    /// <summary>"system" | "light" | "dark".</summary>
    [ObservableProperty]
    private string _theme = "system";

    /// <summary>Instance property so XAML can bind it.</summary>
#pragma warning disable CA1822
    public IReadOnlyList<string> Themes => FgScanner.App.Services.ThemeSetting.Choices;
#pragma warning restore CA1822

    /// <summary>How this station sends mail (Email.SendWith) — Gmail on Franz's, Yahoo on Jim's.</summary>
    [ObservableProperty]
    private FgScanner.Core.Sharing.MailPath _sendWith;

    /// <summary>Instance property so XAML can bind it.</summary>
#pragma warning disable CA1822
    public IReadOnlyList<SendWithOption> SendWithChoices => SendWithOption.All;
#pragma warning restore CA1822

    /// <summary>What was stored when the screen opened, so a save can tell a change from a no-op.</summary>
    private int _retentionAsLoaded = TrashService.DefaultRetentionDays;

    public async Task LoadRetentionAsync()
    {
        _retentionAsLoaded = await _trashService.GetRetentionDaysAsync();
        RetentionDays = _retentionAsLoaded;
    }

    public async Task LoadSendWithAsync() =>
        SendWith = await Services.EmailSettings.ReadSendWithAsync(_appSettings);

    public async Task LoadThemeAsync() =>
        Theme = await _appSettings.GetAsync(FgScanner.App.Services.ThemeSetting.Key, "system");

    [ObservableProperty]
    private string _statusText = "";

    // ---- capture policy per profile (PLAN prompt 10) ----

    [ObservableProperty]
    private bool _separatorDetectionEnabled;

    [ObservableProperty]
    private bool _keepSeparatorPages;

    public IReadOnlyList<FgScanner.Core.Capture.BlankPagePolicy> BlankPolicies { get; } =
        Enum.GetValues<FgScanner.Core.Capture.BlankPagePolicy>();

    [ObservableProperty]
    private FgScanner.Core.Capture.BlankPagePolicy _blankPolicy;

    // ---- feature flags + commit hook (PLAN prompt 10) ----

    [ObservableProperty]
    private bool _featurePatchT;

    [ObservableProperty]
    private bool _featureBlankPolicy;

    /// <summary>Shows or hides the Search section; applied on save, no relaunch (SPEC-2026-004).</summary>
    [ObservableProperty]
    private bool _featureSearch = true;

    [ObservableProperty]
    private bool _featureCommitHook;

    [ObservableProperty]
    private bool _featureAutoOrient = true;

    [ObservableProperty]
    private bool _featurePreserveOriginals;

    [ObservableProperty]
    private string _hookCommandLine = "";

    [ObservableProperty]
    private string _hookWebhookUrl = "";

    private async Task LoadFeatureSettingsAsync()
    {
        try
        {
            FeaturePatchT = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.PatchT);
            FeatureBlankPolicy = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.BlankPolicy);
            FeatureSearch = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.Search);
            FeatureCommitHook = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.CommitHook);
            FeatureAutoOrient = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.AutoOrient);
            FeaturePreserveOriginals = await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.PreserveOriginals);
            HookCommandLine = await _appSettings.GetAsync(CommitHookRunner.CommandKey, "");
            HookWebhookUrl = await _appSettings.GetAsync(CommitHookRunner.WebhookUrlKey, "");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading feature settings");
        }
    }

    // ---- OCR (PLAN §5.5) ----

    [ObservableProperty]
    private bool _ocrEnabled;

    /// <summary>Tesseract language string, e.g. "eng" or "eng+deu".</summary>
    [ObservableProperty]
    private string _ocrLanguages = "eng";

    public ObservableCollection<string> InstalledLanguages { get; } = [];

    public IReadOnlyList<OcrLanguage> DownloadableLanguages { get; }

    [ObservableProperty]
    private OcrLanguage? _languageToInstall;

    private async Task LoadOcrSettingsAsync()
    {
        try
        {
            OcrLanguages = await _appSettings.GetAsync(AppSettingsService.OcrLanguagesKey, "eng");
            RefreshInstalledLanguages();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading OCR settings");
        }
    }

    private void RefreshInstalledLanguages()
    {
        InstalledLanguages.Clear();
        foreach (var code in _languageManager.InstalledCodes())
        {
            InstalledLanguages.Add(code);
        }
    }

    [RelayCommand]
    private async Task InstallLanguageAsync()
    {
        if (LanguageToInstall is not { } language)
        {
            return;
        }

        try
        {
            StatusText = $"Downloading {language.DisplayName}…";
            await _languageManager.InstallAsync(language.Code);
            RefreshInstalledLanguages();
            StatusText = $"{language.DisplayName} installed. Add \"{language.Code}\" to the language string to use it.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installing language {Code}", language.Code);
            StatusText = $"Download failed: {ex.Message}";
        }
    }

    // ---- AI descriptions (PLAN §5.6, §4 privacy) ----

    public const string ConsentSettingKey = "Ai.ConsentUtc";

    private const string PrivacyNotice =
        "AI descriptions send each page image to Google's Gemini API under YOUR Google account " +
        "and YOUR agreement with Google.\n\n" +
        "Important:\n" +
        "• Google's FREE tier may use submitted content for training and allows human review — " +
        "use a PAID-tier key for real documents.\n" +
        "• Users in the EEA, UK, and Switzerland are contractually required to use the paid tier.\n" +
        "• FG Scanner stores your key in Windows Credential Manager, never logs it, and sends " +
        "nothing anywhere until you start an AI run.\n\n" +
        "Enable the AI description feature?";

    [ObservableProperty]
    private string _apiKeyInput = "";

    [ObservableProperty]
    private bool _hasStoredKey;

    [ObservableProperty]
    private string _aiModel = FgScanner.Ai.GeminiDescriptionProvider.DefaultModel;

    [ObservableProperty]
    private string _spendText = "";

    private async Task LoadAiSettingsAsync()
    {
        try
        {
            HasStoredKey = _credentials.HasKey;
            AiModel = await _appSettings.GetAsync(
                AiWorker.ModelSettingKey, FgScanner.Ai.GeminiDescriptionProvider.DefaultModel);
            var spend = await _appSettings.GetAsync(AiWorker.SpendSettingKey, "0");
            SpendText = $"Cumulative AI spend this install: ${spend}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading AI settings");
        }
    }

    /// <summary>False when the installer's machine-wide privacy opt-out disabled AI (PLAN §4).
    /// Instance property so XAML can bind it.</summary>
#pragma warning disable CA1822
    public bool AiFeatureEnabled => !AiOptOutPolicy.IsOptedOut;
#pragma warning restore CA1822

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        var key = ApiKeyInput.Trim();
        if (!AiFeatureEnabled)
        {
            StatusText = "The AI feature was disabled machine-wide during installation.";
            return;
        }

        if (key.Length == 0)
        {
            StatusText = "Paste your Google AI Studio API key first.";
            return;
        }

        // First enable: privacy notice + recorded consent (SignPath requirement, PLAN §4).
        var consent = await _appSettings.GetAsync(ConsentSettingKey, "");
        if (consent.Length == 0)
        {
            var answer = System.Windows.MessageBox.Show(
                PrivacyNotice, "FG Scanner — AI privacy notice",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                StatusText = "AI feature not enabled.";
                return;
            }

            await _appSettings.SetAsync(
                ConsentSettingKey, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        StatusText = "Validating key with a 1-token test call…";
        try
        {
            using var provider = new FgScanner.Ai.GeminiDescriptionProvider(key, AiModel.Trim());
            var result = await provider.ValidateKeyAsync();
            // An empty MAX_TOKENS response still proves auth worked; only transport/auth failures matter.
            if (!result.Success && result.FailureReason?.Contains("HTTP 4", StringComparison.Ordinal) == true)
            {
                StatusText = $"Key rejected: {result.FailureReason}";
                return;
            }

            _credentials.SetKey(key);
            await _appSettings.SetAsync(AiWorker.ModelSettingKey, AiModel.Trim());
            ApiKeyInput = "";
            HasStoredKey = true;
            StatusText = "API key validated and stored in Windows Credential Manager.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Validating API key"); // exception text never contains the key
            StatusText = $"Validation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        _credentials.ClearKey();
        HasStoredKey = false;
        StatusText = "Stored API key cleared.";
    }

    // ---- updates ----

    [ObservableProperty]
    private bool _checkForUpdates = true;

    private async Task LoadUpdatePreferenceAsync()
    {
        try
        {
            CheckForUpdates = await _appSettings.GetAsync(UpdateService.NoUpdatePromptKey, "false") != "true";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading update preference");
        }
    }

    // ---- keyboard shortcuts (PLAN §5.8) ----

    public const string ShortcutsSettingKey = "Shortcuts.Json";

    public ObservableCollection<ShortcutRow> Shortcuts { get; } = [];

    /// <summary>Raised after saving so the shell re-applies key bindings immediately.</summary>
    public event Action<FgScanner.Core.ShortcutMap>? ShortcutsChanged;

    private async Task LoadShortcutsAsync()
    {
        try
        {
            var map = FgScanner.Core.ShortcutMap.FromJson(await _appSettings.GetAsync(ShortcutsSettingKey, ""));
            Shortcuts.Clear();
            foreach (var (action, gesture) in map.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                Shortcuts.Add(new ShortcutRow { Action = action, Gesture = gesture });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading shortcuts");
        }
    }

    [RelayCommand]
    private async Task ResetShortcutsAsync()
    {
        await _appSettings.SetAsync(ShortcutsSettingKey, "");
        await LoadShortcutsAsync();
        ShortcutsChanged?.Invoke(FgScanner.Core.ShortcutMap.CreateDefault());
        StatusText = "Shortcuts reset to the NAPS2 defaults.";
    }

    // ---- profile import/export (.fgprofile, PLAN §5.8) ----

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export profile",
            Filter = "FG Scanner profile|*.fgprofile",
            FileName = SelectedProfile.Name + ".fgprofile",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = await _profileService.ExportProfileJsonAsync(SelectedProfile.Id);
            await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
            StatusText = $"Profile exported to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exporting profile");
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import profile",
            Filter = "FG Scanner profile|*.fgprofile|All files|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var profile = await _profileService.ImportProfileJsonAsync(
                await System.IO.File.ReadAllTextAsync(dialog.FileName));
            await ReloadAsync();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
            await AnnounceAsync(SettingsChange.Profiles);
            StatusText = $"Profile \"{profile.Name}\" imported.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Importing profile");
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Announces what a settings change moved, so the other sections pick it up without the
    /// operator restarting the program (SPEC-2026-004).
    ///
    /// It returns a Task and the raiser awaits it: the section view models reload from the
    /// database, and a caller that reports "saved" before those reloads finish is telling the
    /// operator the change has landed when it has not. Awaiting also makes the propagation
    /// testable without sleeping.
    /// </summary>
    public event Func<SettingsChange, Task>? SettingsChanged;

    private async Task AnnounceAsync(SettingsChange change)
    {
        if (SettingsChanged is null)
        {
            return;
        }

        // Invoke() on a multicast Func returns only the LAST handler's task, so the earlier
        // handlers would be started and never awaited.
        foreach (var handler in SettingsChanged.GetInvocationList().Cast<Func<SettingsChange, Task>>())
        {
            try
            {
                await handler(change);
            }
            catch (Exception ex)
            {
                // One subscriber failing must not abandon the others, and must not turn a save
                // that committed every write into "Save failed" — the operator would redo a save
                // that already happened.
                Log.Error(ex, "Announcing settings change {Change}", change);
            }
        }
    }

    private async Task ReloadAsync()
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in await _profileService.ListAsync())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    async partial void OnSelectedProfileChanged(Profile? value)
    {
        Fields.Clear();
        if (value is null)
        {
            return;
        }

        ExportCsv = value.ExportCsv;
        ExportXlsx = value.ExportXlsx;
        ExportXml = value.ExportXml;
        ExportJson = value.ExportJson;
        CsvDelimiter = value.CsvDelimiter;
        OcrEnabled = value.OcrEnabled;
        SeparatorDetectionEnabled = value.SeparatorDetectionEnabled;
        KeepSeparatorPages = value.KeepSeparatorPages;
        BlankPolicy = value.BlankPolicy;
        try
        {
            var schema = await _profileService.GetLatestSchemaAsync(value.Id);
            foreach (var field in schema.Fields)
            {
                Fields.Add(FieldRow.From(field));
            }

            StatusText = $"Schema version {schema.Version} — saving field changes creates version {schema.Version + 1}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading schema");
        }
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            return;
        }

        try
        {
            var profile = await _profileService.CreateAsync(NewProfileName);
            NewProfileName = "";
            await ReloadAsync();
            SelectedProfile = Profiles.First(p => p.Id == profile.Id);
            await AnnounceAsync(SettingsChange.Profiles);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not create profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds the Evidence capture profile, or repairs one somebody has edited.
    ///
    /// The thirteen field NAMES are parsed by the JimsStuff importer, which
    /// cannot tell a misspelled field from an absent one -- so hand-entering
    /// them made a single typo a silent break in a legal pipeline. Pressing
    /// this twice is how a damaged profile is repaired, and re-seeding an
    /// intact one mints no schema version, so it costs nothing.
    /// </summary>
    [RelayCommand]
    private async Task CreateEvidenceProfileAsync()
    {
        try
        {
            var profile = await _profileService.EnsureEvidenceProfileAsync();
            await ReloadAsync();
            SelectedProfile = Profiles.First(p => p.Id == profile.Id);
            await AnnounceAsync(SettingsChange.Profiles);
            StatusText = $"\"{ProfileService.EvidenceProfileName}\" profile is ready — "
                       + $"{FgScanner.Core.Evidence.EvidenceProfile.Fields.Count} fields.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not build the Evidence profile: {ex.Message}";
        }
    }

    /// <summary>Base folder of the selected profile; empty means "ask every time".</summary>
    public string BaseDirectory => SelectedProfile?.BaseDirectory ?? "";

    [RelayCommand]
    private async Task PickBaseDirectoryAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            StatusText = "Select a profile first.";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Base folder for new \"{profile.Name}\" groups",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await SetBaseDirectoryAsync(profile.Id, dialog.FolderName);
    }

    [RelayCommand]
    private async Task ClearBaseDirectoryAsync()
    {
        if (SelectedProfile is { } profile)
        {
            await SetBaseDirectoryAsync(profile.Id, "");
        }
    }

    private async Task SetBaseDirectoryAsync(Guid profileId, string folder)
    {
        try
        {
            await _profileService.UpdateBaseDirectoryAsync(profileId, folder);
            await ReloadAsync();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profileId);
            OnPropertyChanged(nameof(BaseDirectory));
            await AnnounceAsync(SettingsChange.Profiles);
            StatusText = folder.Length == 0
                ? "New groups will ask where to go."
                : $"New groups will be created under {folder}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not set the base folder: {ex.Message}";
        }
    }

    /// <summary>Deletes the selected profile; refused while any group still uses it.</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            StatusText = "Select a profile to delete.";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete the profile \"{profile.Name}\"?\n\nIts index schema and field definitions go with it. "
                + "Groups already created keep their scans and their stored field values.",
            "Delete profile",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _profileService.DeleteAsync(profile.Id);
            await ReloadAsync();
            SelectedProfile = Profiles.FirstOrDefault();
            await AnnounceAsync(SettingsChange.Profiles);
            StatusText = $"Deleted profile \"{profile.Name}\".";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>Renames the selected profile using the name box beside "New profile".</summary>
    [RelayCommand]
    private async Task RenameProfileAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            StatusText = "Select a profile to rename.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            StatusText = $"Type the new name for \"{profile.Name}\" in the box first.";
            return;
        }

        try
        {
            var previous = profile.Name;
            await _profileService.RenameAsync(profile.Id, NewProfileName);
            var renamedId = profile.Id;
            NewProfileName = "";
            await ReloadAsync();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == renamedId);
            await AnnounceAsync(SettingsChange.Profiles);
            StatusText = $"Renamed \"{previous}\" to \"{SelectedProfile?.Name}\".";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not rename profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddField()
    {
        if (Fields.Count >= ProfileService.MaxFields)
        {
            StatusText = $"A profile can have at most {ProfileService.MaxFields} custom fields.";
            return;
        }

        Fields.Add(new FieldRow { Name = $"Field{Fields.Count + 1}" });
    }

    [RelayCommand]
    private void RemoveField(FieldRow? row)
    {
        if (row is not null)
        {
            Fields.Remove(row);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        // The constructor's loads are started, not awaited. Saving before the retention and theme
        // have landed would write the hard defaults over what is stored — which is the very bug
        // this spec exists to fix, reappearing as a race.
        await Ready;

        try
        {
            var versionBefore = (await _profileService.GetLatestSchemaAsync(SelectedProfile.Id)).Version;
            var schema = await _profileService.SaveSchemaAsync(
                SelectedProfile.Id, [.. Fields.Select(f => f.ToDefinition())]);
            var schemaChanged = schema.Version != versionBefore;
            await _profileService.UpdateExportSettingsAsync(
                SelectedProfile.Id, ExportCsv, ExportXlsx, ExportXml, ExportJson, CsvDelimiter);
            await _profileService.UpdateOcrEnabledAsync(SelectedProfile.Id, OcrEnabled);
            SelectedProfile.OcrEnabled = OcrEnabled;
            await _profileService.UpdateCapturePolicyAsync(
                SelectedProfile.Id, SeparatorDetectionEnabled, KeepSeparatorPages, BlankPolicy);
            SelectedProfile.SeparatorDetectionEnabled = SeparatorDetectionEnabled;
            SelectedProfile.KeepSeparatorPages = KeepSeparatorPages;
            SelectedProfile.BlankPolicy = BlankPolicy;
            await _appSettings.SetAsync(FeatureFlags.PatchT, FeaturePatchT ? "true" : "false");
            await _appSettings.SetAsync(FeatureFlags.BlankPolicy, FeatureBlankPolicy ? "true" : "false");
            await _appSettings.SetAsync(FeatureFlags.Search, FeatureSearch ? "true" : "false");
            await _appSettings.SetAsync(FeatureFlags.CommitHook, FeatureCommitHook ? "true" : "false");
            await _appSettings.SetAsync(FeatureFlags.AutoOrient, FeatureAutoOrient ? "true" : "false");
            await _appSettings.SetAsync(FeatureFlags.PreserveOriginals, FeaturePreserveOriginals ? "true" : "false");
            await _appSettings.SetAsync(CommitHookRunner.CommandKey, HookCommandLine.Trim());
            await _appSettings.SetAsync(CommitHookRunner.WebhookUrlKey, HookWebhookUrl.Trim());
            var retention = Math.Max(1, RetentionDays);
            await _trashService.SetRetentionDaysAsync(retention);

            // The model used to be written only when a new API key was pasted, so changing it
            // alone persisted nothing and the box silently reverted on the next launch.
            await _appSettings.SetAsync(AiWorker.ModelSettingKey, AiModel.Trim());

            await _appSettings.SetAsync(FgScanner.App.Services.ThemeSetting.Key, Theme);
            await Services.EmailSettings.WriteSendWithAsync(_appSettings, SendWith);
            FgScanner.App.Services.ThemeSetting.Apply(Theme);

            // Shortening the retention only matters once the purge runs, and that used to happen
            // at startup alone — so a change made now took effect at some unrelated launch later.
            var purged = 0;
            if (retention != _retentionAsLoaded)
            {
                purged = await _trashService.PurgeExpiredAsync();
                _retentionAsLoaded = retention;
            }

            await _appSettings.SetAsync(
                AppSettingsService.OcrLanguagesKey,
                string.IsNullOrWhiteSpace(OcrLanguages) ? "eng" : OcrLanguages.Trim());

            var shortcutMap = FgScanner.Core.ShortcutMap.CreateDefault();
            foreach (var row in Shortcuts)
            {
                shortcutMap.Set(row.Action, row.Gesture);
            }

            await _appSettings.SetAsync(
                UpdateService.NoUpdatePromptKey, CheckForUpdates ? "false" : "true");
            await _appSettings.SetAsync(ShortcutsSettingKey, shortcutMap.ToJson());
            ShortcutsChanged?.Invoke(shortcutMap);
            // Naming the way out matters: this used to state the consequence and stop, leaving the
            // user to conclude their fields simply did not work on the group they were looking at.
            var behind = await _groupService.GroupsOnOlderSchemaAsync(SelectedProfile!.Id);
            var purgedNote = purged == 0
                ? ""
                : $" Trash purge removed {purged} item(s) under the new retention.";
            StatusText = (behind.Count == 0
                ? $"Saved as field layout v{schema.Version}."
                : $"Saved as field layout v{schema.Version}. New groups use it; "
                    + $"{behind.Count} existing group(s) stay on their own — open one in Groups and "
                    + "choose \"Use latest field layout\" to move it.") + purgedNote;
            // Only claim the schema moved when a version was actually minted. SaveSchemaAsync
            // short-circuits on an identical layout, and announcing Schema anyway would rebuild
            // the open group's field editors — discarding a grid cell mid-edit — on every save,
            // including one that only changed the theme.
            var change = SettingsChange.Profiles | SettingsChange.Flags;
            if (schemaChanged)
            {
                change |= SettingsChange.Schema;
            }

            await AnnounceAsync(change);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }
}

public sealed partial class FieldRow : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    /// <summary>
    /// What the operator picked in the Type list, which offers Memo as a fifth entry. The stored
    /// type and the memo flag are derived from it — see <see cref="FieldDisplayType"/> for why a
    /// fifth <see cref="FieldType"/> is not an option.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText))]
    private FieldDisplayType _displayType = FieldDisplayType.Text;

    /// <summary>Only a text field takes a length; the grid disables that column otherwise.</summary>
    public bool IsText =>
        DisplayType is FieldDisplayType.Text or FieldDisplayType.Memo;

    partial void OnDisplayTypeChanged(FieldDisplayType value) => ClampLength();

    /// <summary>
    /// A length the new type cannot hold is dropped rather than carried. Memo allows 2000 and
    /// plain text 100, so a memo shortened to Text used to arrive at SaveSchemaAsync with a
    /// length it refuses — and that refusal is thrown before the rest of the save, taking theme,
    /// retention, feature flags and the shortcut map down with one field.
    /// </summary>
    private void ClampLength()
    {
        var limit = DisplayType switch
        {
            FieldDisplayType.Memo => ProfileService.MaxMemoLength,
            FieldDisplayType.Text => ProfileService.MaxTextLength,
            _ => 0,
        };

        if (Length > limit)
        {
            Length = null;
        }
    }

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private bool _sticky;

    [ObservableProperty]
    private FieldScope _scope;

    /// <summary>
    /// Sticky ("chain to the next row") is meaningless for a value the group owns, so ticking
    /// Batch clears it rather than letting the schema express both at once.
    /// </summary>
    partial void OnScopeChanged(FieldScope value)
    {
        if (value == FieldScope.Batch)
        {
            Sticky = false;
        }
    }

    [ObservableProperty]
    private string? _defaultValue;

    /// <summary>Semicolon-separated choices for List fields.</summary>
    [ObservableProperty]
    private string? _choices;

    [ObservableProperty]
    private int? _length;

    public static FieldRow From(FieldDefinition field)
    {
        var row = new FieldRow
        {
            Name = field.Name,
            DisplayType = FieldDisplayTypes.From(field.Type, field.Memo),
            Required = field.Required,
            Sticky = field.Sticky,
            Scope = field.Scope,
            DefaultValue = field.DefaultValue,
            Choices = field.ListChoicesJson is null
                ? null
                : string.Join("; ", IndexingService.ParseChoices(field.ListChoicesJson) ?? []),
            Length = field.MaxLength,
        };

        // The stored length is applied after the type, so the setter's own guard has already run
        // and been overwritten by the time the row exists.
        row.ClampLength();
        return row;
    }

    public FieldDefinition ToDefinition() => new()
    {
        Name = Name,
        Type = FieldDisplayTypes.ToStored(DisplayType).Type,
        Required = Required,
        Sticky = Sticky,
        Scope = Scope,
        DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
        ListChoicesJson = DisplayType == FieldDisplayType.List && !string.IsNullOrWhiteSpace(Choices)
            ? JsonSerializer.Serialize(Choices.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            : null,
        MaxLength = Length,
        Memo = FieldDisplayTypes.ToStored(DisplayType).Memo,
    };
}

public sealed partial class ShortcutRow : ObservableObject
{
    public required string Action { get; init; }

    [ObservableProperty]
    private string _gesture = "";
}

/// <summary>
/// One entry in Settings' "Send email with" list. ToString is the label, so a screen reader
/// announces "Gmail in the browser" rather than the type name.
/// </summary>
public sealed record SendWithOption(FgScanner.Core.Sharing.MailPath Value, string Label)
{
    public static IReadOnlyList<SendWithOption> All { get; } =
    [
        new(FgScanner.Core.Sharing.MailPath.MailApp, "A mail program on this PC"),
        new(FgScanner.Core.Sharing.MailPath.Gmail, "Gmail in the browser"),
        new(FgScanner.Core.Sharing.MailPath.Yahoo, "Yahoo Mail in the browser"),
    ];

    public override string ToString() => Label;
}
