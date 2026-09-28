using System.Text.Json;
using CleanSlate.Core;
using CleanSlate.Inventory;
using CleanSlate.Leftover;
using CleanSlate.Safety;
using CleanSlate.Uninstall;

namespace CleanSlate.Cli;

/// <summary>
/// Silent CLI for enterprise / scripted use.
/// Usage:
///   cleanslate list [--json]
///   cleanslate uninstall --name "App" [--silent] [--yes]
///   cleanslate clean-leftovers --name "App" [--yes]
///   cleanslate orphans [--json]
///   cleanslate export-report --out dir
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var policy = PolicyWhitelist.Load();
        var command = args[0].ToLowerInvariant();
        var opts = ParseOptions(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "list" => CmdList(opts),
                "uninstall" => await CmdUninstall(opts, policy),
                "clean-leftovers" => CmdCleanLeftovers(opts, policy),
                "orphans" => CmdOrphans(opts),
                "export-report" => CmdExportReport(opts),
                "help" or "--help" or "-h" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static int Help()
    {
        PrintHelp();
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CleanSlate CLI
              list [--json] [--hidden]
              uninstall --name <name> [--silent] [--yes]
              clean-leftovers --name <name> [--yes]
              orphans [--json]
              export-report --out <dir>
            """);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[i + 1];
                i++;
            }

            map[key] = value;
        }

        return map;
    }

    private static int CmdList(Dictionary<string, string> opts)
    {
        var inv = new CompositeSoftwareInventory();
        var hidden = opts.ContainsKey("hidden");
        var items = inv.Scan(includeStore: true, includeHidden: hidden);
        if (opts.ContainsKey("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(items.Select(ToDto), new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var i in items)
            {
                Console.WriteLine($"{i.DisplayName}\t{i.Version}\t{i.Source}\t{i.InstallLocation}");
            }
        }

        return 0;
    }

    private static async Task<int> CmdUninstall(Dictionary<string, string> opts, PolicyWhitelist policy)
    {
        if (!opts.TryGetValue("name", out var name))
        {
            Console.Error.WriteLine("--name required");
            return 1;
        }

        if (!policy.IsSoftwareAllowed(name))
        {
            Console.Error.WriteLine("E_POLICY: software blocked by policy");
            return 3;
        }

        var inv = new CompositeSoftwareInventory();
        var target = inv.Scan(true, true).FirstOrDefault(s =>
            s.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            Console.Error.WriteLine("E_NOT_FOUND: software not found");
            return 4;
        }

        if (target.IsSystem)
        {
            Console.Error.WriteLine("E_PROTECTED: system component");
            return 5;
        }

        if (!opts.ContainsKey("yes"))
        {
            Console.WriteLine($"About to uninstall: {target.DisplayName}. Pass --yes to confirm.");
            return 6;
        }

        var executor = new UninstallExecutor();
        var cmd = UninstallCommandParser.Parse(target.UninstallString, installLocation: target.InstallLocation);
        if (!string.IsNullOrWhiteSpace(target.InstallLocation))
        {
            executor.TerminateRelatedProcesses([target.DisplayName], target.InstallLocation);
        }

        var silent = opts.ContainsKey("silent");
        var result = await executor.ExecuteAsync(cmd, silent);
        Console.WriteLine(result.Success
            ? $"OK uninstall {target.DisplayName}"
            : $"FAIL {result.ErrorMessage} exit={result.ExitCode}");
        return result.Success ? 0 : 7;
    }

    private static int CmdCleanLeftovers(Dictionary<string, string> opts, PolicyWhitelist policy)
    {
        if (!opts.TryGetValue("name", out var name))
        {
            Console.Error.WriteLine("--name required");
            return 1;
        }

        if (!policy.IsSoftwareAllowed(name))
        {
            Console.Error.WriteLine("E_POLICY: blocked");
            return 3;
        }

        if (!opts.ContainsKey("yes"))
        {
            Console.WriteLine($"Would clean leftovers for {name}. Pass --yes to confirm.");
            return 6;
        }

        var fs = new LeftoverScanner();
        var advanced = new AdvancedLeftoverScanner();
        var reg = new RegistryLeftoverScanner();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        var hints = new[] { name };
        var items = fs.ScanDirectories(roots, hints)
            .Concat(advanced.ScanAllAdvanced(hints))
            .Concat(reg.Scan(hints, 50))
            .ToList();

        var quarantine = new QuarantineService();
        var policyEngine = new PathSafetyPolicy();
        int ok = 0, skip = 0;

        foreach (var item in items)
        {
            if (policy.IsPathBlocked(item.PathOrKey) || item.Risk == RiskLevel.Protected || !policyEngine.IsDeletionAllowed(item.PathOrKey))
            {
                skip++;
                continue;
            }

            if (item.Kind == LeftoverKind.File && File.Exists(item.PathOrKey))
            {
                var r = quarantine.QuarantineFile(item.PathOrKey, "cli-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), item.MatchReason);
                if (r.Success)
                {
                    ok++;
                }
                else
                {
                    skip++;
                }
            }
            else if (item.Kind == LeftoverKind.Folder && Directory.Exists(item.PathOrKey))
            {
                var dest = Path.Combine(quarantine.Root, "cli-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), Path.GetFileName(item.PathOrKey));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    Directory.Move(item.PathOrKey, dest);
                    ok++;
                }
                catch
                {
                    skip++;
                }
            }
            else
            {
                skip++;
            }
        }

        Console.WriteLine($"OK cleaned={ok} skipped={skip} total={items.Count}");
        return 0;
    }

    private static int CmdOrphans(Dictionary<string, string> opts)
    {
        var scanner = new OrphanScanner();
        var orphans = scanner.FindRegistryOrphans();
        if (opts.ContainsKey("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(orphans, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var o in orphans)
            {
                Console.WriteLine($"{o.DisplayName}\t{o.Kind}\t{o.Location}");
            }
        }

        return 0;
    }

    private static int CmdExportReport(Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("out", out var outDir))
        {
            Console.Error.WriteLine("--out required");
            return 1;
        }

        var report = new CleanupReport(
            JobId: "cli-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            UtcTimestamp: DateTime.UtcNow,
            Mode: "CLI",
            TargetName: "manual",
            QuarantineRoot: null,
            Succeeded: 0,
            Skipped: 0,
            Failed: 0,
            Items: []);

        var exporter = new CleanupReportExporter();
        var json = exporter.ExportJson(report, outDir);
        var html = exporter.ExportHtml(report, outDir);
        Console.WriteLine(json);
        Console.WriteLine(html);
        return 0;
    }

    private static object ToDto(SoftwareEntry e) => new
    {
        e.DisplayName,
        e.Publisher,
        e.Version,
        Source = e.Source.ToString(),
        e.InstallLocation,
        e.IsSystem,
    };
}
