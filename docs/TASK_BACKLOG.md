# NovelSpeaker 架构优化任务 Backlog

## 1. 文档定位

本文件是架构优化期间唯一的过程与任务状态文档。数字编号文档描述产品和架构终态；本文件描述从当前实现迁移到终态的批次、依赖、风险和验收。

目标不是改写产品，而是在保持现有功能、SQLite 数据、缓存和 UI 行为的前提下：

- 恢复 Domain、Application、Infrastructure、App 的真实职责边界。
- 按功能切片重新组织代码。
- 拆分播放、页面和持久化中的超大职责集合。
- 建立明确的启动、页面、操作和播放生命周期。
- 重组测试体系并用自动检查防止架构回退。
- 删除迁移产生的旧入口、重复 DTO 和一次性适配器。

已完成的历史功能/UI backlog 见 `docs/archives/`，不再复制到本文件。

## 2. 状态、优先级与任务规则

状态：

- `[ ]`：未开始。
- `[~]`：进行中，只能有一个负责该任务的主 Agent。
- `[x]`：实现、测试、文档和清理全部完成。
- `[!]`：被明确阻塞，必须记录阻塞证据和恢复条件。

优先级：

- `P0`：安全、数据或后续迁移保护网，必须先完成。
- `P1`：架构主路径。
- `P2`：可维护性收口，不能跳过但可在主迁移后执行。

每个任务交给 AI 时必须遵守：

1. 阅读任务列出的设计文档、相关生产代码和现有测试。
2. 先增加或确认能固定当前行为的测试。
3. 一次只建立一个目标实现，不创建长期 `New/V2/Refactored/Compat` 平行版本。
4. 迁移所有调用者和直接测试后，在同一任务删除旧入口。
5. 不修改任务“非目标”中的行为。
6. 运行任务级测试；Wave 收口运行完整质量门禁。
7. 行为或边界变化同步数字设计文档；只移动代码时不要把迁移过程写入数字文档。

完整质量门禁固定为：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## 3. 审计基线

### 3.1 量化证据

审计时点的主要规模：

| 区域 | 证据 |
|---|---|
| Domain | 30 个 C# 文件，约 580 行；混入 HTTP、导入预览和 UI read model |
| Application | 约 120 个 C# 文件、1664 行、121 个公共类型；主要是接口/DTO，几乎没有用例实现 |
| Infrastructure | 59 个 C# 文件、约 10827 行；同时承载 SQL、文件、HTTP、Jint、NAudio 和大量业务用例 |
| App | 128 个 C# 文件、约 11610 行；Page/View/ViewModel 按技术类型分散 |
| Tests | 单项目 90 个 C# 文件、约 18267 行；纯单元、SQLite、HTTP、NAudio 和 WPF 混合且全局禁用并行 |
| PlaybackCoordinator | 2070 行，约 50 个方法，拥有全部播放状态与多类协作职责 |
| PlayerViewModel | 1193 行，混合命令、内容加载、Snapshot 投影、规则/设置和滚动请求 |
| PlayerViewTests | 2053 行；PlayerViewModelTests 1794 行 |

### 3.2 Cleanup/refactor 风险分类

#### A：安全清理

- 播放协调器文件名与类型名错位：`BookPlaybackCoordinator.cs` 实际是 `PlaybackCoordinator`，`PlaybackCoordinator.cs` 实际是 `LocalAudioPlaybackCoordinator`。
- 正式 App 输出包含三个测试音频，包括损坏 MP3；应移入 tests/TestAssets。
- `BookManagementService.GetBookCachedBytesAsync` 无引用。
- 多处代码注释仍把已实现的缓存、进度或恢复称为未来功能。

#### B：低风险重构

- 正则 Workspace 的压缩单行风格与相邻代码不一致。
- 当前段与预取重复构造同一缓存键。
- UI 测试重复实现视觉树遍历和大量相同 fake。
- Infrastructure/App DI 注册平铺为超长清单。
- `CacheWorkspaceService` 通用 catch 吞取消；设置同步阻塞异步读取；JSON 保存非原子。

#### C：必须分阶段验证

- Application 去 SQLite、迁移用例出 Infrastructure。
- Domain 模型重新归位。
- Books/TTS/Cache 公共接口和重复 DTO 收敛。
- 2070 行播放协调器、1193 行 PlayerViewModel 和三套规则工作台拆分。
- App feature slice、强类型导航和 Page activation。
- 数据库/文件恢复 journal、路径格式和 schema 追加迁移。
- 测试项目拆分。

#### D：架构重组中不得顺手改变

- 已发布 SQLite migration 4/5 和版本历史。
- `AudioCacheKey` 的位置相关字段顺序、最终 SpeechText 和版本命名空间。
- Jint 白名单、超时、语句、递归和输出限制。
- Playback SessionId、取消、迟到结果隔离和暂停跳转语义。
- 正在播放/预取/写入文件的缓存保护。
- 迁移、规则样本、损坏音频和 WPF STA Host 等回归资产。
- 当前产品不支持 Cookie/LoginInfo 的事实；若要实现必须另立功能 Epic。

