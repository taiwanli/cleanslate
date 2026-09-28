using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class QuarantineServiceTests
{
    [Fact]
    public void Quarantine_and_restore_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateQuarantineTests", Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "CleanSlateQWork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            var source = Path.Combine(work, "victim.txt");
            File.WriteAllText(source, "important");

            var service = new Safety.QuarantineService(root);
            var result = service.QuarantineFile(source, jobId: "job1", reason: "test");

            result.Success.Should().BeTrue(result.ErrorMessage);
            result.Record!.SourcePath.Should().Be(source);
            File.Exists(source).Should().BeFalse();
            File.Exists(result.Record.QuarantinePath).Should().BeTrue();

            service.Restore(result.Record.QuarantinePath).Should().BeTrue();
            File.Exists(source).Should().BeTrue();
            File.ReadAllText(source).Should().Be("important");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    [Fact]
    public void Rejects_protected_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateQuarantineTests", Guid.NewGuid().ToString("N"));
        try
        {
            // Use a Windows protected path string without requiring the file to exist under it
            // For this test create under a non-protected temp path and verify policy blocks C:\Windows
            var service = new Safety.QuarantineService(root);
            var result = service.QuarantineFile(@"C:\Windows\System32\notepad.exe", "job", "should-not");
            // File exists but is protected → E_PATH_PROTECTED (or not found if file missing in some systems)
            result.Success.Should().BeFalse();
            result.ErrorCode.Should().BeOneOf("E_PATH_PROTECTED", "E_NOT_FOUND");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_file_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanSlateQuarantineTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new Safety.QuarantineService(root);
            var result = service.QuarantineFile(Path.Combine(root, "nope.txt"), "job", "x");
            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be("E_NOT_FOUND");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
