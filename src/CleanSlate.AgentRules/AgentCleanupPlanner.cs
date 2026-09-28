using System.Text.RegularExpressions;
using CleanSlate.Core;

namespace CleanSlate.AgentRules;

/// <summary>
/// Resolves agent rule path patterns and builds layered cleanup plan items.
/// Issue 002: each layer must have its own relative path; layers never map to the agent root.
/// </summary>
public sealed class AgentCleanupPlanner
{
    public IReadOnlyList<AgentLayerItem> BuildPlan(
        AgentCleanupRule rule,
        bool onlySelectedLayers = false,
        ISet<string>? selectedLayerNames = null)
    {
        var items = new List<AgentLayerItem>();
        var roots = ExpandRoots(rule.PathPatterns);

        foreach (var layer in rule.Layers)
        {
            if (onlySelectedLayers && selectedLayerNames is not null && !selectedLayerNames.Contains(layer.Name))
            {
                continue;
            }

            // Issue 002: no layer-specific path => not executable
            if (string.IsNullOrWhiteSpace(layer.RelativePath))
            {
                items.Add(new AgentLayerItem(
                    AgentId: rule.AgentId,
                    DisplayName: rule.DisplayName,
                    LayerName: layer.Name,
                    Risk: layer.Risk,
                    DefaultSelected: false,
                    TargetPath: string.Empty,
                    Notes: string.IsNullOrWhiteSpace(layer.Notes)
                        ? "Layer has no relativePath — execution blocked"
                        : layer.Notes + " (no relativePath — blocked)",
                    Expanded: false));
                continue;
            }

            foreach (var root in roots)
            {
                var target = CombineLayerPath(root, layer.RelativePath!);
                items.Add(new AgentLayerItem(
                    AgentId: rule.AgentId,
                    DisplayName: rule.DisplayName,
                    LayerName: layer.Name,
                    Risk: layer.Risk,
                    DefaultSelected: layer.DefaultSelected,
                    TargetPath: target,
                    Notes: layer.Notes,
                    Expanded: Directory.Exists(target) || File.Exists(target)));
            }
        }

        return items;
    }

    /// <summary>Join root + relative layer path; block path traversal outside root.</summary>
    public static string CombineLayerPath(string root, string relativePath)
    {
        var rel = relativePath.Trim().Replace('/', '\\').TrimStart('\\');
        if (rel.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Layer relativePath must not contain '..' (issue 002).");
        }

        return Path.GetFullPath(Path.Combine(root, rel));
    }

    public IReadOnlyList<string> ExpandRoots(IEnumerable<string> patterns)
    {
        var roots = new List<string>();
        foreach (var pattern in patterns)
        {
            var expanded = Environment.ExpandEnvironmentVariables(pattern.Trim());
            expanded = expanded.Replace('/', '\\');

            // Strip trailing wildcards for directory roots
            expanded = Regex.Replace(expanded, @"[\\*]+$", string.Empty);
            if (expanded.EndsWith(":", StringComparison.Ordinal))
            {
                expanded += "\\";
            }

            if (!string.IsNullOrWhiteSpace(expanded) && !roots.Contains(expanded, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(expanded);
            }
        }

        return roots;
    }

    public IReadOnlyList<string> EnumerateTargets(AgentLayerItem item, int max = 200)
    {
        var results = new List<string>();
        if (!item.Expanded || string.IsNullOrWhiteSpace(item.TargetPath))
        {
            return results;
        }

        try
        {
            if (File.Exists(item.TargetPath))
            {
                results.Add(item.TargetPath);
                return results;
            }

            if (!Directory.Exists(item.TargetPath))
            {
                return results;
            }

            foreach (var file in Directory.EnumerateFiles(item.TargetPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 4,
            }))
            {
                results.Add(file);
                if (results.Count >= max)
                {
                    break;
                }
            }
        }
        catch
        {
            // ignore IO races
        }

        return results;
    }
}

public sealed record AgentLayerItem(
    string AgentId,
    string DisplayName,
    string LayerName,
    RiskLevel Risk,
    bool DefaultSelected,
    string TargetPath,
    string? Notes,
    bool Expanded);

public sealed record AgentCleanupResult(
    int Processed,
    int Deleted,
    int Skipped,
    IReadOnlyList<string> Errors);
