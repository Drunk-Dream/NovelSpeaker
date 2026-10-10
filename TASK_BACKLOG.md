# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：Book / Source / CurrentCatalog / DB 基础重构

- **规划基线：** `dev` @ `4ed4c4750bbdd72d1f7978caad7a1c22d6084219`（`test: close permanent coverage reduction phase`）。执行时以本地实际 HEAD 为准复核，不回退用户后续合法修改。
- **阶段目标：** 把当前已经部分 Source 化但仍使用“Source-owned Catalog / 可编辑 Book metadata”语义的实现，收敛到新的 Book Identity + BookSourceBinding + Active Source + CurrentCatalog + Book-level ReadingState 模型，并让现有 Local TXT 书籍功能稳定运行。
- **本轮实现：** 领域模型、Application 边界、SQLite schema/migration、Local TXT 导入与元数据确认、书库/详情/播放/进度/删除等本地书主链路。
- **本轮不实现：** Online Source Definition 规则 schema、Legado 兼容、在线书源编辑器、全局搜索、SearchSession、RefreshSession、在线目录获取、在线正文获取、Online Text Cache 运行时或换源 UI。
- **长期合同：** `docs/specs/BOOK_DATA_MODEL.md`、`docs/specs/BOOK_IMPORT.md` 为本阶段首要领域事实；数据迁移遵守 `docs/05_DATA_AND_COMPATIBILITY.md`。
- 当前 v12 SQLite 已包含 `BookSources / LocalBookSources / Chapters(SourceId)`，但这是旧语义的中间形态。本阶段允许追加一次新的 schema migration，把它迁到 CurrentCatalog 模型；不得修改已发布 v1–v12 migration。

## 2. 执行规则

- `[ ]` 未开始；`[-]` 进行中；`[x]` 已完成；`[!]` 仅用于发现超出已授权边界且必须由用户决策的问题。
- 按 **T001 → T005** 串行执行。
- **T001–T003 属于一个 staged breaking migration window。** 这些切片允许在 task spec 明确列出的 Book/Source/Catalog/Import 调用方上暂时处于中间破坏态，不得为保持旧调用方编译而新增长期 Compat/V2/forwarder。
- **T004 必须关闭 staged breaking migration window**：恢复本地书核心用户路径、Release build 与对应 focused tests。
- **T005 是最终收口**：清理旧模型残留、复核核心回归、执行标准完整门禁。
- staged window 不等于跳过验证：每个任务仍必须完成 task spec 声明的静态检查、结构验证或 focused tests。
- 不为本轮设新的“测试数量目标”。只按 `docs/08_QUALITY_AND_TESTING.md` 保留/新增高价值核心测试；若临时验证测试不值得永久保留，任务结束前删除。
- 人工验收始终可选，不阻塞 Agent 连续执行。
- 所有文本文件保持 LF。

## 3. 本轮固定架构决策

以下是已批准边界，执行任务时不要重新讨论或另建第二套解释：

- 产品语义上，规范化 `Title + Author` 唯一确定 Book；BookId 是技术主键。
- Title / Author 只在入库前确认，入库后不提供普通编辑能力。
- 一个 Book 最多一个 Local Binding；未来允许多个 Online Binding；最多一个 Active Binding。
- Source Definition 与 BookSourceBinding 是两个概念；当前不实现 Online Source Definition 规则系统。
- SQLite 只保存 Active Source 对应的一份 CurrentCatalog，不保存所有 Binding 的 Catalog。
- Local Source 切回时重新解析目录，而不是读取一份长期持久的 Local Catalog。
- ReadingProgress 属于 Book；Source/Catalog 变化只按 ordinal/position 做边界截断，不做章节匹配。
- Local 正文是持久业务数据，不属于 Cache。
- Future Online 正文缓存与 Audio Cache 分离，本轮只留边界，不实现。
- 不建立 Source 自动 fallback、跨 Source Chapter Identity、模糊 Book/章节匹配或长期旧模型 compatibility layer。

