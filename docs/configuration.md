# 配置管理

## 优先级（低 → 高覆盖）

1. 代码内默认值  
2. 安装目录/程序只读配置  
3. 用户设置 `%LOCALAPPDATA%\CleanSlate\settings.json`  
4. 策略/规则包（版本化）  
5. 环境变量（仅开发诊断，前缀 `CLEANSLATE_`）  

## 用户设置示例（schema 草案）

```json
{
  "schemaVersion": 1,
  "appearance": {
    "theme": "system",
    "reduceTransparency": false
  },
  "cleanup": {
    "defaultRecoverability": "Quarantine",
    "quarantineRetentionDays": 14
  },
  "scan": {
    "depth": "standard"
  },
  "privacy": {
    "telemetry": false
  }
}
```

## 敏感信息

- 证书、API Key **禁止**入库。  
- 用户凭证清理只删除用户明确勾选的路径；应用自身不存储第三方凭证。  

## 规则包

- 位置：`%LOCALAPPDATA%\CleanSlate\rules\`  
- 格式：见 `docs/api/agent-rules.schema.json`  
- 加载失败：跳过并记警告，不得崩溃。  

## 环境

| 环境 | 日志级别 | Mock | 说明 |
|------|----------|------|------|
| Development | Debug | 可用 | `--dev-mock` |
| Test | Information | 关 | |
| Release | Warning | 关 | |
