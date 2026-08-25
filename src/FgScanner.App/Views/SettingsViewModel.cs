using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
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

    public SettingsViewModel(
        ProfileService profileService,
        TrashService trashService,
        AppSettingsService appSettings,
        LanguageManager languageManager,
        FgScanner.Ai.CredentialStore credentials)
    {
        _profileService = profileService;
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
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ObservableCollection<FieldRow> Fields { get; } = [];

    public IReadOnlyList<FieldType> FieldTypes { get; } = Enum.GetValues<FieldType>();

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

    /// <summary>Applies on next launch — the section list is built at startup.</summary>
    [ObservableProperty]
    private bool _featureSearch = true;

    [ObservableProperty]
    private bool _featureCommitHook;

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
            ProfilesChanged?.Invoke();
            StatusText = $"Profile \"{profile.Name}\" imported.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Importing profile");
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    /// <summary>Notifies other views (Groups) that profiles changed.</summary>
    public event Action? ProfilesChanged;

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
            ProfilesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not create profile: {ex.Message}";
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
            ProfilesChanged?.Invoke();
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

        try
        {
            var schema = await _profileService.SaveSchemaAsync(
                SelectedProfile.Id, [.. Fields.Select(f => f.ToDefinition())]);
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
            await _appSettings.SetAsync(CommitHookRunner.CommandKey, HookCommandLine.Trim());
            await _appSettings.SetAsync(CommitHookRunner.WebhookUrlKey, HookWebhookUrl.Trim());
            await _trashService.SetRetentionDaysAsync(Math.Max(1, RetentionDays));
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
            StatusText = $"Saved as schema version {schema.Version}. New groups use it; existing groups keep theirs.";
            ProfilesChanged?.Invoke();
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

    [ObservableProperty]
    private FieldType _type = FieldType.Text;

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private bool _sticky;

    [ObservableProperty]
    private string? _defaultValue;

    /// <summary>Semicolon-separated choices for List fields.</summary>
    [ObservableProperty]
    private string? _choices;

    public static FieldRow From(FieldDefinition field) => new()
    {
        Name = field.Name,
        Type = field.Type,
        Required = field.Required,
        Sticky = field.Sticky,
        DefaultValue = field.DefaultValue,
        Choices = field.ListChoicesJson is null
            ? null
            : string.Join("; ", IndexingService.ParseChoices(field.ListChoicesJson) ?? []),
    };

    public FieldDefinition ToDefinition() => new()
    {
        Name = Name,
        Type = Type,
        Required = Required,
        Sticky = Sticky,
        DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
        ListChoicesJson = Type == FieldType.List && !string.IsNullOrWhiteSpace(Choices)
            ? JsonSerializer.Serialize(Choices.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            : null,
    };
}

public sealed partial class ShortcutRow : ObservableObject
{
    public required string Action { get; init; }

    [ObservableProperty]
    private string _gesture = "";
}
