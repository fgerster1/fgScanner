using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Index;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// The record editor (SPEC-2026-002): one page's values as a form, beside its image and above the
/// group's grid. The rows, the selection, the batch editors and the commands all belong to the
/// GroupDetailViewModel it was opened from, so a form edit takes the grid's own path to the database
/// and there is no second copy of a value to fall out of step.
/// </summary>
public sealed partial class RecordEditorViewModel : ObservableObject, IDisposable
{
    public const string NoPagesText = "No pages in this group yet";

    private readonly GroupDetailViewModel _detail;

    /// <summary>
    /// The page being edited, kept by id rather than by instance: a reload replaces every
    /// DocumentRow, and an editor still holding the old one would edit a row nothing shows (§16 R6).
    /// </summary>
    private Guid? _documentId;

    public RecordEditorViewModel(GroupDetailViewModel detail)
    {
        _detail = detail;
        Layout = new RecordEditorLayoutStore(detail.Settings);
        _detail.PropertyChanged += OnDetailPropertyChanged;
        _detail.Rows.CollectionChanged += OnRowsChanged;
        _detail.SchemaLoaded += BuildFormFields;
        BuildFormFields();

        if (_detail.SelectedRow is null && Rows.Count > 0)
        {
            _detail.SelectedRow = Rows[0];
        }
        else
        {
            Follow(_detail.SelectedRow);
        }
    }

    /// <summary>Raised after the form fields are rebuilt, so the window can rebuild its grid columns too.</summary>
    public event Action? FieldsRebuilt;

    public ObservableCollection<DocumentRow> Rows => _detail.Rows;

    public Group Group => _detail.Group;

    public IReadOnlyList<FieldDefinition> Fields => _detail.Fields;

    /// <summary>Where the editor's pane and memo sizes are remembered for this group.</summary>
    public RecordEditorLayoutStore Layout { get; }

    /// <summary>The group's status line, shown in the editor as well: a refused paste says why where the operator is looking.</summary>
    public string StatusText
    {
        get => _detail.StatusText;
        set => _detail.StatusText = value;
    }

    /// <summary>The group's batch fields, shown once at the top of the form (§05 N6).</summary>
    public ObservableCollection<FormField> BatchFormFields { get; } = [];

    public ObservableCollection<FormField> RowFormFields { get; } = [];

    public DocumentRow? SelectedRow
    {
        get => _detail.SelectedRow;
        set => _detail.SelectedRow = value;
    }

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => IsEmpty ? NoPagesText : "";

    public IAsyncRelayCommand DeleteSelectedCommand => _detail.DeleteSelectedCommand;

    public IAsyncRelayCommand AddMissedPageCommand => _detail.AddMissedPageCommand;

    public IAsyncRelayCommand ImportFilesCommand => _detail.ImportFilesCommand;

    public IAsyncRelayCommand UndoCommand => _detail.UndoCommand;

    public IAsyncRelayCommand RedoCommand => _detail.RedoCommand;

    private int CurrentIndex => _detail.SelectedRow is { } row ? Rows.IndexOf(row) : -1;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage()
    {
        var index = CurrentIndex;
        if (index < Rows.Count - 1)
        {
            SelectedRow = Rows[index + 1];
        }
    }

    private bool CanGoToNextPage() => CurrentIndex < Rows.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage()
    {
        var index = CurrentIndex;
        if (index > 0)
        {
            SelectedRow = Rows[index - 1];
        }
    }

    private bool CanGoToPreviousPage() => CurrentIndex > 0;

    public void Dispose()
    {
        _detail.PropertyChanged -= OnDetailPropertyChanged;
        _detail.Rows.CollectionChanged -= OnRowsChanged;
        _detail.SchemaLoaded -= BuildFormFields;
        foreach (var field in BatchFormFields.Concat(RowFormFields))
        {
            field.Dispose();
        }
    }

    /// <summary>Rebuilt when the group moves to another field layout, which also rebuilds the batch editors these wrap.</summary>
    private void BuildFormFields()
    {
        foreach (var stale in BatchFormFields.Concat(RowFormFields))
        {
            stale.Dispose();
        }

        BatchFormFields.Clear();
        RowFormFields.Clear();
        foreach (var editor in _detail.BatchFields)
        {
            BatchFormFields.Add(new FormField(editor));
        }

        foreach (var field in _detail.Fields.Where(f => f.Scope != FieldScope.Batch))
        {
            var formField = new FormField(field);
            formField.Bind(_detail.SelectedRow?.Values);
            RowFormFields.Add(formField);
        }

        FieldsRebuilt?.Invoke();
    }

