using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanSlate.Core;

namespace CleanSlate.Safety;

/// <summary>
/// Exports cleanup audit reports as JSON and HTML.
/// </summary>
public sealed class CleanupReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ExportJson(CleanupReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"cleanup-report-{report.JobId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
        return path;
    }

    public string ExportHtml(CleanupReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"cleanup-report-{report.JobId}.html");
        File.WriteAllText(path, BuildHtml(report), Encoding.UTF8);
        return path;
    }

    private static string BuildHtml(CleanupReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\" />");
        sb.AppendLine("<title>CleanSlate 清理报告</title>");
        sb.AppendLine("""
        <style>
          body { font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; background:#E8EEF5; color:#0F1C2E; margin:24px; }
          .card { background:#fff; border-radius:16px; padding:20px; margin-bottom:16px; box-shadow:0 8px 24px rgba(15,28,46,.08); }
          h1 { font-size:22px; margin:0 0 8px; }
          .meta { color:#3D5168; font-size:13px; }
          table { width:100%; border-collapse:collapse; font-size:13px; }
          th, td { text-align:left; padding:8px; border-bottom:1px solid #E4EBF3; }
          .ok { color:#18A673; } .warn { color:#E6A100; } .bad { color:#E24B4A; }
          .badge { display:inline-block; padding:2px 8px; border-radius:8px; font-size:12px; }
        </style>
        </head><body>
        """);

        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine($"<h1>CleanSlate 清理报告</h1>");
        sb.AppendLine($"<div class=\"meta\">任务 {System.Net.WebUtility.HtmlEncode(report.JobId)} · {report.UtcTimestamp:yyyy-MM-dd HH:mm:ss} UTC · 模式 {System.Net.WebUtility.HtmlEncode(report.Mode)}</div>");
        sb.AppendLine($"<div class=\"meta\">结果：成功 <b class=\"ok\">{report.Succeeded}</b> · 跳过 <b class=\"warn\">{report.Skipped}</b> · 失败 <b class=\"bad\">{report.Failed}</b></div>");
        if (!string.IsNullOrWhiteSpace(report.TargetName))
        {
            sb.AppendLine($"<div class=\"meta\">目标：{System.Net.WebUtility.HtmlEncode(report.TargetName)}</div>");
        }

        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"card\"><h2>项目明细</h2><table><thead><tr><th>类型</th><th>路径/键</th><th>风险</th><th>结果</th><th>说明</th></tr></thead><tbody>");
        foreach (var item in report.Items)
        {
            var riskClass = item.Risk switch
            {
                RiskLevel.Low => "ok",
                RiskLevel.High => "bad",
                RiskLevel.Protected => "bad",
                _ => "warn",
            };

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(item.Kind)}</td>");
            sb.AppendLine($"<td><code>{System.Net.WebUtility.HtmlEncode(item.PathOrKey)}</code></td>");
            sb.AppendLine($"<td><span class=\"badge {riskClass}\">{System.Net.WebUtility.HtmlEncode(item.Risk.ToString())}</span></td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(item.Outcome)}</td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(item.Message ?? item.MatchReason ?? "")}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></div>");

        sb.AppendLine("<div class=\"card\"><div class=\"meta\">隔离区：");
        sb.AppendLine(System.Net.WebUtility.HtmlEncode(report.QuarantineRoot ?? "—"));
        sb.AppendLine(" · 先审后删 · 高风险默认不选 · 系统路径受保护</div></div>");

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}

public sealed record CleanupReport(
    string JobId,
    DateTime UtcTimestamp,
    string Mode,
    string? TargetName,
    string? QuarantineRoot,
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<CleanupReportItem> Items);

public sealed record CleanupReportItem(
    string Kind,
    string PathOrKey,
    RiskLevel Risk,
    string Outcome,
    string? MatchReason = null,
    string? Message = null);
