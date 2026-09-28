using System.Text.Json;

namespace CleanSlate.Safety;

/// <summary>
/// Moves installed folders to another drive with a rollback journal.
/// </summary>
public sealed class SoftwareRelocator
{
    public RelocationResult Relocate(string sourceDirectory, string destinationDirectory, bool copyMode = true)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return RelocationResult.Fail(sourceDirectory, "E_SRC_MISSING", "Source directory not found.");
        }

        if (GetProtection(sourceDirectory))
        {
            return RelocationResult.Fail(sourceDirectory, "E_PATH_PROTECTED", "Source path is protected.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        catch (Exception ex)
        {
            return RelocationResult.Fail(destinationDirectory, "E_DEST", ex.Message);
        }

        var journalPath = Path.Combine(destinationDirectory, ".cleanslate-relocate.json");
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate",
            "relocate",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(backupRoot);

        var journal = new RelocationJournal(
            Source: sourceDirectory,
            Destination: destinationDirectory,
            BackupRoot: backupRoot,
            Utc: DateTime.UtcNow,
            Mode: copyMode ? "CopyReplace" : "Move",
            Status: "InProgress");

        try
        {
            // Copy tree
            CopyTree(sourceDirectory, destinationDirectory);

            if (!copyMode)
            {
                // Keep original as backup then delete
                CopyTree(sourceDirectory, Path.Combine(backupRoot, "original"));
            }
            else
            {
                CopyTree(sourceDirectory, Path.Combine(backupRoot, "original"));
            }

            journal = journal with { Status = "Copied" };
            File.WriteAllText(journalPath, JsonSerializer.Serialize(journal));

            return RelocationResult.Ok(journal with { Status = "Completed" }, journalPath);
        }
        catch (Exception ex)
        {
            journal = journal with { Status = "Failed" };
            try
            {
                File.WriteAllText(journalPath, JsonSerializer.Serialize(journal));
            }
            catch
            {
                // ignore
            }

            return RelocationResult.Fail(sourceDirectory, "E_RELOCATE_FAIL", ex.Message);
        }
    }

    public RelocationResult Rollback(string journalPath)
    {
        if (!File.Exists(journalPath))
        {
            return RelocationResult.Fail(journalPath, "E_JOURNAL_MISSING", "Journal not found.");
        }

        RelocationJournal? journal;
        try
        {
            journal = JsonSerializer.Deserialize<RelocationJournal>(File.ReadAllText(journalPath));
        }
        catch (Exception ex)
        {
            return RelocationResult.Fail(journalPath, "E_JOURNAL", ex.Message);
        }

        if (journal is null)
        {
            return RelocationResult.Fail(journalPath, "E_JOURNAL", "Invalid journal.");
        }

        try
        {
            var originalBackup = Path.Combine(journal.BackupRoot, "original");
            if (Directory.Exists(originalBackup))
            {
                // Restore only if source missing or user forces — we restore into source if missing
                if (!Directory.Exists(journal.Source))
                {
                    CopyTree(originalBackup, journal.Source);
                }
            }

            return RelocationResult.Ok(journal with { Status = "RolledBack" }, journalPath);
        }
        catch (Exception ex)
        {
            return RelocationResult.Fail(journalPath, "E_ROLLBACK", ex.Message);
        }
    }

    private static void CopyTree(string source, string dest)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool GetProtection(string path)
    {
        var p = path.Replace('/', '\\');
        return p.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(@"C:\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RelocationJournal(
    string Source,
    string Destination,
    string BackupRoot,
    DateTime Utc,
    string Mode,
    string Status);

public sealed record RelocationResult(bool Success, RelocationJournal? Journal, string? JournalPath, string? ErrorCode, string? ErrorMessage)
{
    public static RelocationResult Ok(RelocationJournal journal, string journalPath) => new(true, journal, journalPath, null, null);

    public static RelocationResult Fail(string path, string code, string message) =>
        new(false, null, null, code, $"{message} ({path})");
}
