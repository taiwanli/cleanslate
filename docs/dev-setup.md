# 开发环境搭建

## 1. 系统要求

- Windows 10 22H2+ 或 Windows 11（x64）
- 管理员权限（调试提权助手时）
- 磁盘：建议 ≥ 40GB 可用（样本软件与 VM）

## 2. 必装工具

| 工具 | 版本 | 安装 |
|------|------|------|
| Visual Studio 2022 | 17.8+ | 工作负载：.NET 桌面开发；组件：Windows 11 SDK |
| .NET SDK | 8.0 | `winget install Microsoft.DotNet.SDK.8` 或 https://dotnet.microsoft.com |
| Git | 2.4x | `winget install Git.Git` |
| Windows Terminal | 最新 | 可选 |

```powershell
dotnet --list-sdks   # 应含 8.x
git --version
```

## 3. 克隆与构建

```powershell
git clone <repo-url> CleanSlate
cd CleanSlate
dotnet restore CleanSlate.sln
dotnet build CleanSlate.sln
dotnet test tests\CleanSlate.UnitTests\CleanSlate.UnitTests.csproj
```

## 4. 开发配置

- 见 [configuration.md](configuration.md)、[paths.md](paths.md)。
- 调试删除逻辑时注意杀软拦截；建议在虚拟机中做集成测试。

## 5. 测试样本软件

见 `docs/test-software-matrix.md`（待 QA 补全）。

## 6. 常见问题

| 现象 | 处理 |
|------|------|
| 缺少 Windows SDK | VS Installer 补装 |
| WPF 目标框架错误 | 确认 `net8.0-windows` 与 `UseWPF` |
| UAC 提权测试 | 准备本地管理员账户 |
