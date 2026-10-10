# T004 — 恢复本地书主链路并关闭 breaking window

## 目标

把书库、详情、播放、ReadingProgress、删除/重新导入等现有 Local Book 用户路径全部接到新模型，并在本任务结束时关闭 staged breaking migration window。

## 必读

- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/specs/BOOK_DATA_MODEL.md`

## 1. Library / Details read models

- Library 直接读取 Book identity/display data，不再从 Active Source metadata snapshot 投影 Title/Author。
- BookDetails 从 Book + Binding summaries + CurrentCatalog 构造场景化 read model。
- 当前只有 Local Binding 时 UI 可以保持简洁，不新增没有业务价值的“Source 状态面板”。
- 删除普通 Title / Author 编辑功能及其 UI/use case/store。如果旧 generic metadata editor 主要为此存在，优先整体删除；不要为了保留一个边缘 Description 编辑而维持复杂旧路径，本轮也不新增新的 Description editor。
- UI 不增加“已加入书架”等与动作按钮重复的状态文字。

## 2. CurrentCatalog consumption

- Details chapter list、Player chapter list、Cache/Speech Plan catalog lookup 都以 Book 的 CurrentCatalog 为唯一目录真值。
- 删除/改写仍假设 `Catalog belongs to Source` 的 query/projector。
- 不创建隐藏 Local catalog cache 来模拟旧模型。

## 3. Local content / Playback

- `IBookPlaybackContentService` 或等价稳定角色通过 CurrentCatalog entry + Active Local Binding 读取正文。
- App/Playback 不接触 StoredContentPath。
- UI logical target 先行与迟到音频拒绝等现有 Playback 合同不能回退。
- Active Source/Catalog change 应通过现有 typed committed change 进入 Playback/Cache owner；页面不手工刷新所有模块。

## 4. ReadingProgress

- re-import / CurrentCatalog replacement 后只使用统一 clamp 逻辑；
- ChapterIndex 超出范围 → last valid chapter；
- 章内位置超出范围 → valid bound；
- 不比较 ChapterId/title/content/hash。

确保 SQLite checkpoint 与当前 PlaybackSnapshot 的既有优先级不变。

## 5. Delete / removal

当前只有 Local Binding 时：

- 删除 Book 协调 Book/Binding/CurrentCatalog/Local content/ReadingProgress/Speech Plan/Audio Cache；
- 不依赖 SQLite cascade 代替文件协调；
- reparse-point trust boundary 不回退。

保留未来 remove-binding 的领域边界，但本轮不新增没有 UI 入口的复杂 Source manager。

## 6. Incompatible migration UX

把 T002 的 normalized duplicate / impossible-current-catalog migration failure 接到已有启动兼容/错误处理边界：

- 明确告诉用户现有书库无法安全自动迁移，需要重新导入本地书；
- 不自动 merge/delete；
- 不静默重建并丢数据；
- 优先复用已有 incompatible schema / startup failure UI，而不是新建大型 recovery flow。

## 7. 删除旧 metadata mutation 路径

重点检查并删除不再有真实调用者的：

- `BookMetadataUpdateService` / store / request 等 Title/Author mutation path；
- Source metadata committed change 若已无语义；
- App details edit commands/buttons/dialogs；
- 仅为 Source-owned Catalog 存在的 source-change projection。

不要影响 Regex/Chapter/Provider 等独立规则编辑能力。

## 8. 测试与门禁

本任务结束必须恢复标准“可构建、可测试”状态，至少执行：

```powershell
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
```

并运行 Books/Playback/Persistence/Presentation 中与本次风险直接相关的 focused tests。

重点保护：

- Library/Details 能读取升级后的本地书；
- CurrentCatalog 正常显示；
- 正文读取/播放恢复；
- ReadingProgress clamp；
- re-import；
- delete cleanup；
- Title/Author 不再可编辑；
- UI target-before-audio 行为不回退。

若仍有编译失败或核心本地书路径不可用，不得完成 T004，也不得进入 T005。
