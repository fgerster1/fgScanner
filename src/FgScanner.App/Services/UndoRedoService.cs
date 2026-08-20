using System.IO;

namespace FgScanner.App.Services;

public interface IUndoableAction
{
    string Description { get; }

    Task UndoAsync();

    Task RedoAsync();
}

/// <summary>
/// Undo/redo for page edits and reorders (NAPS2 parity; deletions are excluded — they go to Trash).
/// File-based edits snapshot before/after image bytes in a temp folder, capped to bound disk use.
/// </summary>
public sealed class UndoRedoService : IDisposable
{
    private const int MaxDepth = 25;
    private readonly List<IUndoableAction> _undo = [];
    private readonly List<IUndoableAction> _redo = [];

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string SnapshotRoot { get; } = Directory.CreateTempSubdirectory("fgscanner-undo").FullName;

    public void Push(IUndoableAction action)
    {
        _undo.Add(action);
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    public async Task UndoAsync()
    {
        if (!CanUndo)
        {
            return;
        }

        var action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        await action.UndoAsync();
        _redo.Add(action);
        Changed?.Invoke();
    }

    public async Task RedoAsync()
    {
        if (!CanRedo)
        {
            return;
        }

        var action = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        await action.RedoAsync();
        _undo.Add(action);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(SnapshotRoot))
            {
                Directory.Delete(SnapshotRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Restores an image file from byte snapshots, then lets the owner refresh checksum/UI.</summary>
public sealed class FileEditAction(
    string description, string livePath, string beforeSnapshot, string afterSnapshot,
    Func<Task> afterRestore) : IUndoableAction
{
    public string Description => description;

    public async Task UndoAsync()
    {
        File.Copy(beforeSnapshot, livePath, overwrite: true);
        await afterRestore();
    }

    public async Task RedoAsync()
    {
        File.Copy(afterSnapshot, livePath, overwrite: true);
        await afterRestore();
    }
}

/// <summary>Restores a captured page order.</summary>
public sealed class ReorderAction(
    string description, IReadOnlyList<Guid> before, IReadOnlyList<Guid> after,
    Func<IReadOnlyList<Guid>, Task> apply) : IUndoableAction
{
    public string Description => description;

    public Task UndoAsync() => apply(before);

    public Task RedoAsync() => apply(after);
}
