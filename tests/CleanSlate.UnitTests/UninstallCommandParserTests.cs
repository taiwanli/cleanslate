using FluentAssertions;
using Xunit;
using CleanSlate.Uninstall;

namespace CleanSlate.UnitTests;

public class UninstallCommandParserTests
{
    [Fact]
    public void Parses_msiexec_msi_command()
    {
        var cmd = UninstallCommandParser.Parse(
            "MsiExec.exe /X{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}");

        cmd.Kind.Should().Be(UninstallKind.Msi);
        cmd.FileName.Should().Be("MsiExec.exe");
        cmd.SupportsSilent.Should().BeTrue();
        cmd.SilentArguments.Should().Contain("/qn");
    }

    [Fact]
    public void Parses_quoted_inno_uninstaller()
    {
        var cmd = UninstallCommandParser.Parse(
            "\"C:\\Program Files\\App\\unins000.exe\"");

        cmd.FileName.Should().Be(@"C:\Program Files\App\unins000.exe");
        cmd.Kind.Should().Be(UninstallKind.Inno);
        cmd.SilentArguments.Should().Contain("/VERYSILENT");
    }

    [Fact]
    public void Parses_ns_uninstall()
    {
        var cmd = UninstallCommandParser.Parse(
            "\"C:\\Program Files\\App\\uninstall.exe\" /S");

        cmd.Kind.Should().Be(UninstallKind.Nsis);
        cmd.SupportsSilent.Should().BeTrue();
    }

    [Fact]
    public void Prefers_quiet_uninstall_string()
    {
        var cmd = UninstallCommandParser.Parse(
            uninstallString: "\"C:\\a\\unins000.exe\"",
            quietUninstallString: "\"C:\\a\\unins000.exe\" /VERYSILENT");

        cmd.SilentArguments.Should().Contain("/VERYSILENT");
        cmd.SupportsSilent.Should().BeTrue();
    }

    [Fact]
    public void Parses_express_install_uninstall_string()
    {
        var cmd = UninstallCommandParser.Parse(
            "C:\\Program Files\\MyApp\\uninstall.exe -silent");

        cmd.FileName.Should().EndWith("uninstall.exe");
        cmd.Arguments.Should().Contain("-silent");
    }
}

public class UninstallExecutorTests
{
    [Fact]
    public async Task Missing_exe_returns_not_found()
    {
        var executor = new UninstallExecutor();
        var cmd = new UninstallCommand(
            Original: "C:\\nope\\unins000.exe",
            FileName: "C:\\nope\\unins000.exe",
            Arguments: "",
            Kind: UninstallKind.Inno,
            SupportsSilent: false,
            SilentArguments: null,
            RequiresElevation: false);

        var result = await executor.ExecuteAsync(cmd, silent: false, elevate: false);
        result.Success.Should().BeFalse();
        result.Success.Should().BeFalse(); result.ErrorMessage.Should().StartWith("E_");
    }

    [Fact]
    public void Terminate_related_processes_is_safe_with_empty_names()
    {
        var executor = new UninstallExecutor();
        // Should not throw and should not kill system processes when names are empty
        executor.TerminateRelatedProcesses([], null).Should().Be(0);
    }
}
