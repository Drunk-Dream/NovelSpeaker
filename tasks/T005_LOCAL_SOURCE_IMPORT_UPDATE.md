# T005：重构 Local TXT 导入与重新导入更新

## 依赖

T004。

## 阶段性质

staged breaking migration window。允许其它尚未迁移 runtime 调用方暂时失败，不建立旧模型 compatibility。

## 目标

把当前 `DirectBookImportService` 的“hash duplicate → 新 Book + Chapters”流程改成：

```text
analyze TXT
→ metadata + catalog
→ strict Title+Author Book candidate resolution
→ prepare complete LocalSource snapshot
→ create/update Book + LocalSource + Catalog
```

## 必读

- `AGENTS.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`

## Book candidate resolution

移除“SourceHash 就是重复 Book 身份”的产品语义。

实现：

- metadata extraction 完成后，用最终 Title + Author 查候选 Book；
- 空 Author 作为明确值；
- 0 candidate → create new Book；
- 1 candidate → create/update that Book's Local Source；
- >1 candidates → import result 进入明确的 `RequiresBookSelection` 或等价状态；
- 用户选择候选 Book 后重试/继续同一导入意图；
- 用户可以选择“作为新 Book 导入”；
- 不按最近导入、最近阅读、BookId、列表第一项猜测。

SourceHash 可以保留在 Local Source typed data 中用于内容相同/no-op 优化，但不能再是 Book identity。

## Local Source singleton

- 每 Book 最多一个 Local Source。
- Book 已有 Local Source → update snapshot。
- Book 没有 Local Source → bind new Local Source。
- 更新/绑定非 ActiveSource 不自动激活；当前阶段所有既有本地书通常 Local Source 就是 active，但实现不要硬编码“Local 永远 Active”。

## Snapshot update

新 Local Source snapshot 至少包括：

- Source metadata；
- complete Catalog；
- normalized internal content；
- Local typed metadata。

要求：

```text
prepare
→ validate
→ commit
```

失败时旧 snapshot 完整保留。

允许复用/扩展现有：

- Book operation journal；
- staged temp file；
- atomic file replace；
- SQLite transaction；

但不要建设通用 distributed transaction framework。

当前 internal file 仍可使用 `Books/{BookId}/content.txt`。

更新 active Local Source 时：

- Book Display Metadata = latest Source metadata；
- empty Description 可以清空旧值；
- BookId 保持；
- ReadingProgress 不在此任务做内容匹配。

## Multiple-candidate UI

只做轻量明确选择，不做导入 Wizard。

用户需要看到足以区分候选 Book 的最小信息，并可选择：

- update candidate A/B/...；
- import as new Book；
- cancel。

不要显示小说正文或把诊断敏感信息带进日志。

## Technical Chapter IDs

Catalog replacement 不建立产品级 Chapter Identity。

可以按最简单可靠方式生成新 Chapter IDs；如果为了保留现有 Audio/Speech Plan 价值能明确安全复用技术 ID，也可以实现，但不得使用标题/正文相似度猜测。

T004 migration 对既有旧书保留 Chapter IDs；这里讨论的是后续重新导入更新。

## 测试

核心测试至少保护：

- 0 candidate create；
- 1 candidate update existing BookId；
- multiple candidates no guessing；
- blank Author matching；
- Local Source singleton；
- SourceHash 不再独立决定 Book identity；
- update active source projects metadata；
- update failure preserves old content/catalog；
- source external file can disappear after import。

本任务使用 focused Application/Infrastructure tests；完整 solution gate 留给 T008。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
