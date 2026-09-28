using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.Safety;

/// <summary>
/// Enterprise-style policy: protected path patterns and software name allow/deny lists.
/// </summary>
public sealed class PolicyWhitelist
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public PolicyDocument Document { get; private set; } = PolicyDocument.Default();

    public static PolicyWhitelist Load(string? path = null)
    {
        var service = new PolicyWhitelist();
        path ??= DefaultPath();
        if (File.Exists(path))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<PolicyDocument>(File.ReadAllText(path), Json);
                if (doc is not null)
                {
                    service.Document = doc;
                }
            }
            catch
            {
                // keep default
            }
        }

        return service;
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Document, Json), Encoding.UTF8);
    }

    public bool IsSoftwareAllowed(string displayName)
    {
        if (Document.DenySoftwareNames.Any(d => displayName.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Document.AllowSoftwareNames.Count == 0)
        {
            return true;
        }

        return Document.AllowSoftwareNames.Any(a => displayName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsPathBlocked(string path)
    {
        var p = path.Replace('/', '\\');
        foreach (var pattern in Document.BlockedPathPatterns)
        {
            if (p.Contains(pattern.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanSlate", "policy.json");
}

public sealed record PolicyDocument(
    IReadOnlyList<string> BlockedPathPatterns,
    IReadOnlyList<string> AllowSoftwareNames,
    IReadOnlyList<string> DenySoftwareNames,
    bool AllowBatchUninstall,
    bool AllowRegistryDelete)
{
    public static PolicyDocument Default() => new(
        BlockedPathPatterns:
        [
            @"C:\Windows\",
            @"C:\Program Files\WindowsApps\",
            @"\Desktop\",
            @"\Documents\",
            @"\Downloads\",
            @"OneDrive\",
        ],
        AllowSoftwareNames: [],
        DenySoftwareNames: [],
        AllowBatchUninstall: true,
        AllowRegistryDelete: true);
}
