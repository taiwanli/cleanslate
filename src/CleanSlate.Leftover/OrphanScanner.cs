using System.Runtime.Versioning;
using Microsoft.Win32;
using CleanSlate.Core;

namespace CleanSlate.Leftover;

/// <summary>
/// Finds orphan leftovers: Uninstall registry entries whose install path is gone,
/// or AppData folders with no corresponding installed software.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OrphanScanner
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public IReadOnlyList<OrphanCandidate> FindRegistryOrphans(CancellationToken cancellationToken = default)
    {
        var results = new List<OrphanCandidate>();
        var policy = new Safety.PathSafetyPolicy();

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var uninstallPath in UninstallKeyPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var uninstallKey = root.OpenSubKey(uninstallPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subName in uninstallKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var key = uninstallKey.OpenSubKey(subName);
                    if (key is null)
                    {
                        continue;
                    }

                    var displayName = (key.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var installLocation = (key.GetValue("InstallLocation") as string)?.Trim();
                    var uninstallString = (key.GetValue("UninstallString") as string)?.Trim();
                    var systemComponent = key.GetValue("SystemComponent") is int sc && sc == 1;

                    var missingLocation = !string.IsNullOrWhiteSpace(installLocation)
                                          && !Directory.Exists(installLocation);

                    var brokenUninstall = !string.IsNullOrWhiteSpace(uninstallString)
                                          && UninstallStringBroken(uninstallString);

                    if (!missingLocation && !brokenUninstall)
                    {
                        continue;
                    }

                    // Registry key itself is an orphan candidate (path points to missing software)
                    var reasons = new List<string>();
                    if (missingLocation)
                    {
                        reasons.Add($"InstallLocation missing: {installLocation}");
                    }

                    if (brokenUninstall)
                    {
                        reasons.Add("Uninstall command target missing");
                    }

                    var confidence = missingLocation && brokenUninstall ? 85 : 70;
                    var risk = systemComponent ? RiskLevel.Protected : RiskLevel.Medium;

                    results.Add(new OrphanCandidate(
                        Id: $"reg:{root.Name}\\{uninstallPath}\\{subName}",
                        DisplayName: displayName,
                        Kind: OrphanKind.BrokenUninstallEntry,
                        Location: installLocation ?? uninstallString ?? subName,
                        MatchReason: string.Join("; ", reasons),
                        Confidence: confidence,
                        Risk: risk,
                        DefaultSelected: false,
                        Publisher: key.GetValue("Publisher") as string,
                        Version: key.GetValue("DisplayVersion") as string));
                }
            }
        }

        return results.OrderBy(o => o.DisplayName).ToList();
    }

    public IReadOnlyList<OrphanCandidate> FindFolderOrphans(
        IEnumerable<string> knownInstalledNames,
        IEnumerable<string> scanRoots,
        CancellationToken cancellationToken = default)
    {
        var installed = knownInstalledNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<OrphanCandidate>();

        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || installed.Contains(name))
                {
                    continue;
                }

                // Skip well-known shared folders
                if (IsWellKnown(name))
                {
                    continue;
                }

                // Only report when it looks like an app folder (has exe/dll or many files)
                if (!LooksLikeAppFolder(dir))
                {
                    continue;
                }

                results.Add(new OrphanCandidate(
                    Id: $"dir:{dir}",
                    DisplayName: name,
                    Kind: OrphanKind.OrphanFolder,
                    Location: dir,
                    MatchReason: "App-like folder with no matching installed software name",
                    Confidence: 55,
                    Risk: RiskLevel.High,
                    DefaultSelected: false));
            }
        }

        return results.OrderBy(o => o.DisplayName).ToList();
    }

    private static bool UninstallStringBroken(string uninstallString)
    {
        // Extract a quoted or .exe path
        var match = System.Text.RegularExpressions.Regex.Match(
            uninstallString,
            "\"(?<p>[^\"]+\\.exe)\"|(?<p>^[^\\s\"]+\\.exe)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        var path = match.Groups["p"].Value;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path);
        }
        catch
        {
            return false;
        }

        return !File.Exists(path) && path.Contains('\\');
    }

    private static bool IsWellKnown(string name) =>
        name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Packages", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Temp", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrashDumps", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Diagnostics", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAppFolder(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 3,
            }).Take(20).Any(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch
        {
            return false;
        }
    }
}

public enum OrphanKind
{
    BrokenUninstallEntry,
    OrphanFolder,
    OrphanRegistryValue,
}

public sealed record OrphanCandidate(
    string Id,
    string DisplayName,
    OrphanKind Kind,
    string Location,
    string MatchReason,
    int Confidence,
    RiskLevel Risk,
    bool DefaultSelected,
    string? Publisher = null,
    string? Version = null);