## 4. 任务安排

### Phase A — 领域边界切换

- [x] **T001（P0）— 收敛 Book / Binding / CurrentCatalog 领域与 Application 边界**
  完成成果：建立共享 `BookIdentity`（NFC、Unicode 空白折叠、空 Author、保留大小写和标点），Book 身份字段只读；将旧 Source entity 重命名为 `BookSourceBinding / LocalBookSourceBinding`，删除 Binding metadata 真值；Chapter 显式关联 `BookId + SourceBindingId`，增加完整不可变 `CurrentCatalog` 与 Book-level `ReadingState.Clamp`（空目录未定位）。Application 导入 port 改为唯一身份查询和可空 CurrentCatalog 提交，正文 port 改为 Book 当前目录 entry/context；删除 Title/Author 编辑 contract、use case、SQLite store、注册和旧编辑服务测试，未增加兼容层或 Online runtime。
  自动验收：Domain Release 编译与全部 34 个 Domain 测试通过，新增身份规范化、阅读边界截断及目录快照完整性核心覆盖；Domain/tests 格式验证、改动 Application 文件空白格式检查与 Domain 无外层/WPF 依赖静态检查通过。既有其它核心测试保留。无环境限制；本任务按 staged window 局部门禁验收，未执行全解决方案完整门禁。
  迁移缺口：Application Release 编译当前有 11 个错误，全部位于允许暂时失效的 `DirectBookImportService`（旧候选查询、Source/LocalSource 属性、旧 entity/Chapter 构造及 ActiveSourceId），由 T003 接回。静态确认 T002–T004 仍需接回 Books persistence、SourceContentReader/正文导出、BookDetails/Library/playback metadata query、详情身份编辑 UI 与对应旧模型测试；不得以兼容 API 恢复旧模型。T004 关闭 breaking window，T005 执行完整门禁。未发现长期合同冲突。

### Phase B — 持久化模型切换

- [x] **T002（P0）— SQLite v13 迁移到 Book Identity + Binding + CurrentCatalog**
  完成成果：追加 v13 migration，复用领域 `BookIdentity` 计算唯一规范身份；重建 Book / Binding / CurrentCatalog 关系，保留 BookId、BindingId、ChapterId、正文路径和正式进度、Speech Plan、Audio Cache 数据。身份冲突、缺失 typed data 或歧义目录返回可识别 `IncompatibleBookLibraryException` 并原子回滚；提交前检查外键。数据库约束保护 Active Binding 所属 Book、Local 单例与当前目录 ordinal，删除最后一个 Binding 级联删除 Book。导入 repository 支持唯一身份查询、活动目录原子替换和非活动 Local 仅更新正文；书库/详情/播放查询、正文读取与导出、进度、缓存、删除恢复和路径迁移全部切到新 schema。删除 Binding metadata 真值与旧候选查询、Source-owned Catalog 运行时路径，未留双读双写或新增 Online runtime。
  自动验收：新增 11 个 v13 核心集成用例，覆盖正常升级与重启幂等、正式数据/文件保留、身份冲突/歧义状态回滚、SQL/回调/取消失败后的恢复、约束及原子快照提交；保留 v12 历史迁移测试，把旧读取 smoke 合并到 v13 升级测试，更新通用迁移测试至当前 schema。35 个 persistence focused tests 和全部 34 个 Domain 测试通过。临时验证项目仅排除待 T003 接回的 `DirectBookImportService` 及 Application 两个依赖它的注册入口，使用真实 Domain/Application/Infrastructure 分层项目完成 Release 编译；验证项目及排除配置已删除。格式验证、LF、`git diff --check`、已发布 v4–v12 migration 未变及 Infrastructure 无旧 schema 运行时引用检查通过。
  迁移缺口：常规 Infrastructure build 仍被既有 `DirectBookImportService` 11 个编译错误阻断，由 T003 接回；App 和其余旧模型测试由 T003/T004 集成。T004 仍须关闭 breaking window，并将兼容失败接到用户可理解的重新导入路径；T005 执行全解决方案完整门禁。本任务依 staged window 局部门禁验收，未运行标准完整门禁；无环境限制，未发现长期合同冲突。

