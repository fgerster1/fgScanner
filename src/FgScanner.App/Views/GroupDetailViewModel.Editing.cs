using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.App.Views.Dialogs;
using FgScanner.Core.Naming;
using FgScanner.Data;
using FgScanner.Scanning;
using FgScanner.Scanning.Editing;
using Microsoft.Win32;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>Phase-4 editing, reorder, export, import, print, and clipboard commands.</summary>
public sealed partial class GroupDetailViewModel
{
    /// <summary>Rows the next edit applies to: the multi-selection, else the focused row.</summary>
    private IReadOnlyList<DocumentRow> EditTargets =>
        SelectedRows.Count > 0 ? [.. SelectedRows] : SelectedRow is { } row ? [row] : [];

    private NamingContext BuildNamingContext() => new()
    {
        Timestamp = DateTime.Now,
        GroupName = Group.Name,
        DocumentSequence = SelectedRow?.Sequence ?? 0,
        PageSequence = SelectedRow?.Sequence ?? 0,
        FieldValues = SelectedRow?.Values.Snapshot() ?? new Dictionary<string, string?>(),
    };

    private async Task AfterPageFileChangedAsync(Guid pageId)
    {
        await _toolset.Reorder.RefreshChecksumAsync(pageId);
        await ReloadRowsAsync();
        if (Group.State == GroupState.Committed)
        {
            await _indexingService.ReexportAsync(Group.Id);
        }
    }

    private async Task ApplyEditsAsync(string description, IReadOnlyList<PageEdit> edits)
    {
        var targets = EditTargets;
        if (targets.Count == 0 || edits.Count == 0)
        {
            StatusText = targets.Count == 0 ? "Select a page first." : StatusText;
            return;
        }

        try
        {
            foreach (var row in targets)
            {
                var before = Path.Combine(UndoRedo.SnapshotRoot, Guid.NewGuid().ToString("N") + ".bin");
                File.Copy(row.ImagePath, before);
                foreach (var edit in edits)
                {
                    await _toolset.Editor.ApplyAsync(row.ImagePath, edit);
                }

                var after = Path.Combine(UndoRedo.SnapshotRoot, Guid.NewGuid().ToString("N") + ".bin");
                File.Copy(row.ImagePath, after);
                var pageId = row.PageId;
                UndoRedo.Push(new FileEditAction(
                    description, row.ImagePath, before, after, () => AfterPageFileChangedAsync(pageId)));
                await _toolset.Reorder.RefreshChecksumAsync(pageId);
            }

            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = $"{description} applied to {targets.Count} page(s).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Applying {Edit}", description);
            StatusText = $"{description} failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task RotateLeftAsync() => ApplyEditsAsync("Rotate left", [new PageEdit.Rotate(-90)]);

    [RelayCommand]
    private Task RotateRightAsync() => ApplyEditsAsync("Rotate right", [new PageEdit.Rotate(90)]);

    [RelayCommand]
    private Task FlipAsync() => ApplyEditsAsync("Flip", [new PageEdit.Rotate(180)]);

    [RelayCommand]
    private Task DeskewAsync() => ApplyEditsAsync("Deskew", [new PageEdit.Deskew()]);

    [RelayCommand]
    private async Task CustomRotateAsync()
    {
        var input = InputDialog.Show(
            Application.Current.MainWindow, "Custom rotation", "Angle in degrees (clockwise, e.g. -2.5):", "0");
        if (input is null
            || !double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)
            || degrees == 0)
        {
            return;
        }

        await ApplyEditsAsync($"Rotate {degrees}°", [new PageEdit.Rotate(degrees)]);
    }

    [RelayCommand]
    private async Task AdjustAsync()
    {
        if (EditTargets.Count == 0)
        {
            StatusText = "Select a page first.";
            return;
        }

        var dialog = new AdjustDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ApplyEditsAsync("Adjust", dialog.SelectedEdits);
    }

