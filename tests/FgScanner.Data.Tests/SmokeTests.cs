using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public class SmokeTests
{
    [Fact]
    public void Module_reports_its_name() => Assert.Equal("FgScanner.Data", DataModule.Name);
}
