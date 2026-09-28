using System.IO;
using System.Runtime.Versioning;
using CleanSlate.AgentRules;
using CleanSlate.Core;
using CleanSlate.ElevatedHelper;
using CleanSlate.Inventory;
using CleanSlate.Leftover;
using CleanSlate.Safety;
using CleanSlate.Uninstall;

namespace CleanSlate.App.Services;

/// <summary>
/// Orchestrates inventory 鈫?uninstall 鈫?leftover scan 鈫?quarantine 鈫?undo log 鈫?report.
/// </summary>
public sealed class CleanupWorkflowService
{
    private readonly ElevatedCleanupClient _elevated = new(
        helperExePath: Path.Combine(AppContext.BaseDirectory, "CleanSlate.ElevatedHelper.exe"));

    private readonly CleanupReportExporter _reportExporter = new();
    private readonly UndoLogService _undoLog = new();
    private readonly HistoryStore _history = new();
    private readonly QuarantineRestorer _restorer = new();
    private readonly AgentRuleLoader _agentLoader = new();
    private readonly AgentCleanupPlanner _agentPlanner = new();
    private readonly ProjectAgentResidualScanner _projectAgentScanner = new();
    private readonly BatchUninstallService _batchUninstall = new();
    private readonly ForceRemovalPlanner _forceRemoval = new();
    private readonly ForceRemovalExecutor _forceExecutor = new();
    private readonly DelayedFileDeleter _delayedDeleter = new();
    private readonly InstallMonitor _installMonitor = new();
    private readonly BrowserExtensionInventory _browserExtensions = new();
    private readonly SoftwareRelocator _relocator = new();
    private readonly PolicyWhitelist _policy = PolicyWhitelist.Load();
    private readonly RulePackUpdater _ruleUpdater = new();
    private RulePackWatcher? _ruleWatcher;
    private InstallSnapshot? _monitorBefore;

    public UndoLogService UndoLog => _undoLog;

    public HistoryStore History => _history;

    public QuarantineRestorer Restorer => _restorer;

    public DelayedFileDeleter DelayedDeleter => _delayedDeleter;

    public ForceRemovalPlanner ForceRemoval => _forceRemoval;

    public InstallMonitor Monitor => _installMonitor;

    public PolicyWhitelist Policy => _policy;

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<SoftwareEntry> LoadSoftware(bool includeStore = true, bool includeHidden = false, bool includePortable = false)
    {
        var inventory = new CompositeSoftwareInventory();
        var items = inventory.Scan(includeStore, includeHidden).ToList();

        if (includePortable)
        {
            try
            {
                var portable = new PortableSoftwareInventory();
                items.AddRange(portable.Scan(items.Select(i => i.DisplayName), max: 30));
            }
            catch
            {
                // portable is heuristic
            }
        }

        return items;
    }

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<StartupItem> LoadStartupItems() => new StartupItemInventory().Scan();

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<StartupItem> StartupFor(SoftwareEntry entry)
    {
        var hints = new List<string> { entry.DisplayName };
        if (!string.IsNullOrWhiteSpace(entry.Publisher))
        {
            hints.Add(entry.Publisher!);
        }

        return new StartupItemInventory().ForSoftware(hints);
    }

    /// <summary>List processes that would be closed (issue 006) — never auto-kill.</summary>
    public IReadOnlyList<RelatedProcess> FindRelatedProcesses(SoftwareEntry entry)
    {
        var executor = new UninstallExecutor();
        return executor.FindRelatedProcesses([entry.DisplayName], entry.InstallLocation);
    }

    public UninstallResult Uninstall(SoftwareEntry entry, bool silent = true, bool elevate = true, IEnumerable<RelatedProcess>? processesToClose = null)
    {
        var executor = new UninstallExecutor();
        var command = UninstallCommandParser.Parse(entry.UninstallString, installLocation: entry.InstallLocation);

        // Issue 006: only terminate processes the user explicitly approved
        if (processesToClose is not null)
        {
            executor.TerminateProcesses(processesToClose);
        }

        return executor.ExecuteAsync(command, silent, elevate).GetAwaiter().GetResult();
    }

