# T007：收口 Source/Book 生命周期并删除旧模型残留

## 依赖

T006。

## 阶段性质

staged breaking migration window 的最后一个实现任务。任务结束时应尽量恢复 solution build；完整阶段门禁仍由 T008 执行。

## 目标

完成新 Book/Source 模型下的数据生命周期，并删除所有旧运行模型残留。

## 必读

- `AGENTS.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`

## Source lifecycle

即使当前 UI 只有一个 Local Source，也要让 Application/Infrastructure 生命周期符合通用模型：

### Remove non-last Source

清理：

- Source row；
- Source Catalog；
- Source-owned persistent content；
- Source-owned future online content cache（当前可以只有 port/边界，不实现 online cache）；
- 只依赖该 Source catalog 的 Speech Plan / audio cache rows/files。

### Remove ActiveSource

- 允许执行；
- ActiveSourceId → null；
- 不自动选择其它 Source；
- playback/source work 安全停止；
- Book 保留最后 display metadata snapshot。

### Remove last Source

- 同时删除 Book。

当前每 Book 只有 Local Source，因此用户侧“删除唯一 Local Source”可以直接复用 Book deletion，不需要为了未来建立复杂 Source 管理 UI。

## Book deletion

适配现有 `BookDeletionOperationStore` / operation recovery：

必须协调：

- Books；
- Sources；
- Catalog；
- Local Source content file；
- ReadingProgress；
- ChapterSpeechPlans/segments；
- AudioCacheEntries + physical audio files；
- operation journal/recovery。

保留 reparse-point / storage root trust boundary。

不要仅依赖 SQLite cascade 处理真实文件。

## 删除旧模型残留

至少审计并清理：

- Domain `Book` 中已删除 Local-only fields 的旧调用；
- `IBookDuplicateDetector` / `BookDuplicateDetector` 以 SourceHash 判断 Book duplicate 的旧路径；
- Book-owned content reader contracts；
- query SQL 对 `Books.StoredFilePath`/`Chapters.BookId` 的引用；
- tests/fixtures 手写旧 Books/Chapters insert；
- 只为 old schema 存在的 compatibility DTO/helper；
- documentation comments 仍称“Book points to normalized TXT file”的过期描述。

不建立 legacy alias/compat constructor 让旧测试继续编译。

## ActiveSource=None

所有长期入口至少做到：

- Library 可以显示 Book last metadata；
- BookDetails 可以显示 metadata + “无当前来源”的稳定状态；
- Playback 不崩溃且不伪造目录；
- 删除/导航安全。

本阶段不需要完整 Source picker UI。

## 测试

核心永久测试：

- Book deletion 完整清理 DB + Local Source file；
- deletion recovery/rollback；
- last Source removal deletes Book；
- active Source removal no fallback；
- ActiveSource=None Library/details safe；
- no old SourceHash duplicate behavior；
- reparse point trust boundary remains。

旧 schema fixture 应统一更新为新 helper，避免每个测试复制大段 SQL。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
