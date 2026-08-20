using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Core.Index;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// One entry-grid row (= one document). Cell values bind via the indexer ("Values[Vendor]");
/// per-cell validation surfaces through INotifyDataErrorInfo so the DataGrid highlights
/// invalid cells with the reason (PLAN §5.4).
/// </summary>
public sealed class DocumentRow : ObservableObject
{
    public required Guid DocumentId { get; init; }

    public required int Sequence { get; init; }

    public required string ImageName { get; init; }

    public required string ImagePath { get; init; }

    public required string OcrStatus { get; init; }

    public required string AiStatus { get; init; }

    public required RowValues Values { get; init; }
}

public sealed class RowValues(IReadOnlyList<FieldDefinition> fields) : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>Raised after a cell value changes, so the VM can persist the row.</summary>
    public event Action? ValueChanged;

    public bool HasErrors => _errors.Count > 0;

    public IReadOnlyList<FieldDefinition> Fields { get; } = fields;

    public string? this[string fieldName]
    {
        get => _values.GetValueOrDefault(fieldName);
        set
        {
            _values[fieldName] = value;
            Revalidate(fieldName);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{fieldName}]"));
            ValueChanged?.Invoke();
        }
    }

    public IReadOnlyDictionary<string, string?> Snapshot() => new Dictionary<string, string?>(_values);

    public void Load(IReadOnlyDictionary<string, string?> values)
    {
        _values.Clear();
        foreach (var (key, value) in values)
        {
            _values[key] = value;
        }

        foreach (var field in Fields)
        {
            Revalidate(field.Name);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null)
        {
            return _errors.Values;
        }

        var field = ExtractField(propertyName);
        return field is not null && _errors.TryGetValue(field, out var error) ? new[] { error } : Array.Empty<string>();
    }

    private static string? ExtractField(string propertyName) =>
        propertyName.StartsWith("Item[", StringComparison.Ordinal) && propertyName.EndsWith(']')
            ? propertyName[5..^1]
            : null;

    private void Revalidate(string fieldName)
    {
        var field = Fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return;
        }

        var error = FieldValidator.Validate(
            new IndexFieldDef(field.Name, (IndexFieldType)field.Type, field.Required),
            _values.GetValueOrDefault(fieldName),
            IndexingService.ParseChoices(field.ListChoicesJson));
        if (error is null)
        {
            _errors.Remove(fieldName);
        }
        else
        {
            _errors[fieldName] = error;
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs($"Item[{fieldName}]"));
    }
}
