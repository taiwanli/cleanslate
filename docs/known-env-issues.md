# 已知环境问题（开发机）

## NuGet restore 失败：`Value cannot be null. (Parameter 'path1')`

### 状态：**已定位并修复**（2026-09-27）

### 根因

进程环境中 **`ProgramFiles` / `ProgramFiles(x86)` 环境变量为 null**（注册表里可能有，但当前会话/工具链启动的进程未继承）。

NuGet 的 `NuGetEnvironment.GetFolderPath` 在解析 `MachineWideSettingsBaseDirectory` / `MachineWideConfigDirectory` 时执行：

```text
Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)"), "NuGet", ...)
```

`path1` 为 null → `ArgumentNullException`。

与 SDK 版本无关：系统 .NET 10 与侧装 .NET 8 均会中招。

### 修复

1. **注册表**（已写入 HKLM / HKCU）：
   - `ProgramFiles` = `C:\Program Files`
   - `ProgramFiles(x86)` = `C:\Program Files (x86)`
   - `ProgramW6432` = `C:\Program Files`
   - `NUGET_PACKAGES` = `%USERPROFILE%\.nuget\packages`（用户级）

2. **每次调用 dotnet 前在进程内设置**（工具链会话可能仍不继承注册表）：

```powershell
[Environment]::SetEnvironmentVariable("ProgramFiles", "C:\Program Files", "Process")
[Environment]::SetEnvironmentVariable("ProgramFiles(x86)", "C:\Program Files (x86)", "Process")
[Environment]::SetEnvironmentVariable("ProgramW6432", "C:\Program Files", "Process")
[Environment]::SetEnvironmentVariable("NUGET_PACKAGES", "C:\Users\Administrator\.nuget\packages", "Process")
$env:MSBUILDDISABLENODEREUSE = "1"
```

**推荐直接使用包装脚本**：

```powershell
cd CleanSlate
.\tools\dotnet-env.ps1 restore CleanSlate.sln
.\tools\dotnet-env.ps1 build CleanSlate.sln -c Release
.\tools\dotnet-env.ps1 test tests\CleanSlate.UnitTests\CleanSlate.UnitTests.csproj -c Release --no-build
```

### 侧装 .NET 8 SDK

系统仅预装 SDK 10.0.401，缺少 net8.0 运行时（testhost 会失败）。已在：

```text
C:\dotnet8\   # SDK 8.0.425 + runtime 8.0.31
```

包装脚本优先使用 `C:\dotnet8\dotnet.exe`。测试时需 `DOTNET_ROOT=C:\dotnet8`。

### 验证记录

| 步骤 | 结果 |
|------|------|
| `dotnet restore CleanSlate.sln` | 11 项目成功 |
| `dotnet build CleanSlate.sln -c Release` | 0 警告 0 错误 |
| `dotnet test CleanSlate.UnitTests` | 通过 1/1 |

### 若再次出现 path1

1. 检查：`[Environment]::GetEnvironmentVariable('ProgramFiles(x86)')` 是否为 null  
2. 必须用 `tools/dotnet-env.ps1` 或上述 `SetEnvironmentVariable(..., 'Process')`  
3. 勿依赖 PowerShell `$env:ProgramFiles(x86)`（括号名在 Env: 提供程序下易出错）  

### 附带发现

- `USERNAME=SYSTEM` 但 `USERPROFILE=C:\Users\Administrator`（工具链会话特征）  
- 工作负载解析器曾报 `WorkloadAutoImportPropsLocator` location=null（次要，未影响最终构建）
