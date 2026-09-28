using CleanSlate.Core;

namespace CleanSlate.AgentRules;

/// <summary>
/// Finds project-level agent leftover directories (.claude, .cursor, ...) under a workspace root.
/// </summary>
public sealed class ProjectAgentResidualScanner
{
    private static readonly string[] Markers =
    [
        ".claude",
        ".cursor",
        ".codex",
        ".continue",
        ".aider",
        ".github",
        ".codeium",
        ".windsurf",
        ".trae",
        ".mimo",
        ".agent",
    ];

    public IReadOnlyList<LeftoverItem> Scan(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var items = new List<LeftoverItem>();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return items;
        }

        foreach (var dir in Directory.EnumerateDirectories(workspaceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            if (!Markers.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new LeftoverItem(
                Id: $"proj:{dir}",
                Kind: LeftoverKind.Folder,
                PathOrKey: dir,
                MatchReason: "Project-level agent config folder",
                Confidence: 80,
                Risk: RiskLevel.Medium,
                DefaultSelected: false,
                Recoverable: Recoverability.Quarantine));
        }

        foreach (var file in Directory.EnumerateFiles(workspaceRoot, "CLAUDE.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(new LeftoverItem(
                Id: $"proj:{file}",
                Kind: LeftoverKind.File,
                PathOrKey: file,
                MatchReason: "Agent instruction file (keep unless user confirms)",
                Confidence: 60,
                Risk: RiskLevel.High,
                DefaultSelected: false,
                Recoverable: Recoverability.Quarantine));
        }

        return items;
    }
}
