using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public SettingsViewModel(
        ProfileService profileService,
        TrashService trashService,
        AppSettingsService appSettings,
        LanguageManager languageManager)
    {
        _profileService = profileService;
        _trashService = trashService;
        _appSettings = appSettings;
        _languageManager = languageManager;
        DownloadableLanguages = [.. LanguageManager.KnownLanguages.Where(l => l.Code != "eng")];
        _ = ReloadAsync();
        _ = LoadOcrSettingsAsync();
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
            await _trashService.SetRetentionDaysAsync(Math.Max(1, RetentionDays));
            await _appSettings.SetAsync(
                AppSettingsService.OcrLanguagesKey,
                string.IsNullOrWhiteSpace(OcrLanguages) ? "eng" : OcrLanguages.Trim());
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
