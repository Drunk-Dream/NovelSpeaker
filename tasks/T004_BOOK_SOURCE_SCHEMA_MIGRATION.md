# T004：建立 Book/Source 持久化模型并完成 v0.8.0 schema migration

## 依赖

T003。

## 阶段性质

从本任务开始进入 `TASK_BACKLOG.md` 声明的 staged breaking migration window。

本任务允许结束时部分 Application/App 调用方暂时未迁移、solution 暂时无法完整 build。不要为让中间态变绿建立旧/新 Book 模型 compatibility wrapper。T008 负责恢复完整门禁。

## 目标

把 v0.8.0：

```text
Books(Local TXT fields + display metadata)
Chapters(BookId + local text range)
```

迁移为：

```text
Books(display metadata + ActiveSourceId)
BookSources(base source)
LocalBookSources(local typed data)
Chapters(SourceId + catalog fields)
LocalChapterContents(local typed range)
ReadingProgress(BookId ...)
```

具体命名可以在不改变语义的前提下适配项目现状，但不要用万能 JSON。

## 必读

- `AGENTS.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`

## 已批准的 schema change set

本 task 中以下变更已获用户明确授权，不需要再次逐项请示：

1. 追加 schema version 12（当前 main 是 v11），不得修改 v4–v11。
2. 新增 Source 基表：
   - SourceId；
   - BookId FK；
   - SourceType；
   - Source Title / Author / Description metadata snapshot；
   - 必要 created/updated metadata。
3. 新增 Local Source typed table：
   - SourceId PK/FK；
   - OriginalFileName；
   - StoredContentPath/storage key；
   - SourceHash；
   - Encoding；
   - import timestamps 等现有 Local 专属数据。
4. `Books`：
   - 保留 BookId 与当前 display metadata；
   - 新增 nullable ActiveSourceId；
   - 保留真正属于 Book 的 Imported/LastPlayed/Updated 等时间语义；
   - 删除已经迁入 Local Source 的 OriginalFileName / StoredFilePath / SourceHash / Encoding 等 Source-only 字段；
   - 删除旧 `IX_Books_SourceHash`。
5. `Chapters` 作为 Source Catalog entries：
   - 保留现有 Chapter.Id；
   - `BookId` ownership 改为 `SourceId`；
   - 保留 ChapterIndex / SortOrder / Title；
   - 不把 Local-only content range 继续定义成所有 Source 的通用字段。
6. 新增 Local chapter content typed table承接 StartOffset / Length，优先保持 `ChapterId` PK/FK。
7. 每个 Book 最多一个 Local Source，建立数据库可表达的唯一约束。
8. 每本 v0.8.0 Book 自动创建一个 Local Source，并设为 ActiveSource。
9. 保留现有 BookId、ChapterId、ReadingProgress。
10. 保持 ChapterSpeechPlans / AudioCacheEntries 对既有 Chapter 技术 ID 的有效关系；若 SQLite 表重建要求重建依赖表，可以做最小必要 migration，但不得改变音频 cache 的产品语义。
11. 迁移后 runtime 只使用新 schema，不双读/双写。

如果实际 schema 需要一个纯技术索引/FK 才能保证上述 invariant，Codex 可自行增加；如果需要引入新的业务持久化概念，才属于超范围并应停止请求授权。

## 自动迁移策略

优先实现有界 SQLite 数据迁移。

现有：

```text
Books/{BookId}/content.txt
```

不搬迁。每 Book 最多一个 Local Source，因此 v0.8.0 的内部规范化正文文件可以直接成为新 Local Source 的 StoredContentPath。

禁止仅为了新目录更“漂亮”引入跨数据库/文件系统搬迁。

### fallback

如果实现证据证明自动迁移必须：

- 重新找到用户外部 TXT；
- 通过标题/正文猜测 Chapter identity；
- 建立旧/新 schema 长期双读；
- 建立复杂一次性文件 transaction/recovery framework；

则不要继续复杂迁移。改为 migration 只建立新 schema，并在产品层使用已批准的“需要重新导入本地书籍” fallback。该 fallback 不需要再次询问用户，但必须保证不会把无法迁移的数据伪装为成功迁移。

基于当前代码，预计不需要 fallback。

## Domain / Application contracts

建立或调整纯模型合同，使：

- Book 不再包含 StoredFilePath/SourceHash/Encoding 等 Local 字段；
- Source 成为独立领域概念；
- Catalog ownership 使用 SourceId；
- technical ChapterId 可以保留；
- ReadingPosition 仍是 Book-level index/inside-chapter position。

不要在此任务把 Online Source 具体类层级设计成复杂插件系统。只需要 SourceType + Local typed model。

## 允许的中间破坏

任务结束时允许仍有调用方编译失败，例如：

- DirectBookImportService 仍构造旧 Book；
- BookDetailsQuery/LibraryQuery 仍读取旧列；
- BookContentReader 仍接受旧 stored path query；
- BookDeletionOperationStore 仍按旧 schema 删除。

这些必须在 T005–T007 迁走，不得用兼容构造器/alias 掩盖。

## 强制验证

至少建立/更新 migration integration tests，验证：

- fresh DB 创建到 v12；
- v11 fixture 升级到 v12；
- 每本旧 Book 得到一个 Local Source；
- ActiveSource 正确；
- BookId/ChapterId 保留；
- ReadingProgress 保留；
- Local content path 原值保留；
- old source fields/index 不再存在；
- Local Source singleton constraint 生效；
- migration failure transaction rollback 不留下半 schema 数据。

本任务只要求 migration/domain focused tests 和必要静态检查；不要求完整 solution build。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
