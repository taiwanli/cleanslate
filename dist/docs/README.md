# CleanSlate — Windows 完美卸载工具

通透干净、可审可控的 Windows 卸载与残留治理工具。覆盖：全量软件枚举（含隐藏项）、卸载后残留清理、历史孤儿残留、Agent 工具专项清理，以及隔离区撤销等安全机制。

## 文档索引

| 文档 | 说明 |
|------|------|
| [docs/requirements/PRD.md](docs/requirements/PRD.md) | 产品需求基线 |
| [docs/adr/](docs/adr/) | 架构决策记录 |
| [docs/dev-setup.md](docs/dev-setup.md) | 开发环境搭建 |
| [docs/git-ops.md](docs/git-ops.md) | 分支与版本策略 |
| [docs/coding-standards.md](docs/coding-standards.md) | 编码规范 |
| [docs/configuration.md](docs/configuration.md) | 配置管理 |
| [docs/paths.md](docs/paths.md) | 本地路径与权限 |
| [docs/test-strategy.md](docs/test-strategy.md) | 测试策略 |
| [docs/api/](docs/api/) | 数据模型与接口 |
| [docs/release-checklist.md](docs/release-checklist.md) | 发布与回滚 |

历史设计稿（会话交付物）：功能与实现范围、UI 设计报告（液态玻璃×新拟态）、开发前准备清单。

## 解决方案结构

```
src/
  CleanSlate.App              UI（WPF / WinUI 3）
  CleanSlate.Core             领域模型与工作流
  CleanSlate.Inventory        软件枚举
  CleanSlate.Uninstall        卸载执行
  CleanSlate.Leftover         残留扫描与清理
  CleanSlate.AgentRules       Agent 规则包
  CleanSlate.Safety           白名单、隔离区、撤销
  CleanSlate.ElevatedHelper   提权助手
tests/
  CleanSlate.UnitTests
  CleanSlate.IntegrationTests
  CleanSlate.UiSmokeTests
```

## 快速开始

```powershell
# 需要：.NET 8 SDK、Windows 10/11 x64
dotnet --list-sdks
dotnet restore CleanSlate.sln
dotnet build CleanSlate.sln
dotnet test CleanSlate.sln
```

详细步骤见 [docs/dev-setup.md](docs/dev-setup.md)。  
若本机 `dotnet restore` 报 NuGet `path1` 错误，见 [docs/known-env-issues.md](docs/known-env-issues.md)。

## 设计原则（摘要）

1. **先审后删**：任何删除必须经过审阅列表。  
2. **红色项永不默认勾选**；系统/凭证/模型/工作区强保护。  
3. **优先可恢复**（隔离区 / 回收站），永久删除为次级动作。  
4. **玻璃 UI + 新拟态控件**，风险操作保持高对比。  

## 版本

SemVer：`0.1.0-alpha`（预开发）。详见 [docs/git-ops.md](docs/git-ops.md)。

## 许可

待定（TBD）— 发布前由产品与法务确认。
