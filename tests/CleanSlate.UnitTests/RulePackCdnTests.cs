using FluentAssertions;
using Xunit;
using CleanSlate.AgentRules;

namespace CleanSlate.UnitTests;

public class RulePackCdnTests
{
    [Fact]
    public void Compute_local_sha256()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateCdn", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "rules.json");
        File.WriteAllText(file, "{\"schemaVersion\":1}");
        try
        {
            var sha = RulePackCdnClient.ComputeLocalSha256(file);
            sha.Should().NotBeNullOrEmpty();
            sha!.Length.Should().Be(64);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Cdn_check_fails_gracefully_when_unreachable()
    {
        var client = new RulePackCdnClient("https://invalid.cleanslate.local/manifest.json");
        var result = await client.CheckAsync();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Updater_cdn_apply_rejects_bad_hash()
    {
        var updater = new RulePackUpdater();
        var dest = Path.Combine(Path.GetTempPath(), "CleanSlateCdn", Guid.NewGuid().ToString("N"), "r.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"rules\":[]}");
            var bad = updater.ApplyForCdn(bytes, dest, "deadbeef", "cdn-test");
            bad.Success.Should().BeFalse();
            bad.ErrorCode.Should().Be("E_HASH");

            var ok = updater.ApplyForCdn(bytes, dest, null, "cdn-test");
            ok.Success.Should().BeTrue(ok.ErrorMessage);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dest)!, true);
        }
    }
}
