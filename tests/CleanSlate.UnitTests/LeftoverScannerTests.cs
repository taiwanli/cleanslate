using CleanSlate.Core;
using CleanSlate.Leftover;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class LeftoverScannerTests
{
    [Fact]
    public void Finds_matching_files_and_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MyApp", "cache"));
            File.WriteAllText(Path.Combine(root, "MyApp", "cache", "data.bin"), "x");
            File.WriteAllText(Path.Combine(root, "MyApp.log"), "log");
            File.WriteAllText(Path.Combine(root, "Unrelated.txt"), "nope");

            var scanner = new LeftoverScanner();
            var items = scanner.ScanDirectories([root], ["MyApp"]);

            items.Should().NotBeEmpty();
            items.Should().Contain(i => i.PathOrKey.Contains("MyApp") && i.Kind == LeftoverKind.Folder);
            items.Should().Contain(i => i.PathOrKey.EndsWith("MyApp.log", StringComparison.OrdinalIgnoreCase));
            items.Should().NotContain(i => i.PathOrKey.Contains("Unrelated.txt"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Empty_hints_return_empty()
    {
        var scanner = new LeftoverScanner();
        scanner.ScanDirectories([Path.GetTempPath()], []).Should().BeEmpty();
    }
}
