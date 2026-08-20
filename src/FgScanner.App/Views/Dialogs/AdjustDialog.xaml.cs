using System.Globalization;
using System.Windows;
using FgScanner.Scanning.Editing;

namespace FgScanner.App.Views.Dialogs;

public partial class AdjustDialog : Window
{
    public AdjustDialog() => InitializeComponent();

    /// <summary>The edits the user chose, in a sensible application order; empty if all neutral.</summary>
    public IReadOnlyList<PageEdit> SelectedEdits
    {
        get
        {
            var edits = new List<PageEdit>();
            AddCrop(edits);
            AddIfNonZero(edits, BrightnessSlider.Value, v => new PageEdit.Brightness(v));
            AddIfNonZero(edits, ContrastSlider.Value, v => new PageEdit.Contrast(v));
            AddIfNonZero(edits, HueSlider.Value, v => new PageEdit.Hue(v));
            AddIfNonZero(edits, SaturationSlider.Value, v => new PageEdit.Saturation(v));
            AddIfNonZero(edits, SharpenSlider.Value, v => new PageEdit.Sharpen(v));
            if (BlackWhiteCheck.IsChecked == true)
            {
                edits.Add(new PageEdit.BlackWhite((int)ThresholdSlider.Value));
            }

            return edits;
        }
    }

    private void AddCrop(List<PageEdit> edits)
    {
        var left = ParsePixels(CropLeft.Text);
        var top = ParsePixels(CropTop.Text);
        var right = ParsePixels(CropRight.Text);
        var bottom = ParsePixels(CropBottom.Text);
        if (left + top + right + bottom > 0)
        {
            edits.Add(new PageEdit.Crop(left, top, right, bottom));
        }
    }

    private static int ParsePixels(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;

    private static void AddIfNonZero(List<PageEdit> edits, double value, Func<int, PageEdit> factory)
    {
        if ((int)value != 0)
        {
            edits.Add(factory((int)value));
        }
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
