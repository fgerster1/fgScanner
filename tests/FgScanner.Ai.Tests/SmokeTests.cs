using FgScanner.Ai;
using Xunit;

namespace FgScanner.Ai.Tests;

public class SmokeTests
{
    [Fact]
    public void Module_reports_its_name() => Assert.Equal("FgScanner.Ai", AiModule.Name);
}
