using System.Diagnostics;
using System.Text.RegularExpressions;
using CleanSlate.Core;

namespace CleanSlate.Uninstall;

public enum UninstallKind
{
    Unknown,
    Msi,
    Nsis,
    Inno,
    QuietCapable,
    PortableOrCustom,
}

public sealed record UninstallCommand(
    string Original,
    string FileName,
    string Arguments,
    UninstallKind Kind,
    bool SupportsSilent,
    string? SilentArguments,
    bool RequiresElevation);

public sealed record UninstallResult(
    bool Success,
    int ExitCode,
    UninstallKind Kind,
    string? ErrorMessage,
    bool UsedSilent,
    TimeSpan Duration);

/// <summary>
/// Parses UninstallString / QuietUninstallString into an executable command.
/// </summary>
public static class UninstallCommandParser
{
    public static UninstallCommand Parse(string? uninstallString, string? quietUninstallString = null, string? installLocation = null)
    {
        var raw = FirstNonEmpty(quietUninstallString, uninstallString) ?? string.Empty;
        var (file, args) = SplitCommand(raw);

        var kind = DetectKind(file, args, installLocation);
        var silentArgs = BuildSilentArgs(kind, args, quietUninstallString);

        return new UninstallCommand(
            Original: raw.Trim(),
            FileName: file,
            Arguments: args,
            Kind: kind,
            SupportsSilent: silentArgs is not null || !string.IsNullOrWhiteSpace(quietUninstallString),
            SilentArguments: FirstNonEmpty(quietUninstallString is not null ? args : null, silentArgs),
            RequiresElevation: true);
    }

