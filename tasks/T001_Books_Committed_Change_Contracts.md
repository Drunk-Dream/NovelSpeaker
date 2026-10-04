# T001：建立 Books 已提交语义变更合同

## 依赖与阶段性质

无前置任务。从本任务开始进入 T001–T003 staged breaking migration window。

允许结束时 `IBookSourceChangeSource` 的现有 Playback/App 消费者暂时未全部迁移，solution 可以因这些明确调用方而无法完整 build。禁止为旧 `CatalogChanged` 形状保留兼容事件、转发 wrapper 或双重发布；T002 负责消费者迁移，T003 负责阶段收口。

## 目标

让 Books mutation owner 在持久变更成功后发布足以表达真实业务事实的窄 typed change，使调用者不再负责猜测和传播变更后果。

至少覆盖：

- Book metadata committed；
- Active Source catalog committed/replaced；
- ActiveSource changed/cleared（当前没有 UI 入口也要正确表达现有生命周期）；
- Source removed；
- Book removed。

具体可以是多个窄事件，也可以是 Books 专属 sealed union，但不得演化成跨系统通用 EventBus、字符串 topic 或万能 payload。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、6 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 2、4、8 节
- `docs/specs/BOOK_DATA_MODEL.md`

## 实施范围

1. 审计所有 Books durable mutation：`DirectBookImportService`、`BookDeletionService`、`BookMetadataUpdateService`，以及现有 Source lifecycle 写入口。
2. 变更只在数据库/文件持久提交成功后发布。observer 抛错不得让已经完成的 durable mutation 向调用者伪装成失败；继续隔离各 observer。
3. change payload 只携带消费者安全重新查询或判断相关性所需的稳定身份/事实，不携带第二份 Book/Catalog mutable snapshot。
4. 删除把 removal 伪装成空 `BookSourceCatalogChanged` 的表达；删除仅由旧合同服务的类型。
5. 保留 `IBookRemovalWorkStopper` 的 pre-commit 语义。停止 Playback/Active Cache 是删除用例本身的前置协调，不应错误改成 post-commit observer。
6. 让 metadata mutation 也由 Application use case 拥有；必要时把当前 Infrastructure `BookMetadataUpdateService` 拆成 Application orchestration + Infrastructure persistence port。Infrastructure 只报告持久化结果，不拥有跨模块通知；不得让 App 或 SQLite 实现直接补发事件。

## 非目标

- 不修改 schema、operation journal、回滚与文件 lease 语义。
- 不在本任务迁移 Playback/ViewModel。
- 不为 UI 发布“请刷新某页面”事件。
- 不把 Regex/Provider/Settings 变化并入 Books change source。

## 强制验证

- focused tests 证明每类 mutation 只在成功提交后发布正确 change。
- observer failure 不改变 mutation result，后续 observer 仍收到通知。
- deletion rollback/failure 不发布 Removed；source removal 与整 Book removal 可区分。
- import/update 的 Catalog 与 Metadata 事实按实际提交结果发布，不重复/漏发。
- 运行相关 Application/Infrastructure focused tests、format 检查；完整 solution gate 留给 T003。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
