# T003：发布 NovelSpeaker v0.7.0

## 目标

把 Speech Provider 重构和 T001–T002 的稳定性修复作为一个正式稳定基线发布为 `v0.7.0`。后续诊断系统职责优化不得提前混入本次 Release。

## 权威流程

严格使用仓库：

- `.codex/skills/release-version/SKILL.md`
- `.github/workflows/release.yml`

用户已经明确指定本次版本为 **0.7.0**，无需重新判断 SemVer。

## 必须完成

1. 确认 T001、T002 已完成，工作区不存在未解释修改和临时验证产物。
2. 按 `release-version` Skill 执行发布前检查、版本配置更新、完整质量门禁、版本提交、标准 PR 合并、`v0.7.0` tag、Release workflow、资产和中文 Release Note 验证。
3. 不直接 push 主分支，不绕过失败 checks，不因为发布而修改产品行为。
4. Release Note 基于上一正式版本到 `v0.7.0` 的真实净差异，重点准确描述 Speech Provider、实验性 Edge Provider、统一语速、缓存/播放接线以及本阶段稳定性修复。
5. Release 成功、资产与 notes 均验证后，从最终发布后的主分支创建并切换到 `feature/diagnostics-hardening`，用于继续执行 T004–T005；旧 `feature/speech-provider-refactor` 按 release Skill 的安全条件清理。
6. 新 diagnostics 分支必须基于实际发布主线，不携带未发布的额外源码修改。

## 自动验收

以 `release-version` Skill 的最终检查为准，并额外确认：

- GitHub Release 为 `v0.7.0`；
- `NovelSpeaker-v0.7.0-win-x64.zip` 与对应 `.sha256` 存在；
- Release workflow 成功；
- 当前后续开发分支为 `feature/diagnostics-hardening`，其起点包含最终 `v0.7.0` 发布主线；
- T004–T005 任务文件和 Backlog 仍存在于后续开发分支。
