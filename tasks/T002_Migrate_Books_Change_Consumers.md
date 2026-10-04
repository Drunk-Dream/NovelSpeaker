# T002：迁移 Books 变更消费者并删除 UI 后果编排

## 依赖与阶段性质

依赖 T001。处于 T001–T003 staged breaking migration window。

允许 Regex 相关旧 refresher port 继续存在到 T003；本任务结束时 Books 变化链路必须只剩新合同，不得保留旧 `BookSourceCatalogChanged` 兼容入口。

## 目标

让变化的长期消费者自己解释 Books 已提交事实，并从 `LibraryViewModel`、`BookDetailsViewModel` 删除 Playback/Books 跨系统一致性编排。

最终调用路径应是：

```text
UI calls one Books use case
→ Books commits and publishes semantic fact
→ Playback / active page independently consumes relevant fact
```

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、6 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 7、8、12 节

## Playback 消费

- MetadataChanged：仅当前 Book 相关时，在现有 serialized command boundary 内重新查询/合并 metadata，不打断当前音频。
- Catalog/ActiveSource change：只在当前 playback source context 相关时停止/失效旧 session/prefetch，迟到结果不得恢复旧上下文。
- Removed：删除用例已有 `IBookRemovalWorkStopper` 时不得重复 stop；post-commit change 只处理确有必要的剩余投影一致性。
- 所有事件入口只 enqueue，不在 publisher 回调中同步执行长任务或冒泡异常。

## Books presentation 消费

- 删除 `BookCatalogInvalidationState`/`IBookCatalogInvalidationState` 这一全局 bool 及 DI 注册。
- Library/BookDetails 可以在当前 activation 内订阅 Books change source，或使用 Books-owned monotonic revision/query contract；不得建立新的 App 全局 dirty bool。
- active 页面只刷新/移除受影响 Book/Source 的投影；非 active 页面下次正常 load 即可。
- 本次 mutation 的 ViewModel 仍负责确认、feedback、navigation 和局部 editor state，但不调用其它业务系统刷新。

## 必须删除

- `IPlaybackBookCommands.RefreshBookMetadataAsync` 和 UI 调用。
- `IPlaybackBookCommands.HandleBookDeletedAsync` 和 UI 调用；如果接口不再有其它真实命令，则删除接口本身。
- `BookCatalogInvalidationState` 全部实现、依赖和测试。
- 旧 Books change contract、临时 adapter 和重复 publish。

## 强制验证

- 删除当前播放 Book 时，停止动作只由删除用例协调一次，UI 不参与；数据库/文件失败仍符合原 rollback 语义。
- metadata save 后 Library/BookDetails/当前 Playback metadata 最终一致，且发起页面无需显式 refresh Playback。
- re-import/catalog replacement 后旧 playback/prefetch 失效，非相关 Book 的播放不受影响。
- 页面 deactivation 后不再响应 change；observer failure 不影响 mutation。
- 运行 Books、Playback ActiveSource 与相关 presentation focused tests、format；完整门禁留给 T003。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
