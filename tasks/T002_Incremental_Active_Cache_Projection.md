# T002：避免 Active Cache 进度更新重建整份章节列表

## 目标

Active Cache coordinator 继续作为批次与快照的唯一 owner；Shell 在每个片段完成后避免清空并重建所有章节行，只更新确实变化的投影数据。

## 背景与证据

审计报告 F002：`ActiveCacheCoordinator` 在片段完成后发布快照；`ShellActiveCacheController.ApplySnapshot` 对每个 active snapshot 清空 `Chapters` 并重新创建所有行。K 个选中章节、S 个完成片段时，当前列表投影产生 O(K×S) 行分配和 collection notifications。List virtualization 不消除源集合重建成本。

## 范围

- 保持 coordinator 快照为业务真值，不在 App 新增第二份 mutable owner 或事件总线。
- 保持当前章节顺序、状态、计数、总进度、终态摘要及取消/失败语义。
- 以稳定章节 key 更新现有行；只有批次身份或所选章节集合真正改变时才重建集合。
- 不为性能修复使用 `Task.Yield()` 或在 Dispatcher 上执行无界 projection。
- 评估 Flyout 关闭时是否仍需维护章节级详情；除非现有交互合同允许，否则不改变显示语义。

## 验收

- Presentation tests 覆盖多次递增快照后行身份保持、仅对应章节字段更新、排序稳定、批次替换重建、成功/失败/取消终态正确。
- 重复进度更新不产生全量 Clear/Add；实现复杂度随实际变化行数增长。
- 不增加新的永久实现形状测试；只保留稳定投影行为合同。
- 运行 Shell Active Cache focused tests、format 和 Release build。

## 长期合同

- `docs/01_SYSTEM_ARCHITECTURE.md` 第 4、7 节：单一 mutable owner、immutable catalog + sparse mutable decoration。
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 6、8 节：Dispatcher 和 background job owner。
- `docs/06_UI_AND_VISUAL_SYSTEM.md` 第 10、11 节：大型列表与有界 projection。

## 依赖与风险

无前置任务。不得将 UI 优化扩展为 coordinator 状态模型重写。
