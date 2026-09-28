using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanSlate.Core;

namespace CleanSlate.Safety;

/// <summary>
/// Persists undo log entries and exports registry .reg backups before deletion.
/// </summary>
public sealed class UndoLogService
{
    private readonly string _root;

    public UndoLogService(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate",
            "undo");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string BeginJob(string jobId, string mode, string? targetName = null)
    {
        var job = new UndoJob(
            JobId: jobId,
            Mode: mode,
            TargetName: targetName,
            UtcStarted: DateTime.UtcNow,
            Items: []);

        var path = JobPath(jobId);
        File.WriteAllText(path, JsonSerializer.Serialize(job, JsonOptions), Encoding.UTF8);
        return path;
    }

    public void Append(string jobId, UndoLogEntry entry)
    {
        var path = JobPath(jobId);
        var job = Load(jobId);
        if (job is null)
        {
            job = new UndoJob(jobId, "Unknown", null, DateTime.UtcNow, [entry]);
        }
        else
        {
            job = job with { Items = job.Items.Append(entry).ToList() };
        }

        File.WriteAllText(path, JsonSerializer.Serialize(job, JsonOptions), Encoding.UTF8);
    }

    public UndoJob? Load(string jobId)
    {
        var path = JobPath(jobId);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<UndoJob>(File.ReadAllText(path), JsonOptions);
    }

    public IReadOnlyList<UndoJob> ListJobs()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return Directory.EnumerateFiles(_root, "undo-*.json")
            .Select(f => JsonSerializer.Deserialize<UndoJob>(File.ReadAllText(f), JsonOptions))
            .Where(j => j is not null)
            .Select(j => j!)
            .OrderByDescending(j => j.UtcStarted)
            .ToList();
    }

    /// <summary>Export a single registry key to .reg (best-effort via reg.exe).</summary>
    public string? BackupRegistryKey(string keyPath, string jobId)
    {
        try
        {
            var backupDir = Path.Combine(_root, jobId, "reg");
            Directory.CreateDirectory(backupDir);

            // Normalize to reg.exe friendly: HKCU\Software\...
            var normalized = keyPath.Replace('/', '\\');
            if (normalized.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "HKCU" + normalized["HKEY_CURRENT_USER".Length..];
            }
            else if (normalized.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "HKLM" + normalized["HKEY_LOCAL_MACHINE".Length..];
            }

            var safeName = RegexSafeFile(normalized);
            var outPath = Path.Combine(backupDir, safeName + ".reg");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{normalized}\" \"{outPath}\" /y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(10_000);
            return process.ExitCode == 0 && File.Exists(outPath) ? outPath : null;
        }
        catch
        {
            return null;
        }
    }

    private string JobPath(string jobId) => Path.Combine(_root, $"undo-{Sanitize(jobId)}.json");

    private static string Sanitize(string jobId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            jobId = jobId.Replace(c, '_');
        }

        return jobId;
    }

    private static string RegexSafeFile(string keyPath) =>
        Sanitize(keyPath.Replace('\\', '_').Replace('/', '_'));
}

public sealed record UndoJob(
    string JobId,
    string Mode,
    string? TargetName,
    DateTime UtcStarted,
    IReadOnlyList<UndoLogEntry> Items);

public sealed record UndoLogEntry(
    string Action,
    string PathOrKey,
    RiskLevel Risk,
    string Outcome,
    string? BackupPath,
    string? Message,
    DateTime Utc);
