using FgScanner.Ocr;
using Xunit;

namespace FgScanner.Ocr.Tests;

public class SmokeTests
{
    [Fact]
    public void Module_reports_its_name() => Assert.Equal("FgScanner.Ocr", OcrModule.Name);
}