## 4. 总体依赖顺序

```text
Wave 0 事实基线与保护网
  ↓
Wave 1 架构骨架、Application 纯化准备、设置/SQLite 基础加固
  ↓
Wave 2 Books 与 TextProcessing 纵向迁移
  ↓
Wave 3 Speech/TTS 纵向迁移
  ↓
Wave 4 Cache 纵向迁移
  ↓
Wave 5 Playback 状态机迁移与拆分
  ↓
Wave 6 App 导航、feature slice 与 ViewModel 拆分
  ↓
Wave 7 Bootstrap、DI 和进程生命周期
  ↓
Wave 8 测试项目拆分与确定性测试
  ↓
Wave 9 全仓清理、文档收口和发布级验证
```

Wave 内标注依赖的任务必须按依赖执行。不同功能任务只有在不修改同一公共合同/DI 文件时才可并行。

---

## Wave 0：事实基线与保护网

### [x] ARC-000（P0）：完成全仓架构审计和终态设计

交付：

- 核对四项目、主要业务链路、App/WPF、持久化、启动和测试组织。
- 修正 Cookie/LoginInfo、schema、播放能力和质量门禁等文档事实冲突。
- 更新 `02/05/06/08/09/10/11/12` 与本 backlog。

说明：本任务只修改文档/项目基线说明，未修改业务代码。

### [x] ARC-001（P0）：建立架构依赖测试

前置：ARC-000。

范围：当前测试项目、四个 csproj、Solution；暂不移动生产类型。

实现：

- 检查 Domain 不引用其它产品项目或技术包。
- 检查 Application 不新增 SQLite/Jint/NAudio/WPF/Wpf.Ui/Infrastructure 依赖；对现有 SQLite 引用建立“已知唯一例外”并要求后续清零。
- 检查 App 中只有 Bootstrap/App 组合根可引用 Infrastructure。
- 检查 Infrastructure 不引用 App/WPF。
- 检查 ViewModel 公共 API 不新增 WPF/Wpf.Ui 类型。
- 检查文件名、主公共类型和命名空间的基本一致性。

验收：测试能对故意加入的非法引用失败；不引入重量级架构框架。

完成说明：已在当前综合测试项目中增加零第三方架构框架的仓库级检查和规则契约测试，覆盖 Solution/项目依赖、Application SQLite 唯一例外、App 启动组合边界、ViewModel 公共 UI 类型以及文件/命名空间/主公共类型一致性。既有 ViewModel UI 公共签名和文件命名债务使用精确基线冻结，新增或清理条目都必须显式更新基线；生产类型未移动。

### [x] ARC-002（P0）：冻结关键行为特征测试

前置：ARC-001。

范围：播放、导入/删除、规则 Workspace、设置、缓存、导航生命周期现有测试。

补充：

- 播放的暂停跳转、快速切换、旧结果晚到、正则仅 Display/Speech 变化、缓存损坏和事件协作者抛错。
- 导入/删除的文件与数据库故障点。
- TTS/章节/正则编辑从所有 Shell 导航路径离开的未保存保护。
- DI 关键服务可解析和生命周期基线。
- 正式发布输出不得包含测试 fixture。

非目标：不先重写实现来迎合测试；测试描述用户可观察行为。

完成说明：已在现有综合测试项目中补充播放快速跳转最终态、独立 SessionId、重复损坏恢复和事件订阅异常，导入最终切换失败清理、删除遇受保护缓存时的文件/数据库恢复，设置取消与损坏 JSON 回退，以及 TTS/章节/正则编辑草稿的保存、放弃、取消和保存失败保护。Shell 导航入口、DI 可解析与 Singleton/Transient 生命周期、Release 门禁和正式输出测试音频均建立了精确契约测试。UI-003 前三个规则 Page 尚未接入全局导航守卫，CLEAN-004 前正式输出仍包含三个测试音频；两项已作为显式债务基线冻结，本任务未提前修复或误报完成。

### [x] UI-003（P0）：修复规则页面全局未保存保护缺口

前置：ARC-002。

证据：当前只有 `BookDetailsPage` 注册 `INavigationGuardService`；TTS、章节和正则页只保护自己的 Back/选择命令。

实现：

- 将三个规则 Page 注册到现有全局导航守卫。
- 覆盖返回按钮、一级导航、`Alt+Left`、`Ctrl+,`、正在播放入口和窗口关闭。
- 页面离开/销毁时可靠注销，避免旧页面继续阻止导航。

验收：保存、放弃、取消三条路径均有 Shell 级测试；不改变编辑字段和视觉布局。

完成说明：TTS、章节和正则规则 Page 已在进入/重新加载时注册统一导航守卫，并在离开或卸载时幂等注销；三个 ViewModel 公开并复用同一保存/放弃/取消协议。主窗口关闭已通过异步 Closing 桥接覆盖取消、批准、重复关闭和异常投影，现有一级导航、快捷键与正在播放入口继续统一经过守卫。已删除对应债务基线并增加协议、注册生命周期和窗口关闭回归测试。

