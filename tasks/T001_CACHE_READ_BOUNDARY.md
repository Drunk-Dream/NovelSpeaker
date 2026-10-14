# T001 — Cache 只读查询与 PlanMissing 止血

- **类型/优先级：** Implementation / P0 Query Boundary。
- **基线：** 本地实际 HEAD + 工作区（含未推送 T007），不得重置。后续任务 T002/T003 以本任务完成后代码为基线。
- **必读：** `AGENTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`；`CacheReadModel`、`CacheCoverageQuery`、`CacheCatalog`、`SpeechPlanRepairCoordinator` 及 Player/BookDetails/CacheManagement 的 viewport 查询路径。

## 问题与目标

当前目录滚动会改变可视查询窗口；`CacheReadModel.GetChaptersAsync` 将 `PlanMissing` 与 `PlanStale` 一并登记后台 repair。后续 repair 读取正文、构建并保存 Plan，使 `ChapterSpeechPlanSegments` 随浏览范围无意义增长。**立即截断这条因果链**，不以 debounce 或仅限并发掩盖。

## 实施边界

1. 使普通目录/覆盖率读取保持无副作用。`PlanMissing` 仅返回状态，不登记任何补建，禁止 query 本身加载正文、分段、修改 Plan 或写入数据库。
2. `PlanStale` 暂可沿用既有 repair owner，但登记前必须基于同一查询范围已有**物理音频缓存事实**筛选；不能只因有一份过期 Plan 就触发修复。注意物理缓存可以属于旧 Provider，不能只看当前 synthesis profile 下的命中数。
3. 不把 `PlanMissing` 误认为可显示的 0%，不为了性能跳过 `Available` 的精确匹配；保留批量/window 查询和异步结果合并。
4. 保持当前 Storage/Provider 指纹匹配行为；Provider 改变只重新匹配，不生成 Plan。不要新增缓存完整度的第二真值。
5. 修订依赖旧的“missing 自动修复”行为的测试，优先一组行为级回归证明纯滚动未缓存章节不导致正文读取与 Plan 写入。不要为每一种内部状态单独堆测试。
6. 本任务不尝试完成 T002 的 Build/Save 拆分或 T003 的完整队列策略，但不得引入两者难以清除的新兼容层。若发现修复路径由 UI 之外调用，保持现有职责并在交付中记录。

## 强制验收

- 隔离构造 10k 章节、无音频缓存、多次切换窗口，修复请求数为 0，正文读取数为 0，Plan/Segment 写入数为 0；可用测试桩证明，必要时辅以一次真实 SQLite 检查。
- 有真实音频索引的 Stale 章节仍可触发修复；无音频 Stale 章节不触发。默认 Provider 变化不引发 Plan 重建。
- 大目录 Viewport 查询可取消、旧请求的迟到结果不会错误提交；不更改 UI 滚动/选择行为。
- `dotnet format --verify-no-changes --no-restore`、Release build、相关 Cache Application/Infrastructure/Presentation focused tests、`git diff --check`。若尚未 restore，按 `AGENTS.md` 恢复锁定依赖后运行；如实记录环境限制。
- 更新 Backlog `[x]` 简短成果并删除本任务文件；后续继续 T002，无需人工验收。

## 禁止事项

不改数据库 schema，不删除合法音频数据，不以“在下一次启动清理”代替问题修复，不运行全量文件/音频扫描来判断是否 Stale。