    /// <summary>Split/combine restructure documents, so like deletions they are not undoable.</summary>
    [RelayCommand]
    private async Task SplitAsync(string direction)
    {
        if (SelectedRow is not { } row)
        {
            StatusText = "Select a page first.";
            return;
        }

        try
        {
            var secondHalf = Path.Combine(
                Path.GetTempPath(), "fgscanner-split-" + Guid.NewGuid().ToString("N") + ".png");
            await _toolset.Editor.SplitAsync(row.ImagePath, direction == "v", secondHalf);
            await _toolset.Reorder.RefreshChecksumAsync(row.PageId);
            await _indexingService.InsertMissedPageAsync(Group.Id, secondHalf, row.Sequence + 1);
            UndoRedo.Clear();
            await AfterPageFileChangedAsync(row.PageId);
            StatusText = "Page split in two.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Splitting page");
            StatusText = $"Split failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CombineWithNextAsync()
    {
        if (SelectedRow is not { } row)
        {
            StatusText = "Select a page first.";
            return;
        }

        var next = Rows.FirstOrDefault(r => r.Sequence == row.Sequence + 1);
        if (next is null)
        {
            StatusText = "There is no page after this one to combine with.";
            return;
        }

        try
        {
            await _toolset.Editor.CombineAsync(row.ImagePath, next.ImagePath, vertical: true);
            await _toolset.Reorder.RefreshChecksumAsync(row.PageId);
            await _trashService.DeleteDocumentAsync(next.DocumentId);
            UndoRedo.Clear();
            await AfterPageFileChangedAsync(row.PageId);
            StatusText = "Pages combined; the second page is restorable from Trash.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Combining pages");
            StatusText = $"Combine failed: {ex.Message}";
        }
    }

    private async Task ReorderWithUndoAsync(string description, Func<Task> operation)
    {
        try
        {
            var before = await _toolset.Reorder.GetOrderAsync(Group.Id);
            await operation();
            var after = await _toolset.Reorder.GetOrderAsync(Group.Id);
            UndoRedo.Push(new ReorderAction(description, before, after, async order =>
            {
                await _toolset.Reorder.SetOrderAsync(Group.Id, order);
                await ReloadRowsAsync();
                if (Group.State == GroupState.Committed)
                {
                    await _indexingService.ReexportAsync(Group.Id);
                }
            }));
            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = $"{description} done.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Operation}", description);
            StatusText = $"{description} failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MoveUpAsync()
    {
        if (SelectedRow is { Sequence: > 1 } row)
        {
            await ReorderWithUndoAsync(
                "Move up", () => _toolset.Reorder.MoveAsync(Group.Id, row.DocumentId, row.Sequence - 1));
        }
    }

    [RelayCommand]
    private async Task MoveDownAsync()
    {
        if (SelectedRow is { } row && row.Sequence < Rows.Count)
        {
            await ReorderWithUndoAsync(
                "Move down", () => _toolset.Reorder.MoveAsync(Group.Id, row.DocumentId, row.Sequence + 1));
        }
    }

    [RelayCommand]
    private Task ReverseOrderAsync() =>
        ReorderWithUndoAsync("Reverse", () => _toolset.Reorder.ReverseAsync(Group.Id));

    [RelayCommand]
    private Task InterleaveAsync() =>
        ReorderWithUndoAsync("Interleave", () => _toolset.Reorder.InterleaveAsync(Group.Id));

    [RelayCommand]
    private Task DeinterleaveAsync() =>
        ReorderWithUndoAsync("Deinterleave", () => _toolset.Reorder.DeinterleaveAsync(Group.Id));

    private bool CanUndo() => UndoRedo.CanUndo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        await UndoRedo.UndoAsync();
        StatusText = "Undone.";
    }

