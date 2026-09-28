using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanSlate.AgentRules;
using CleanSlate.App.Services;
using CleanSlate.Core;
using CleanSlate.Inventory;
using CleanSlate.Leftover;
using CleanSlate.Uninstall;

namespace CleanSlate.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class SoftwareItemViewModel : ObservableObject
{
    private bool _isSelected;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string SizeText { get; init; } = "-";
    public string SourceText { get; init; } = "Desktop";
    public string RiskText { get; init; } = "Low";
    public string RiskLevel { get; init; } = "Low";
    public string? InstallLocation { get; init; }
    public string? UninstallString { get; init; }
    public bool IsSystem { get; init; }
    public SoftwareEntry? Entry { get; init; }
    public object? Icon { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class ReviewItemViewModel : ObservableObject
{
    private bool _isSelected;
    public required string PathOrKey { get; init; }
    public required string Kind { get; init; }
    public required string MatchReason { get; init; }
    public int Confidence { get; init; }
    public string RiskLevel { get; init; } = "Medium";
    public string RiskText { get; init; } = "Review";
    public string RecoverableText { get; init; } = "Quarantine";
    public bool DefaultSelected { get; init; }
    public LeftoverItem? Item { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class AgentLayerViewModel : ObservableObject
{
    private bool _isSelected;
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public required string LayerName { get; init; }
    public required string LayerTitle { get; init; }
    public required string RiskText { get; init; }
    public string RiskLevel { get; init; } = "Medium";
    public required string TargetPath { get; init; }
    public string? Notes { get; init; }
    public bool Expanded { get; init; }
    public string StatusText { get; init; } = "Pending";
    public AgentLayerItem? Item { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly CleanupWorkflowService _workflow = new();
    private string _searchText = string.Empty;
    private string _pageTitle = "Software List";
    private string _statusText = "Ready";
    private string _currentPage = "List";
#if DEBUG
    private bool _useDemoData = true;
#else
    private bool _useDemoData = false;
#endif
    private InstallSnapshot? _beforeSnap;
    private InstallSnapshot? _afterSnap;

    public ObservableCollection<SoftwareItemViewModel> Software { get; } = new();
    public ObservableCollection<ReviewItemViewModel> ReviewItems { get; } = new();
    public ObservableCollection<AgentLayerViewModel> AgentLayers { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) OnPropertyChanged(nameof(FilteredSoftware)); }
    }

    public string PageTitle { get => _pageTitle; set => Set(ref _pageTitle, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string CurrentPage { get => _currentPage; set => Set(ref _currentPage, value); }

    public bool UseDemoData
    {
        get => _useDemoData;
        set { if (Set(ref _useDemoData, value)) LoadSoftwareData(); }
    }

    public IEnumerable<SoftwareItemViewModel> FilteredSoftware =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Software
            : Software.Where(s =>
                s.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (s.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

    public MainViewModel() => LoadSoftwareData();

    public void NavigateList() { CurrentPage = "List"; PageTitle = "Software List"; }
    public void NavigateReview() { CurrentPage = "Review"; PageTitle = "Leftover Review"; }
    public void NavigateAgent() { CurrentPage = "Agent"; PageTitle = "Agent Cleanup"; LoadAgentRulesIfEmpty(); }
    public void NavigateSafety() { CurrentPage = "Safety"; PageTitle = "Safety Center"; }

    public void LoadSoftwareData()
    {
        Software.Clear();
        if (_useDemoData) { LoadDemoData(); return; }
        try
        {
            StatusText = "Scanning installed software...";
            foreach (var entry in _workflow.LoadSoftware(includeStore: true, includeHidden: false))
            {
                Software.Add(new SoftwareItemViewModel
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Publisher = entry.Publisher,
                    Version = entry.Version,
                    SizeText = FormatSize(entry.EstimatedSize),
                    SourceText = SourceToText(entry.Source),
                    RiskLevel = entry.IsSystem ? "Protected" : "Low",
                    RiskText = entry.IsSystem ? "系统保护" : "低风险",
                    InstallLocation = entry.InstallLocation,
                    UninstallString = entry.UninstallString,
                    IsSystem = entry.IsSystem,
                    Entry = entry,
                    Icon = TryLoadIcon(entry),
                });
            }
            StatusText = $"Found {Software.Count} applications (including Store)";
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed: " + ex.Message;
            LoadDemoData();
        }
    }

    public void LoadDemoData()
    {
        Software.Clear();
        Software.Add(new SoftwareItemViewModel
        {
            Id = "demo-1", DisplayName = "Example Editor 3.2", Publisher = "Example Soft",
            Version = "3.2.1", SizeText = "245 MB", SourceText = "Desktop",
            RiskLevel = "Low", RiskText = "Low",
            InstallLocation = @"C:\Program Files\Example Editor",
            UninstallString = @"C:\Program Files\Example Editor\unins000.exe",
        });
        Software.Add(new SoftwareItemViewModel
        {
            Id = "demo-2", DisplayName = "Claude Code", Publisher = "Anthropic",
            Version = "2.1", SizeText = "88 MB", SourceText = "Agent",
            RiskLevel = "Medium", RiskText = "Review",
            InstallLocation = @"C:\Users\Administrator\AppData\Local\Programs\claude-code",
            UninstallString = "msiexec /x {AAAAAAAA-BBBB-CCCC-DDDD-EEEEFFFF0000}",
        });
        Software.Add(new SoftwareItemViewModel
        {
            Id = "demo-3", DisplayName = "System Update Component", Publisher = "Microsoft",
            Version = "10.0", SizeText = "32 MB", SourceText = "System",
            RiskLevel = "Protected", RiskText = "Protected", IsSystem = true,
            InstallLocation = @"C:\Windows\System32", UninstallString = "",
        });
        StatusText = $"Loaded {Software.Count} software (demo data)";
    }

    public void ScanLeftoversFor(SoftwareItemViewModel item)
    {
        ReviewItems.Clear();
        if (_useDemoData || item.Entry is null) { LoadDemoReview(); StatusText = $"Found {ReviewItems.Count} leftovers (demo)"; return; }
        try
        {
            StatusText = "Scanning leftovers...";
            foreach (var leftover in _workflow.ScanLeftovers(item.Entry))
            {
                ReviewItems.Add(ToReviewVm(leftover));
            }
            StatusText = $"Found {ReviewItems.Count} leftovers";
        }
        catch (Exception ex) { StatusText = "Leftover scan failed: " + ex.Message; }
    }

    public void ScanOrphans()
    {
        ReviewItems.Clear();
        try
        {
            StatusText = "Scanning history residuals...";
            foreach (var orphan in _workflow.ScanOrphans())
            {
                ReviewItems.Add(new ReviewItemViewModel
                {
                    PathOrKey = orphan.Location,
                    Kind = orphan.Kind.ToString(),
                    MatchReason = orphan.MatchReason,
                    Confidence = orphan.Confidence,
                    RiskLevel = orphan.Risk.ToString(),
                    RiskText = RiskToText(orphan.Risk.ToString()),
                    RecoverableText = orphan.Risk == RiskLevel.Protected ? "Protected" : "Quarantine",
                    DefaultSelected = orphan.DefaultSelected,
                    IsSelected = orphan.DefaultSelected,
                });
            }
            StatusText = $"Found {ReviewItems.Count} history residuals";
        }
        catch (Exception ex) { StatusText = "Orphan scan failed: " + ex.Message; }
    }

    public IReadOnlyList<RelatedProcess> ListRelatedProcesses(SoftwareItemViewModel item)
    {
        if (item.Entry is null) return Array.Empty<RelatedProcess>();
        try { return _workflow.FindRelatedProcesses(item.Entry); }
        catch { return Array.Empty<RelatedProcess>(); }
    }

    public string RunUninstallWithProcessConfirm(SoftwareItemViewModel item, IReadOnlyList<RelatedProcess> toClose)
    {
        if (_useDemoData || item.Entry is null) return "Demo: would uninstall " + item.DisplayName;
        if (item.IsSystem) return "System component is protected";
        try
        {
            StatusText = "Uninstalling " + item.DisplayName + "...";
            var result = _workflow.Uninstall(item.Entry, silent: false, elevate: true, processesToClose: toClose);
            var msg = result.Success ? "Uninstalled: " + item.DisplayName : "Uninstall failed: " + result.ErrorMessage;
            StatusText = msg;
            return msg;
        }
        catch (Exception ex) { StatusText = "Uninstall error: " + ex.Message; return StatusText; }
    }

    public async Task<string> RunBatchUninstallAsync()
    {
        if (!_workflow.Policy.Document.AllowBatchUninstall)
        {
            StatusText = "策略已禁用批量卸载";
            return StatusText;
        }
        var selected = Software.Where(s => s.IsSelected && s.Entry is not null && !s.IsSystem).Select(s => s.Entry!).ToList();
        if (selected.Count == 0) { StatusText = "No software selected"; return StatusText; }
        if (_useDemoData) { StatusText = $"Demo: batch uninstall {selected.Count}"; return StatusText; }
        try
        {
            StatusText = $"Batch uninstalling {selected.Count}...";
            var summary = await _workflow.BatchUninstallAsync(selected).ConfigureAwait(true);
            StatusText = $"Batch done: ok={summary.Succeeded} fail={summary.Failed} skip={summary.Skipped}";
            LoadSoftwareData();
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Batch failed: " + ex.Message; return StatusText; }
    }


    public bool HealthBusy { get; private set; }
    public string HealthSoftwareText { get; private set; } = "—";
    public string HealthAgentText { get; private set; } = "—";
    public string HealthSafeText { get; private set; } = "—";
    public string HealthSummary { get; private set; } = "点击「立即体检」检查可清理项";
    public event EventHandler? HealthUpdated;

    public void RunHealthCheck()
    {
        if (HealthBusy) return;
        HealthBusy = true;
        HealthSummary = "正在体检…";
        HealthUpdated?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = _workflow.RunHealthCheck(includeAgent: true);
            HealthSoftwareText = result.SoftwareCount + " 个软件";
            HealthAgentText = FormatSize(result.AgentCacheBytes) + " · " + result.AgentCachePaths + " 处";
            HealthSafeText = result.SafeCount + " 项可安全清理";
            HealthSummary = result.ReviewCount > 0
                ? $"发现 {result.SafeCount} 项推荐清理，{result.ReviewCount} 项建议确认"
                : result.SafeCount > 0
                    ? $"发现 {result.SafeCount} 项可安全清理"
                    : "状态良好，暂无推荐清理项";
            // Load recommended into review list
            ReviewItems.Clear();
            foreach (var item in result.RecommendedItems)
            {
                ReviewItems.Add(ToReviewVm(item));
            }
        }
        catch (Exception ex)
        {
            HealthSummary = "体检失败: " + ex.Message;
        }
        finally
        {
            HealthBusy = false;
            HealthUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenHealthReview()
    {
        if (ReviewItems.Count == 0)
        {
            RunHealthCheck();
        }
    }
    public string RunCleanup()
    {
        var selected = ReviewItems.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) { StatusText = "No leftovers selected"; return StatusText; }
        if (_useDemoData) { StatusText = $"Demo: clean {selected.Count} items"; return StatusText; }
        try
        {
            var items = selected.Select(r => r.Item).Where(i => i is not null).Select(i => i!).ToList();
            if (items.Count == 0) { StatusText = "Selected items need elevated helper"; return StatusText; }
            var summary = _workflow.Clean(items, "job-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            StatusText = $"Clean done: quarantined={summary.Quarantined} skipped={summary.Skipped} failed={summary.Failed}";
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Clean failed: " + ex.Message; return StatusText; }
    }

    public string ExportLastReport()
    {
        if (_useDemoData) return "Demo mode: disable demo to export";
        try
        {
            var report = new CleanSlate.Safety.CleanupReport(
                "export-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), DateTime.UtcNow, "ManualExport", "session", null,
                ReviewItems.Count(r => r.IsSelected), ReviewItems.Count(r => !r.IsSelected), 0,
                ReviewItems.Select(r => new CleanSlate.Safety.CleanupReportItem(
                    r.Kind, r.PathOrKey, Enum.Parse<RiskLevel>(r.RiskLevel),
                    r.IsSelected ? "Selected" : "Unselected", r.MatchReason)).ToList());
            var path = _workflow.ExportReport(report);
            StatusText = "Report: " + path;
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Export failed: " + ex.Message; return StatusText; }
    }

    public string LoadUndoJobs()
    {
        try
        {
            var jobs = _workflow.UndoLog.ListJobs();
            if (jobs.Count == 0) { StatusText = "No undo log"; return StatusText; }
            var last = jobs[0];
            StatusText = $"Undo log {jobs.Count}; last {last.JobId} / {last.Mode} / {last.Items.Count} items";
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Undo log failed: " + ex.Message; return StatusText; }
    }

    public string RestoreLatestQuarantine()
    {
        try
        {
            var folders = _workflow.Restorer.ListJobFolders();
            if (folders.Count == 0) { StatusText = "Quarantine empty"; return StatusText; }
            var summary = _workflow.RestoreJob(folders[0]);
            StatusText = $"Restore {folders[0]}: ok={summary.Restored} fail={summary.Failed}";
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Restore failed: " + ex.Message; return StatusText; }
    }

    public CleanSlate.Uninstall.ForceRemovalPlan PlanForceRemovalObject(string name) => _workflow.PlanForceRemoval(name);

    public string PlanForceRemoval(string name)
    {
        try
        {
            var plan = _workflow.PlanForceRemoval(name);
            StatusText = $"Force plan '{name}': {plan.Targets.Count} traces";
            return StatusText + "\n" + string.Join("\n", plan.Targets.Take(12).Select(t => "  " + t.Kind + ": " + t.Path));
        }
        catch (Exception ex) { StatusText = "Force plan failed: " + ex.Message; return StatusText; }
    }

    public string ExecuteForceRemoval(CleanSlate.Uninstall.ForceRemovalPlan plan)
    {
        try
        {
            var result = _workflow.ExecuteForceRemoval(plan, "force-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            StatusText = $"Force: removed={result.Removed} skipped={result.Skipped} failed={result.Failed}";
            return StatusText;
        }
        catch (Exception ex) { StatusText = "Force failed: " + ex.Message; return StatusText; }
    }

    public string CaptureSnapshot(string label)
    {
        if (_useDemoData) return "Demo mode: disable demo for install monitor";
        try
        {
            var snap = _workflow.CaptureInstallSnapshot(label);
            if (label == "before") _beforeSnap = snap; else _afterSnap = snap;
            StatusText = $"Snapshot {label}: {snap.Files.Count} files";
            return StatusText;
        }
        catch (Exception ex) { return "Snapshot failed: " + ex.Message; }
    }

    public string DiffSnapshots()
    {
        if (_beforeSnap is null || _afterSnap is null) return "Need before+after snapshots";
        var diff = _workflow.DiffInstallSnapshots(_beforeSnap, _afterSnap);
        StatusText = $"Diff: +{diff.AddedFiles.Count} ~{diff.ChangedFiles.Count} -{diff.RemovedFiles.Count}";
        return StatusText;
    }

    public IReadOnlyList<BrowserExtension> LoadBrowserExtensions()
    {
        try { return _workflow.LoadBrowserExtensions(); }
        catch { return Array.Empty<BrowserExtension>(); }
    }

    public string Relocate(string source, string dest)
    {
        try
        {
            var result = _workflow.RelocateSoftware(source, dest);
            StatusText = result.Success ? "Relocated: " + result.JournalPath : result.ErrorMessage ?? "Relocate failed";
            return StatusText;
        }
        catch (Exception ex) { return "Relocate failed: " + ex.Message; }
    }

    public string RollbackRelocation(string journalPath)
    {
        try
        {
            var result = _workflow.RollbackRelocation(journalPath);
            StatusText = result.Success ? "Rollback OK" : result.ErrorMessage ?? "Rollback failed";
            return StatusText;
        }
        catch (Exception ex) { return "Rollback failed: " + ex.Message; }
    }

    public string UpdateAgentRulesInteractive()
    {
        try
        {
            var load = _workflow.LoadAgentRules();
            StatusText = load.Success ? $"Rules reloaded: {load.Rules?.Count ?? 0}" : "Rules failed: " + load.ErrorMessage;
            return StatusText;
        }
        catch (Exception ex) { return "Update failed: " + ex.Message; }
    }

    public async Task<string> UpdateRulesFromCdnAsync()
    {
        try
        {
            StatusText = "Checking CDN rules...";
            var result = await _workflow.UpdateRulesFromCdnAsync();
            StatusText = result.Success ? "CDN rules updated" : "CDN failed: " + result.ErrorCode + " " + result.ErrorMessage;
            return StatusText;
        }
        catch (Exception ex) { return "CDN error: " + ex.Message; }
    }

    public void LoadAgentRulesIfEmpty()
    {
        if (AgentLayers.Count > 0) return;
        if (_useDemoData)
        {
            AgentLayers.Add(new AgentLayerViewModel
            {
                AgentId = "demo", DisplayName = "Demo Agent", LayerName = "L1Cache",
                LayerTitle = L.Get("Layer_L1"), RiskText = L.Get("Risk_Low"),
                TargetPath = @"%APPDATA%\DemoAgent\cache", Notes = "safe", Expanded = true, StatusText = "Demo",
            });
            StatusText = "Agent rules (demo)";
            return;
        }
        var load = _workflow.LoadAgentRules();
        if (!load.Success || load.Rules is null) { StatusText = "Agent rules failed: " + load.ErrorMessage; return; }
        foreach (var rule in load.Rules)
        {
            foreach (var layer in _workflow.PlanAgentLayers(rule))
            {
                AgentLayers.Add(new AgentLayerViewModel
                {
                    AgentId = layer.AgentId,
                    DisplayName = layer.DisplayName,
                    LayerName = layer.LayerName,
                    LayerTitle = LayerTitle(layer.LayerName),
                    RiskText = RiskToText(layer.Risk.ToString()),
                    RiskLevel = layer.Risk.ToString(),
                    TargetPath = layer.TargetPath,
                    Notes = layer.Notes,
                    Expanded = layer.Expanded,
                    StatusText = layer.Expanded ? "Ready" : (string.IsNullOrEmpty(layer.TargetPath) ? "Blocked" : "Path missing"),
                    Item = layer,
                    IsSelected = layer.DefaultSelected,
                });
            }
        }
        StatusText = $"Loaded {AgentLayers.Count} agent layers ({load.Rules.Count} rules)";
    }

    public void NavigateAgentItemsToReview()
    {
        LoadAgentRulesIfEmpty();
        ReviewItems.Clear();
        foreach (var a in AgentLayers.Where(x => x.Expanded && !string.IsNullOrEmpty(x.TargetPath)))
        {
            ReviewItems.Add(new ReviewItemViewModel
            {
                PathOrKey = a.TargetPath,
                Kind = "Agent · " + a.LayerTitle,
                MatchReason = a.Notes ?? a.DisplayName,
                Confidence = 80,
                RiskLevel = a.RiskLevel ?? "Medium",
                RiskText = a.RiskText,
                RecoverableText = "隔离区",
                DefaultSelected = a.LayerName is "L1Cache" or "L2Session",
                IsSelected = a.LayerName is "L1Cache" or "L2Session",
            });
        }
        StatusText = "已加载 Agent 清理项 " + ReviewItems.Count + " 条（L1/L2 默认推荐）";
    }

    public string RunAgentCleanup()
    {
        var selected = AgentLayers.Where(a => a.IsSelected && a.Item is not null).Select(a => a.Item!).ToList();
        if (selected.Count == 0) { StatusText = "No agent layers selected"; return StatusText; }
        if (_useDemoData) { StatusText = $"Demo: agent clean {selected.Count}"; return StatusText; }
        try
        {
            var result = _workflow.CleanAgentLayers(selected, "agent-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            StatusText = $"Agent clean: processed={result.Processed} deleted={result.Deleted} skipped={result.Skipped}";
            return StatusText;
        }
        catch (Exception ex) { return "Agent clean failed: " + ex.Message; }
    }

    private static ReviewItemViewModel ToReviewVm(LeftoverItem leftover) => new()
    {
        PathOrKey = leftover.PathOrKey,
        Kind = leftover.Kind.ToString(),
        MatchReason = leftover.MatchReason,
        Confidence = leftover.Confidence,
        RiskLevel = leftover.Risk.ToString(),
        RiskText = RiskToText(leftover.Risk.ToString()),
        RecoverableText = leftover.Recoverable == Recoverability.Quarantine ? "Quarantine" : "-",
        DefaultSelected = leftover.DefaultSelected,
        IsSelected = leftover.DefaultSelected,
        Item = leftover,
    };

    private void LoadDemoReview()
    {
        ReviewItems.Add(new ReviewItemViewModel
        {
            PathOrKey = @"C:\Users\Administrator\AppData\Local\Example Editor\cache",
            Kind = "Folder", MatchReason = "name match", Confidence = 90,
            RiskLevel = "Low", RiskText = "Low", RecoverableText = "Quarantine",
            DefaultSelected = true, IsSelected = true,
        });
        ReviewItems.Add(new ReviewItemViewModel
        {
            PathOrKey = @"HKCU\Software\Example Editor",
            Kind = "Registry", MatchReason = "vendor key", Confidence = 75,
            RiskLevel = "Medium", RiskText = "Review", RecoverableText = "RegExport",
            DefaultSelected = false, IsSelected = false,
        });
        ReviewItems.Add(new ReviewItemViewModel
        {
            PathOrKey = @"C:\Users\Administrator\Documents\notes.txt",
            Kind = "File", MatchReason = "weak match", Confidence = 30,
            RiskLevel = "High", RiskText = "High", RecoverableText = "Confirm",
            DefaultSelected = false, IsSelected = false,
        });
    }

    private static string LayerTitle(string name) => name switch
    {
        "L1Cache" or "L1" => L.Get("Layer_L1"),
        "L2Session" or "L2" => L.Get("Layer_L2"),
        "L3Config" or "L3" => L.Get("Layer_L3"),
        "L4Credential" or "L4" => L.Get("Layer_L4"),
        "L5Model" or "L5" => L.Get("Layer_L5"),
        _ => name,
    };

    public static string FormatSize(long bytes) =>
        bytes <= 0 ? "-" : $"{bytes / 1024.0 / 1024.0:F1} MB";

    /// <summary>Extract 32x32 icon from exe/ico; fallback to generated letter tile.</summary>
    public static object? TryLoadIcon(SoftwareEntry entry)
    {
        try
        {
            string? path = entry.InstallLocation;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                // try uninstall string exe
                path = null;
            }

            if (path is not null)
            {
                var exe = Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f.Length)
                    .FirstOrDefault();
                if (exe is not null)
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (icon is not null)
                    {
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                        if (src is not null) { src.Freeze(); return src; }
                    }
                }
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    public static string RiskToText(string risk) => risk switch
    {
        "Low" => "低风险",
        "Medium" => "建议确认",
        "High" => "高风险",
        "Protected" => "系统保护",
        _ => risk,
    };

    public static string SourceToText(SoftwareSource source) => source switch
    {
        SoftwareSource.Msi => "MSI 安装",
        SoftwareSource.Store => "商店应用",
        SoftwareSource.Hidden => "隐藏项",
        SoftwareSource.Service => "服务",
        SoftwareSource.Portable => "便携软件",
        _ => "桌面软件",
    };
}