    private void Follow(DocumentRow? row)
    {
        // A null selection is the grid clearing during a reload, not the user leaving the page, so
        // the id is kept for the row that is about to come back.
        if (row is not null)
        {
            _documentId = row.DocumentId;
        }

        foreach (var field in RowFormFields)
        {
            field.Bind(row?.Values);
        }

        OnPropertyChanged(nameof(SelectedRow));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }

    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupDetailViewModel.SelectedRow))
        {
            Follow(_detail.SelectedRow);
        }
        else if (e.PropertyName == nameof(GroupDetailViewModel.StatusText))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null && _documentId is { } id)
        {
            foreach (var row in e.NewItems.OfType<DocumentRow>())
            {
                if (row.DocumentId == id && !ReferenceEquals(row, _detail.SelectedRow))
                {
                    _detail.SelectedRow = row;
                }
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// One input on the record editor's form. It holds no value of its own: a row field reads and
/// writes the selected row's RowValues, the same object the grid's cell binds, and a batch field
/// reads and writes the group's PendingFieldEditor, the same one the Batch values panel binds.
/// </summary>
public sealed class FormField : ObservableObject, IDisposable
{
    private readonly PendingFieldEditor? _batchEditor;
    private RowValues? _values;

    public FormField(FieldDefinition field)
    {
        Field = field;
        Choices = IndexingService.ParseChoices(field.ListChoicesJson);
    }

    public FormField(PendingFieldEditor batchEditor)
        : this(batchEditor.Field)
    {
        _batchEditor = batchEditor;
        _batchEditor.PropertyChanged += OnBatchEditorChanged;
    }

    public FieldDefinition Field { get; }

    public string Name => Field.Name;

    /// <summary>Required fields carry the same "*" as their grid column header.</summary>
    public string Label => Field.Required ? $"{Field.Name} *" : Field.Name;

    public bool IsBatch => _batchEditor is not null;

    public bool IsText => Field.Type == FieldType.Text;

    /// <summary>A text field that is not a memo: one line, filling the form's width.</summary>
    public bool IsPlainText => IsText && !Field.Memo;

    /// <summary>
    /// Length and memo mean something only on Text. ProfileService clears them on other types when
    /// saving, but a definition can be built without passing through it.
    /// </summary>
    public bool IsMemo => IsText && Field.Memo;

    public bool IsDate => Field.Type == FieldType.Date;

    public bool IsList => Field.Type == FieldType.List;

    public IReadOnlyList<string>? Choices { get; }

    public int? MaxLength => IsText ? Field.MaxLength : null;

    /// <summary>The grid's own row values for the selected page; null when no page is selected.</summary>
    public RowValues? Values => _values;

    public bool IsEditable => IsBatch || _values is not null;

    public string? Value
    {
        get => _batchEditor is not null ? _batchEditor.Value : _values?[Field.Name];
        set
        {
            if (_batchEditor is not null)
            {
                _batchEditor.Value = value;
                return;
            }

            // Every write to a row is a database merge, and on a committed group a re-export, so a
            // binding that writes back what it read must not cost one.
            if (_values is null || string.Equals(_values[Field.Name], value, StringComparison.Ordinal))
            {
                return;
            }

            _values[Field.Name] = value;
        }
    }

    /// <summary>The value as a date for the picker, stored as ISO-8601 like PendingFieldEditor.DateValue.</summary>
    public DateTime? DateValue
    {
        get => DateTime.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        set => Value = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The message shown under the input. Row values skip batch fields because the grid would repeat
    /// one missing answer on every row; on the form a batch field is a single input again, so it is
    /// checked as one.
    /// </summary>
    public string? Error => _batchEditor is not null
        ? FieldValidator.Validate(Field.ToIndexFieldDef(), _batchEditor.Value, Choices)
        : _values?.GetErrors(ItemName).OfType<string>().FirstOrDefault();

    private string ItemName => $"Item[{Field.Name}]";

    internal void Bind(RowValues? values)
    {
        if (ReferenceEquals(values, _values))
        {
            return;
        }

        Unsubscribe();
        _values = values;
        if (_values is not null)
        {
            _values.PropertyChanged += OnValuesChanged;
            _values.ErrorsChanged += OnErrorsChanged;
        }

        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(IsEditable));
        RaiseValueChanged();
    }

    public void Dispose()
    {
        Unsubscribe();
        _values = null;
        if (_batchEditor is not null)
        {
            _batchEditor.PropertyChanged -= OnBatchEditorChanged;
        }
    }

    private void Unsubscribe()
    {
        if (_values is not null)
        {
            _values.PropertyChanged -= OnValuesChanged;
            _values.ErrorsChanged -= OnErrorsChanged;
        }
    }

    private void OnValuesChanged(object? sender, PropertyChangedEventArgs e)
    {
        // "Item[]" is RowValues.Load replacing every value at once.
        if (e.PropertyName == "Item[]" || string.Equals(e.PropertyName, ItemName, StringComparison.OrdinalIgnoreCase))
        {
            RaiseValueChanged();
        }
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, ItemName, StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(Error));
        }
    }

    private void OnBatchEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingFieldEditor.Value))
        {
            RaiseValueChanged();
        }
    }

    private void RaiseValueChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(DateValue));
        OnPropertyChanged(nameof(Error));
    }
}