    private bool CanRedo() => UndoRedo.CanRedo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        await UndoRedo.RedoAsync();
        StatusText = "Redone.";
    }

    /// <summary>Selection of 2+ exports just those pages; otherwise the whole group.</summary>
    private IReadOnlyList<string> ExportImagePaths =>
        (SelectedRows.Count > 1 ? SelectedRows.OrderBy(r => r.Sequence) : Rows.AsEnumerable())
        .Select(r => r.ImagePath)
        .ToList();

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (Rows.Count == 0)
        {
            StatusText = "Nothing to export.";
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export PDF",
            Filter = "PDF|*.pdf",
            FileName = NamingEngine.Expand("$(group)", BuildNamingContext()) + ".pdf",
        };
        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        var optionsDialog = new ExportPdfDialog(Group.Name) { Owner = Application.Current.MainWindow };
        if (optionsDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _toolset.PdfExport.ExportAsync(ExportImagePaths, saveDialog.FileName, optionsDialog.Options);
            StatusText = $"PDF exported: {saveDialog.FileName}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDF export");
            StatusText = $"PDF export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportImagesAsync()
    {
        if (Rows.Count == 0)
        {
            StatusText = "Nothing to export.";
            return;
        }

        var folderDialog = new OpenFolderDialog { Title = "Choose the export folder" };
        if (folderDialog.ShowDialog() != true)
        {
            return;
        }

        var dialog = new ExportImagesDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var baseName = NamingEngine.ExpandUnique(
                dialog.Pattern, BuildNamingContext(),
                name => Directory.EnumerateFiles(folderDialog.FolderName, name + ".*").Any());
            var written = await _toolset.ImageExport.ExportAsync(
                ExportImagePaths, folderDialog.FolderName, baseName, dialog.Options);
            StatusText = $"Exported {written.Count} file(s) to {folderDialog.FolderName}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Image export");
            StatusText = $"Image export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import PDF or images into this group",
            Filter = "Documents and images|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ImportFilePathsAsync(dialog.FileNames);
    }

    /// <summary>Shared by the Import button and "Open with FG Scanner" file-association launches.</summary>
    public async Task ImportFilePathsAsync(IReadOnlyList<string> files)
    {
        var storage = new TempPageStorage();
        try
        {
            var imported = new List<string>();
            foreach (var file in files)
            {
                imported.AddRange(await ImportOneFileAsync(file, storage));
            }

            if (imported.Count == 0)
            {
                return;
            }

            var triage = await _toolset.Triage.TriageAsync(Group, imported);
            var result = await _groupService.AdoptPagesAsync(
                Group.Id, triage.FilesToAdopt, triage.IsBlankFlagged);
            await _indexingService.ApplyInitialValuesAsync(
                Group.Id, [.. result.Adopted.Select(p => p.DocumentId)], _activeGroup.PendingValues);
            if (Group.ProfileId is { } profileId
                && (await _profileService.ListAsync()).FirstOrDefault(p => p.Id == profileId)?.OcrEnabled == true)
            {
                await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id);
            }

            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = $"Imported {result.Adopted.Count} page(s)."
                + (result.DuplicateSourceFiles.Count > 0
                    ? $" {result.DuplicateSourceFiles.Count} duplicate(s) skipped."
                    : "")
                + (triage.DroppedCount > 0
                    ? $" {triage.DroppedCount} page(s) dropped by capture policy (see journal.txt)."
                    : "");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import");
            StatusText = $"Import failed: {ex.Message}";
        }
        finally
        {
            storage.Cleanup();
        }
    }

    private async Task<List<string>> ImportOneFileAsync(string file, TempPageStorage storage)
    {
        var pages = new List<string>();
        try
        {
            await foreach (var page in _toolset.FileImport.ImportAsync(file, storage))
            {
                pages.Add(page.FilePath);
            }
        }
        catch (Exception) when (Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Most likely password-protected; ask once and retry.
            var password = InputDialog.Show(
                Application.Current.MainWindow, "Password required",
                $"\"{Path.GetFileName(file)}\" needs a password to open:");
            if (password is null)
            {
                return pages;
            }

            await foreach (var page in _toolset.FileImport.ImportAsync(file, storage, password))
            {
                pages.Add(page.FilePath);
            }
        }

        return pages;
    }

    [RelayCommand]
    private void Print()
    {
        var targets = EditTargets.Count > 0 ? EditTargets : [.. Rows];
        if (targets.Count == 0)
        {
            StatusText = "Nothing to print.";
            return;
        }

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                return;
            }

            var document = new FixedDocument();
            foreach (var row in targets.OrderBy(r => r.Sequence))
            {
                var page = new FixedPage
                {
                    Width = printDialog.PrintableAreaWidth,
                    Height = printDialog.PrintableAreaHeight,
                };
                page.Children.Add(new Image
                {
                    Source = LoadFullImage(row.ImagePath),
                    Stretch = Stretch.Uniform,
                    Width = printDialog.PrintableAreaWidth,
                    Height = printDialog.PrintableAreaHeight,
                });
                var content = new PageContent();
                ((IAddChild)content).AddChild(page);
                document.Pages.Add(content);
            }

            printDialog.PrintDocument(document.DocumentPaginator, $"FG Scanner — {Group.Name}");
            StatusText = $"Sent {targets.Count} page(s) to the printer.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Print");
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyPage()
    {
        if (SelectedRow is not { } row)
        {
            StatusText = "Select a page first.";
            return;
        }

        try
        {
            Clipboard.SetImage(LoadFullImage(row.ImagePath));
            StatusText = "Page image copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clipboard copy");
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    private static BitmapImage LoadFullImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Import staging area (same adoption path as scanning, without a recovery session).</summary>
    private sealed class TempPageStorage : IPageStorage
    {
        private readonly string _root =
            Directory.CreateTempSubdirectory("fgscanner-import").FullName;
        private int _next;

        public string ReserveNextPagePath(string extension)
        {
            _next++;
            return Path.Combine(
                _root, $"page-{_next.ToString("00000", CultureInfo.InvariantCulture)}.{extension}");
        }

        public void CommitPage(ScannedPage page)
        {
        }

        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