### [x] CLEAN-004（P0）：移出正式包中的测试音频

前置：ARC-002。

实现：

- 将 demo/corrupt 音频移动到 `tests/.../TestAssets/Audio`。
- 测试项目直接复制测试资产，不再从 App 资产链接。
- 移除 App csproj 的 `Assets/Audio/*.*` Content。

验收：相关 NAudio/WPF 测试通过；publish 输出不含 demo/corrupt 音频。

完成说明：三个受保护音频 fixture 已原样移入测试项目的 `TestAssets/Audio`，测试项目直接复制自身资产，App 项目不再声明或持有测试音频。债务基线已改为正向隔离契约；Release 工作流在压缩前检查 publish 目录，并在压缩后再次检查 ZIP 条目，发现任一 fixture 即失败。

### [x] COMPAT-005（P0）：统一 Cookie/LoginInfo 不支持行为

前置：ARC-002。

实现：

- 规则转换、导入、新建/保存和 UI 错误投影一致地将 Cookie/LoginInfo 依赖判为不兼容。
- 保留脱敏器对相关字段的防御性识别。
- 将 `cookie-sample.json`、`login-info-sample.json` 明确归类为不支持回归样本；若后者实际只含普通 Header，应改名避免误导。
- 删除或标记未调用的 Cookie 成功端点，只有在确认不再作为未来 fixture 时才能删除。

验收：不得通过当前 handler 产生 Cookie；文档、测试名和 UI 结果一致。

完成说明：转换、导入、编辑校验、旧持久化规则编译和 HTTP 执行现统一拒绝 Cookie/LoginInfo 依赖；导入与保存 UI 使用固定脱敏文案明确提示不兼容，混合导入继续报告新增、失败和跳过数量。普通 Authorization Header 保持支持，测试样本已拆分为支持的 Header 样本与不支持的 Cookie/LoginInfo 样本；Cookie Header 负向端点用于验证请求发送前即被拒绝，未使用的 Cookie 成功端点已删除。

---

## Wave 1：架构骨架与基础加固

### [x] ARCH-101（P1）：建立目标功能命名空间与模块化注册骨架

前置：Wave 0 全部完成。

实现：

- 在 Application 建立 Books、Speech、Playback、Settings 功能目录和 `AddNovelSpeakerApplication()`。
- 在 Infrastructure 建立 Persistence、Storage、Speech、Audio 等适配器注册模块。
- 顶层注册方法先委托现有实现，保持行为和生命周期不变。
- 记录每个 Singleton/Page/operation/session 的状态所有权。

验收：仅结构和注册重排；所有旧测试与 DI 可解析测试通过；不出现循环注册或 service locator。

完成说明：Application 已提供幂等的 `AddNovelSpeakerApplication()`，按 Books、Speech、Playback、Settings 功能边界组合注册并统一提供进程级 `TimeProvider`。Infrastructure 原单体清单已拆为 Persistence、FileStorage、Books、Speech、Audio、Settings 适配器模块，顶层方法只按固定顺序组合；App 与 WPF Test Host 统一按 Application、Infrastructure、Desktop 三层装配。现有服务映射和 Singleton/Transient 生命周期保持不变，所有注册入口可重复调用；DI 测试覆盖容器 scope/build 校验、关键服务解析、共享实现映射和瞬态实例。运行时文档已登记 process/page/operation/playback session 的实际状态所有权，以及仍为 Singleton 的页面 ViewModel 迁移债务。

### [x] DOMAIN-102（P1）：清理 Domain 中的流程/传输/UI 模型

前置：ARCH-101。

范围：`Domain/Speech` 为主，并审计 Books/Settings。

实现：

- 将 import preview/item/result、rule summary/test result、请求预览、parsed HTTP request/response 等移到对应 Application 切片。
- 为 SQLite row 和 JSON source DTO 建立 Infrastructure mapper，不让 Domain 保存持久化字符串格式。
- `HttpTtsRule` 不在实体方法中解析模板；规范化由 Application 服务完成。
- 统一新代码的 `DateTimeOffset`/`TimeProvider` 边界；数据库字段保持兼容。

验收：Domain 不包含 UI/HTTP/SQLite DTO；schema 和序列化输出不变；调用者/测试一次迁完，无兼容副本。

完成说明：Domain/Speech 现仅保留结构化 `HttpTtsRule` 与 `TtsErrorKind`；规则导入/列表/测试投影、模板规范化、请求编译模型和 HTTP 执行结果已一次迁入 Application 的 Rules、Compilation、Execution 切片，并由 `ITtsRuleNormalizer` 负责实体到运行时模板的转换。Infrastructure 新增 Legado JSON source DTO、TTS SQLite/JSON mapper 和统一 SQLite 时间 mapper，继续按既有 `Header`、`RequestOptionsJson`、ISO round-trip 格式读写且导出 JSON 语义不变；Domain 仅保存语义化 body 文本及其结构值标志，不保存 JSON 引号编码。Book、ChapterRule、BookSummary、ReadingProgressEntry 与 TTS 元数据已改用 `DateTimeOffset`，导入、章节规则、规则保存、书籍元数据和进度流程通过 `TimeProvider` 获取时间；迁移 4/5 与 schema 未修改。架构测试锁定 Domain/Speech 类型白名单并禁止 transport/SQLite DTO，Speech/Books 特征与持久化 round-trip 测试及完整测试均通过。

