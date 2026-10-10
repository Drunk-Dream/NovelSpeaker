# T002 — SQLite 迁移到 Book Identity + Binding + CurrentCatalog

## 目标

在不修改历史 migration 的前提下，从当前 v12 Source-owned Catalog schema 追加迁移到新的 Book Identity / BookSourceBinding / CurrentCatalog 模型，并切换 persistence repository 到新 schema。

本任务属于 staged breaking migration window。

## 必读

- `AGENTS.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `tasks/T001_BOOK_DOMAIN_FOUNDATION.md`（若已按流程删除，则从 Git 查看 T001 完成 diff/Backlog 成果）

## 已授权持久化变更

本任务规格即为用户已经批准的具体持久化变更集合。以下范围无需再次逐字段请示：

1. `Books` 增加 `NormalizedTitle / NormalizedAuthor` 并建立唯一约束。
2. `ActiveSourceId` 可重建/重命名为 `ActiveSourceBindingId`。
3. `BookSources` 收敛为通用 Binding 表，删除 Binding 上旧 Title / Author / Description 字段。
4. `LocalBookSources` 收敛为 Local Binding typed table，可按新命名重建。
5. `Chapters` 从 `SourceId` owner 改为当前目录记录，至少关联 `BookId + SourceBindingId`，并保证 `(BookId, ChapterIndex)` 唯一。
6. `LocalChapterContents` 保留或等价重建。
7. 建立必要 FK / unique index / trigger，保护 Active Binding 所属 Book、每 Book 最多一个 Local Binding、CurrentCatalog 关系。
8. 保留能安全保留的 BookId、Binding/Source Id、ChapterId、ReadingProgress、ChapterSpeechPlans、AudioCacheEntries 等正式数据。
9. 不移动现有 normalized local content 文件。
10. 删除失去语义的旧字段/索引/运行时 repository 路径。
11. 不新增 Online Source Definition/规则/Search/Refresh/Online cache 表。

若实现发现必须超出上述集合，停止超出部分并在 Backlog 标 `[!]`，不要自行扩大 schema。

## Migration 语义

### Normalization

迁移必须调用与运行时相同的 normalization 逻辑或共享纯实现，不在 migration 中另造不同规则。

### Duplicate identity

若历史 Book 在 normalization 后发生唯一键冲突：

- 不自动 merge；
- 不保留第一条；
- 不覆盖/删除 Local content；
- 整个 migration rollback；
- 返回可识别的 incompatible-library failure，供 T004 接到用户可理解的重新导入路径。

不要为此建立 alias/dedup 长期表。

### CurrentCatalog

当前 v12 只有 Local Source，正常数据库可以把每本书当前 local source 的 Chapter rows 转换为 CurrentCatalog，并尽量保留 ChapterId。

遇到无法无歧义判断当前目录来源的非法/历史状态时 fail closed，不猜测。

### Foreign keys

表重建必须在事务边界内安全处理 SQLite foreign key 限制，并在 commit 前执行 `foreign_key_check` 或等价验证。

## Repository cutover

更新 persistence 代码只读写新 schema：

- Book identity query 使用 normalized identity；
- Library/Details query 从 Books + Active Binding + CurrentCatalog 投影；
- Import repository 准备支持 T003 的 Local snapshot commit；
- Content reader 通过当前 Catalog + Local typed data 定位正文；
- 不保留 v12 双读/双写。

## 测试

只新增/保留核心 migration integration tests：

- 正常 v12 local library → 新 schema；
- BookId/ChapterId/ReadingProgress/关键 Speech/Audio FK 安全保留；
- normalized duplicate → 原子失败且旧库不被部分升级；
- foreign key/integrity；
- schema 已升级后重复启动幂等。

避免逐列 schema snapshot 类测试泛滥；保护的是迁移风险和约束，而不是 SQL 文本排版。

## 任务结束允许状态

Books/Import/App 部分调用方仍允许等待 T003/T004 接回；Persistence 新 schema 与 migration 本身必须有 focused integration tests 通过。
