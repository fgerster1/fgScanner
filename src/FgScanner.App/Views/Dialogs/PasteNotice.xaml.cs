using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// "PDF copied — press Ctrl+V", in the corner of the screen, over the browser. Once the webmail
/// message opens the operator is looking at the browser, and FG Scanner's status line is behind it
/// (Franz, 2026-09-23). It closes itself, or on a click; it never takes focus from the message.
/// </summary>
public partial class PasteNotice : Window
{
    /// <summary>Long enough to outlast a slow compose page loading underneath it.</summary>
    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(20);

    private const double EdgeGap = 16;

    private static PasteNotice? _current;

    private readonly DispatcherTimer _timer;

    private PasteNotice(string headline, string detail)
    {
        InitializeComponent();
        HeadlineText.Text = headline;
        DetailText.Text = detail;
        _timer = new DispatcherTimer { Interval = ShownFor };
        _timer.Tick += (_, _) => Close();
        Loaded += (_, _) => PlaceInCorner();
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_current == this)
            {
                _current = null;
            }
        };
    }

    /// <summary>One at a time: a second send replaces the first send's notice rather than stacking.</summary>
    public static void Show(string headline, string detail)
    {
        _current?.Close();
        _current = new PasteNotice(headline, detail);
        _current.Show();
        _current._timer.Start();
    }

    /// <summary>
    /// The monitor FG Scanner is on — the browser most often opens there too. Placed after it has a
    /// size, since SizeToContent has nothing to measure before the first layout.
    /// </summary>
    private void PlaceInCorner()
    {
        var owner = Application.Current?.MainWindow;
        var area = owner is { IsLoaded: true } ? MonitorWorkArea.For(owner) : MonitorWorkArea.Primary();
        Left = area.Left + area.Width - ActualWidth - EdgeGap;
        Top = area.Top + area.Height - ActualHeight - EdgeGap;
    }

    private void OnClicked(object sender, MouseButtonEventArgs e) => Close();
}
