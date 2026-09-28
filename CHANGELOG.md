# CHANGELOG

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 解决方案骨架：Core / Inventory / Uninstall / Leftover / AgentRules / Safety / ElevatedHelper / App
- 文档模板：PRD、ADR、开发环境、Git 工作流、编码规范、配置、路径、测试策略、数据契约、发布检查单
- 领域模型草案与 Agent 规则包 JSON Schema + 样例
- **Safety**：`PathSafetyPolicy`（保护路径/共享运行时/风险分级）、`QuarantineService`（隔离与恢复）
- **Inventory**：`RegistrySoftwareInventory`（HKLM/HKCU Uninstall 枚举、隐藏/捆绑标记、静默检测）
- **AgentRules**：`AgentRuleLoader`（JSON 规则包，L4/L5 强制默认不勾选）
- **Leftover**：`LeftoverScanner`（目录残留扫描 + 置信度/风险标注）
- **Uninstall**：`UninstallCommandParser`（MSI/NSIS/Inno 解析、静默参数）+ `UninstallExecutor`（执行与相关进程结束）
- **Leftover**：`OrphanScanner`（注册表损坏卸载项 + 孤儿文件夹）
- **App**：液态玻璃 × 新拟态主界面（软件列表 / 残留审阅 / Agent / 安全中心）
- **CI**：`.github/workflows/ci.yml` 使用 `tools/dotnet-env.ps1`（ProgramFiles 环境修复）
- **Workflow**：`CleanupWorkflowService` 串联枚举→卸载→残留扫描→隔离区；UI 可切换演示/真实数据
- **ElevatedHelper**：命名管道协议 + SafetyGate + `ElevatedCleanupClient`（注册表/文件删除，可提权）
- **Agent**：规则包计划器 + UI 分层清理（L4/L5 默认跳过自动删除）
- **Report**：`CleanupReportExporter` JSON/HTML 报告
- **Inventory**：Appx/UWP（Get-AppxPackage）、隐藏项、Composite 合并枚举
- **Leftover**：服务/计划任务/快捷方式/注册表残留扫描
- **Uninstall**：`BatchUninstallService` 批量卸载
- **Agent**：`agent-rules.json` 30 条规则 + `ProjectAgentResidualScanner`
- **Safety**：`UndoLogService` 撤销日志 + 注册表 `.reg` 备份
- **UI**：深色主题、中英切换、批量卸载、安全中心撤销入口
- **Inventory**：启动项关联、便携软件启发式枚举
- **Uninstall**：强制卸载向导规划、占用文件延迟删除（PendingFileRename）
- **Storage**：`HistoryStore`（SQLite 清理历史）、`RulePackWatcher` 规则热更新
- **Safety**：`QuarantineRestorer` 一键恢复隔离区任务
- **Phase2**：`InstallMonitor` 安装快照 diff、`BrowserExtensionInventory`、`SoftwareRelocator`、`PolicyWhitelist`
- **CLI**：`cleanslate` 静默命令（list/uninstall/clean-leftovers/orphans/export-report）
- **Phase2 UI**：安装监控 / 浏览器扩展 / 软件搬迁页签；强制卸载可执行
- **Rules**：`RulePackUpdater` URL/文件更新 + SHA256 校验 + 备份；`RulePackCdnClient` 正式 CDN（manifest/fallback）
- **UI**：拖拽文件/目录到窗口 → 强制卸载向导/执行
- **GapFill**：COM/防火墙/环境变量残留；服务停止/禁用；扩展禁用/移除；占用文件延迟删除接线；孤儿三组 UI；列表虚拟化
- **Release**：便携 ZIP 发布脚本、PRIVACY、LICENSE；集成测试 5 项
- **Bugfix**：用户已知文件夹整树保护；UNC 路径不再被压扁；PendingFileRename 格式；文件夹隔离 meta/可恢复；注册表优先删键；强制卸载注册表走 Helper；批量卸载 UI 异步；启动项不再误读 StartupApproved
- **P0 001–008**：Demo 默认关闭、rules 路径、层 relativePath、跨卷隔离、命令解析、进程确认、UAC 提权、Helper 管道 ACL+Token
- 单元测试 64

### Changed

- 无

### Fixed

- 无

## [0.1.0-alpha.1] — 未发布

初始工程骨架。
