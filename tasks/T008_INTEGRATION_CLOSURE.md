# T008：恢复完整可运行状态并执行阶段级验收

## 依赖

T001–T007。

## 目标

结束 staged breaking migration window，确认统一批量管理与 Book/Source 模型已经成为仓库唯一正式实现。

本任务不再增加新产品能力；只做集成、缺口修复、测试收敛、旧路径删除和最终验收。

## 必读

- `AGENTS.md`
- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`

按发现的问题再阅读对应 owner 文档，不遍历所有 docs 作为需求来源。

## 必须审计：Batch Management

确认：

- shared Management Mode 不包含业务 action；
- Library normal click/open 与 management click/toggle 分离；
- Player 不再把章节管理命名/建模成 ActiveCache-specific selection；
- Provider/Rules normal editor selection 与 management selection 分离；
- selected count=0 不退出；
- hidden filtered items 不参与 batch；
- Select All 只选择 visible/manageable；
- right-click selected/unselected 语义正确；
- batch delete 一次确认、partial continuation；
- batch export 一个 batch result；
- CacheManagement 仍保持 Extended Selection 例外。

删除过期：

- ActiveCacheSelection 专名/接口；
- 只为旧 always-multiselect 交互存在的 controller；
- 临时 screenshot/test/fixture。

## 必须审计：Book/Source

仓库中不应再有运行时旧模型：

- Book 直接拥有 StoredFilePath；
- Book.SourceHash 作为 identity；
- Chapters.BookId 作为 Catalog ownership；
- ReadingProgress → ChapterId；
- “外部 TXT 是已导入正文运行时权威来源”的注释/逻辑；
- 新旧 schema 双读/双写；
- 为过渡存在的 legacy adapter/wrapper。

允许出现这些词的地方只能是 migration test/历史 migration SQL 等明确历史上下文。

确认：

- new import；
- unique candidate re-import update；
- multi-candidate explicit choice；
- library/details；
- playback；
- reading progress；
- speech plan/audio cache；
- book delete；
- app restart from migrated v11 DB。

## 测试收敛

遵守 `docs/08_QUALITY_AND_TESTING.md`：

- 删除只冻结旧实现 shape 的测试；
- 合并重复测试；
- 保留 migration/data safety/core playback/core batch behavior；
- 不为了覆盖率新增细节测试；
- 临时测试全部删除。

## Architecture review

确认：

- Domain/Application/Infrastructure/App 依赖方向；
- Source content 技术实现不泄露进 Application；
- Shared selection 不依赖 Feature；
- mutable state owner 单一；
- 没有新 EventBus/Service Locator/万能 Manager；
- Online Source 未被提前实现成通用插件框架。

## 完整标准门禁

必须执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

如果环境限制导致无法运行，必须记录真实限制；不能把阶段标记完成为“自动验收通过”。若代码/测试本身失败则必须修复。

## 文档一致性

只修正实现过程中发现的真实长期偏差：

- `docs/` 最终事实与实现一致；
- 不把 T004–T007 一次性迁移步骤追加进长期文档；
- `TASK_BACKLOG.md` 记录最终成果；
- 本 task 完成后删除 `tasks/T008_INTEGRATION_CLOSURE.md`。

完成后 staged breaking migration window 正式结束，仓库必须恢复正常可运行状态。
