using FluentAssertions;
using Xunit;
using CleanSlate.Safety;

namespace CleanSlate.UnitTests;

public class SafetyRegressionTests
{
    [Fact]
    public void Unc_paths_are_not_collapsed()
    {
        var policy = new PathSafetyPolicy();
        // UNC should not become \server\share
        var level = policy.GetProtection(@"\\server\share\App\file.txt");
        level.Should().Be(PathProtectionLevel.None);
    }

    [Fact]
    public void Folder_quarantine_writes_meta_and_restores()
    {
        var qRoot = Path.Combine(Path.GetTempPath(), "CleanSlateFolderQ", Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "CleanSlateFolderW", Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(work, "MyAppData");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.bin"), "x");
        try
        {
            var service = new QuarantineService(qRoot);
            var q = service.QuarantineFolder(srcDir, "jobF", "test folder");
            q.Success.Should().BeTrue(q.ErrorMessage);
            Directory.Exists(srcDir).Should().BeFalse();
            Directory.Exists(q.Record!.QuarantinePath).Should().BeTrue();
            File.Exists(Path.Combine(Path.GetDirectoryName(q.Record.QuarantinePath)!, "meta.json")).Should().BeTrue();

            var restorer = new QuarantineRestorer(service);
            var summary = restorer.RestoreJob("jobF");
            summary.Restored.Should().Be(1);
            Directory.Exists(srcDir).Should().BeTrue();
            File.Exists(Path.Combine(srcDir, "a.bin")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(qRoot)) Directory.Delete(qRoot, true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    [Fact]
    public void Desktop_content_not_deletable_by_policy()
    {
        var policy = new PathSafetyPolicy();
        policy.IsDeletionAllowed(@"C:\Users\x\Desktop\important.txt").Should().BeFalse();
    }
}
