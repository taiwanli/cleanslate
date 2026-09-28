using System.Globalization;
using System.Windows;

namespace CleanSlate.App;

/// <summary>
/// Simple culture switcher + string lookup for zh-CN / en-US.
/// </summary>
public static class L
{
    // Default to Simplified Chinese; fall back to system culture only if explicitly zh-*/en-* preferred otherwise.
    public static CultureInfo Culture { get; private set; } = ResolveInitialCulture();

    private static CultureInfo ResolveInitialCulture()
    {
        try
        {
            var sys = CultureInfo.CurrentUICulture;
            if (sys.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return new CultureInfo("zh-CN");
            }

            if (sys.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return new CultureInfo("en-US");
            }
        }
        catch
        {
            // ignore
        }

        return new CultureInfo("zh-CN");
    }

    public static event EventHandler? CultureChanged;

    public static void SetCulture(string name)
    {
        Culture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        var en = Culture.TwoLetterISOLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        return (key, en) switch
        {
            ("AppTitle", _) => en ? "CleanSlate — Uninstaller" : "CleanSlate — 完美卸载",
            ("NavList", _) => en ? "Software List" : "软件列表",
            ("NavReview", _) => en ? "Leftover Review" : "残留审阅",
            ("NavAgent", _) => en ? "Agent Cleanup" : "Agent 专项",
            ("NavSafety", _) => en ? "Safety Center" : "安全中心",
            ("SafeMode", _) => en ? "Safe mode" : "安全模式",
            ("SafeToQuarantine", _) => en ? "Default clean to quarantine" : "默认清理至隔离区",
            ("SearchHint", _) => en ? "Filter by name or publisher" : "按名称或发布者筛选",
            ("Refresh", _) => en ? "Refresh" : "刷新",
            ("ScanLeftover", _) => en ? "Scan leftovers" : "扫描残留",
            ("HistoryResidual", _) => en ? "History residuals" : "历史残留",
            ("DemoData", _) => en ? "Demo data" : "演示数据",
            ("Uninstall", _) => en ? "Uninstall" : "卸载",
            ("Detail", _) => en ? "Details" : "详情",
            ("CleanToQuarantine", _) => en ? "Clean to quarantine" : "清理到隔离区",
            ("ExportReport", _) => en ? "Export report" : "导出报告",
            ("BatchUninstall", _) => en ? "Batch uninstall" : "批量卸载",
            ("SelectAll", _) => en ? "Select all" : "全选",
            ("AgentClean", _) => en ? "Clean selected layers" : "清理选中层",
            ("ReviewHint", _) => en ? "Review before clean. High-risk items are not pre-selected. Protected paths are never deleted." : "请审阅后再清理。高风险项默认不勾选。系统保护项永不删除。",
            ("AgentHint", _) => en ? "L1 cache · L2 session · L3 config · L4 credential · L5 model. L4/L5 need extra confirmation." : "L1 缓存 · L2 会话 · L3 配置 · L4 凭证 · L5 模型。L4/L5 默认不勾选。",
            ("UndoLog", _) => en ? "Undo log" : "撤销日志",
            ("RegistryBackup", _) => en ? "Registry backup" : "注册表备份",
            ("DarkTheme", _) => en ? "Dark theme" : "深色主题",
            ("Language", _) => en ? "Language" : "语言",
            ("Ready", _) => en ? "Ready" : "就绪",
            ("Layer_L1", _) => en ? "L1 Cache" : "L1 缓存",
            ("Layer_L2", _) => en ? "L2 Session" : "L2 会话",
            ("Layer_L3", _) => en ? "L3 Config" : "L3 配置",
            ("Layer_L4", _) => en ? "L4 Credential" : "L4 凭证",
            ("Layer_L5", _) => en ? "L5 Model" : "L5 模型",
            ("Risk_Low", _) => en ? "Low risk" : "低风险",
            ("Risk_Medium", _) => en ? "Review needed" : "需确认",
            ("Risk_High", _) => en ? "High risk" : "高风险",
            ("Risk_Protected", _) => en ? "System protected" : "系统保护",
            ("ProjectResidual", _) => en ? "Project agent residuals" : "项目级 Agent 残留",
            _ => key,
        };
    }
}
