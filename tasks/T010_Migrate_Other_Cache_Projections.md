# T010：迁移 Player、BookDetails 与 CacheAndData 的缓存投影

## 依赖与阶段性质

依赖 T009。处于 T008–T011 staged breaking migration window。

## 目标

让所有非 CacheManagement 页面使用 T008 的同一 Cache-owned read model/change source，删除每页各自对 internal invalidation 与 coverage refresh 的解释。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 4、6、7 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 10、12 节
- `docs/04_CACHE_AND_BACKGROUND_WORK.md` 第 3–5、7、10 节

## Player

- `PlayerCacheDecorationController` 继续拥有当前页面的 decoration window 与章节 management selection。
- 改为请求组合 chapter views，并订阅窄 book/chapter read-model change。
- 删除对 `ICacheInvalidationCoordinator.BatchPublished`、aspect flags 和通用 `ChapterCacheStatusRefreshController` 的依赖。
- Active Cache snapshot 仍来自 `IActiveCacheCoordinator`；不要混入 read model owner。

## BookDetails

- 目录/catalog 与动态 cache decoration 继续分离。
- 只查询 current/viewport/明确受影响章节，Books catalog change 与 Cache read-model change 分别由各自 owner 提供。
- 删除对 Cache internal invalidation aspect 的判断和页面自建 coverage refresh protocol。

## CacheAndData

- overview 直接消费 Cache overview read model/change。
- 不因任意 coverage change 重算 global physical summary；scope 判断由 Cache owner完成。
- cleanup mutation 仍通过明确 command/store use case，提交后由 Cache 自行发布变化。

## Shared 清理方向

`ChapterCacheStatusRefreshController` 若迁移后没有其它真实使用者，删除。若多个页面仍需要相同的 page-scoped latest-window query 行为，可以保留纯 presentation operation slot，但不得包含 Cache domain invalidation/repair 语义。

## 强制验证

- Player/BookDetails 只刷新 current/viewport/受影响章节，非相关 Book change 不扰动投影。
- Active Cache 进度、selection、普通 cache read-model change 不互相覆盖。
- CacheAndData overview 在 cleanup/write 后追上真值且不因纯 coverage change无谓重查。
- 快速切 Book/离开页面后迟到 query 不提交。
- App 生产代码不再解释 `CacheInvalidationAspect`。
- 运行相关 presentation/Application focused tests 与必要 build；完整门禁留给 T011。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
