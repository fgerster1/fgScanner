using FgScanner.Core;
using Xunit;

namespace FgScanner.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Module_reports_its_name() => Assert.Equal("FgScanner.Core", CoreModule.Name);
}