### [ ] DB-103（P0）：加固 SQLite 连接和版本检查

前置：ARC-002，可与 DOMAIN-102 并行。

实现：

- 每连接启用 `PRAGMA foreign_keys=ON` 和合理 busy timeout。
- 明确评估 WAL；只有并发测试证明需要且兼容时启用。
- migration runner 拒绝高于当前版本的数据库。
- 保留并追加 migration，绝不改写 4/5。

测试：FK 约束/级联、版本 3/6 拒绝、并发 busy/cancel、迁移回滚。

### [ ] SETTINGS-104（P0）：建立单一设置快照和原子 JSON 保存

前置：ARCH-101。

实现：

- 启动只读取一次设置，日志与容器复用同一规范化快照。
- 同步 provider 只读内存，不再同步阻塞 `LoadAsync`。
- 设置更新串行化并发布变更通知。
- 保存使用同目录临时文件、flush 和原子 replace；定义损坏文件恢复。
- 使用 `TimeProvider` 处理更新时间或防抖。

测试：并发更新、旧保存晚到、取消、写入中断、损坏 JSON、SynchronizationContext 下不死锁。

### [ ] INFRA-105（P0）：修复取消和安全错误投影基础缺陷

前置：ARC-002。

实现：

- `CacheWorkspaceService.TryEstimateSegmentCountAsync` 传播取消，只降级预期异常。
- HTTP/模板异常不把原始 `Exception.Message` 拼入用户结果。
- 详细异常只经 redactor 写日志；用户消息使用稳定错误类别。
- 增加异常文本含 token/query/body/正文的回归测试。

非目标：不在本任务拆分整个 HTTP client 或 Cache 服务。

---

## Wave 2：Books 与 TextProcessing 纵向迁移

### [ ] BOOK-201（P1）：建立书籍 read/write 语义端口

前置：DOMAIN-102、DB-103。

实现：

- 按用例建立书库摘要、详情/章节查询、导入提交、元信息更新和删除所需端口。
- SQL 与 ordinal mapper 全部留在 Infrastructure/Persistence/Books。
- 拆开 `IBookManagementService` 中 query、metadata update、delete 三类用例合同。
- Application 合同不返回活动 connection、绝对路径或 SQLite 类型。

验收：现有 UI 结果字段不减少；repository/query 集成测试覆盖缺失书籍、排序和事务。

### [ ] TEXT-202（P1）：迁移章节规则和正则 Workspace 到 Application

前置：BOOK-201。

实现：

- 移动 `ChapterRuleManagementService`、`ChapterRuleWorkspaceService`、`RegexReplacementRuleWorkspaceService` 的用例行为。
- Repository 留在 Infrastructure；正则/章节纯校验与排序逻辑留在 Application/Domain。
- `IRegexReplacementRuleErrorStore` 和主线 Pipeline 改为必需依赖，不允许 null/no-op 绕过。
- 展开正则 Workspace 压缩代码并与章节 Workspace 共享明确的小型校验/编辑会话模式；不得做万能泛型框架。

测试：字段级保存不覆盖左侧状态、非法历史规则隔离、取消传播、规则刷新行为。

### [ ] BOOK-203（P1）：迁移直接导入用例到 Application

前置：BOOK-201、TEXT-202、SETTINGS-104。

实现：

- `DirectBookImportService` 移入 Application/Books/Import。
- 文件分析、hash、正文 store 和数据库提交通过语义端口协作。
- 元数据时间和 ID 生成使用可控服务。
- 保持高置信度直接导入、低置信度选择、重复拒绝和取消语义。

测试：所有原导入测试迁移到 Application/Infrastructure 对应层；不访问真实用户文件。

### [ ] BOOK-204（P0）：实现导入/删除 operation journal 与路径约束

前置：BOOK-203。

实现：

- 设计 Staged → DatabaseCommitted → Completed 的持久化操作记录。
- 启动时幂等恢复中断的导入和删除。
- 建立 `IAppStoragePathResolver`，做 canonicalization、root containment 和 reparse point 策略。
- 新记录优先存相对 storage key；旧绝对路径兼容读取并通过新增 migration/惰性迁移处理。
- 删除用例拆为 Application 协调 + Infrastructure 原子数据/文件操作；永不触碰外部 TXT。

测试：每个故障点进程中断、journal 重放两次、恶意 DB 路径、`..`、根外文件、部分缓存删除失败。

### [ ] BOOK-205（P1）：拆分播放内容查询与装配

前置：BOOK-201、TEXT-202。

实现：

- SQLite adapter 只返回书籍/章节元数据；Application 内容服务负责读取、分段和正则装配。
- 建立显式 `Unloaded/LoadedEmpty/Loaded/Failed` 章节状态，不用 `Segments.Count == 0` 表示未加载。
- 进度映射仍使用原始字符偏移。

