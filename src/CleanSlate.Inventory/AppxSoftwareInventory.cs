using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using CleanSlate.Core;

namespace CleanSlate.Inventory;

/// <summary>
/// Enumerates Microsoft Store / AppX packages via PowerShell Get-AppxPackage.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppxSoftwareInventory
{
    public IReadOnlyList<SoftwareEntry> Scan(CancellationToken cancellationToken = default)
    {
        var results = new List<SoftwareEntry>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"Get-AppxPackage | Select-Object Name,PackageFullName,Publisher,Version,InstallLocation,IsFramework,NonRemovable | ConvertTo-Json -Depth 3\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return results;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return results;
            }

            using var doc = JsonDocument.Parse(output);
            foreach (var element in EnumeratePackages(doc.RootElement))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = Map(element);
                if (entry is not null)
                {
                    results.Add(entry);
                }
            }
        }
        catch
        {
            // Appx enumeration is best-effort
        }

        return results
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<JsonElement> EnumeratePackages(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
        }
    }

    private static SoftwareEntry? Map(JsonElement el)
    {
        string? Name(string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        bool Flag(string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True;

        var name = Name("Name");
        var fullName = Name("PackageFullName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var isFramework = Flag("IsFramework");
        var nonRemovable = Flag("NonRemovable");
        var riskTags = new List<string> { "Store" };
        if (isFramework)
        {
            riskTags.Add("Framework");
        }

        if (nonRemovable)
        {
            riskTags.Add("NoRemove");
        }

        return new SoftwareEntry(
            Id: $"appx:{fullName ?? name}",
            DisplayName: name!,
            Publisher: Name("Publisher"),
            Version: Name("Version"),
            Source: SoftwareSource.Store,
            InstallLocation: Name("InstallLocation"),
            UninstallString: $"powershell -NoProfile -Command \"Remove-AppxPackage -Package '{fullName ?? name}'\"",
            EstimatedSize: 0,
            RiskTags: riskTags,
            CanSilentUninstall: true,
            IsSystem: nonRemovable || isFramework);
    }
}

/// <summary>
/// Flags hidden / system-component entries from registry inventory.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HiddenSoftwareInventory
{
    public IReadOnlyList<SoftwareEntry> Scan(CancellationToken cancellationToken = default)
    {
        var inventory = new RegistrySoftwareInventory();
        return inventory.Scan(cancellationToken)
            .Where(e => e.IsSystem || e.RiskTags.Contains("SystemComponent") || e.RiskTags.Contains("NoRemove") || e.RiskTags.Contains("Bundle"))
            .ToList();
    }
}

/// <summary>
/// Composite inventory: registry + AppX + hidden.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CompositeSoftwareInventory
{
    public IReadOnlyList<SoftwareEntry> Scan(bool includeStore = true, bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in new RegistrySoftwareInventory().Scan(cancellationToken))
        {
            map[entry.Id] = entry;
        }

        if (includeStore)
        {
            foreach (var entry in new AppxSoftwareInventory().Scan(cancellationToken))
            {
                map[entry.Id] = entry;
            }
        }

        var items = map.Values.AsEnumerable();
        if (!includeHidden)
        {
            items = items.Where(e => !e.IsSystem && e.Source != SoftwareSource.Hidden);
        }

        return items
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
