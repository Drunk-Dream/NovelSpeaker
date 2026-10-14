# T005 — 长目录性能、完整度和完整质量门禁

- **类型/优先级：** Closure / P1。依赖 T001–T004。
- **必读：** `AGENTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/08_QUALITY_AND_TESTING.md`、`docs/specs/REGEX_REPLACEMENT.md`。基于最新完整 diff，按 `.codex/review-checklist.md` 逐项复核。

## 收口检查

1. **根因闭环：** `CacheReadModel`/Coverage/read model 对 `PlanMissing` 不登记补建。虚拟目录持续滚动不会加载未缓存章节正文，不写入 `ChapterSpeechPlanSegments`。有缓存的 `PlanStale` 才有按需修复机会，且后台单 worker 不积累过期可视需求。
2. **职责闭环：** Plan 计算不等于 SQLite 持久化；真实缓存 Ready 写入所需 Plan 保持重启后可复原，失败/取消/并发/失效不会产生持久坏状态；删除最后物理缓存时最终可清理孤立 Plan。
3. **完整度闭环：** Text Profile、Synthesis Profile、正文身份、分段位置及 SpeechText 哈希共同约束匹配；Stale 修复前隐藏旧百分比，修复后只按当前配置计算。切 Provider 不重建 Plan，旧 Provider 缓存仍可切回复用。`0%` 和其它无有效缓存状态都空白。
4. **用户体验：** 目录先显示、后填充百分比；快速滚动及页面退出后不会有延迟结果污染新页面、跨窗口重复重试或 UI Dispatcher 长工作段；CacheManagement 物理缓存条目不因当前配置下覆盖率为 0 而隐藏。
5. **资源闭环：** 数据库和内存占用与实际需要缓存/修复的章节相关；Active Cache 不为冻结批次而同时强引用大量完整章节分段；进程退出有界取消/排空仍按上一轮 T007 已完成的行为执行。
6. **做减法：** 搜索并删除本轮遗留的“missing 自动 repair”路径、无用状态分支、旧断言、重复 CTS/队列、临时 harness；不扩张为新后台框架。不重写无关成熟系统。
7. **文档：** 实现与已交付的长期 docs 相符。如果确有与本地 T007 已完成、未推送的改动冲突，保留 T007 行为并报告具体冲突；不能擅自推翻此前已批准合同。

## 实际规模与性能验收

- 用隔离本地测试数据库构建至少 **10,000 章的目录**，其中绝大多数无音频缓存。记录滚动前/后 `ChapterSpeechPlans` 和 `ChapterSpeechPlanSegments` 行数、SQLite 文件及 WAL 大小、正文读取次数、修复任务数、每个窗口查询耗时的代表性采样；纯滚动应不产生新的 Plan 行或 Segment 行。
- 增加少量具有音频缓存且处于 `PlanStale` 的章节，在快速跨区域滚动/离开页面情况下检验修复上限及旧任务废弃；在稳定停留时检查最终正确百分比。不要以固定延时猜测异步完成。
- 这是针对当前缺陷的**一次性受控验证**，不把大体量临时 fixture、诊断音频、benchmark 脚本和样本结果提交进 Git；永久测试只留下少量核心路径用例。
- 如已有 WPF 测试基础设施，进行范围明确的真实容器/滚动确认；任何无法跑的 Desktop 隔离/Windows 项如实披露。不能声称仅凭测试桩就证明用户机器上绝对无卡顿。

## 标准完整门禁

按 `AGENTS.md` 完整执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

另检查 `git diff --check`、全部文本 LF、核心架构约束、无意外 schema migration、无调试/性能临时副产物、没有用户真实数据修改。完整代码审查遵守 `.codex/review-checklist.md`，禁止仅审某一阶段 diff 或忽略原本存在的未推送 T007 修改。

## 最终交付

- Backlog T005 标记 `[x]`，简述根因修复、删除的旧路径、少量关键测试数量/范围、真实 SQL 行数与规模采样结论，以及门禁结果/环境限制。
- 删除本规格及全部完成任务规格，不在 Git 保留一次性审计/采样报告。
- 不等待人工验收、不自动启动 U1、不自行 push 或发布。
