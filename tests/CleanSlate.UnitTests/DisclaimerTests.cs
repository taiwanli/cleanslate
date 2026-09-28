using FluentAssertions;
using Xunit;
using CleanSlate.Safety;

namespace CleanSlate.UnitTests;

public class DisclaimerTests
{
    [Fact]
    public void Disclaimer_text_covers_key_risks()
    {
        var text = Disclaimer.ChineseText;
        text.Should().Contain("免责");
        text.Should().Contain("风险");
        text.Should().Contain("备份");
        text.Should().Contain("责任");
        text.Should().Contain("不同意");
    }

    [Fact]
    public void Accept_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "CleanSlateDisclaimer", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            Disclaimer.IsAccepted(path).Should().BeFalse();
            Disclaimer.MarkAccepted(path);
            Disclaimer.IsAccepted(path).Should().BeTrue();
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
