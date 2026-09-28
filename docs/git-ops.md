# Git 工作流与版本策略

## 分支模型

| 分支 | 用途 | 规则 |
|------|------|------|
| `main` | 主干，可发布 | PR + ≥1 审批 + CI 绿；禁止 force-push |
| `feature/<scope>` | 功能 | 从 main 开出，短生命周期 |
| `fix/<issue>` | 缺陷 | 同上 |
| `release/*` | 发版稳定 | 仅 bugfix 与文档 |
| `hotfix/*` | 紧急 | TL + SEC 双审 |

## 提交信息

采用 Conventional Commits：

```
feat(inventory): support Store apps enumeration
fix(safety): never default-select high-risk leftovers
docs: update release checklist
chore: bump test sdk
```

类型：`feat` `fix` `docs` `style` `refactor` `test` `chore` `build` `ci`

## PR 要求

- [ ] 说明动机与改动范围  
- [ ] UI 改动附截图  
- [ ] 涉及删除/权限附风险说明  
- [ ] 测试已运行或说明为何不需要  
- [ ] 文档已同步（如适用）  
- [ ] 关联需求/Issue  

## 版本号（SemVer）

`MAJOR.MINOR.PATCH[-prerelease]`

- 破坏性数据/行为：MAJOR  
- 功能新增：MINOR  
- 修复：PATCH  
- 当前：`0.1.0-alpha`

## 标签与发布

- 标签：`vX.Y.Z`  
- 打标签后由 CI 产出安装包并归档  

## CODEOWNERS（示例，按团队改名）

```
*                         @cleanslate/tl
/src/CleanSlate.Safety/   @cleanslate/tl @cleanslate/sec
/src/CleanSlate.ElevatedHelper/ @cleanslate/sec
/docs/adr/                @cleanslate/tl
```
