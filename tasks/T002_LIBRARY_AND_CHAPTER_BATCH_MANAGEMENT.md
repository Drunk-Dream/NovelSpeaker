# T002：迁移 Library 与 Playback 章节批量管理

## 依赖

T001。

## 目标

把 Library 和 Playback Chapter Catalog 接入统一 Management Mode。

- Library：Select All / Export / Delete / Cancel。
- Playback：把现有“Active Cache Selection Mode”改为通用 Chapter Management Mode；当前 batch action 仍只有 Cache。

## 必读

- `AGENTS.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`

## Library

Normal Mode：

- 普通 click 继续打开 Book。
- Ctrl/Shift click 或显式管理入口进入 Management Mode。

Management Mode：

- 普通 click toggle selection；
- 不打开 Book；
- Page Header 显示 selected count / Select All / Export / Delete / Cancel；
- right-click 遵守统一 selection 语义；
- search/filter 后隐藏 Book 从 selection 移除；
- selected count = 0 保持模式。

### Batch Delete

- 整个批次只确认一次。
- 逐 Book 删除；单 Book 失败继续其它项。
- 汇总 success / skipped / failed。
- 不提供 Undo / recycle bin。

### Batch Export

当前 Local Source 场景输出一个 batch-level 目标目录：

```text
target/
├─ Book A.txt
├─ Book B.txt
└─ ...
```

- UTF-8。
- 每 Book 一个完整正文文件。
- 一次批量动作只选择一次目标位置。
- 文件名必须安全、冲突时确定性加后缀，不覆盖已有文件。
- 当前没有完整可导出正文能力的对象按 skipped 处理，不阻塞其它项。
- 导出不改变 Book/Source/ReadingProgress。
- 具体 folder picker / file operation primitive 复用项目现有平台能力，避免新造框架。

## Playback Chapter Catalog

重构当前：

- `PlayerActiveCacheSelectionController`
- `IsActiveCacheSelectionMode`
- 仅为 Active Cache 存在的 selection 命名

目标：

- selection/mode 语义改成通用 Chapter Management。
- `ActiveCacheCoordinator` 仍只拥有 batch 生命周期，不拥有页面 selection。
- Enter Management Mode 后章节 click 不跳转，只改变 selection。
- 当前 Chapter 的 Current rail 与 Selected background 可以叠加。
- Select All → Cache 合法。
- Cache 幂等：
  - fully cached → skipped；
  - partially cached → fill missing；
  - uncached → normal cache。
- batch 完成/接受后是否退出模式可沿用当前简单行为，但必须与统一 spec 一致，不能让 Active Cache coordinator 隐式成为 selection owner。

## CacheManagement 例外

不得把 CacheManagement 强制迁移成 Management Mode。它继续使用当前 Extended Selection。

## 测试

保留少量稳定行为测试：

- Library management mode 不打开 Book；
- batch delete 单次确认 + partial continuation；
- batch export 单次目标选择 + 多文件结果；
- Playback management mode click 不 jump；
- Chapter Cache 幂等 selection 提交；
- CacheManagement 行为未被 shared change 破坏。

视觉细节使用临时 WPF/StyleGallery 验证后删除。

## 自动验收

本任务不属于 breaking migration，完成时执行标准完整门禁。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
