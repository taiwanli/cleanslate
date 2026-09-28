using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using CleanSlate.Core;

namespace CleanSlate.Leftover;

/// <summary>
/// Scans COM registration, firewall rules, and environment variables related to an app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComFirewallEnvScanner
{
    private static readonly string[] ComRoots =
    [
        @"SOFTWARE\Classes\CLSID",
        @"SOFTWARE\Classes\Wow6432Node\CLSID",
        @"SOFTWARE\Classes\TypeLib",
        @"SOFTWARE\Classes\Wow6432Node\TypeLib",
        @"SOFTWARE\Classes\Interface",
    ];

    public IReadOnlyList<LeftoverItem> ScanCom(IEnumerable<string> nameHints, int max = 80, CancellationToken ct = default)
    {
        var hints = Normalize(nameHints);
        var items = new List<LeftoverItem>();

        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            foreach (var rootPath in ComRoots)
            {
                ct.ThrowIfCancellationRequested();
                using var root = hive.OpenSubKey(rootPath);
                if (root is null)
                {
                    continue;
                }

                foreach (var clsid in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(clsid);
                    if (key is null)
                    {
                        continue;
                    }

                    var name = key.GetValue(null) as string ?? string.Empty;
                    var inproc = key.OpenSubKey("InprocServer32")?.GetValue(null) as string;
                    var blob = $"{name} {inproc} {clsid}";

                    if (hints.Any(h => blob.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new LeftoverItem(
                            Id: $"com:{hive.Name}\\{rootPath}\\{clsid}",
                            Kind: LeftoverKind.Com,
                            PathOrKey: $"{hive.Name}\\{rootPath}\\{clsid}",
                            MatchReason: $"COM registration matches '{name}'",
                            Confidence: 70,
                            Risk: RiskLevel.High,
                            DefaultSelected: false,
                            Recoverable: Recoverability.None));

                        if (items.Count >= max)
                        {
                            return items;
                        }
                    }
                }
            }
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanFirewall(IEnumerable<string> nameHints, int max = 40, CancellationToken ct = default)
    {
        var hints = Normalize(nameHints);
        var items = new List<LeftoverItem>();
        var rulePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(rulePath);
            if (key is null)
            {
                return items;
            }

            foreach (var valueName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                var data = key.GetValue(valueName) as string;
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                if (hints.Any(h => data.Contains(h, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new LeftoverItem(
                        Id: $"fw:{valueName}",
                        Kind: LeftoverKind.Other,
                        PathOrKey: valueName,
                        MatchReason: "Firewall rule payload matches app name",
                        Confidence: 65,
                        Risk: RiskLevel.Medium,
                        DefaultSelected: false,
                        Recoverable: Recoverability.None));

                    if (items.Count >= max)
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
            // may need elevation
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanEnvironmentVariables(IEnumerable<string> nameHints, int max = 40, CancellationToken ct = default)
    {
        var hints = Normalize(nameHints);
        var items = new List<LeftoverItem>();

        foreach (var scope in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            ct.ThrowIfCancellationRequested();
            System.Collections.IDictionary vars;
            try
            {
                vars = Environment.GetEnvironmentVariables(scope);
            }
            catch
            {
                continue;
            }

            foreach (System.Collections.DictionaryEntry entry in vars)
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                var value = entry.Value?.ToString() ?? string.Empty;
                if (hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)
                                   || value.Contains(h, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new LeftoverItem(
                        Id: $"env:{scope}:{name}",
                        Kind: LeftoverKind.Other,
                        PathOrKey: $"{scope}:{name}",
                        MatchReason: "Environment variable references app",
                        Confidence: 75,
                        Risk: RiskLevel.Medium,
                        DefaultSelected: false,
                        Recoverable: Recoverability.None));

                    if (items.Count >= max)
                    {
                        return items;
                    }
                }
            }
        }

        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanAll(IEnumerable<string> nameHints, CancellationToken ct = default) =>
        ScanCom(nameHints, ct: ct)
            .Concat(ScanFirewall(nameHints, ct: ct))
            .Concat(ScanEnvironmentVariables(nameHints, ct: ct))
            .ToList();

    private static IReadOnlyList<string> Normalize(IEnumerable<string> nameHints) =>
        nameHints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// Service stop / disable / delete via ServiceController (delete needs elevated helper).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceCleanup
{
    public ServiceActionResult Stop(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                return ServiceActionResult.FromOk(serviceName, "Already stopped");
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            return ServiceActionResult.FromOk(serviceName, "Stopped");
        }
        catch (Exception ex)
        {
            return ServiceActionResult.Fail(serviceName, "E_STOP", ex.Message);
        }
    }

    public ServiceActionResult Disable(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @$"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key is null)
            {
                return ServiceActionResult.Fail(serviceName, "E_NOT_FOUND", "Service key not found");
            }

            var originalStart = key.GetValue("Start");
            key.SetValue("Start", 4); // Disabled
            return ServiceActionResult.FromOk(serviceName, "Disabled; original Start=" + (originalStart?.ToString() ?? "?"));
        }
        catch (Exception ex)
        {
            return ServiceActionResult.Fail(serviceName, "E_DISABLE", ex.Message);
        }
    }
}

public sealed record ServiceActionResult(bool Ok, string ServiceName, string? Code, string? Message)
{
    public static ServiceActionResult FromOk(string name, string message) => new(true, name, null, message);

    public static ServiceActionResult Fail(string name, string code, string message) => new(false, name, code, message);
}
