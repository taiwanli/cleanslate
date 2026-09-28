using System.Text.Json;
using CleanSlate.Core;

namespace CleanSlate.Leftover;

/// <summary>
/// Discovers browser extensions for Chrome / Edge / Firefox from profile folders.
/// </summary>
public sealed class BrowserExtensionInventory
{
    public IReadOnlyList<BrowserExtension> Scan(CancellationToken cancellationToken = default)
    {
        var results = new List<BrowserExtension>();

        results.AddRange(ScanChromium("Chrome", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data"), cancellationToken));

        results.AddRange(ScanChromium("Edge", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data"), cancellationToken));

        results.AddRange(ScanFirefox(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox", "Profiles"), cancellationToken));

        return results.OrderBy(e => e.Name).ToList();
    }

    public IReadOnlyList<BrowserExtension> ForSoftware(IEnumerable<string> nameHints)
    {
        var hints = nameHints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();
        return Scan().Where(e =>
            hints.Any(h =>
                e.Name.Contains(h, StringComparison.OrdinalIgnoreCase)
                || (e.ExtensionId?.Contains(h, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
    }

    private static IEnumerable<BrowserExtension> ScanChromium(string browser, string userData, CancellationToken ct)
    {
        if (!Directory.Exists(userData))
        {
            yield break;
        }

        foreach (var profile in Directory.EnumerateDirectories(userData))
        {
            var name = Path.GetFileName(profile);
            if (!name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) && !name.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extRoot = Path.Combine(profile, "Extensions");
            if (!Directory.Exists(extRoot))
            {
                continue;
            }

            foreach (var extIdDir in Directory.EnumerateDirectories(extRoot))
            {
                ct.ThrowIfCancellationRequested();
                var extId = Path.GetFileName(extIdDir);
                string? title = null;
                string? version = null;

                foreach (var verDir in Directory.EnumerateDirectories(extIdDir))
                {
                    var manifest = Path.Combine(verDir, "manifest.json");
                    if (!File.Exists(manifest))
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                        if (doc.RootElement.TryGetProperty("name", out var n))
                        {
                            title = n.GetString();
                        }

                        if (doc.RootElement.TryGetProperty("version", out var v))
                        {
                            version = v.GetString();
                        }

                        break;
                    }
                    catch
                    {
                        // invalid manifest
                    }
                }

                yield return new BrowserExtension(
                    Browser: browser,
                    Profile: name,
                    ExtensionId: extId,
                    Name: title ?? extId,
                    Version: version,
                    InstallPath: extIdDir);
            }
        }
    }

    private static IEnumerable<BrowserExtension> ScanFirefox(string profilesRoot, CancellationToken ct)
    {
        if (!Directory.Exists(profilesRoot))
        {
            yield break;
        }

        foreach (var profile in Directory.EnumerateDirectories(profilesRoot))
        {
            ct.ThrowIfCancellationRequested();
            var extensionsJson = Path.Combine(profile, "extensions.json");
            if (!File.Exists(extensionsJson))
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(extensionsJson));
            }
            catch
            {
                continue;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("addons", out var addons))
                {
                    continue;
                }

                foreach (var addon in addons.EnumerateArray())
                {
                    if (!addon.TryGetProperty("id", out var idEl))
                    {
                        continue;
                    }

                    var id = idEl.GetString() ?? "?";
                    var name = addon.TryGetProperty("defaultLocale", out var loc)
                               && loc.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                    var path = addon.TryGetProperty("path", out var p) ? p.GetString() : null;

                    yield return new BrowserExtension(
                        Browser: "Firefox",
                        Profile: Path.GetFileName(profile),
                        ExtensionId: id,
                        Name: name,
                        Version: addon.TryGetProperty("version", out var ver) ? ver.GetString() : null,
                        InstallPath: path);
                }
            }
        }
    }
}

public sealed record BrowserExtension(
    string Browser,
    string Profile,
    string ExtensionId,
    string Name,
    string? Version,
    string? InstallPath);