    public Task<BatchUninstallSummary> BatchUninstallAsync(IReadOnlyList<SoftwareEntry> targets, bool silent = true, CancellationToken ct = default) =>
        _batchUninstall.RunAsync(targets, silent, ct);

    public IReadOnlyList<LeftoverItem> ScanLeftovers(SoftwareEntry entry)
    {
        var fsScanner = new LeftoverScanner();
        var advanced = new AdvancedLeftoverScanner();
        var regScanner = new RegistryLeftoverScanner();
        var comScanner = new ComFirewallEnvScanner();

        var hints = new List<string> { entry.DisplayName };
        if (!string.IsNullOrWhiteSpace(entry.Publisher))
        {
            hints.Add(entry.Publisher);
        }

        var distinct = hints.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var items = new List<LeftoverItem>();
        items.AddRange(fsScanner.ScanDirectories(DefaultScanRoots(), distinct));
        items.AddRange(advanced.ScanAllAdvanced(distinct));
        items.AddRange(regScanner.Scan(distinct, maxItems: 80));
        items.AddRange(comScanner.ScanAll(distinct));
        return items;
    }

    public IReadOnlyList<LeftoverItem> ScanProjectAgentResiduals(string workspaceRoot) =>
        _projectAgentScanner.Scan(workspaceRoot);

