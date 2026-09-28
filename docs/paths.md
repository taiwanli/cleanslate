# 本地路径与权限

| 数据 | 路径 | 权限 | 备注 |
|------|------|------|------|
| 用户设置 | `%LOCALAPPDATA%\CleanSlate\settings.json` | 用户 | |
| 日志 | `%LOCALAPPDATA%\CleanSlate\logs\` | 用户 | 可一键打包 |
| 隔离区 | `%LOCALAPPDATA%\CleanSlate\quarantine\` | 用户 | 可恢复删除项 |
| 清理历史 | `%LOCALAPPDATA%\CleanSlate\data\history.db` | 用户 | SQLite |
| 规则包 | `%LOCALAPPDATA%\CleanSlate\rules\` | 用户/管理员写 | 热更新 |
| 安装目录 | `Program Files\CleanSlate\` | 管理员 | 程序只读 |
| 暂存/转储 | `%TEMP%\CleanSlate\` | 用户 | 用后清理 |

## 保护路径（规则层硬拒绝删除）

- `C:\Windows\**`（除明确列出的缓存且产品策略允许，MVP 默认不删）  
- `C:\Program Files\WindowsApps\**`（系统包策略）  
- 用户已知文件夹：文档、桌面、下载、图片、音乐、视频、OneDrive 根  
- `$Recycle.Bin`、系统还原、EFI/恢复分区  
- 共享运行时：`Common Files`、VC++/DirectX 等（默认保护）  

## 提权

- 删除系统服务/驱动/部分 Program Files 残留时启动 `CleanSlate.ElevatedHelper`。  
- 辅助进程仅接受主进程管道消息，消息含一次性令牌。
