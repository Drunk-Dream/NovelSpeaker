# T006：把 Query、正文读取、Playback 与 ReadingProgress 接到 ActiveSource Catalog

## 依赖

T004、T005。

## 阶段性质

staged breaking migration window。

## 目标

迁移运行时读链路，彻底移除“Book 直接拥有 StoredFilePath + Chapters”的假设。

最终：

```text
Book
→ ActiveSource
→ Source Catalog
→ Source Content Reader
→ Text/Speech/Playback
```

ReadingProgress 仍属于 Book。

## 必读

- `AGENTS.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`

## Query/read model

迁移至少：

- Library summaries；
- BookDetails header；
- ActiveSource summary；
- ActiveSource Catalog；
- ReadingPosition；
- chapter content read path；
- statistics / remaining chapter projection。

要求：

- UI/Application 不直接读取 LocalBookSources storage path；
- Infrastructure 通过 Source content port 解析 Local Source；
- ActiveSource=None 时 query 返回明确可表达状态，而不是 NullReference/假 Catalog；
- Book 不复制 Catalog。

## Playback

Playback start / jump / next / previous 必须基于 ActiveSource Catalog。

- ActiveSource=None → 不开始正文播放，返回稳定可解释失败/empty state。
- current PlaybackSnapshot 继续是运行时位置真值。
- Source context/version 必须足够防止迟到的旧 content/audio/prefetch 结果覆盖新上下文。
- 当前阶段没有 Online Source UI，不需要新增完整 Source picker。

## ReadingProgress

继续保持 Book-level：

```text
BookId
ChapterIndex
SegmentIndex
CharacterOffset
AudioPositionMilliseconds
```

不增加 ChapterId FK。

Local Source re-import/Catalog replace 后：

- ChapterIndex 超界 → clamp 到最后合法 chapter；
- 章内 Segment/Character position 在实际目标章节解析后做边界 clamp；
- 不做 title/body/hash matching；
- Catalog empty → unlocated/empty state；
- 只在成功确定合法新位置后 checkpoint。

不要把“clamp”实现成百分比换算。

## Speech Plan / Audio Cache

- technical ChapterId 仍可作为 plan/cache key reference。
- runtime lookup 必须从 ActiveSource Catalog 得到正确 Chapter。
- 新 Catalog replacement 后不存在的旧 technical Chapter 相关派生数据按现有/新的 cleanup owner 清理；不要重新挂到“看起来相似”的新章节。
- Audio Cache 模块不成为 Source Catalog owner。

## UI projections

适配：

- Library；
- BookDetails；
- Player；
- MiniPlayer/SMTC 所需 summary；
- chapter current/selected decoration。

UI 不需要在本任务加入 Online Source 管理入口。

## 测试

长期核心测试优先保护：

- migrated Local Source Book 可以正常打开/播放；
- ActiveSource Catalog query；
- ReadingProgress survives restart；
- catalog shrink clamp；
- inside-chapter clamp；
- ActiveSource=None safe behavior；
- stale/late result cannot overwrite new source context；
- Speech Plan/Audio Cache 仍能围绕技术 ChapterId 正常工作。

中间实现可以删除/重写只绑定旧 StoredFilePath query shape 的测试。

本任务应尽量恢复主要项目 build；如果仍有 T007 明确负责的 deletion/legacy compile failures，可以记录，不要求完整 gate。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
