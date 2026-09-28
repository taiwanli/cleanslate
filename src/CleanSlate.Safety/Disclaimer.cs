namespace CleanSlate.Safety;

/// <summary>
/// Startup disclaimer / terms shown before first use.
/// </summary>
public static class Disclaimer
{
    public const string Version = "DISCLAIMER-CN-01";

    public static string ChineseText { get; } = """
        【免责声明】请在使用前仔细阅读。使用本软件即表示您接受以下条款。

        一、软件性质
        CleanSlate（以下简称「本软件」）是一款运行于 Windows 的本地卸载、残留清理与系统维护辅助工具。本软件按「现状」提供，不保证适用于所有软件、所有 Windows 版本或所有硬件环境。

        二、风险提示（重要）
        1. 卸载、删除文件、清理注册表、禁用服务、搬迁软件目录等操作可能导致程序无法运行、数据丢失、系统不稳定，严重时可能需要重装系统或恢复备份。
        2. 尽管本软件内置保护路径、风险分级、隔离区与撤销日志等安全机制，但仍无法完全避免误判。任何自动化或启发式匹配都可能产生误报。
        3. 「强制卸载」「Agent 凭证/模型清理」「注册表删除」等高风险功能，一旦执行可能造成不可逆后果。请务必自行确认目标。

        三、用户责任
        1. 您应在使用前备份重要数据（包括文档、项目代码、数据库、密钥与模型文件）。
        2. 您应对自己的勾选与点击负责。本软件默认尽量不删除用户文档、桌面、下载、OneDrive 等内容，但最终以您确认的列表为准。
        3. 请勿在无管理员授权的设备上使用提权清理功能。

        四、责任限制
        在适用法律允许的最大范围内，作者与贡献者不对因使用或无法使用本软件而产生的任何直接、间接、附带、特殊、惩罚性或后果性损害承担责任，包括但不限于数据丢失、利润损失、业务中断。使用本软件即表示您自行承担全部风险。

        五、第三方与隐私
        1. 本软件默认不上传您的软件清单与文件内容。
        2. 可选的「从 CDN 更新规则包」仅下载公开 Agent 清理规则，详见隐私说明。
        3. 浏览器、系统组件与第三方软件的使用须遵守其各自许可与条款。

        六、开源与许可
        本软件遵循仓库中的 LICENSE。第三方依赖遵循各自许可证。

        ——
        若您不同意上述任何条款，请选择「不同意，退出」。
        """;

    /// <summary>Returns true if disclaimer was previously accepted on this machine.</summary>
    public static bool IsAccepted(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            var text = File.ReadAllText(settingsPath);
            return text.Contains("\"disclaimerAccepted\":true", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("\"disclaimerAccepted\": true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void MarkAccepted(string settingsPath)
    {
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(settingsPath, $$"""
            {
              "disclaimerAccepted": true,
              "disclaimerVersion": "{{Version}}",
              "acceptedUtc": "{{DateTime.UtcNow:o}}"
            }
            """);
    }

    public static string DefaultSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanSlate", "settings.json");
}
