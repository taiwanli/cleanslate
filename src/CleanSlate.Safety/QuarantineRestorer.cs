using System.IO;
using System.Text.Json;
using CleanSlate.Core;

namespace CleanSlate.Safety;

/// <summary>
/// Restores quarantined items and supports multi-file undo for a job.
/// </summary>
public sealed class QuarantineRestorer
{
    private readonly QuarantineService _quarantine;

    public QuarantineRestorer(QuarantineService? quarantine = null)
    {
        _quarantine = quarantine ?? new QuarantineService();
    }

    public QuarantineRestoreResult RestoreFile(string quarantinePath)
    {
        var ok = _quarantine.Restore(quarantinePath);
        return ok
            ? QuarantineRestoreResult.Ok(quarantinePath)
            : QuarantineRestoreResult.Fail(quarantinePath, "E_RESTORE_FAILED");
    }

    public QuarantineRestoreSummary RestoreJob(string jobId)
    {
        var root = _quarantine.Root;
        var jobDir = Path.Combine(root, jobId);
        if (!Directory.Exists(jobDir))
        {
            return new QuarantineRestoreSummary(0, 0, ["Job quarantine folder not found: " + jobDir]);
        }

        int restored = 0, failed = 0;
        var errors = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(jobDir, "*", SearchOption.AllDirectories))
        {
            var meta = Path.Combine(dir, "meta.json");
            if (!File.Exists(meta))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<QuarantineRecord>(File.ReadAllText(meta));
                if (record is null)
                {
                    continue;
                }

                if (File.Exists(record.QuarantinePath))
                {
                    if (_quarantine.Restore(record.QuarantinePath))
                    {
                        restored++;
                    }
                    else
                    {
                        failed++;
                        errors.Add(record.SourcePath);
                    }
                }
                else if (Directory.Exists(record.QuarantinePath))
                {
                    try
                    {
                        var dest = record.SourcePath;
                        if (!Directory.Exists(dest))
                        {
                            var parent = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            Directory.Move(record.QuarantinePath, dest);
                            restored++;
                        }
                        else
                        {
                            failed++;
                            errors.Add(dest + " already exists");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add(ex.Message);
                    }
                }
                else
                {
                    failed++;
                    errors.Add(record.QuarantinePath + " missing");
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(ex.Message);
            }
        }

        return new QuarantineRestoreSummary(restored, failed, errors);
    }

    public IReadOnlyList<string> ListJobFolders()
    {
        if (!Directory.Exists(_quarantine.Root))
        {
            return [];
        }

        return Directory.GetDirectories(_quarantine.Root)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
    }
}

public sealed record QuarantineRestoreResult(bool Success, string Path, string? Error)
{
    public static QuarantineRestoreResult Ok(string path) => new(true, path, null);

    public static QuarantineRestoreResult Fail(string path, string error) => new(false, path, error);
}

public sealed record QuarantineRestoreSummary(int Restored, int Failed, IReadOnlyList<string> Errors);