    public static UninstallCommand? FromSoftwareEntry(SoftwareEntry entry) =>
        string.IsNullOrWhiteSpace(entry.UninstallString)
            ? null
            : Parse(entry.UninstallString, installLocation: entry.InstallLocation);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static (string FileName, string Arguments) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        // Quoted executable path (issue 004)
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 1)
            {
                var file = command[1..end];
                var rest = command[(end + 1)..].Trim();
                return (file, rest);
            }
        }

        // Unquoted path may contain spaces; terminate file at first ".exe" (case-insensitive)
        var idx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var fileLen = idx + 4;
            var file = command[..fileLen].Trim().Trim('"');
            var rest = command[fileLen..].Trim();
            return (file, rest);
        }

        var space = command.IndexOf(' ');
        return space < 0
            ? (command.Trim().Trim('"'), string.Empty)
            : (command[..space].Trim().Trim('"'), command[(space + 1)..].Trim());
    }

    private static UninstallKind DetectKind(string file, string args, string? installLocation)
    {
        var fileName = Path.GetFileName(file);
        var full = $"{file} {args}".ToLowerInvariant();

        if (fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(args, @"\{[0-9A-Fa-f\-]{36}\}", RegexOptions.CultureInvariant))
        {
            return UninstallKind.Msi;
        }

        if (HasSilentFlag(args, "/VERYSILENT") ||
            HasSilentFlag(args, "/SILENT") ||
            HasSilentFlag(args, "/SUPPRESSMSGBOXES"))
        {
            return UninstallKind.Inno;
        }

        if (HasSilentFlag(args, "/S") || fileName.Contains("uninst", StringComparison.OrdinalIgnoreCase))
        {
            // unins*.exe is Inno; /S alone is NSIS
            if (fileName.Contains("unins", StringComparison.OrdinalIgnoreCase) && !fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return UninstallKind.Inno;
            }

            return UninstallKind.Nsis;
        }

        if (fileName.Contains("unins", StringComparison.OrdinalIgnoreCase))
        {
            return UninstallKind.Inno;
        }

        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            return UninstallKind.PortableOrCustom;
        }

        return UninstallKind.Unknown;
    }

    /// <summary>Token-level flag match 鈥?/S must not match /SETUP (issue 004).</summary>
    private static bool HasSilentFlag(string args, string flag)
    {
        foreach (var token in SplitArgs(args))
        {
            if (token.Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // MSI style: /qn, /quiet as full tokens only
        }

        return false;
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        var inQ = false;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in args)
        {
            if (ch == '"')
            {
                inQ = !inQ;
                continue;
            }

            if (!inQ && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }

                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static string? BuildSilentArgs(UninstallKind kind, string existingArgs, string? quietUninstallString)
    {
        if (!string.IsNullOrWhiteSpace(quietUninstallString))
        {
            return existingArgs;
        }

        if (HasSilentFlag(existingArgs, "/S") ||
            HasSilentFlag(existingArgs, "/quiet") ||
            HasSilentFlag(existingArgs, "/qn") ||
            HasSilentFlag(existingArgs, "/VERYSILENT"))
        {
            return existingArgs;
        }

        return kind switch
        {
            UninstallKind.Msi => Append(existingArgs, "/qn /norestart"),
            UninstallKind.Nsis => Append(existingArgs, "/S"),
            UninstallKind.Inno => Append(existingArgs, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"),
            _ => null,
        };
    }

    private static string Append(string existing, string extra) =>
        string.IsNullOrWhiteSpace(existing) ? extra : $"{existing.Trim()} {extra}";
}

/// <summary>
/// Launches uninstall commands and terminates related processes first (best-effort).
/// </summary>
/// <summary>
/// Launches uninstall commands; process termination is explicit and user-confirmed (issue 006).
/// </summary>
public sealed class UninstallExecutor
{
    public TimeSpan ProcessKillTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public async Task<UninstallResult> ExecuteAsync(
        UninstallCommand command,
        bool silent = true,
        bool elevate = false,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.UtcNow;
        var args = silent && !string.IsNullOrWhiteSpace(command.SilentArguments)
            ? command.SilentArguments
            : command.Arguments;

        var fileName = ResolveExecutable(command.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || (!File.Exists(fileName) && !IsOnPath(fileName)))
        {
            var isMsiexec = Path.GetFileName(fileName ?? command.FileName)
                .Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase);
            if (!isMsiexec)
            {
                return new UninstallResult(false, -1, command.Kind, "E_UNINSTALL_NOT_FOUND", silent, DateTime.UtcNow - start);
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName ?? command.FileName,
                Arguments = args ?? string.Empty,
                CreateNoWindow = true,
            };

            // Issue 007: elevate machine-level uninstallers when requested
            if (elevate && command.RequiresElevation)
            {
                psi.UseShellExecute = true;
                psi.Verb = "runas";
            }
            else
            {
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new UninstallResult(false, -1, command.Kind, "E_UNINSTALL_START_FAILED", silent, DateTime.UtcNow - start);
            }

            if (!psi.UseShellExecute)
            {
                // drain to avoid pipe full
                _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
                _ = process.StandardError.ReadToEndAsync(cancellationToken);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var duration = DateTime.UtcNow - start;

            return process.ExitCode == 0
                ? new UninstallResult(true, process.ExitCode, command.Kind, null, silent, duration)
                : new UninstallResult(false, process.ExitCode, command.Kind, "E_UNINSTALL_NONZERO", silent, duration);
        }
        catch (OperationCanceledException)
        {
            return new UninstallResult(false, -1, command.Kind, "E_CANCELLED", silent, DateTime.UtcNow - start);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new UninstallResult(false, -1, command.Kind, "E_UAC_DENIED", silent, DateTime.UtcNow - start);
        }
        catch (Exception ex)
        {
            return new UninstallResult(false, -1, command.Kind, $"E_UNINSTALL_FAILED:{ex.Message}", silent, DateTime.UtcNow - start);
        }
    }

    /// <summary>List related processes without killing (issue 006).</summary>
    public IReadOnlyList<RelatedProcess> FindRelatedProcesses(IEnumerable<string> processNames, string? installLocation = null)
    {
        var names = NormalizeNames(processNames);
        var root = NormalizeDir(installLocation);
        var found = new List<RelatedProcess>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var path = TryGetPath(process);
                var matchName = names.Contains(name);
                var matchPath = root is not null && path is not null && IsUnderDirectory(path, root);

                if (matchName || matchPath)
                {
                    found.Add(new RelatedProcess(process.Id, name, path, matchPath ? "path" : "name"));
                }
            }
            catch
            {
                // access denied / exited
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>Kill only the listed processes (issue 006). Must be user-confirmed.</summary>
    public int TerminateProcesses(IEnumerable<RelatedProcess> targets)
    {
        var killed = 0;
        foreach (var t in targets)
        {
            try
            {
                using var p = Process.GetProcessById(t.Id);
                p.Kill(entireProcessTree: true);
                p.WaitForExit((int)ProcessKillTimeout.TotalMilliseconds);
                killed++;
            }
            catch
            {
                // already exited
            }
        }

        return killed;
    }

    /// <summary>Legacy helper: find + kill (only used where explicit confirm exists).</summary>
    public int TerminateRelatedProcesses(IEnumerable<string> processNames, string? installLocation = null)
    {
        var targets = FindRelatedProcesses(processNames, installLocation);
        return TerminateProcesses(targets);
    }

    private static HashSet<string> NormalizeNames(IEnumerable<string> processNames) =>
        processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim().Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().TrimEnd('\\') + "\\").TrimEnd('\\');
        }
        catch
        {
            return path.Trim().TrimEnd('\\');
        }
    }

    /// <summary>Path boundary: process path must be under root, not merely StartsWith overlap (issue 006).</summary>
    public static bool IsUnderDirectory(string filePath, string rootDir)
    {
        var root = NormalizeDir(rootDir);
        if (root is null)
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(filePath);
        }
        catch
        {
            return false;
        }

        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || full.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        if (File.Exists(fileName))
        {
            return fileName;
        }

        // system32 relative names e.g. rundll32.exe (issue 007)
        var name = Path.GetFileName(fileName);
        if (!string.IsNullOrWhiteSpace(name) && !fileName.Contains('\\') && !fileName.Contains('/'))
        {
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(sys, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", name);
            if (File.Exists(sys32))
            {
                return sys32;
            }
        }

        return fileName;
    }

    private static bool IsOnPath(string fileName)
    {
        try
        {
            var name = Path.GetFileName(fileName);
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(dir.Trim(), name)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static string? TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record RelatedProcess(int Id, string Name, string? Path, string MatchReason);
