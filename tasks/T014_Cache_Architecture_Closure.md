# T014：删除 Cache 泄漏接口并完成 Phase C 验收

## 依赖与阶段性质

依赖 T013。本任务结束 T011–T014 staged breaking migration window，必须恢复标准完整门禁。

## 目标

确认 Cache 的 physical facts、catalog、coverage、repair 与 invalidation 仍由内聚组件分别承担，但一致性协议不再泄漏到 App；清理旧接口、重复状态机和迁移残留。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、6、7、10 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 8、11 节
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/08_QUALITY_AND_TESTING.md`

## 必须删除或内收

- App-facing `ICachePlanRepairRequestor` / `SpeechPlanRepairRequestor` 页面协议；
- App 对 `CacheInvalidationAspect`、`CacheInvalidationBatch` 和 internal coordinator publish/flush 的业务依赖；
- 页面分别拼接 `ICacheCatalog` + `ICacheCoverageQuery` 的一致性逻辑；
- 不再使用的 `ChapterCacheStatusRefreshController`；
- CacheManagement 的 pending aspect/epoch/generation/worker 残留；
- 为迁移保留的 old/new read model adapter、wrapper、重复 change publish。

Bootstrap 如仍需 shutdown process owner，应依赖窄生命周期 role，不应因此把 internal invalidation 重新暴露给整个 App。

## 核心行为验收

- physical write/delete/cleanup 的 summary/catalog/coverage 最终一致；
- Provider/Regex/speed/text config change 只使必要 projection/plan 失效；
- missing/stale Speech Plan 自动登记、去重、完成后刷新；
- Player/BookDetails/CacheManagement 的 0%/正常/异常显示合同；
- large catalog sparse window 与 incremental update；
- Active Cache/Export/repair 页面离开后继续，shutdown 有界；
- Book/Source removal 清理与 protection/lease 保持。

## 测试收敛

- 保留 Cache identity、atomic write、coverage、repair、invalidation/read-model scope、background owner 和核心页面投影测试。
- 删除只冻结旧 aspect routing、private refresh flags 或调用顺序的测试。
- 不为每个页面复制同一 Cache composition 测试；Application 层验证一致性，页面层只验证窗口/投影/选择。

## 完整门禁

执行 `AGENTS.md` 标准完整门禁和 architecture tests。用 `rg` 确认 App 不再引用 Cache internal invalidation/repair 类型。环境限制如实记录；代码或测试失败不得结束窗口。

完成后在 `TASK_BACKLOG.md` 记录新 read model 边界、净删除的 UI consistency state 和测试变化，并删除本文件。
