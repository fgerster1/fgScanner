using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

public class SmokeTests
{
    [Fact]
    public void Shell_view_model_starts_on_scan_section()
    {
        var vm = new ShellViewModel();
        Assert.Equal(["Scan", "Groups", "Settings"], vm.Sections);
        Assert.Equal("Scan", vm.SelectedSection);
    }
}