测试：正则把整章过滤为空时只加载一次、自动推进稳定、取消与旧结果隔离。

---

## Wave 3：Speech/TTS 纵向迁移

### [ ] TTS-301（P1）：拆分规则来源、业务规则、运行时规则和持久化行

前置：DOMAIN-102、SETTINGS-104、COMPAT-005。

实现：

- Legado/NovelSpeaker JSON source DTO 留在 Infrastructure/Speech/Legado。
- Domain 只保留业务规则和值；Application 拥有 editor/import/preview/result 和 normalized runtime contract。
- SQLite mapper 单独负责 row ↔ business model。
- `ITtsRuleConverter` 不再以 `JsonElement` 作为高层公共用例接口；来源 parser/convert adapter 明确分层。

验收：导入、导出和脱敏预览字节/语义兼容；不保存原始 JSON 真相源。

### [ ] TTS-302（P1）：拆分并迁移 TTS 规则库用例

前置：TTS-301。

实现：

- 将 533 行大服务拆为 Import、Editor、Selection 和 Queries 用例并迁入 Application。
- 当前规则只有一个写入口；删除/禁用保护复用 Selection 用例。
- mapper、validator、serializer 拆分但保持 internal，不建立公共万能映射器。
- UI 迁移到窄接口，删除旧 `ITtsRuleLibraryService` 后一次修复所有重复 fake。

测试：对象/数组导入、重复/同名、编辑副本、设为当前、禁用/删除当前、导出和试听草稿。

### [ ] TTS-303（P1）：拆分编译、运输、重试和响应验证

前置：TTS-301。

实现：

- Application 保留编译/执行合同和用例编排。
- Infrastructure 分出 Template/Jint evaluator、Http transport、Retry policy、Response validator、TemporaryAudioStore、AudioProbe。
- `HttpTtsClient` 不再同时负责全部职责；明确 HttpClient/handler ownership。
- 限流与 Retry-After 协同但仍为独立概念。

测试：GET/POST、Header/Body、超时/取消、401/429/5xx、错误消息脱敏、临时文件清理和音频解码。

### [ ] TTS-304（P1）：迁移试听与播放音频提供用例

前置：TTS-302、TTS-303、Wave 4 的 CACHE-401 接口可用。

实现：

- `TtsRuleTestService` 的校验/编译/执行/试听编排进入 Application。
- `PlaybackAudioProvider` 进入 Application/Playback/Audio。
- HTTP、缓存、临时音频和本地播放器都通过端口调用。
- 统一当前段和预取的缓存键转换，删除重复构造方法。

验收：规则页试听与正式播放使用同一编译/执行安全边界；草稿不被自动保存。

---

## Wave 4：Cache 纵向迁移

### [ ] CACHE-401（P1）：收敛缓存用例接口和结果模型

前置：BOOK-201、DOMAIN-102。

实现：

- 合并 `AudioCacheSummary/CacheOverviewModel` 等双层近重复 DTO，只保留 Infrastructure store model 与 Application use-case model 两层。
- Application 提供缓存查询/清理门面；App 不依赖 SQLite cache 类型。
- 章节完整度通过 Books/Text 查询端口组合，不在 Cache 服务中直接 SQL。

验收：二级总览、书籍/章节列表、完整度和四类清理 UI 字段保持不变。

### [ ] CACHE-402（P1）：拆分 SqliteAudioCache

前置：CACHE-401、DB-103、BOOK-204 路径解析器。

实现：

- 拆为 SQLite index/query、音频 file store、maintenance/LRU 和保护 registry。
- 保留一个窄 facade 供迁移期调用；任务结束迁移所有调用者并删除旧大类入口。
- 所有路径使用 resolver；设置/缓存写入遵循原子协议。
- 当前播放、生成和写入保护语义不变。

测试：命中、写入、并发、损坏重建、孤儿清理、LRU、保护和根外路径。

### [ ] CACHE-403（P1）：迁移缓存 Workspace 到 Application

前置：CACHE-401、CACHE-402、BOOK-205。

实现：

- 将缓存总览/书籍/章节组合、完整度估算和清理结果映射移入 Application。
- 使用 Books/Chapter/Text 端口，不直接连接 SQLite。
- 取消必须传播；预期内容读取失败才把完整度降级为未知，并记录安全诊断。

验收：CacheAndData/CacheManagement/BookDetails 复用同一用例与保护策略。

---

## Wave 5：Playback 状态机迁移与拆分

本 Wave 风险最高，必须在 Books、Speech、Cache 端口稳定后执行。每项只迁移一个职责并运行完整 Playback 测试。

### [ ] PLAY-501（P1）：校正播放文件/类型命名和必需合同

前置：ARC-002、BOOK-205。

实现：

- 文件名改为 `PlaybackCoordinator.cs` 与 `LocalAudioPlaybackCoordinator.cs`，测试名同步。
- 修正仍称缓存/进度为未来功能的注释。
- 删除 `RefreshRegexReplacementAsync` 默认 no-op；正则 Pipeline/ErrorStore 改必需依赖。
- 对 UI 不使用的 Skip API 做调用审计；先以测试固定“UI 不暴露跳过”，是否删除在 PLAY-507 完成。

