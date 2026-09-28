using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.Safety;

/// <summary>
/// Moves files to quarantine (or recycle bin later) and records undo metadata.
/// </summary>
public sealed class QuarantineService
{
    private readonly string _root;
    private readonly PathSafetyPolicy _policy = new();

    public QuarantineService(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate",
            "quarantine");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public QuarantineResult QuarantineFile(string sourcePath, string jobId, string reason)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return QuarantineResult.Fail(sourcePath, "E_NOT_FOUND", "File not found.");
        }

        if (!_policy.IsDeletionAllowed(sourcePath))
        {
            return QuarantineResult.Fail(sourcePath, "E_PATH_PROTECTED", "Path is protected.");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var safeName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return QuarantineResult.Fail(sourcePath, "E_NAME", "Invalid file name.");
        }

        var destDir = Path.Combine(_root, jobId, stamp);
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, safeName);

        try
        {
            File.Move(sourcePath, destPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return QuarantineResult.Fail(sourcePath, "E_QUARANTINE_FAILED", ex.Message);
        }

        var record = new QuarantineRecord(
            SourcePath: sourcePath,
            QuarantinePath: destPath,
            JobId: jobId,
            Reason: reason,
            UtcTimestamp: DateTime.UtcNow);

        var metaPath = Path.Combine(destDir, "meta.json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(record, JsonOptions));

        return QuarantineResult.Ok(record);
    }

    /// <summary>Move a folder into quarantine and write restore metadata.</summary>
    public QuarantineResult QuarantineFolder(string sourcePath, string jobId, string reason)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
        {
            return QuarantineResult.Fail(sourcePath, "E_NOT_FOUND", "Folder not found.");
        }

        if (!_policy.IsDeletionAllowed(sourcePath))
        {
            return QuarantineResult.Fail(sourcePath, "E_PATH_PROTECTED", "Path is protected.");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var safeName = Path.GetFileName(sourcePath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return QuarantineResult.Fail(sourcePath, "E_NAME", "Invalid folder name.");
        }

        var destDir = Path.Combine(_root, jobId, stamp);
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, safeName);

        try
        {
            MoveDirectoryCrossVolume(sourcePath, destPath);
        }
        catch (Exception ex)
        {
            return QuarantineResult.Fail(sourcePath, "E_QUARANTINE_FAILED", ex.Message);
        }

        var record = new QuarantineRecord(
            SourcePath: sourcePath,
            QuarantinePath: destPath,
            JobId: jobId,
            Reason: reason,
            UtcTimestamp: DateTime.UtcNow);

        var metaPath = Path.Combine(destDir, "meta.json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(record, JsonOptions));

        return QuarantineResult.Ok(record);
    }

    public bool Restore(string quarantinePath)
    {
        if (!File.Exists(quarantinePath))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(quarantinePath)!;
        var metaPath = Path.Combine(dir, "meta.json");
        if (!File.Exists(metaPath))
        {
            return false;
        }

        var record = JsonSerializer.Deserialize<QuarantineRecord>(File.ReadAllText(metaPath), JsonOptions);
        if (record is null)
        {
            return false;
        }

        var dest = record.SourcePath;
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(dest))
        {
            return false;
        }

        File.Move(quarantinePath, dest, overwrite: false);
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Directory.Move fails across volumes — copy, verify size, delete source (issue 003).</summary>
    private static void MoveDirectoryCrossVolume(string sourcePath, string destPath)
    {
        try
        {
            Directory.Move(sourcePath, destPath);
            return;
        }
        catch (IOException)
        {
            // fall through to copy+delete
        }
        catch (UnauthorizedAccessException)
        {
            // fall through
        }

        CopyDirectory(sourcePath, destPath);
        var srcSize = DirSize(sourcePath);
        var dstSize = DirSize(destPath);
        if (srcSize != dstSize)
        {
            try { Directory.Delete(destPath, true); } catch { }
            throw new IOException("Cross-volume quarantine verify failed (size mismatch).");
        }

        Directory.Delete(sourcePath, true);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static long DirSize(string path)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(file).Length;
        }

        return total;
    }
}

public sealed record QuarantineRecord(
    string SourcePath,
    string QuarantinePath,
    string JobId,
    string Reason,
    DateTime UtcTimestamp);

public sealed record QuarantineResult(
    bool Success,
    QuarantineRecord? Record,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static QuarantineResult Ok(QuarantineRecord record) => new(true, record, null, null);

    public static QuarantineResult Fail(string path, string code, string message) =>
        new(false, null, code, $"{message} ({path})");
}

