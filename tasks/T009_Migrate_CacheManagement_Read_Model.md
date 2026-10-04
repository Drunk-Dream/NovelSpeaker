# T009：迁移 CacheManagement 到 Cache-owned read model

## 依赖与阶段性质

依赖 T008。处于 T008–T011 staged breaking migration window。

## 目标

让 `CacheManagementViewModel` 只拥有页面 activation、Book/Chapter selection、viewport/window、用户命令和 immutable read-model projection；删除其中已经形成第二套 Cache consistency algorithm 的状态。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 4、6、7 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3、5、6 节
- `docs/04_CACHE_AND_BACKGROUND_WORK.md` 第 3–5、9、10 节
- `docs/06_UI_AND_VISUAL_SYSTEM.md` 中 CacheManagement/大列表相关合同

## 必须从页面移除的知识

- `CacheInvalidationAspect.PhysicalSummary/CatalogStructure/Coverage` 的解释；
- `_pendingCacheRefresh*`、book epoch、whole-book/reload-books/coverage dirty 的 Cache 内部状态机；
- 读取 status 后主动调用 `ICachePlanRepairRequestor`；
- 为 stale result 重新排队 Cache domain refresh 的 worker 协议；
- 分别调用 `ICacheCatalog`、`ICacheCoverageQuery` 后在 UI 拼一致 snapshot。

页面仍可保留：

- 当前 selected Book；
- Extended Selection 与 stable chapter key；
- 当前 viewport/decoration window；
- 防止旧 page activation 或旧 selected Book 写 UI 的 page/latest-operation identity；
- WPF incremental collection reconciliation。

## 投影与性能

- 首屏按 staged loading 请求 overview/book list/selected chapter window。
- read-model change 只触发受影响 Book/Chapter 且当前需要范围的重查。
- 普通更新不 `Clear + Add` 全目录，不清空仍有效选择。
- Cache read model owner 返回一致数据；ViewModel 不再自行等待 repair 或通过多轮 query 猜测稳定时点。
- cleanup/export/selection 的用户行为保持，后台 Export/repair 不跟随页面取消。

## 减法目标

迁移完成后删除只服务旧算法的字段、private worker、record/delta 和 tests。不要为了追求行数建立一个新的 App `CacheManagementCoordinator` 复制相同状态；若代码属于 Cache consistency，放回 Application；若属于页面 projection，留在 Feature-local 小 owner。

## 强制验证

- active 页面自动追上 committed cache truth；离开页面不再接收/提交 UI 结果。
- selected Book 被清空/删除、章节增删、physical cleanup、coverage change 时投影正确。
- 0% cached chapter 仍按产品合同显示；普通目录百分比合同不受影响。
- 大目录只更新 window/affected rows，选择与滚动 identity 稳定。
- 页面不直接引用 Cache internal aspect 或 repair requestor。
- 运行 CacheManagement presentation/core WPF behavior focused tests、format；完整门禁留给 T011。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
