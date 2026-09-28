# 数据模型与接口

实现以 `src/CleanSlate.Core/Models.cs` 为代码源；本文件为语义说明。

## 领域模型

### SoftwareEntry

| 字段 | 说明 |
|------|------|
| Id | 稳定 ID（优先 Uninstall 键 / 包全名） |
| DisplayName / Publisher / Version | 展示 |
| Source | Registry, Msi, Store, Portable, Service, Hidden, Other |
| InstallLocation / UninstallString | 路径与卸载命令 |
| EstimatedSize | 字节 |
| RiskTags | 捆绑/隐藏/系统等 |
| CanSilentUninstall / IsSystem | 能力与保护 |

### LeftoverItem

| 字段 | 说明 |
|------|------|
| Kind | File, Folder, Registry, Service, Task, Com, Shortcut, Other |
| PathOrKey | 可展示、可复制 |
| MatchReason | 匹配理由（供 UI「五问」） |
| Confidence | 0–100 |
| Risk | Low / Medium / High / Protected |
| DefaultSelected | 仅 Low 且非保护可为 true |
| Recoverable | Quarantine / RecycleBin / None |

### AgentCleanupLayer

层名：`L1Cache` `L2Session` `L3Config` `L4Credential` `L5Model`  
默认：L1 可选清；L2–L5 默认不选；L4/L5 强制二次确认。

## IPC（UI ↔ 引擎 ↔ 提权助手）

| 消息 | 方向 | 载荷 | 超时 |
|------|------|------|------|
| `Inventory.Scan` | App→Engine | filters | 可取消 |
| `Uninstall.Run` | App→Engine | softwareId, options | 可取消 |
| `Leftover.Scan` | App→Engine | jobId, scope | 可取消 |
| `Leftover.Clean` | App→Engine | jobId, itemIds, recover mode | — |
| `Elevated.Delete` | Engine→Helper | paths/keys + token | 30s |
| `Progress` | Engine→App | percent, phase | — |

协议：JSON Lines over Named Pipe `\\.\pipe\CleanSlate`；错误格式：

```json
{ "ok": false, "code": "E_PATH_PROTECTED", "message": "..." }
```

## 错误码（节选）

| Code | 含义 |
|------|------|
| E_PATH_PROTECTED | 命中保护路径 |
| E_RISK_NOT_CONFIRMED | 高风险未确认 |
| E_ELEVATION_DENIED | 用户拒绝 UAC |
| E_IN_USE | 文件占用（可延迟删除） |
| E_RULE_INVALID | 规则包无效 |
| E_UNDO_EXPIRED | 撤销期已过 |

## 规则包 Schema

见 `agent-rules.schema.json`（同目录）。

## 报告导出

- `report.json`：job 元数据 + items + 结果  
- 版本字段 `reportVersion: 1`
