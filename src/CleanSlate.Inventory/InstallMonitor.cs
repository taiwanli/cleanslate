using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.Inventory;

/// <summary>
/// Install monitor: snapshot file/registry markers before and after an installer to map changes.
/// </summary>
public sealed class InstallMonitor
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public InstallSnapshot Capture(string label, IEnumerable<string>? roots = null, CancellationToken ct = default)
    {
        var scanRoots = (roots ?? DefaultRoots()).ToList();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 3,
            }))
            {
                ct.ThrowIfCancellationRequested();
                dirs.Add(dir);
                if (files.Count > 200_000)
                {
                    break;
                }
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 3,
            }))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    files[file] = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                }
                catch
                {
                    // skip
                }

                if (files.Count > 200_000)
                {
                    break;
                }
            }
        }

        return new InstallSnapshot(
            Label: label,
            Utc: DateTime.UtcNow,
            Roots: scanRoots,
            Files: files,
            Directories: dirs.ToList());
    }

    public InstallDiff Diff(InstallSnapshot before, InstallSnapshot after)
    {
        var addedFiles = after.Files.Keys.Where(k => !before.Files.ContainsKey(k)).ToList();
        var removedFiles = before.Files.Keys.Where(k => !after.Files.ContainsKey(k)).ToList();
        var changedFiles = before.Files
            .Where(kv => after.Files.TryGetValue(kv.Key, out var v) && v != kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        var addedDirs = after.Directories.Where(d => !before.Directories.Contains(d)).ToList();
        var removedDirs = before.Directories.Where(d => !after.Directories.Contains(d)).ToList();

        return new InstallDiff(
            BeforeLabel: before.Label,
            AfterLabel: after.Label,
            AddedFiles: addedFiles,
            ChangedFiles: changedFiles,
            RemovedFiles: removedFiles,
            AddedDirectories: addedDirs,
            RemovedDirectories: removedDirs);
    }

    public string Save(InstallSnapshot snapshot, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"snapshot-{Sanitize(snapshot.Label)}-{snapshot.Utc:yyyyMMddHHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Json), Encoding.UTF8);
        return path;
    }

    public InstallSnapshot? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<InstallSnapshot>(File.ReadAllText(path), Json);
    }

    private static IEnumerable<string> DefaultRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c, '_');
        }

        return s;
    }
}

public sealed record InstallSnapshot(
    string Label,
    DateTime Utc,
    IReadOnlyList<string> Roots,
    Dictionary<string, string> Files,
    IReadOnlyList<string> Directories);

public sealed record InstallDiff(
    string BeforeLabel,
    string AfterLabel,
    IReadOnlyList<string> AddedFiles,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> RemovedFiles,
    IReadOnlyList<string> AddedDirectories,
    IReadOnlyList<string> RemovedDirectories)
{
    public int TotalChanges => AddedFiles.Count + ChangedFiles.Count + RemovedFiles.Count
                               + AddedDirectories.Count + RemovedDirectories.Count;
}
