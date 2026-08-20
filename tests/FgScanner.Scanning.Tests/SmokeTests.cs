using FgScanner.Scanning;
using Xunit;

namespace FgScanner.Scanning.Tests;

public class SmokeTests
{
    [Fact]
    public void Module_reports_its_name() => Assert.Equal("FgScanner.Scanning", ScanningModule.Name);
}
