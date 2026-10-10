# T006 — 全量测试收口与约 600 例验收

依赖：T005。本任务是本轮唯一强制全量门禁阶段。规划基线 `main` @ `833333d`，以最终工作区实际 HEAD 为准。

## 目标

检视 T001–T005 全部最新 diff 与真正可运行测试发现数，确认永久测试集中在核心产品、持久化/安全和重要架构边界。目标总数 **575–625**（约 600），优先保护真实风险，不为硬凑数字删去独一份关键回归。

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`、`.codex/review-checklist.md`、`TASK_BACKLOG.md`。按本轮实际改变的模块阅读其长期 owner 文档；不借本任务修改产品或广泛改写 `docs/`。

## 执行内容

1. 在同一有效构建/目标框架条件下重新运行 test discovery/完整 `dotnet test`，汇总 Domain、Application、Infrastructure、Presentation、WPF 的**实际执行数、通过数、失败数、跳过数与总计**；和 T001 重测基线比较，说明 `[Theory]` 展开、filtered/ignored 项和任何测试项目未执行的情况。确保没有偷偷排除测试、配置 CI 路径、Skip 或藏起项目造成的数字下降。
2. 复核剩余测试的唯一保护面，至少覆盖：Books 导入/删除/数据恢复、现行/已发布 SQLite migration、TXT 外部不可写、Data root 与内部链接防逃逸；Playback logical target UI 先行、迟到 audio/取消、pause/resume/progress、Provider selection 与真实音频；Cache identity/清理/一致性、导出、规则 sandbox/HTTP 安全；UI 主要页面/导航/离开取消；Logging/Telemetry/Diagnostics 失败不破坏业务和隐私；WPF isolated Desktop fail closed、曾导致崩溃的真实 Popup 入口；分层、模块边界和主要 owner。
3. 若超出 625，优先回头删除跨层重复、细粒度排列或视觉/文本形状测试，按原所属模块做最后一轮 focused tests；绝不专删某层的少量独特高风险用例。如果确无安全余量，如实保留超过目标的合理结果并在 Backlog 中解释，不得以 Skip、测试过滤或改 CI 作假。
4. 若低于 575，检查是否误删关键风险；只恢复真正缺失的核心回归，不为达到指标而添加无意义测试。清除不再被任何永久测试引用的 TestKit、parser、fixtures、测试资源和一次性验证脚本，确认 .csproj 与解决方案/CI 仍正常引用所有应运行的测试项目。
5. 检查 `git diff --check`、LF 统一、Release 构建与全量测试。若环境限制不能运行，精确记录命令和错误，不得声称完成完整门禁或绕过隔离。

## 必须执行的完整门禁（Windows / .NET 10 环境）

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## 完成标准

- 所有应运行的测试项目可发现且无新 skip，保留测试全部通过；关键风险映射完整、无仅服务已删测试的长期负担、无本轮临时产物。
- `TASK_BACKLOG.md` 简要记录：最终 HEAD，旧→新分层与合计真实案例数、主要删减类别与保留边界、完整门禁结果/环境限制及必要的预算偏差解释。
- 删除本 task spec，Backlog 任务标记 `[x]`；不新增与当前清理无关的源码、迁移、CI 改造或长期文档。
