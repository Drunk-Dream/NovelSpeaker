# T016：迁移 Library 与 BookDetails 的页面异步状态

## 依赖与阶段性质

依赖 T015。任务结束时仓库必须保持可构建、相关 focused tests 通过。

## 目标

让 Library/BookDetails 的 page lifetime 由 activation scope 唯一拥有，让真正 latest-wins 的操作使用 T015 primitive；删除只重复表达 cancellation/currentness 的 CTS、version、OwnedTaskRegistry 与手工 dispose。

## 必读

- `AGENTS.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3、5、6 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 12、13 节
- `docs/06_UI_AND_VISUAL_SYSTEM.md` 中 Library/BookDetails/大列表合同

## Library 分类与迁移

逐项判断现有 `_searchVersion`、`_projectionVersion`、`_loadVersion`、`_importVersion`、`_playbackProjectionVersion`、`_playbackSnapshotVersion`、`_rowLayoutVersion` 及各 CTS：

- page load/import/search/projection 若为 latest-wins，迁入 operation slot；
- playback snapshot 与 projection 的数据一致性 revision 若仍需比较，保留并改成语义明确名称；
- row layout/viewport 若由 WPF/layout event 的独立 identity 保护，保留 feature-local，不强塞入异步原语；
- management lifetime 是局部交互 session，不得错误并入 page activation，但可用明确 owner 替换裸 CTS。

## BookDetails 分类与迁移

- load/header/enrichment、cache decoration、playback projection 分别识别 activation、latest operation 与 data revision。
- Page code-behind 的 initial locator version 属于 WPF staged-locator 生命周期；只有能由 activation/locator owner完整替代时才删除。
- navigation guard/editor dirty state 不属于 operation lifecycle，保持原 owner。

## 订阅与任务

- Books/Playback/Cache change subscriptions 注册到当前 activation，离开页面自动解除。
- page-owned task 使用 activation/operation owner观察；不再并列使用 `OwnedTaskRegistry` 保存同一批任务。
- process/session/background 工作不得因页面离开取消。

## 强制验证

- 快速进入/离开/返回、连续搜索、快速切 Book、导入完成与播放 snapshot 竞争时，旧结果不能写新页面。
- Library card current/progress、BookDetails header/catalog/cache decoration 不跨 Book 拼接。
- staged first frame、10k catalog、incremental rows、selection/scroll identity 行为不退化。
- 删除的每个 version/CTS 有明确替代 owner；保留的 revision 在注释/命名中说明保护哪种数据事实。
- 不新增等待固定延时的测试；运行两页 presentation/WPF focused tests、format、Release build。

完成后在 `TASK_BACKLOG.md` 记录净删除的 lifecycle state 和保留 revision 的理由，并删除本文件。
