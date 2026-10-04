# T008：建立 Cache-owned read model 与内部 repair 流水线

## 依赖与阶段性质

依赖 T007。从本任务开始进入 T008–T011 staged breaking migration window。

允许结束时 CacheManagement、BookDetails、Player、CacheAndData 仍引用旧 `ICacheCatalog`/`ICacheCoverageQuery`/invalidation aspect，solution 暂时不能完整 build。不得建立旧新双发布或 forwarding adapter；T009–T010 迁移调用方，T011 收口。

## 目标

把以下一致性链路完整收回 Cache Application 模块：

```text
physical/cache/config/catalog change
→ Cache-internal invalidation coalescing
→ compose physical facts + catalog + coverage
→ register missing/stale plan repair internally
→ publish narrow book/chapter read-model change
```

App 只查询已经组合的场景化 immutable read model，并观察 book/chapter scope + revision；不再理解 invalidation 原因、Speech Plan repair 协议或底层 query 组合顺序。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、6、7 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 8 节
- `docs/04_CACHE_AND_BACKGROUND_WORK.md` 第 2–5、10、12、13 节

## 对外 read model

根据实际页面场景定义最小 query/records，至少支持：

- global cache overview；
- cached book summary/list；
- 某 Book 的 cached chapter catalog；
- 指定章节窗口的 physical summary + current-configuration coverage + export availability；
- current/viewport/明确 indices 的稀疏查询。

名称可适配代码，不要求一套巨型 DTO。Overview、book list、chapter window 可以是不同 read model；不得建大一统 `CacheManager` 或把所有章节长期保存在 process mutable mirror。

## Change source

- Cache 内部保留最窄 invalidation scope/aspect 供重算使用。
- App-facing notification 只表达 Global/Book/Chapters 的 read model 已有新 revision，或等价窄事实；不暴露 `PhysicalSummary/CatalogStructure/Coverage`。
- 高频提交继续 Cache 内合并；observer failure 隔离。
- query/result 通过 revision 或等价 identity 防止旧查询覆盖新结果，但不持久化 revision。

## Repair

- Coverage 发现 PlanMissing/PlanStale 时返回当前状态，不等待 repair。
- 同一次 Cache-owned projection/orchestration 自动向 `ISpeechPlanRepairCoordinator` 登记需要补建的章节。
- repair 仍是 process background owner，保持 in-flight dedupe、并发限制、shutdown 和完成后最窄 invalidation。
- 删除“由页面读取 statuses 后再调用 repair requestor”的协议；T011 最终删除 public `ICachePlanRepairRequestor`。

## 强制验证

- read model 正确组合 physical only、valid coverage、0%、missing/stale plan、provider unavailable。
- missing/stale 只登记一次等价 repair，不阻塞 query；repair 完成后产生最窄 read-model change。
- unrelated book/chapter 不被错误标脏；mutation commit 前不通知。
- change coalescing、observer isolation、shutdown drain 保持。
- 执行 Cache Application/Infrastructure focused tests 与必要静态检查；完整 solution gate 留给 T011。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