验收：只做命名和合同强制，不改变状态转换。

### [ ] PLAY-502（P1）：将播放协调用例迁入 Application

前置：TTS-304、CACHE-403、PLAY-501。

实现：

- 移动书籍级 `PlaybackCoordinator`、本地音频协调器、PrefetchScheduler 和相关用例实现到 Application。
- Infrastructure 只保留 NAudio、SQLite progress/cache、HTTP/Jint/file adapter。
- 保持当前 facade、DI 生命周期和 Snapshot 事件。

验收：Application 不需引用任何技术包；所有原播放测试仅调整命名空间仍通过。

### [ ] PLAY-503（P1）：抽取纯位置解析与 Snapshot 投影

前置：PLAY-502。

实现：

- 抽取 `PlaybackPositionResolver`：相邻段/章、恢复、边界、原始偏移映射。
- 抽取 `PlaybackSnapshotProjector`。
- 协调器仍是唯一可变状态所有者；新协作者为纯函数/internal。

测试：表驱动覆盖首尾章节、空章节、连续空 Speech、恢复越界和映射回退。

### [ ] PLAY-504（P1）：抽取当前段执行与恢复策略

前置：PLAY-503。

实现：

- `PlaybackSegmentRunner` 负责当前段音频获取和本地播放调用。
- `PlaybackRecoveryPolicy` 负责损坏缓存重建、重试次数和连续失败暂停决策。
- 协调器提交状态和快照，runner/policy 不直接发布 UI 事件。

测试：缓存命中/未命中、空语音、音频损坏、失败阈值、401/429/5xx。

### [ ] PLAY-505（P1）：抽取预取、进度与会话资源

前置：PLAY-504。

实现：

- 预取 controller 拥有窗口、优先级、去重和 session Token。
- Progress service 统一保存/恢复时机。
- PlaybackSessionState 唯一拥有当前书/规则/位置/SessionId/CTS/文件保护句柄。
- 暂停最多预取一个、停止取消、换章隔离语义保持。

测试：预取迟到、并发去重、暂停/停止、保存失败和退出保存。

### [ ] PLAY-506（P0）：建立事件命令队列与安全关闭

前置：PLAY-505。

实现：

- NAudio 完成/失败/Snapshot 事件只投递内部命令，不在 `async void` 中执行长流程。
- 事件命令复用协调器串行入口和进程/session Token。
- 所有异常映射为状态或安全日志，不形成未观察异常。
- Dispose 阻止新命令、保存进度、取消会话并释放播放器。

测试：事件依赖抛错、Dispose 竞态、重复完成、完成后立即跳章。

### [ ] PLAY-507（P1）：收敛播放公共接口和删除过渡代码

前置：PLAY-506。

实现：

- 按 UI/应用实际命令拆窄接口或保留单 facade，删除只为旧大接口产生的重复 fake。
- 若全仓无产品调用，删除 `SkipCurrentSegmentAsync`、`CanSkip` 和对应内部分支；更新状态机测试与文档。
- 删除旧 namespace、默认实现、可选主线依赖和迁移 adapter。

验收：播放 facade 职责明确；无功能行为回退；全量长稳测试通过。

---

## Wave 6：App 导航、feature slice 与 ViewModel

### [ ] APP-601（P1）：建立强类型路由和 Page activation 协议

前置：UI-003、ARCH-101、PLAY-502。

实现：

- 定义 App route ID、强类型参数和 navigator；ViewModel 不引用 Page 类型。
- Wpf.Ui Page 映射集中到 Shell adapter，移除 MainWindow 的分散反射选中逻辑。
- Page 进入创建 activation CTS/version，离开取消并注销事件/guard。
- 明确播放会话不随 Page activation 取消。

测试：所有路由、快速重入、旧结果晚到、守卫与窗口关闭。

### [ ] APP-602（P1）：按 feature slice 移动 App 文件

前置：APP-601。

实现：

- 按 `06` 目标结构移动 Library、BookDetails、Playback、三类 Rules、Cache、Settings、Appearance、Diagnostics。
- 每个 feature 就近放 Page、ViewModel、组件、presentation service 和 DI module。
- Shared 只保留两个以上稳定消费者。
- 本任务只移动/改 namespace/注册，不拆 ViewModel 行为。

验收：导航、XAML pack URI、资源、截图和 UI 测试全部通过；全局 Pages/Views/ViewModels 旧目录清空删除。

### [ ] APP-603（P1）：收敛 Page + 同名 View 一对一 wrapper

前置：APP-602。

实现：

- 导航目标直接使用 Page。
- 只保留 BookCard、BookCover、歌词正文、播放控制等可复用或独立视觉行为组件。
- 合并页面 XAML/code-behind 时保留 activation、虚拟化、滚动和独立测试能力。

验收：无纯 DataContext 转发 wrapper；视觉树、导航生命周期和页面高度测试通过。

