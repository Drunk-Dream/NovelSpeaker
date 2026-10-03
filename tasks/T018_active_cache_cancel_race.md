# T018：修复 Active Cache 取消与 CTS 替换竞态

## 目标

消除 `CancelAsync` 使用已被新批次释放的 `CancellationTokenSource` 的竞态，保持 Active Cache 的单一任务 owner。

审计依据：`CODEBASE_AUDIT_REPORT.md` F03。相关合同：`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`。

## 范围与约束

- 对照 `ChapterExportCoordinator` 已有的 active-slot/cancel 同步边界，确认锁内取消是否符合当前 owner 生命周期。
- 让 active task 检查、token source 读取和取消与替换/dispose 处于安全同步协议中。
- 不引入额外锁、registry 或新的任务状态；保留重复取消、自然完成、启动新批次和取消的现有语义。

## 验收

- 用可控并发验证旧任务完成、新批次替换 CTS 与取消交错时不抛 `ObjectDisposedException`，新批次不被误取消。
- 保留正常启动/取消/完成的核心行为覆盖，不测试私有调用次数。
- 执行 Active Cache focused tests、format 和 Release build；记录未执行项。

## 交付

移除不再需要的同步/状态代码，更新 Backlog 成果并删除本规格。