### Phase C — Local Import 恢复

- [x] **T003（P0）— 重接 Local TXT 导入、身份确认与原子 CurrentCatalog 提交**
  完成成果：Local TXT 导入接回统一 `BookIdentity` 与唯一身份查询；规则显式识别完整 Title/Author 时直接继续，否则通过 Application result/request continuation 和轻量确认对话框完成入库前修正（文件名 Title fallback、空 Author 合法、空 Title 禁止）。编码选择先于身份确认，确认面板与大文件进度面板串行展示，结果沿用现有 Snackbar。新 Book 激活唯一 Local Binding；已有 Book 保持身份和 BindingId，活动 Local 原子替换完整 CurrentCatalog/正文范围，非活动 Local 仅更新持久正文。复用 journal/recovery 保留失败/取消时旧正文和旧目录，提交后才发布 typed changes。删除多候选 DTO、选择对话框及 RequiresBookSelection/TargetBookId/CreateNewBook 路径，未新增 schema migration、兼容层或 Online runtime。
  自动验收：重写旧候选/可变身份测试为规范身份与唯一 Local Binding 核心回归，新增缺失身份确认、最终修正、空身份校验和并发唯一性覆盖；保留编码、规则、原子失败/取消、恢复幂等及外部 TXT 独立性核心测试。使用临时 focused projects 直接编译真实 Domain/Application/Infrastructure 与导入 UI 生产源码：81 个导入/持久化测试、9 个导入协调器用例、2 个全量 XAML 主题检查及 1 个隔离 Desktop 临时验证通过；34 个 Domain 测试通过。隔离 Desktop 验证覆盖 Light/Dark 先应用主题再创建确认面板、已有面板切换主题、切换后新建面板、字段校验和取消；临时项目/测试/产物已删除。改动文件格式、LF、git diff --check 及导入范围旧 API 残留检查通过。
  迁移缺口：常规 App build 仍在 BookDetailsViewModel 引用已删除 IBookMetadataUpdateService 处失败；常规 Application/Infrastructure tests 仍有 Export/Cache/Playback/BookManagement 的旧 content/metadata contract 调用编译失败，按合同由 T004 接回。ReadingProgress 的统一 clamp 与主链路集成仍归 T004；T004 关闭 breaking window，T005 执行完整门禁。本任务按 staged window 局部门禁验收，未执行标准完整门禁；无环境限制，未发现长期合同冲突。

### Phase D — 本地书主链路集成