### [ ] APP-604（P1）：统一平台适配并清除 ViewModel 视觉类型

前置：APP-602。

实现：

- 统一文件打开/保存、剪贴板、目录打开、Dispatcher/UiScheduler 等 presentation port。
- TTS 规则页不直接 new Open/SaveFileDialog 或访问 Clipboard。
- ViewModel 以语义状态替代 `FontWeight`、`SymbolRegular` 等类型；XAML 映射视觉。
- 页面事件使用 activation/operation Token，不再散落无理由 `CancellationToken.None`。

验收：架构测试禁止回退；平台 adapter 可独立测试。

### [ ] APP-605（P1）：拆分 Shell/MainWindow 职责

前置：APP-601、APP-602。

实现：

- 抽取 Shell activation/navigation coordinator、shortcut context resolver 和必要的平台模板适配。
- MainWindow code-behind 只连接 WPF 生命周期、控件事件和视觉适配。
- 保留全局快捷键仲裁、临时界面优先级、Pane 和正在播放入口行为。

测试：文本输入/Popup/Dialog 不误触快捷键，导航选中和播放入口一致。

### [ ] APP-606（P1）：拆分 PlayerViewModel 与 Player View 行为

前置：PLAY-507、APP-602。

实现：

- PlayerViewModel 保留页面门面和命令。
- 抽取 ContentProjection、SnapshotProjection、RulesAndSpeedController。
- 滚动输入、动画和视觉树定位留在 Scrolling/View；状态恢复使用可控时间。
- PlayerView code-behind 按滚动、进度提交、焦点提交等职责拆小型内部组件。

测试按 Navigation、Commands、Projection、RulesAndSpeed、AutoCentering、VisualLayout、Accessibility 拆分。

### [ ] APP-607（P1）：收敛三套规则编辑会话

前置：TTS-302、TEXT-202、APP-602。

实现：

- 先固定 TTS/章节/正则在新建、选择、dirty、save/discard/cancel、fallback selection 上的差异。
- 提取小型 `EditorSession<TId,TEditor>` 或等价内部组件，只管理编辑基线/脏状态/离开决策。
- 领域校验、字段级保存、试听和默认规则行为留在各 feature。

验收：减少重复但不形成万能规则框架；所有未保存保护路径通过。

---

## Wave 7：Bootstrap、DI 与进程生命周期

### [ ] BOOT-701（P1）：模块化组合根并清除 Infrastructure 用例注册

前置：Wave 2–6 对应用例迁移完成。

实现：

- `AddNovelSpeakerApplication()` 注册所有用例。
- Infrastructure 按 Persistence、Storage、Speech、Audio、Diagnostics 注册 adapter。
- Desktop 按 feature 注册 Page/VM/presentation service。
- 根方法只组合模块；测试/Debug 启用 ValidateOnBuild/ValidateScopes。
- 删除 Infrastructure 中已经迁出的用例类和旧注册。

验收：Application 项目移除 `Microsoft.Data.Sqlite.Core`，删除 Application 的 `ISqliteConnectionFactory`；架构测试零例外。

### [ ] BOOT-702（P1）：实现可测试的启动协调器

前置：SETTINGS-104、BOOK-204、BOOT-701。

实现：

- 将目录、设置、日志、DI、数据库/恢复、主题、Shell 阶段移入 Bootstrap coordinator。
- 引入进程级 CTS 和安全阶段结果。
- App.xaml.cs 只桥接 WPF OnStartup/OnExit/异常事件。
- 启动失败在 Shell 前使用最小安全反馈，且不泄露路径/凭据。

测试：每阶段失败、取消、恢复失败、主题失败降级和设置只加载一次。

### [ ] BOOT-703（P1）：登记后台任务并实现异步关闭

前置：BOOT-702、PLAY-506。

实现：

- 缓存维护等进程任务由 lifecycle registry 拥有，不再裸 fire-and-forget。
- 关闭顺序：阻止新操作 → guard → 保存/结束播放 → 取消进程 Token → 限时等待任务 → 刷新日志/设置 → 释放容器。
- WPF 同步退出只保留有限桥接，不在 Dispatcher 无界等待。

测试：后台失败、关闭超时、播放保存失败、重复退出和资源释放顺序。

---

## Wave 8：测试项目与确定性

### [ ] TEST-801（P1）：拆分五类测试项目

前置：BOOT-701，生产边界已稳定。

实现：

- 建立 Domain.UnitTests、Application.UnitTests、Infrastructure.IntegrationTests、App.PresentationTests、App.WpfTests。
- 移动测试和 TestAssets，更新 Solution、包锁和 CI。
- 纯项目不引用 App/WPF；只有 WPF 项目使用 STA Host 并串行。
- Infrastructure 集成测试按需要标记 Windows 依赖，不让所有核心测试被平台绑死。

验收：每个项目可单独运行；全量覆盖数量/关键场景不减少；锁文件保留 win-x64 目标。

### [ ] TEST-802（P2）：拆分超大测试并统一 TestKit

前置：TEST-801。