    [SupportedOSPlatform("windows")]
    public CleanupSummary Clean(IReadOnlyList<LeftoverItem> items, string jobId, string? targetName = null)
    {
        var quarantine = new QuarantineService();
        var policy = new PathSafetyPolicy();
        var reportItems = new List<CleanupReportItem>();
        int quarantined = 0, skipped = 0, failed = 0;

        _undoLog.BeginJob(jobId, "Cleanup", targetName);

        foreach (var item in items)
        {
            if (item.Risk == RiskLevel.Protected || !policy.IsDeletionAllowed(item.PathOrKey))
            {
                skipped++;
                reportItems.Add(ToReportItem(item, "Skipped", "Protected path"));
                _undoLog.Append(jobId, ToUndo(item, "Skip", null, "Protected path"));
                continue;
            }

            try
            {
                switch (item.Kind)
                {
                    case LeftoverKind.Folder when Directory.Exists(item.PathOrKey):
                    {
                        var result = quarantine.QuarantineFolder(item.PathOrKey, jobId, item.MatchReason);
                        if (result.Success)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "Quarantined", result.Record?.QuarantinePath));
                            _undoLog.Append(jobId, ToUndo(item, "QuarantineFolder", result.Record?.QuarantinePath, "Moved to quarantine"));
                        }
                        else
                        {
                            failed++;
                            reportItems.Add(ToReportItem(item, "Failed", result.ErrorMessage));
                            _undoLog.Append(jobId, ToUndo(item, "Fail", null, result.ErrorMessage));
                        }

                        break;
                    }

                    case LeftoverKind.File when File.Exists(item.PathOrKey):
                    {
                        try
                        {
                            var result = quarantine.QuarantineFile(item.PathOrKey, jobId, item.MatchReason);
                            if (result.Success)
                            {
                                quarantined++;
                                reportItems.Add(ToReportItem(item, "Quarantined", result.Record?.QuarantinePath));
                                _undoLog.Append(jobId, ToUndo(item, "QuarantineFile", result.Record?.QuarantinePath, "Moved to quarantine"));
                            }
                            else if (_delayedDeleter.ScheduleDelete(item.PathOrKey))
                            {
                                quarantined++;
                                reportItems.Add(ToReportItem(item, "DelayedDelete", "Scheduled on reboot (file in use)"));
                                _undoLog.Append(jobId, ToUndo(item, "DelayedDelete", null, "PendingFileRenameOperations"));
                            }
                            else
                            {
                                failed++;
                                reportItems.Add(ToReportItem(item, "Failed", result.ErrorMessage));
                                _undoLog.Append(jobId, ToUndo(item, "Fail", null, result.ErrorMessage));
                            }
                        }
                        catch (IOException ex)
                        {
                            if (_delayedDeleter.ScheduleDelete(item.PathOrKey))
                            {
                                quarantined++;
                                reportItems.Add(ToReportItem(item, "DelayedDelete", ex.Message));
                            }
                            else
                            {
                                failed++;
                                reportItems.Add(ToReportItem(item, "Failed", ex.Message));
                            }
                        }

                        break;
                    }

                    case LeftoverKind.Shortcut when File.Exists(item.PathOrKey):
                    {
                        var result = quarantine.QuarantineFile(item.PathOrKey, jobId, item.MatchReason);
                        if (result.Success)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "Quarantined", result.Record?.QuarantinePath));
                            _undoLog.Append(jobId, ToUndo(item, "QuarantineShortcut", result.Record?.QuarantinePath, null));
                        }
                        else
                        {
                            failed++;
                            reportItems.Add(ToReportItem(item, "Failed", result.ErrorMessage));
                        }

                        break;
                    }

                    case LeftoverKind.Task when File.Exists(item.PathOrKey):
                    {
                        var result = quarantine.QuarantineFile(item.PathOrKey, jobId, item.MatchReason);
                        if (result.Success)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "Quarantined", result.Record?.QuarantinePath));
                            _undoLog.Append(jobId, ToUndo(item, "QuarantineTask", result.Record?.QuarantinePath, null));
                        }
                        else
                        {
                            failed++;
                        }

                        break;
                    }

                    case LeftoverKind.Registry:
                    {
                        if (!_policy.Document.AllowRegistryDelete)
                        {
                            skipped++;
                            reportItems.Add(ToReportItem(item, "Skipped", "Policy disables registry delete"));
                            _undoLog.Append(jobId, ToUndo(item, "Skip", null, "Policy"));
                            break;
                        }

                        string? backup = null;
                        if (item.PathOrKey.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
                        {
                            backup = _undoLog.BackupRegistryKey(item.PathOrKey, jobId);
                        }

                        var response = DeleteRegistryItem(item).GetAwaiter().GetResult();
                        if (response.Ok)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "Deleted", response.Message));
                            _undoLog.Append(jobId, ToUndo(item, "DeleteRegistry", backup, response.Message));
                        }
                        else
                        {
                            skipped++;
                            reportItems.Add(ToReportItem(item, "Skipped", $"{response.Code}: {response.Message}"));
                            _undoLog.Append(jobId, ToUndo(item, "Skip", backup, response.Message));
                        }

                        break;
                    }

                    case LeftoverKind.Service:
                    {
                        // Stop + disable without deleting the service binary blindly
                        var svc = new ServiceCleanup();
                        var stop = svc.Stop(item.PathOrKey);
                        var disable = stop.Ok ? svc.Disable(item.PathOrKey) : stop;
                        if (disable.Ok)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "ServiceDisabled", disable.Message));
                            _undoLog.Append(jobId, ToUndo(item, "ServiceDisable", null, disable.Message));
                        }
                        else
                        {
                            skipped++;
                            reportItems.Add(ToReportItem(item, "Skipped", disable.Message));
                        }

                        break;
                    }

                    case LeftoverKind.Com:
                    {
                        string? backup = _undoLog.BackupRegistryKey(item.PathOrKey, jobId);
                        var response = DeleteRegistryItem(item).GetAwaiter().GetResult();
                        if (response.Ok)
                        {
                            quarantined++;
                            reportItems.Add(ToReportItem(item, "Deleted", response.Message));
                            _undoLog.Append(jobId, ToUndo(item, "DeleteCom", backup, response.Message));
                        }
                        else
                        {
                            skipped++;
                            reportItems.Add(ToReportItem(item, "Skipped", response.Message));
                        }

                        break;
                    }

                    default:
                        skipped++;
                        reportItems.Add(ToReportItem(item, "Skipped", "Unsupported kind or missing target"));
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                reportItems.Add(ToReportItem(item, "Failed", ex.Message));
                _undoLog.Append(jobId, ToUndo(item, "Fail", null, ex.Message));
            }
        }

        var report = new CleanupReport(
            JobId: jobId,
            UtcTimestamp: DateTime.UtcNow,
            Mode: "Cleanup",
            TargetName: targetName,
            QuarantineRoot: quarantine.Root,
            Succeeded: quarantined,
            Skipped: skipped,
            Failed: failed,
            Items: reportItems);

        var jsonPath = _reportExporter.ExportJson(report, ReportDirectory());
        var htmlPath = _reportExporter.ExportHtml(report, ReportDirectory());

        try
        {
            _history.InsertJob(jobId, "Cleanup", targetName, quarantined, skipped, failed, htmlPath);
            foreach (var ri in reportItems)
            {
                _history.InsertItem(jobId, ri.Kind, ri.PathOrKey, ri.Risk.ToString(), ri.Outcome, ri.Message);
            }
        }
        catch
        {
            // history is best-effort
        }

        return new CleanupSummary(quarantined, skipped, failed, quarantine.Root, jsonPath, htmlPath);
    }

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<OrphanCandidate> ScanOrphans(IEnumerable<string>? installedNames = null)
    {
        var scanner = new OrphanScanner();
        var names = (installedNames ?? LoadSoftware().Select(s => s.DisplayName)).ToList();
        var roots = DefaultScanRoots()
            .Where(r => !r.Contains("Program Files", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return scanner.FindRegistryOrphans().Concat(scanner.FindFolderOrphans(names, roots)).ToList();
    }

    public AgentRuleLoadResult LoadAgentRules(string? path = null)
    {
        var resolved = ResolveAgentRulesPath(path);
        if (resolved is null)
        {
            return AgentRuleLoadResult.Fail("E_RULE_NOT_FOUND", "agent-rules.json not found");
        }

        _ruleWatcher ??= new RulePackWatcher(resolved);
        return _agentLoader.LoadFile(resolved);
    }

    public string? ResolveAgentRulesPath(string? path = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.Add(path);
        }

        // Install/package layout first (issue 001): {app}\rules\agent-rules.json
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "rules", "agent-rules.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "docs", "api", "agent-rules.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "agent-rules.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "docs", "api", "agent-rules.sample.json"));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "api", "agent-rules.json")));
        candidates.Add(@"C:\Users\Administrator\XiaomiMiMoProjects\.mimo-sessions\2026\09\27\windows-agent-agent\CleanSlate\docs\api\agent-rules.json");

        return candidates.FirstOrDefault(File.Exists);
    }

    public QuarantineRestoreSummary RestoreJob(string jobId) => _restorer.RestoreJob(jobId);

    public ForceRemovalPlan PlanForceRemoval(string nameOrPath) => _forceRemoval.Build(nameOrPath, []);

    public ForceRemovalExecutionResult ExecuteForceRemoval(ForceRemovalPlan plan, string jobId, IEnumerable<string>? selectedPaths = null)
    {
        var allowed = selectedPaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = _forceExecutor.Execute(plan, jobId, includeRegistry: false, allowedPaths: allowed);

        // Handle registry / uninstall entries through elevated helper (with backup).
        int regRemoved = 0, regSkipped = 0, regFailed = 0;
        var details = result.Details.ToList();

        foreach (var target in plan.Targets)
        {
            if (target.Kind is not (ForceRemovalKind.RegistryKey or ForceRemovalKind.UninstallEntry))
            {
                continue;
            }

            if (allowed is not null && !allowed.Contains(target.Path))
            {
                continue;
            }

            if (_policy.IsPathBlocked(target.Path))
            {
                regSkipped++;
                continue;
            }

            try
            {
                string? backup = _undoLog.BackupRegistryKey(target.Path, jobId);
                var response = DeleteRegistryItem(new LeftoverItem(
                    Id: "force:" + target.Path,
                    Kind: LeftoverKind.Registry,
                    PathOrKey: target.Path,
                    MatchReason: "ForceRemoval:" + target.Hint,
                    Confidence: 80,
                    Risk: RiskLevel.High,
                    DefaultSelected: false,
                    Recoverable: Recoverability.None)).GetAwaiter().GetResult();

                if (response.Ok)
                {
                    regRemoved++;
                    details.Add(new ForceRemovalItemResult(target, "Deleted", response.Message));
                    _undoLog.Append(jobId, ToUndoPath(target.Path, "DeleteRegistry", backup, response.Message));
                }
                else
                {
                    regSkipped++;
                    details.Add(new ForceRemovalItemResult(target, "Skipped", response.Message));
                }
            }
            catch (Exception ex)
            {
                regFailed++;
                details.Add(new ForceRemovalItemResult(target, "Failed", ex.Message));
            }
        }

        return new ForceRemovalExecutionResult(
            result.Removed + regRemoved,
            result.Skipped + regSkipped,
            result.Failed + regFailed,
            details);
    }

    public InstallSnapshot CaptureInstallSnapshot(string label) => _installMonitor.Capture(label);

    public InstallDiff DiffInstallSnapshots(InstallSnapshot before, InstallSnapshot after) =>
        _installMonitor.Diff(before, after);

    public InstallSnapshot? MonitorBefore
    {
        get => _monitorBefore;
        set => _monitorBefore = value;
    }

    public IReadOnlyList<BrowserExtension> LoadBrowserExtensions() => _browserExtensions.Scan();

    public IReadOnlyList<BrowserExtension> BrowserExtensionsFor(string name) =>
        _browserExtensions.ForSoftware([name]);

    public RelocationResult RelocateSoftware(string source, string dest) =>
        _relocator.Relocate(source, dest, copyMode: true);

    public RelocationResult RollbackRelocation(string journalPath) => _relocator.Rollback(journalPath);

    public Task<RulePackUpdateResult> UpdateRulesFromUrlAsync(string url, string? sha256 = null, CancellationToken ct = default)
    {
        var dest = ResolveAgentRulesPath() ?? Path.Combine(AppContext.BaseDirectory, "docs", "api", "agent-rules.json");
        return _ruleUpdater.UpdateFromUrlAsync(url, dest, sha256, ct);
    }

    public async Task<RulePackUpdateResult> UpdateRulesFromCdnAsync(CancellationToken ct = default)
    {
        var dest = ResolveAgentRulesPath() ?? Path.Combine(AppContext.BaseDirectory, "docs", "api", "agent-rules.json");
        var current = RulePackCdnClient.ComputeLocalSha256(dest);
        var cdn = new RulePackCdnClient();
        return await cdn.UpdateAsync(dest, current, ct);
    }

    public RulePackUpdateResult UpdateRulesFromFile(string sourcePath, string? sha256 = null) =>
        _ruleUpdater.UpdateFromFile(sourcePath, ResolveAgentRulesPath() ?? Path.Combine(AppContext.BaseDirectory, "docs", "api", "agent-rules.json"), sha256);

    public bool ScheduleDelayedDelete(string path, bool folder = false) => _delayedDeleter.ScheduleDelete(path, folder);

    public IReadOnlyList<AgentLayerItem> PlanAgentLayers(AgentCleanupRule rule) =>
        _agentPlanner.BuildPlan(rule);

    public AgentCleanupResult CleanAgentLayers(IEnumerable<AgentLayerItem> layers, string jobId)
    {
        var quarantine = new QuarantineService();
        int processed = 0, deleted = 0, skipped = 0;
        var errors = new List<string>();
        var reportItems = new List<CleanupReportItem>();
        _undoLog.BeginJob(jobId, "Agent", layers.FirstOrDefault()?.DisplayName);

        foreach (var layer in layers)
        {
            processed++;
            if (layer.LayerName is "L4Credential" or "L5Model" or "L4" or "L5")
            {
                skipped++;
                reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Skipped", layer.Notes, "L4/L5 require explicit extra confirmation"));
                _undoLog.Append(jobId, new UndoLogEntry("Skip", layer.TargetPath, layer.Risk, "L4/L5 skipped", null, layer.Notes, DateTime.UtcNow));
                continue;
            }

            if (!layer.Expanded)
            {
                skipped++;
                reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Skipped", null, "Path not found"));
                continue;
            }

            try
            {
                if (File.Exists(layer.TargetPath))
                {
                    var r = quarantine.QuarantineFile(layer.TargetPath, jobId, layer.LayerName);
                    if (r.Success)
                    {
                        deleted++;
                        reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Quarantined", r.Record?.QuarantinePath));
                        _undoLog.Append(jobId, new UndoLogEntry("QuarantineFile", layer.TargetPath, layer.Risk, "Quarantined", r.Record?.QuarantinePath, null, DateTime.UtcNow));
                    }
                    else
                    {
                        skipped++;
                        reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Skipped", null, r.ErrorMessage));
                    }
                }
                else if (Directory.Exists(layer.TargetPath) && layer.LayerName is "L1Cache" or "L2Session")
                {
                    var r = quarantine.QuarantineFolder(layer.TargetPath, jobId, layer.LayerName);
                    if (r.Success)
                    {
                        deleted++;
                        reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Quarantined", r.Record?.QuarantinePath));
                        _undoLog.Append(jobId, new UndoLogEntry("QuarantineFolder", layer.TargetPath, layer.Risk, "Quarantined", r.Record?.QuarantinePath, null, DateTime.UtcNow));
                    }
                    else
                    {
                        skipped++;
                        reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Skipped", null, r.ErrorMessage));
                    }
                }
                else if (Directory.Exists(layer.TargetPath))
                {
                    skipped++;
                    reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Skipped", layer.Notes, "Layer folder kept (not auto-deleted)"));
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{layer.LayerName}: {ex.Message}");
                reportItems.Add(new CleanupReportItem("Agent:" + layer.LayerName, layer.TargetPath, layer.Risk, "Failed", null, ex.Message));
            }
        }

        var report = new CleanupReport(
            JobId: jobId,
            UtcTimestamp: DateTime.UtcNow,
            Mode: "Agent",
            TargetName: layers.FirstOrDefault()?.DisplayName,
            QuarantineRoot: quarantine.Root,
            Succeeded: deleted,
            Skipped: skipped,
            Failed: errors.Count,
            Items: reportItems);

        _reportExporter.ExportJson(report, ReportDirectory());
        _reportExporter.ExportHtml(report, ReportDirectory());

        return new AgentCleanupResult(processed, deleted, skipped, errors);
    }

    /// <summary>Software health check (S3) — aggregates safe leftovers, agent caches, old apps.</summary>
    public HealthCheckResult RunHealthCheck(bool includeAgent = true)
    {
        var agentLoader = new AgentCleanupPlanner();
        long agentBytes = 0;
        int agentPaths = 0;
        var recommended = new List<LeftoverItem>();

        if (includeAgent)
        {
            var rules = LoadAgentRules();
            if (rules.Success && rules.Rules is not null)
            {
                foreach (var rule in rules.Rules)
                {
                    foreach (var layer in PlanAgentLayers(rule))
                    {
                        if (layer.LayerName is not ("L1Cache" or "L2Session"))
                        {
                            continue;
                        }

                        if (!layer.Expanded || string.IsNullOrEmpty(layer.TargetPath))
                        {
                            continue;
                        }

                        agentPaths++;
                        agentBytes += SafeDirSize(layer.TargetPath);
                        recommended.Add(new LeftoverItem(
                            Id: "health-agent:" + layer.TargetPath,
                            Kind: Directory.Exists(layer.TargetPath) ? LeftoverKind.Folder : LeftoverKind.File,
                            PathOrKey: layer.TargetPath,
                            MatchReason: layer.DisplayName + " " + layer.LayerName,
                            Confidence: 85,
                            Risk: RiskLevel.Low,
                            DefaultSelected: true,
                            Recoverable: Recoverability.Quarantine));
                    }
                }
            }
        }

        // Temp user cache folder under LocalAppData\CleanSlate only — conservative
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var name in new[] { "Temp", "CrashDumps", "Diagnostics" })
        {
            var dir = Path.Combine(local, name);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir).Take(20))
                {
                    var size = SafeDirSize(sub);
                    if (size < 1_000_000)
                    {
                        continue;
                    }

                    recommended.Add(new LeftoverItem(
                        Id: "health-temp:" + sub,
                        Kind: LeftoverKind.Folder,
                        PathOrKey: sub,
                        MatchReason: "Temporary cache folder",
                        Confidence: 60,
                        Risk: RiskLevel.Medium,
                        DefaultSelected: false,
                        Recoverable: Recoverability.Quarantine));
                }
            }
            catch
            {
                // ignore
            }
        }

        var softwareCount = 0;
        try
        {
            softwareCount = LoadSoftware(true, false).Count;
        }
        catch
        {
            // ignore
        }

        return new HealthCheckResult(
            SoftwareCount: softwareCount,
            AgentCachePaths: agentPaths,
            AgentCacheBytes: agentBytes,
            RecommendedItems: recommended,
            SafeCount: recommended.Count(i => i.DefaultSelected),
            ReviewCount: recommended.Count(i => !i.DefaultSelected));
    }

    private static long SafeDirSize(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 5,
            }))
            {
                total += new FileInfo(file).Length;
                if (total > 20L * 1024 * 1024 * 1024)
                {
                    break;
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }
    public string ExportReport(CleanupReport report)
    {
        var dir = ReportDirectory();
        _reportExporter.ExportJson(report, dir);
        return _reportExporter.ExportHtml(report, dir);
    }

    private async Task<HelperResponse> DeleteRegistryItem(LeftoverItem item)
    {
        var path = item.PathOrKey.Replace('/', '\\');
        if (!path.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
        {
            return new HelperResponse(false, item.Id, "E_KIND", "Not a registry item", path);
        }

        // Prefer deleting the whole key (leftover product/vendor key).
        // Only fall back to single value delete when key delete is refused and path looks like a value.
        var keyResult = await _elevated.DeleteRegistryKeyAsync(path);
        if (keyResult.Ok)
        {
            return keyResult;
        }

        var lastSlash = path.LastIndexOf('\\');
        if (lastSlash > 3)
        {
            var keyPath = path[..lastSlash];
            var valueName = path[(lastSlash + 1)..];
            if (valueName.IndexOfAny(['\\', '/']) < 0)
            {
                return await _elevated.DeleteRegistryValueAsync(keyPath, valueName);
            }
        }

        return keyResult;
    }

    private static UndoLogEntry ToUndo(LeftoverItem item, string action, string? backup, string? message) =>
        new(action, item.PathOrKey, item.Risk, action, backup, message, DateTime.UtcNow);

    private static UndoLogEntry ToUndoPath(string path, string action, string? backup, string? message) =>
        new(action, path, RiskLevel.High, action, backup, message, DateTime.UtcNow);

    private static List<string> DefaultScanRoots()
    {
        var roots = new List<string>();
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.LocalApplicationData,
                     Environment.SpecialFolder.ApplicationData,
                     Environment.SpecialFolder.CommonApplicationData,
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    private static string ReportDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanSlate", "reports");

    private static CleanupReportItem ToReportItem(LeftoverItem item, string outcome, string? message) =>
        new(item.Kind.ToString(), item.PathOrKey, item.Risk, outcome, item.MatchReason, message);
}

public sealed record CleanupSummary(
    int Quarantined,
    int Skipped,
    int Failed,
    string QuarantineRoot,
    string? JsonReportPath = null,
    string? HtmlReportPath = null);


public sealed record HealthCheckResult(int SoftwareCount, int AgentCachePaths, long AgentCacheBytes, IReadOnlyList<LeftoverItem> RecommendedItems, int SafeCount, int ReviewCount);
