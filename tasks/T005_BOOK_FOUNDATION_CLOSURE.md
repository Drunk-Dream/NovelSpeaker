# T005 — Book foundation 收口、做减法与完整门禁

## 目标

基于 T001–T004 已经可运行的新模型，进行一次小范围架构收口：删除旧模型残留、复核数据库与本地书核心风险、确保没有顺手实现 Online Source 业务，并完成完整质量门禁。

## 必读

- `AGENTS.md`
- `.codex/review-checklist.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/08_QUALITY_AND_TESTING.md`

## 1. 旧模型残留扫描

搜索并判断所有相关符号/SQL/注释/测试，删除已经失去语义的实现，重点包括：

- Source-owned Catalog / `Chapter.SourceId` 旧假设；
- Binding 上独立 Title/Author/Description snapshot；
- Title/Author post-import editor；
- multi-candidate Book identity selection；
- Old/New/V2/Compat mapper/repository；
- v12 双读/双写；
- “Local 永远 Active”被错误固化在通用领域层的特例；
- 无调用方的 test doubles/fixtures。

不要因为字符串中仍出现 `Source` 就机械删除；只删除与旧语义绑定且已无真实职责的代码。

## 2. Future boundary 审计

确认本轮没有提前实现：

- Online Source rule schema/runtime；
- Legado parser/compatibility；
- SearchSession / RefreshSession；
- Online HTTP catalog/content；
- Online text cache runtime/UI；
- generic plugin framework。

允许存在少量稳定 type/port 级扩展点，但不能有无调用方的大型未来基础设施。

## 3. 数据与迁移复核

复核：

- normalization 运行时与 migration 一致；
- unique identity 受 DB 约束；
- ActiveSourceBindingId 必须引用同 Book Binding；
- 每 Book 最多一个 Local Binding；
- SQLite 只存在 CurrentCatalog，不存在 inactive source catalog rows；
- v12 正常升级保留 BookId / 可安全保留的 ChapterId / ReadingProgress / Speech/Audio relations；
- duplicate normalized identity fail closed；
- migration `foreign_key_check` 通过。

## 4. 核心测试做减法

本轮不要因为大重构重新堆积细粒度测试。

保留/新增的永久测试应集中于：

- normalization + unique identity；
- v12 migration happy path / conflict rollback；
- metadata confirmation branch；
- Local import atomic commit；
- CurrentCatalog ownership；
- ReadingProgress clamp；
- Local content read/playback；
- delete cleanup / trust boundary；
- UI logical target 与迟到结果核心保护。

重复 DTO mapping、字段排列、私有调用顺序、纯文案/布局测试应删除或不新增。

## 5. 文档一致性

实现若只是在已经授权细节内选择具体类名/表名，不需要改长期文档。

如果发现实现无法满足长期模型，先记录具体冲突；不要通过悄悄修改文档来让实现“合法化”。只有确有必要且不改变已确认产品语义时，才做最小文档同步。

## 6. 完整门禁

最终执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

并按 `.codex/review-checklist.md` 对 T001–T005 累计最终 diff 进行独立审查。

若环境限制无法执行某项，如实记录；不得通过 Skip、删除项目、弱化隔离或改 CI 制造绿色。

## 完成结果应记录

在 `TASK_BACKLOG.md` 的 T005 完成成果中简要说明：

- 新 Book/Binding/CurrentCatalog/DB 模型已落地；
- v12 migration 结果；
- Local TXT 导入与 metadata confirmation 行为；
- 删除了哪些主要旧模型残留；
- 核心测试与完整门禁结果；
- 明确 Online Source 规则/search/refresh/content 仍留待后续阶段。
