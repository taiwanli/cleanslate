using CleanSlate.Core;

namespace CleanSlate.Uninstall;

/// <summary>
/// Uninstalls multiple software entries sequentially; continues after individual failures.
/// </summary>
public sealed class BatchUninstallService
{
    private readonly UninstallExecutor _executor = new();

    public event Action<BatchUninstallProgress>? Progress;

    public async Task<BatchUninstallSummary> RunAsync(
        IReadOnlyList<SoftwareEntry> targets,
        bool silent = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatchUninstallResult>();
        int succeeded = 0, failed = 0, skipped = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = targets[i];
            Progress?.Invoke(new BatchUninstallProgress(i + 1, targets.Count, entry.DisplayName));

            if (entry.IsSystem)
            {
                skipped++;
                results.Add(new BatchUninstallResult(entry.DisplayName, false, "E_PROTECTED", "System component skipped"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.UninstallString))
            {
                skipped++;
                results.Add(new BatchUninstallResult(entry.DisplayName, false, "E_NO_UNINSTALL", "Missing UninstallString"));
                continue;
            }

            var command = UninstallCommandParser.Parse(entry.UninstallString, installLocation: entry.InstallLocation);
            

            var result = await _executor.ExecuteAsync(command, silent, elevate: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                succeeded++;
                results.Add(new BatchUninstallResult(entry.DisplayName, true, null, null));
            }
            else
            {
                failed++;
                results.Add(new BatchUninstallResult(entry.DisplayName, false, result.ErrorMessage, $"exit={result.ExitCode}"));
            }
        }

        return new BatchUninstallSummary(succeeded, failed, skipped, results);
    }
}

public sealed record BatchUninstallProgress(int Current, int Total, string Name);

public sealed record BatchUninstallResult(string Name, bool Success, string? Code, string? Message);

public sealed record BatchUninstallSummary(
    int Succeeded,
    int Failed,
    int Skipped,
    IReadOnlyList<BatchUninstallResult> Results);
