namespace CleanSlate.Core;

public enum SoftwareSource
{
    Registry,
    Msi,
    Store,
    Portable,
    Service,
    Hidden,
    Other
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Protected
}

public enum LeftoverKind
{
    File,
    Folder,
    Registry,
    Service,
    Task,
    Com,
    Shortcut,
    Other
}

public enum Recoverability
{
    Quarantine,
    RecycleBin,
    None
}

public sealed record SoftwareEntry(
    string Id,
    string DisplayName,
    string? Publisher,
    string? Version,
    SoftwareSource Source,
    string? InstallLocation,
    string? UninstallString,
    long EstimatedSize,
    IReadOnlyList<string> RiskTags,
    bool CanSilentUninstall,
    bool IsSystem);

public sealed record LeftoverItem(
    string Id,
    LeftoverKind Kind,
    string PathOrKey,
    string MatchReason,
    int Confidence,
    RiskLevel Risk,
    bool DefaultSelected,
    Recoverability Recoverable);

public sealed record AgentCleanupRule(
    string AgentId,
    string DisplayName,
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> PathPatterns,
    IReadOnlyList<AgentCleanupLayer> Layers);

/// <summary>Layer-specific relative path under agent roots. Null/empty means the layer is not executable (issue 002).</summary>
public sealed record AgentCleanupLayer(
    string Name,
    RiskLevel Risk,
    bool DefaultSelected,
    string? Notes,
    string? RelativePath = null);

public sealed record CleanupJob(
    string Id,
    string Mode,
    IReadOnlyList<LeftoverItem> Items,
    string? BackupRef,
    string? UndoToken);
