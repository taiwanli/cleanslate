using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CleanSlate.Core;

namespace CleanSlate.Inventory;

/// <summary>
/// Enumerates installed software from registry Uninstall keys (HKLM/HKCU, WOW6432Node).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistrySoftwareInventory
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private static readonly string[] RootKeyNames = ["HKLM", "HKCU"];

    public IReadOnlyList<SoftwareEntry> Scan(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootName in RootKeyNames)
        {
            using var root = rootName == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
            foreach (var uninstallPath in UninstallKeyPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var uninstallKey = root.OpenSubKey(uninstallPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var entry = ReadEntry(rootName, uninstallPath, subKeyName, subKey);
                    if (entry is not null)
                    {
                        results[entry.Id] = entry;
                    }
                }
            }
        }

        return results.Values
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static SoftwareEntry? ReadEntry(string rootName, string uninstallPath, string keyName, RegistryKey key)
    {
        var displayName = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            // ParentKeyName / uninst.exe only entries are hidden tools; still surface in expert mode later.
            var quiet = key.GetValue("QuietUninstallString") as string ?? key.GetValue("UninstallString") as string;
            if (string.IsNullOrWhiteSpace(quiet))
            {
                return null;
            }

            displayName = keyName;
        }

        var publisher = key.GetValue("Publisher") as string;
        var version = key.GetValue("DisplayVersion") as string;
        var installLocation = key.GetValue("InstallLocation") as string;
        var uninstallString = key.GetValue("UninstallString") as string;
        var systemComponent = key.GetValue("SystemComponent") is int sc && sc == 1;
        var parentKeyName = key.GetValue("ParentKeyName") as string;
        var noRemove = key.GetValue("NoRemove") is int nr && nr == 1;
        var windowsInstaller = key.GetValue("WindowsInstaller") is int wi && wi == 1;
        var estimatedSize = key.GetValue("EstimatedSize") is int size && size > 0 ? size * 1024L : 0L;

        var source = windowsInstaller
            ? SoftwareSource.Msi
            : systemComponent || !string.IsNullOrEmpty(parentKeyName)
                ? SoftwareSource.Hidden
                : SoftwareSource.Registry;

        var riskTags = new List<string>();
        if (systemComponent)
        {
            riskTags.Add("SystemComponent");
        }

        if (noRemove)
        {
            riskTags.Add("NoRemove");
        }

        if (!string.IsNullOrEmpty(parentKeyName))
        {
            riskTags.Add("Bundle");
        }

        var canSilent = !string.IsNullOrWhiteSpace(key.GetValue("QuietUninstallString") as string)
            || DetectSilent(uninstallString);

        var id = $"{rootName}\\{uninstallPath}\\{keyName}";

        return new SoftwareEntry(
            Id: id,
            DisplayName: displayName.Trim(),
            Publisher: Normalize(publisher),
            Version: Normalize(version),
            Source: source,
            InstallLocation: Normalize(installLocation),
            UninstallString: Normalize(uninstallString),
            EstimatedSize: estimatedSize,
            RiskTags: riskTags,
            CanSilentUninstall: canSilent,
            IsSystem: systemComponent || noRemove);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool DetectSilent(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return false;
        }

        // Common silent flags: /S (NSIS), /quiet /qn (MSI), /verysilent (Inno)
        return Regex.IsMatch(
            uninstallString,
            @"(\s(/S|/silent|/verysilent|/quiet|/qn|/s)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
