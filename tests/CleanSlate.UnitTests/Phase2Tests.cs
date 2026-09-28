using FluentAssertions;
using Xunit;
using CleanSlate.Inventory;
using CleanSlate.Safety;

namespace CleanSlate.UnitTests;

public class InstallMonitorTests
{
    [Fact]
    public void Snapshot_diff_detects_new_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateMon", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var monitor = new InstallMonitor();
            var before = monitor.Capture("before", [root]);

            var newFile = Path.Combine(root, "installed.txt");
            File.WriteAllText(newFile, "hello");

            var after = monitor.Capture("after", [root]);
            var diff = monitor.Diff(before, after);

            diff.AddedFiles.Should().Contain(newFile);
            diff.TotalChanges.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}

public class PolicyWhitelistTests
{
    [Fact]
    public void Blocks_protected_paths_and_denied_software()
    {
        var policy = PolicyWhitelist.Load();
        policy.IsPathBlocked(@"C:\Windows\System32\x.dll").Should().BeTrue();
        policy.IsPathBlocked(@"C:\Users\a\AppData\Local\App\cache").Should().BeFalse();
        policy.IsSoftwareAllowed("Anything").Should().BeTrue();

        // Force deny via document mutation through a new instance save/load
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlatePolicy", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "policy.json");
            File.WriteAllText(path, """
            {
              "BlockedPathPatterns": ["\\\\Desktop\\\\"],
              "AllowSoftwareNames": [],
              "DenySoftwareNames": ["Malware"],
              "AllowBatchUninstall": true,
              "AllowRegistryDelete": false
            }
            """);
            var loaded = PolicyWhitelist.Load(path);
            loaded.IsSoftwareAllowed("Malware App").Should().BeFalse();
            loaded.IsSoftwareAllowed("Good App").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}

public class SoftwareRelocatorTests
{
    [Fact]
    public void Relocate_creates_destination_and_journal()
    {
        var src = Path.Combine(Path.GetTempPath(), "CleanSlateReloc", "src-" + Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(Path.GetTempPath(), "CleanSlateReloc", "dest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "app.txt"), "content");
        try
        {
            var relocator = new SoftwareRelocator();
            var result = relocator.Relocate(src, dest, copyMode: true);
            result.Success.Should().BeTrue(result.ErrorMessage);
            File.Exists(Path.Combine(dest, "app.txt")).Should().BeTrue();
            File.Exists(result.JournalPath!).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
        }
    }
}

public class BrowserExtensionInventoryTests
{
    [Fact]
    public void Scan_does_not_throw()
    {
        var inv = new Leftover.BrowserExtensionInventory();
        var act = () => inv.Scan();
        act.Should().NotThrow();
    }
}