实现：

- 按行为拆 PlayerViewTests、PlayerViewModelTests、PlaybackCoordinatorTests。
- 统一视觉树 helper、手动时间、临时目录、本地 HTTP server 和窄端口 fake。
- 不建立知道所有产品接口的万能 fixture。

验收：重复 helper/fake 明显减少；测试名称能直接定位行为；失败输出安全状态。

### [ ] TEST-803（P1）：消除任意延迟与全局串行

前置：TEST-801、TEST-802。

实现：

- 用 TimeProvider、TaskCompletionSource、事件/状态版本替换任意 `Task.Delay`/`Thread.Sleep`。
- 纯测试恢复并行；只对共享 Dispatcher/设备/数据库 fixture 串行。
- 所有异步等待有测试超时。

验收：重复运行稳定；测试耗时和 flaky 率有前后记录。

### [ ] TEST-804（P1）：更新 CI/发布矩阵

前置：TEST-801、TEST-803。

实现：

- CI 严格按 locked restore → format → Release build → test。
- 按纯测试、Infrastructure、WPF 合理拆 job 或顺序，保留失败可定位性。
- Release 使用相同门禁并验证 publish 不含 TestAssets。

验收：main/PR/release 行为一致；不发生隐式无 RID restore。

---

## Wave 9：清理、文档收口与发布级验证

### [ ] CLEAN-901（P1）：全仓 cleanup-refactor 复审

前置：Wave 1–8 完成。

审计并处理：

- 旧命名空间、空目录、迁移 adapter、默认 no-op、可选主线依赖。
- 无引用私有代码、重复 DTO、重复缓存键、重复 mapper/validator。
- 一次性 wrapper、Page/View 双层残留和 DI 转发。
- 过度嵌套、通用 catch、同步阻塞异步、无所有者 Task。
- `New/V2/Old/Compat/Helper/Manager` 可疑命名。

每项继续按 A/B/C/D 分类；migration、fixture、安全边界和真实平台 adapter 不得误删。

### [ ] DOC-902（P1）：按最终实现复核全部文档

前置：CLEAN-901。

范围：README、AGENTS、docs/00–12 和 TASK_BACKLOG。

验收：

- 数字文档只描述最终行为和架构，无历史 Epic、未来实现误报或临时类名。
- 代码目录、项目依赖、schema、TTS 矩阵、播放/缓存和测试命令一致。
- Cookie/LoginInfo、SecretStore、签名和兼容风险表述一致。
- 已完成架构任务归档，当前 backlog 只保留真正未完成项。

### [ ] VERIFY-903（P0）：完成发布级自动与手动验证

前置：DOC-902。

自动：完整质量门禁、架构测试、故障恢复测试、长稳播放和 publish 内容检查。

手动：

- 干净 Windows 10/11 用户目录启动。
- 导入不同编码 TXT、低置信度选择和重复拒绝。
- 导入/编辑/试听支持的 TTS 规则，拒绝 Cookie/LoginInfo 依赖规则。
- 播放、暂停、跳章/段、规则/语速/正则变化、返回书库继续、换书、重启恢复。
- 缓存命中、按章/书/全部清理和 LRU。
- 所有未保存保护路径、深浅主题、键盘和基础可访问性。
- 模拟中断后 operation journal 恢复，验证根外文件不受影响。

完成条件：所有阻塞问题关闭；非阻塞风险写入已知限制；不得用“文档已更新”代替运行验证。

## 5. 每个 AI 任务的交付模板

后续分派任务时要求使用以下交付结构：

```md
任务：<ID 与名称>
前置：<已完成依赖>
阅读：<设计文档、生产文件、测试文件>
允许修改：<目录/合同>
不可改变：<行为、schema、缓存键、安全边界>

实现步骤：
1. 先补/确认特征测试。
2. 建立目标实现。
3. 迁移调用者与直接测试。
4. 删除旧入口和临时适配。
5. 运行任务级测试与质量门禁。

交付：
- 修改内容与设计理由。
- 新增/更新测试。
- 实际执行的命令与结果。
- 未执行检查、剩余风险和后续依赖。
```

## 6. 全局完成标准

- Product 四项目依赖和职责符合 `02_TECH_STACK_AND_ARCHITECTURE.md`。
- Application 不引用 SQLite/Jint/NAudio/WPF/Wpf.Ui/Infrastructure。
- Infrastructure 不再承载业务用例、页面 Workspace 或播放状态机。
- Domain 不再是 HTTP/SQLite/UI DTO 仓库。
- App 按 feature slice 组织，Page activation、导航守卫和取消统一。
- 播放协调器、PlayerViewModel、规则工作台、Book/Cache/TTS 服务职责可解释且有唯一状态所有者。
- 跨数据库/文件操作可恢复，持久化路径受根目录约束。
- 五类测试项目职责清晰，纯测试可并行且不依赖 WPF。
- 旧实现、重复 DTO、迁移 adapter 和测试 fixture 发布残留已清除。
- 全量门禁、长稳和发布包验证通过；现有用户数据、缓存键与产品行为兼容。
