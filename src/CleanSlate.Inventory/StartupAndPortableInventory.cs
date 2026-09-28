using System.Runtime.Versioning;
using CleanSlate.Core;
using Microsoft.Win32;

namespace CleanSlate.Inventory;

/// <summary>
/// Enumerates startup entries and links them to installed software names.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupItemInventory
{
    private static readonly (string Hive, string Path)[] Keys =
    [
        ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        ("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
    ];

    public IReadOnlyList<StartupItem> Scan(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        foreach (var (hiveName, path) in Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var root = hiveName == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                continue;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var data = key.GetValue(valueName) as string;
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                items.Add(new StartupItem(
                    Name: valueName,
                    Command: data,
                    RegistryPath: $"{hiveName}\\{path}\\{valueName}",
                    Kind: StartupKind.Registry));
            }
        }

        // Startup folders
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                 })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(new StartupItem(
                    Name: Path.GetFileNameWithoutExtension(file),
                    Command: file,
                    RegistryPath: file,
                    Kind: StartupKind.Folder));
            }
        }

        return items;
    }

    /// <summary>Find startup items whose name/command contains any software name hint.</summary>
    public IReadOnlyList<StartupItem> ForSoftware(IEnumerable<string> nameHints, IEnumerable<StartupItem>? all = null)
    {
        var hints = nameHints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();
        var source = all ?? Scan();
        return source.Where(s =>
            hints.Any(h =>
                s.Name.Contains(h, StringComparison.OrdinalIgnoreCase)
                || s.Command.Contains(h, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

public enum StartupKind
{
    Registry,
    Folder,
}

public sealed record StartupItem(string Name, string Command, string RegistryPath, StartupKind Kind);

/// <summary>
/// Heuristic discovery of portable/standalone apps from common directories and running processes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PortableSoftwareInventory
{
    private static readonly string[] CandidateRoots =
    [
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\PortableApps",
        @"D:\PortableApps",
    ];

    public IReadOnlyList<SoftwareEntry> Scan(IEnumerable<string> knownNames, int max = 100, CancellationToken cancellationToken = default)
    {
        var known = knownNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CandidateRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (results.Count >= max)
                {
                    return results.Values.ToList();
                }

                var name = Path.GetFileName(dir);
                if (known.Contains(name))
                {
                    continue;
                }

                // Portable apps often have main exe without uninstall entry
                var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                if (exes.Length == 0 || exes.Length > 30)
                {
                    continue;
                }

                var main = exes.FirstOrDefault(e =>
                {
                    var n = Path.GetFileNameWithoutExtension(e);
                    return n.Contains(name.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase)
                           || name.Contains(n, StringComparison.OrdinalIgnoreCase);
                }) ?? exes[0];

                results[dir] = new SoftwareEntry(
                    Id: $"portable:{dir}",
                    DisplayName: name,
                    Publisher: null,
                    Version: TryProductVersion(main),
                    Source: SoftwareSource.Portable,
                    InstallLocation: dir,
                    UninstallString: null,
                    EstimatedSize: EstimateSize(dir),
                    RiskTags: ["Portable", "Heuristic"],
                    CanSilentUninstall: false,
                    IsSystem: false);
            }
        }

        return results.Values.OrderBy(e => e.DisplayName).ToList();
    }

    private static string? TryProductVersion(string path)
    {
        try
        {
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static long EstimateSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Take(2000)
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }
}
