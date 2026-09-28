using CleanSlate.Core;
using CleanSlate.Safety;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class PathSafetyPolicyTests
{
    private readonly PathSafetyPolicy _policy = new();

    [Theory]
    [InlineData(@"C:\Windows\System32\evil.dll")]
    [InlineData(@"C:\Windows\Temp\cache.tmp")]
    [InlineData(@"C:\Program Files\WindowsApps\App\file.bin")]
    [InlineData(@"C:\Users\Public\Documents\resume.docx")]
    public void System_paths_are_protected(string path)
    {
        _policy.GetProtection(path).Should().Be(PathProtectionLevel.Protected);
        _policy.IsDeletionAllowed(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Users\alice\Desktop\")]
    [InlineData(@"C:\Users\alice\Documents\")]
    [InlineData(@"C:\Users\alice\Downloads\")]
    [InlineData(@"C:\Users\alice\Desktop\notes.docx")]
    [InlineData(@"C:\Users\alice\Documents\resume.pdf")]
    [InlineData(@"C:\Users\alice\Downloads\setup.exe")]
    [InlineData(@"C:\Users\alice\OneDrive\Work\report.xlsx")]
    public void User_known_folder_trees_are_protected(string path)
    {
        _policy.GetProtection(path).Should().Be(PathProtectionLevel.Protected);
        _policy.IsDeletionAllowed(path).Should().BeFalse();
        _policy.EvaluateRisk(path, LeftoverKind.File, 95).Should().Be(RiskLevel.Protected);
    }

    [Fact]
    public void Shared_runtime_is_shared_not_protected()
    {
        var path = @"C:\Program Files\Common Files\SomeVendor\lib.dll";
        _policy.GetProtection(path).Should().Be(PathProtectionLevel.Shared);
        _policy.IsDeletionAllowed(path).Should().BeTrue();
        _policy.EvaluateRisk(path, LeftoverKind.File, 90).Should().Be(RiskLevel.High);
    }

    [Fact]
    public void High_confidence_appdata_file_is_low_risk()
    {
        var path = @"C:\Users\alice\AppData\Local\MyApp\cache.bin";
        _policy.GetProtection(path).Should().Be(PathProtectionLevel.None);
        var risk = _policy.EvaluateRisk(path, LeftoverKind.File, 90);
        risk.Should().Be(RiskLevel.Low);
        _policy.CanDefaultSelect(risk, 90).Should().BeTrue();
    }

    [Fact]
    public void Low_confidence_item_is_high_risk()
    {
        var path = @"C:\Users\alice\AppData\Local\Temp\mystery";
        var risk = _policy.EvaluateRisk(path, LeftoverKind.File, 30);
        risk.Should().Be(RiskLevel.High);
        _policy.CanDefaultSelect(risk, 30).Should().BeFalse();
    }

    [Fact]
    public void Protected_items_never_deletable()
    {
        _policy.EvaluateRisk(@"C:\Windows\notepad.exe", LeftoverKind.File, 100)
            .Should().Be(RiskLevel.Protected);
    }
}
