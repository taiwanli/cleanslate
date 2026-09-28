using FluentAssertions;
using Xunit;
using CleanSlate.Leftover;

namespace CleanSlate.UnitTests;

public class OrphanScannerTests
{
    [Fact]
    public void Finds_folder_orphans_and_skips_installed_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateOrphanTests", Guid.NewGuid().ToString("N"));
        try
        {
            var appDir = Path.Combine(root, "GhostApp");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "ghost.exe"), "MZ");

            var installedDir = Path.Combine(root, "InstalledApp");
            Directory.CreateDirectory(installedDir);
            File.WriteAllText(Path.Combine(installedDir, "app.exe"), "MZ");

            var scanner = new OrphanScanner();
            var orphans = scanner.FindFolderOrphans(["InstalledApp"], [root]);

            orphans.Should().Contain(o => o.DisplayName == "GhostApp");
            orphans.Should().NotContain(o => o.DisplayName == "InstalledApp");
            orphans.Should().OnlyContain(o => !o.DefaultSelected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Skips_well_known_and_non_app_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateOrphanTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Microsoft"));
            var empty = Path.Combine(root, "EmptyFolder");
            Directory.CreateDirectory(empty);
            File.WriteAllText(Path.Combine(empty, "readme.txt"), "not an app");

            var scanner = new OrphanScanner();
            var orphans = scanner.FindFolderOrphans([], [root]);
            orphans.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Registry_orphan_scan_does_not_throw()
    {
        // Registry scan on a real machine may return empty or some entries; must not throw.
        var scanner = new OrphanScanner();
        var act = () => scanner.FindRegistryOrphans();
        act.Should().NotThrow();
    }
}
