using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FgScanner.Scanning;

namespace FgScanner.App.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
    }

    /// <summary>Mirrors the thumbnail selection into the view model; WPF cannot bind SelectedItems.</summary>
    private void OnPageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ScanViewModel vm)
        {
            return;
        }

        vm.SelectedPages.Clear();
        foreach (var item in PageList.SelectedItems)
        {
            if (item is ScannedPage page)
            {
                vm.SelectedPages.Add(page);
            }
        }
    }

    private void OnPageDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // The list also raises this for a double-click on its scroll bar, which opens nothing.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(PageList, source) is ListBoxItem { DataContext: ScannedPage page })
        {
            OpenViewer(page);
            e.Handled = true;
        }
    }

    private void OnPageListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        OpenViewer((Keyboard.FocusedElement as ListBoxItem)?.DataContext as ScannedPage);
        e.Handled = true;
    }

    private void OpenViewer(ScannedPage? page)
    {
        if (DataContext is not ScanViewModel vm || !vm.OpenPageViewerCommand.CanExecute(page))
        {
            return;
        }

        vm.OpenPageViewerCommand.Execute(page);

        // The view model moved the selection to the page the viewer closed on. The list has to show
        // it too, and hold focus there, or what looks selected and what Delete removes disagree.
        if (vm.SelectedPages.FirstOrDefault() is { } landed)
        {
            PageList.SelectedItem = landed;
            PageList.ScrollIntoView(landed);
            PageList.UpdateLayout();
            (PageList.ItemContainerGenerator.ContainerFromItem(landed) as ListBoxItem)?.Focus();
        }
    }
}