- [x] **T004（P0）— 恢复书库 / 详情 / 播放 / 进度 / 删除并关闭 breaking window**
  完成成果：恢复书库、详情、播放、缓存/朗读计划及导出调用方在 Book + Binding + CurrentCatalog 模型上的编译和核心运行路径；详情页仅展示 Book 身份，删除书名/作者编辑、保存/取消命令和草稿离页拦截，瞬时失败统一使用已有 Snackbar。活动 Local 重新导入在目录/正文提交事务中复用 `ReadingState.Clamp` 截断章序与正文位置，并使旧段落/音频坐标失效；非活动 Local 更新不改 Book 进度。启动组合根将无法安全迁移的书库分类交给已有启动失败窗口，明确要求保留数据并重新导入，不自动合并、删除或重建。保留 typed committed changes、Playback logical-target/迟到音频拒绝、SQLite checkpoint 优先级和删除文件协调/reparse-point 边界；未新增 schema migration、兼容层或 Online runtime。
  自动验收：Release build（零警告/错误）、全解决方案 format verify、全部 977 个永久测试通过（Domain 34、Application 245、Infrastructure 336、Presentation 312、隔离 Desktop WPF 50），包含架构、全量 XAML 主题、播放 target-before-audio、持久化/删除恢复和路径边界回归。新增真实导入→书库/详情/正文读取→重新导入→删除的核心集成回归与迁移失败启动提示回归；更新旧模型 fixture、进度 clamp 与详情刷新测试，删除已失效的身份修改测试及依赖非活动持久目录的缓存测试，相关风险由目录替换/删除核心回归继续保护。临时隔离 Desktop 验证覆盖 Light/Dark 先应用主题再创建详情页、已有页切换主题、切换后新建页、身份绑定与编辑入口删除，验证源码已删除；LF、`git diff --check`、身份 mutation API 和运行时旧 schema 残留检查通过。
  阶段状态：staged breaking migration window 已关闭，标准可构建/可测试状态恢复。无环境限制、未发现长期合同冲突；本任务未重新执行 restore，T005 仍负责最终残留复核与标准完整门禁，不在本任务继续执行。

### Phase E — 收口

- [x] **T005（P0）— 删除旧模型残留、核心回归复核与完整门禁**
  完成成果：复核新 Book Identity / BookSourceBinding / Active Binding / CurrentCatalog / Book-level ReadingState 模型已贯通 Local TXT 主链路。v12 → v13 使用同一 `BookIdentity` normalization，数据库约束保护身份唯一、Active Binding 所属 Book、Local 单例和当前目录；升级保留 BookId、BindingId、ChapterId、正文、正式进度及 Speech/Audio 关系，冲突/歧义原子失败，外键完整性检查通过。导入仅在规则未完整识别 Title/Author 时请求入库前确认，空 Author 合法；活动 Local 原子替换目录并截断进度，非活动 Local 仅更新正文。
  清理与核心回归：累计删除 Source-owned Catalog 运行时、Binding metadata 真值、入库后身份编辑、多候选选择和旧 API；残留扫描确认无双读双写、Compat/V2 或无调用方的旧模型 fixture，历史迁移及对应升级 fixture 保留。删除 3 个重复导入 smoke/内部 journal 调用测试及专用 fake 记录字段；身份与 Hash 区分、正常提交/恢复继续由真实 SQLite/文件集成回归和提交后通知测试保护，不新增永久测试。其余 normalization、迁移回滚、身份确认、原子提交、目录所有权、进度 clamp、正文/播放、删除与信任边界、logical target/迟到结果核心测试全部保留。
  自动验收：标准完整门禁四项全部通过：locked restore（win-x64）、全解决方案 format verify、Release build（零警告/错误）、全部 974 个永久测试（Domain 34、Application 242、Infrastructure 336、Presentation 312、隔离 Desktop WPF 50；零失败/跳过），包含架构与全量 XAML 主题检查。已发布 v4–v12 migration 定义未变，累计改动 LF 与 `git diff --check` 通过；无临时验证产物、无环境限制、未发现长期文档冲突。Online Source 规则、Legado、search/refresh、在线目录/正文和正文缓存 runtime/UI 继续留待后续阶段；本阶段完成。

## 5. 阶段完成标准

T005 完成后至少满足：

- Local TXT 从导入到播放的主链路完全运行在新模型上；
- 数据库不存在运行时 Source-owned Catalog 双模型；
- Book 身份唯一性由数据库与统一 normalization 共同保护；
- Title / Author 旧编辑入口和对应仅为旧模型服务的 use case 已删除；
- v12 正常本地书库可以有界升级并保留关键正式数据；冲突数据不会被静默合并/覆盖；
- 未实现任何本轮明确排除的 Online Source 业务功能；
- 没有为了迁移留下 Old/New/V2/Compat 双读双写；
- 标准完整门禁通过，或对确属环境限制的未执行项如实记录。
