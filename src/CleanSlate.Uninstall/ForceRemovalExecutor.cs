using System.Runtime.Versioning;
using CleanSlate.Core;
using CleanSlate.Safety;

namespace CleanSlate.Uninstall;

/// <summary>
/// Executes a force-removal plan with quarantine / elevated registry delete and safety gates.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ForceRemovalExecutor
{
    private readonly QuarantineService _quarantine;
    private readonly PathSafetyPolicy _policy = new();

    public ForceRemovalExecutor(QuarantineService? quarantine = null)
    {
        _quarantine = quarantine ?? new QuarantineService();
    }

    public ForceRemovalExecutionResult Execute(
        ForceRemovalPlan plan,
        string jobId,
        bool includeRegistry = true,
        ISet<string>? allowedPaths = null)
    {
        int removed = 0, skipped = 0, failed = 0;
        var details = new List<ForceRemovalItemResult>();

        foreach (var target in plan.Targets)
        {
            if (allowedPaths is not null && !allowedPaths.Contains(target.Path))
            {
                skipped++;
                details.Add(new ForceRemovalItemResult(target, "Skipped", "Not selected"));
                continue;
            }

            if (_policy.GetProtection(target.Path) == PathProtectionLevel.Protected)
            {
                skipped++;
                details.Add(new ForceRemovalItemResult(target, "Skipped", "Protected path"));
                continue;
            }

            try
            {
                switch (target.Kind)
                {
                    case ForceRemovalKind.File when File.Exists(target.Path):
                    {
                        var r = _quarantine.QuarantineFile(target.Path, jobId, "ForceRemoval:" + target.Hint);
                        if (r.Success)
                        {
                            removed++;
                            details.Add(new ForceRemovalItemResult(target, "Quarantined", r.Record?.QuarantinePath));
                        }
                        else
                        {
                            failed++;
                            details.Add(new ForceRemovalItemResult(target, "Failed", r.ErrorMessage));
                        }

                        break;
                    }

                    case ForceRemovalKind.Folder when Directory.Exists(target.Path):
                    {
                        var fq = _quarantine.QuarantineFolder(target.Path, jobId, "ForceRemoval:" + target.Hint);
                        if (fq.Success)
                        {
                            removed++;
                            details.Add(new ForceRemovalItemResult(target, "Quarantined", fq.Record?.QuarantinePath));
                        }
                        else
                        {
                            failed++;
                            details.Add(new ForceRemovalItemResult(target, "Failed", fq.ErrorMessage));
                        }

                        break;
                    }

                    case ForceRemovalKind.RegistryKey:
                    case ForceRemovalKind.UninstallEntry:
                    {
                        if (!includeRegistry)
                        {
                            skipped++;
                            details.Add(new ForceRemovalItemResult(target, "Skipped", "Registry excluded"));
                            break;
                        }

                        // Caller (workflow) uses ElevatedCleanupClient; here we only mark
                        skipped++;
                        details.Add(new ForceRemovalItemResult(target, "PendingElevated", "Requires elevated helper"));
                        break;
                    }

                    default:
                        skipped++;
                        details.Add(new ForceRemovalItemResult(target, "Skipped", "Missing target"));
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                details.Add(new ForceRemovalItemResult(target, "Failed", ex.Message));
            }
        }

        return new ForceRemovalExecutionResult(removed, skipped, failed, details);
    }
}

public sealed record ForceRemovalItemResult(ForceRemovalTarget Target, string Outcome, string? Message);

public sealed record ForceRemovalExecutionResult(
    int Removed,
    int Skipped,
    int Failed,
    IReadOnlyList<ForceRemovalItemResult> Details);
