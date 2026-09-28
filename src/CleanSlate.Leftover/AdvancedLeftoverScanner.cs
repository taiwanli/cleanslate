using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using CleanSlate.Core;

namespace CleanSlate.Leftover;

/// <summary>
/// Scans service, scheduled task, and shortcut leftovers related to an uninstalled app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdvancedLeftoverScanner
{
    public IReadOnlyList<LeftoverItem> ScanServices(IEnumerable<string> nameHints, CancellationToken cancellationToken = default)
    {
        var hints = NormalizeHints(nameHints);
        var items = new List<LeftoverItem>();

        try
        {
            foreach (var service in ServiceController.GetServices())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var match = hints.FirstOrDefault(h =>
                        service.ServiceName.Contains(h, StringComparison.OrdinalIgnoreCase)
                        || service.DisplayName.Contains(h, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                    {
                        continue;
                    }

                    items.Add(new LeftoverItem(
                        Id: $"svc:{service.ServiceName}",
                        Kind: LeftoverKind.Service,
                        PathOrKey: service.ServiceName,
                        MatchReason: $"Service name matches '{match}'",
                        Confidence: 80,
                        Risk: RiskLevel.High,
                        DefaultSelected: false,
                        Recoverable: Recoverability.None));
                }
                finally
                {
                    service.Dispose();
                }
            }
        }
        catch
        {
            // Service enumeration may fail without elevation
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanScheduledTasks(IEnumerable<string> nameHints, CancellationToken cancellationToken = default)
    {
        var hints = NormalizeHints(nameHints);
        var items = new List<LeftoverItem>();
        var taskRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Tasks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Tasks"),
        };

        foreach (var root in taskRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                });
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(file);
                var match = hints.FirstOrDefault(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    continue;
                }

                items.Add(new LeftoverItem(
                    Id: $"task:{file}",
                    Kind: LeftoverKind.Task,
                    PathOrKey: file,
                    MatchReason: $"Task name matches '{match}'",
                    Confidence: 75,
                    Risk: RiskLevel.Medium,
                    DefaultSelected: false,
                    Recoverable: Recoverability.Quarantine));
            }
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanShortcuts(IEnumerable<string> nameHints, CancellationToken cancellationToken = default)
    {
        var hints = NormalizeHints(nameHints);
        var items = new List<LeftoverItem>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MaxRecursionDepth = 3,
                });
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(file);
                var match = hints.FirstOrDefault(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    continue;
                }

                items.Add(new LeftoverItem(
                    Id: $"lnk:{file}",
                    Kind: LeftoverKind.Shortcut,
                    PathOrKey: file,
                    MatchReason: $"Shortcut name matches '{match}'",
                    Confidence: 85,
                    Risk: RiskLevel.Low,
                    DefaultSelected: true,
                    Recoverable: Recoverability.Quarantine));
            }
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanAllAdvanced(IEnumerable<string> nameHints, CancellationToken cancellationToken = default)
    {
        return ScanServices(nameHints, cancellationToken)
            .Concat(ScanScheduledTasks(nameHints, cancellationToken))
            .Concat(ScanShortcuts(nameHints, cancellationToken))
            .ToList();
    }

    private static IReadOnlyList<string> NormalizeHints(IEnumerable<string> nameHints) =>
        nameHints
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// Scans registry leftovers under HKCU/HKLM Software vendor/product keys.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryLeftoverScanner
{
    private static readonly string[] Roots =
    [
        @"SOFTWARE",
        @"SOFTWARE\WOW6432Node",
    ];

    public IReadOnlyList<LeftoverItem> Scan(IEnumerable<string> nameHints, int maxItems = 200, CancellationToken cancellationToken = default)
    {
        var hints = nameHints
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<LeftoverItem>();
        if (hints.Count == 0)
        {
            return items;
        }

        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            foreach (var rootPath in Roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var root = hive.OpenSubKey(rootPath);
                if (root is null)
                {
                    continue;
                }

                foreach (var vendor in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vendorMatch = hints.FirstOrDefault(h => vendor.Contains(h, StringComparison.OrdinalIgnoreCase));
                    if (vendorMatch is not null)
                    {
                        items.Add(new LeftoverItem(
                            Id: $"reg:{hive.Name}\\{rootPath}\\{vendor}",
                            Kind: LeftoverKind.Registry,
                            PathOrKey: $"{hive.Name}\\{rootPath}\\{vendor}",
                            MatchReason: $"Key name matches '{vendorMatch}'",
                            Confidence: 85,
                            Risk: RiskLevel.Medium,
                            DefaultSelected: false,
                            Recoverable: Recoverability.None));

                        if (items.Count >= maxItems)
                        {
                            return items;
                        }
                    }

                    using var vendorKey = root.OpenSubKey(vendor);
                    if (vendorKey is null)
                    {
                        continue;
                    }

                    foreach (var product in vendorKey.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var productMatch = hints.FirstOrDefault(h => product.Contains(h, StringComparison.OrdinalIgnoreCase));
                        if (productMatch is null)
                        {
                            continue;
                        }

                        items.Add(new LeftoverItem(
                            Id: $"reg:{hive.Name}\\{rootPath}\\{vendor}\\{product}",
                            Kind: LeftoverKind.Registry,
                            PathOrKey: $"{hive.Name}\\{rootPath}\\{vendor}\\{product}",
                            MatchReason: $"Product key matches '{productMatch}'",
                            Confidence: 90,
                            Risk: RiskLevel.Medium,
                            DefaultSelected: false,
                            Recoverable: Recoverability.None));

                        if (items.Count >= maxItems)
                        {
                            return items;
                        }
                    }
                }
            }
        }

        return items;
    }
}
