# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **v0.8.0 后的统一批量管理 + 通用 Book/Source 数据模型阶段**。

规划代码基线：`7b17e4c51d566b0640b49a33c426a61c4a6eaafa`（`main`，v0.8.0）。

上一轮 Backlog 已全部完成，本文件清空旧任务后重新开始编号。当前阶段只实现已经确认的两组长期方向：

1. 把 Library、Playback 章节、Speech Provider、Rules 的批量操作收敛为统一的页面级 Management Mode；CacheManagement 保持文件管理器式选择例外。
2. 把当前“Book 直接拥有 Local TXT/Chapters”的模型迁移为 `Book → Sources → Source-owned Catalog/Content`，当前只实现 Local Source，不实现 Online Source 具体能力。

本阶段明确不做：

- Legado/在线书源规则、目录抓取、正文请求、登录、变量系统；
- 自动 Source fallback；
- 跨 Source Chapter Identity 或模糊章节匹配；
- 高级在线正文缓存管理；
- WebDAV/云同步；
- 新一轮 Diagnostics 扩张；
- 与本阶段无关的大规模 UI/架构重写。

长期合同：

- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，只记录真正需要用户决定的新产品/架构/隐私冲突；普通实现细节由 Codex 自行决定

默认按 T001 → T002 → T003 → T009 → T010 → T011 → T012 → T013 → T014 → T015 → T004 → T005 → T006 → T007 → T008 串行执行。如果调用明确要求连续执行整个 Backlog，可以按依赖顺序自动继续，不等待人工验收。

人工视觉/交互验收始终是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的强制自动验收；
2. 删除所有当前任务临时测试、fixture、脚本、截图和诊断产物；
3. 更新本文件状态并追加简短“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 按调用要求停止或继续下一任务，不等待人工验收。

## 3. Staged breaking migration window

T004 → T007 属于一次明确授权的 **staged breaking migration window**。

在这个窗口中：

- 不要求每个任务结束时整个应用都可运行；
- 不要求每个任务都通过完整 solution build/test；
- task spec 必须明确当前切片允许暂时失效的调用方或测试；
- 当前任务仍必须完成自己的 focused verification；
- 不为维持中间态可运行而建立旧/新 Book 模型双读、双写或 compatibility wrapper；
- T008 必须恢复完整标准门禁，阶段才算完成。

T001 → T003 不属于 breaking migration，原则上应保持仓库正常可构建。

## 4. 已批准的 Book schema 变更边界

本轮用户已经明确批准为通用 Book/Source 模型调整数据库表、删除/调整旧表字段，并要求不长期兼容旧模型。T004 可以直接实施 `docs/05_DATA_AND_COMPATIBILITY.md` 列出的 v0.8.0 → Book/Source 持久化变更集合，不需要逐字段重复请示。

如果实现需要超出该文档已列集合的新持久化概念，才按 `AGENTS.md` 停止并请求授权。

自动迁移应保持简单。当前 v0.8.0 的应用内规范化 `Books/{BookId}/content.txt` 可以继续作为 Local Source 持久正文，不为目录美观搬迁。若真实实现证明自动迁移仍必须引入模糊章节匹配、寻找外部 TXT、长期双模型兼容或新的重型一次性恢复系统，则使用已批准 fallback：要求用户重新导入本地书籍，不实现复杂迁移。

---

# Phase A：统一页面级批量管理

## [x] T001（P0）：建立统一 Management Mode 选择基础设施

目标：在现有 `DesktopSelectionController` 稳定 key 选择能力之上建立页面级 Management Mode 生命周期、visible-set reconciliation、Select All、右键语义与 Normal/Management 行为隔离的共享 primitive；不把业务动作塞进 Shared。

完成成果：新增页面级 `ManagementSelectionController<TKey>`，组合现有 stable-key 选择引擎，提供 Enter/Exit/Reset、Normal/Management 点击分流、toggle/range、Select All、visible/manageable set reconciliation 与右键选择语义；支持零选择保持模式和增量选择装饰通知。Shared 仅拥有交互状态，业务动作与 Dirty Draft 保护由 Feature 承担；CacheManagement 保持原行为。新增 10 项长期行为测试，保留既有选择与页面测试；本任务没有需要删除的旧实现或兼容层，临时实施规格已删除。locked restore、format、Release build（零警告/错误）及全量 914 项测试全部通过（零跳过），无环境受限检查或长期文档冲突；业务页面接线由 T002/T003 完成。

## [x] T002（P1）：迁移 Library 与 Playback 章节批量管理

依赖：T001。

目标：Library 增加显式批量管理、Select All、批量导出/删除；Playback 把现有“主动缓存选择模式”改造成通用章节 Management Mode，当前批量动作仍只有 Cache，并保持 Active Cache coordinator 只负责任务执行。

完成成果：Library 接入页面级 Management Mode、全选、单次确认的逐书删除与单次目录选择的批量 UTF-8 正文导出；文件名复用安全规范，冲突确定性加后缀且不覆盖，缺少完整正文跳过。Playback 删除 Active Cache 专用选择命名，接入通用章节管理与右键/全选，页面拥有选择、coordinator 继续拥有缓存批次；全部命中缓存的章节报告 skipped，缺失音频继续补齐，单章失败继续后续章节并汇总。CacheManagement 保留 Extended Selection。新增 7 项核心行为测试并扩展既有失败场景，保留并适配已有页面、缓存与隔离 WPF 测试，合并重复 selection 样式；没有临时验证产物。locked restore、format、Release build（零警告/错误）和完整 922 项测试通过（零跳过）；先前全量运行出现旧数据库临时文件删除占用，重跑全量通过，原错误已记录。无未执行的强制检查或长期文档冲突；临时实施规格已删除。

## [x] T003（P1）：迁移 Speech Provider 与 Rules 批量管理

依赖：T001。

目标：把 Provider 与四类 Rule workspace 从“普通选择直接兼任多选”的现状迁移到 Normal Mode + Management Mode；保留 Dirty Draft 保护、批量导出/删除、部分跳过和 CurrentProvider 删除后变 None 的既有产品语义。

完成成果：Provider、Chapter/Regex/File Name/Text Header Rules 接入各自页面级 Management Mode，显式入口与 Ctrl/Shift 共用原有草稿保存/放弃/取消保护；管理点击只改变选择，Header 与右键共用批量导出/删除，全选仅覆盖可见项，隐藏与删除后主动收敛选择。正式 exchange schema 与 Provider typed config 保持；内置 Provider 可选且执行时跳过并汇总，HTTP 凭据提示保留；逐项删除一次确认、失败继续，CurrentProvider 删除后为 None，删除编辑规则关闭编辑器且不新增 fallback；页面取消后仍完成已提交 Regex 删除的播放运行态同步，异步导出结果使用稳定快照。移除普通选择兼任批量选择的旧接线，没有兼容层或临时产物。新增/扩展 19 项核心行为用例，保留并适配既有编辑、exchange、架构与隔离 WPF 测试；旧 WPF 编辑按钮检查限定到编辑区，布局 fixture 补齐新的正常模式排序能力。locked restore、format、Release build（零警告/错误）与完整 941 项测试通过（零跳过）；此前旧数据库测试清理时报 IOException（app.db 被其他进程占用，TemporaryDirectory.Dispose/Directory.Delete），重跑完整门禁测试通过。无环境受限的强制检查或长期文档冲突；临时实施规格已删除。

---

## [x] T009（P1）：批量管理 Header 图标与布局修复

依赖：T003；在 T004 前完成。

目标：统一 Library、Playback、Speech Provider 和四类 Rule workspace 的 Header 图标、操作顺序与单行布局，保留既有批量行为。

完成成果：Library、Playback、Speech Provider 与四类 Rule workspace 的 Header 批量管理、全选、导出、删除、退出统一为主题图标按钮，补齐 Tooltip/Automation Name，并按普通/管理模式调整操作顺序、8/12 DIP 间距与单行对齐；Library 搜索/排序固定 180/110 DIP，管理入口位于导入右侧。Playback 工具栏接入 AppPageHeader.Actions，缓存状态移到 Header 下方独立提示行，空提示折叠，保留语速与定时停止原有内容；元数据规则普通操作也改为图标。移除旧文本批量按钮、Header WrapPanel 与播放管理双行按钮布局，没有新增公共 API、兼容层或持久化变化，既有命令/事件接线与业务 owner 保持。保留全部核心测试，仅适配播放页图标/提示位置及章节规则普通工具栏检查；无新增永久布局测试。临时隔离 WPF 验证覆盖 128 个 Light/Dark、最小/常用内容宽度、普通/管理模式、零/非零选择与长缓存提示组合，检查操作顺序、同排、无重叠裁切、尺寸、禁用条件、无障碍名称和命令绑定，验证代码及失败诊断产物已清理。locked restore、format verify、Release build（零警告/错误）与完整 941 项测试（零跳过）通过；完整测试最终使用 `dotnet test -c Release --no-build -m:1` 串行执行测试项目。此前默认并行全量出现既有数据库测试清理 IOException：`app.db` 被其他进程占用，位于 TemporaryDirectory.Dispose/Directory.Delete，涉及 Version_7_rules_migrate_to_providers_and_reconcile_current_selection、Failed_settings_reconciliation_retries_after_database_migration_commits、Foreground_playback_retries_when_the_prefetch_owner_of_shared_audio_is_cancelled；首项 focused 复测通过，最终完整串行套件全部通过。既有数据库清理偶发占用仍为已知风险，本任务未修改持久化代码、删除核心测试或放宽隔离。无环境受限的未执行检查或长期文档冲突；临时实施规格已删除。

---

## [x] T010（P1）：SQLite 资源生命周期与测试清理占用修复

依赖：T009；在 T004 前完成。

目标：定位并修复临时数据库删除占用的资源释放原因，恢复默认并行完整测试的稳定通过，不改变 schema、SQL 或用户数据。

完成成果：定位到未显式释放的 SqliteCommand 经连接弱引用与延迟终结造成底层 SQLite statement 保留文件句柄，复现迁移完成后独占访问 app.db 的 IOException；为生产持久化代码 76 处及测试夹具 75 处命令补齐 using，并释放连接打开取消路径中的连接，消除依赖命令终结器的旧释放方式。SQL、已发布 migration、schema、用户数据与连接池策略保持不变，没有兼容层、新迁移、清理重试、固定等待、生产 GC 或测试串行化。新增成功/失败迁移后立即独占访问数据库的两项核心回归；成功场景修复前稳定失败，修复后两项通过，保留全部既有核心测试，未删除或弱化测试。Infrastructure 343 项通过；独立工作树 locked restore、format verify、Release build（零警告/错误）及默认并行完整 943 项连续三轮通过（零跳过、无数据库清理占用），工作树已清理。包含 T011 的主工作区完整门禁最终通过，默认并行完整 945 项全部通过；首次主工作区全量曾出现 PlayerView_virtualized_target_moves_toward_center_without_direction_reversal 居中断言失败（PlayerViewAnimationTests.cs:367，偏差 176.333，预期 0–1），并伴随 WpfDispatcher collection cleanup failure，针对性及全量复测通过，本任务未修改该用例，仍记录为独立的 WPF 动画偶发风险。临时实施规格已删除，无环境受限的未执行检查或长期文档冲突。

---

## [x] T011（P1）：批量选择提示主题修复与通用防复发门禁

用户追加修复；在 T004 前执行。修复书库、Provider、Rules 暗色冷启动选择提示前景色，并建立防复发约束与主题回归验证。

完成成果：共享 AppPageHeader 提供动态主题前景色，Library、Speech Provider、Chapter/Regex/File Name/Text Header Rules 的选择提示使用既有显式文字样式；补齐元数据规则编辑区三处同类遗漏，下拉框字符串模板显式绑定 ContentPresenter 的主题前景色，保留禁用/交互颜色。移除对 WPF 默认黑色和隐式祖先颜色的依赖，没有新增主题 owner、兼容层或持久化变化。AGENTS.md 与 UI 合同补充全局文字资源规则和冷启动/切换/切换后新建页面验收；新增两项通用架构测试，扫描全部产品 XAML 的 TextBlock、样式继承与前景色定义，拒绝遗漏主题前景、StaticResource 画刷和硬编码可见颜色，无页面白名单。既有核心测试全部保留；按用户要求，页面专用验证只作临时测试，验证后连同诊断产物清理。隔离 Desktop 临时验证先在旧实现复现书库暗色文字对比度 1.18，修复后覆盖六个业务页面与共享页头的暗色启动、Light/Dark 切换及切换后新建页面，并验证 Standard/Compact 下拉框正常/禁用颜色跟随宿主，全部通过。locked restore、format verify、Release build（零警告/错误）、完整 WPF 97 项与 Presentation 272 项测试通过（零跳过），git diff --check 通过；未执行与本次 UI/架构变更无关的完整 solution 测试，无环境受限检查或长期文档冲突。静态门禁不替代任意运行时绑定和 C# 动态创建文字的主题验收；后者已纳入开发约束。临时实施规格已删除。

---

## [x] T012（P1）：书库 Snackbar 通知与项目 Review 检查清单

用户追加修复；在 T004 前执行。书库导出、删除结果复用已有 Snackbar，删除页内结果通知块；保留导入进度分流，增加独立可选项目检查清单与每轮 Review 核对约束。

完成成果：书库单书/批量导出及批量删除结果通过现有 IAppFeedbackService 接入 Snackbar，全部成功使用 Success、存在跳过/失败使用 Warning；删除对象已不存在时刷新后通知，异常仅投影通知，取消不发送完成汇总。删除 LibraryViewModel.StatusMessage、页内结果通知块及 WPF 夹具字段；保留 ImportStatusMessage、5 MiB 导入分流和导入生命周期，没有新增通知宿主、兼容层、公共接口或持久化变化。AGENTS.md 与 UI 合同明确瞬时通知与进度/校验/空状态职责；新增独立 .codex/review-checklist.md 的主题前景色与 Snackbar 两项，通用 skill/指引接入被审查仓库可选清单，覆盖首审、复审、替换 reviewer、缺省及不可读取场景，并要求逐项结论与依据。扩展既有核心导出/删除/取消测试，新增单书导出、取消目录选择及删除对象不存在/失败行为覆盖，合计增加 13 个测试用例；保留全部导入、反馈和主题架构测试，没有永久布局测试。locked restore、format verify、Release build（零警告/错误）、完整 Presentation 285 项、相关 Library/Feedback WPF 6 项通过（零跳过），skill 校验和 git diff --check 通过；清单接入规则已静态核对，两项清单核对均通过。隔离 Desktop 临时主题验证覆盖 Light/Dark 首次创建、已有页面切换及切换后新建页面，两项通过，验证代码已删除；首次脱离窗口的验证发生颜色资源不一致，改为隔离 Desktop 真实窗口宿主后通过，生产主题代码未修改。临时规格已删除，无遗留临时诊断产物、环境受限检查或长期合同冲突；按用户要求未提交及未执行远端操作，未推进 T004。未运行与本次 App 修改无关的其余 solution 测试项目。

---

## [x] T013（P1）：移除小文件导入页内进度

用户追加调整；在 T004 前执行。小于 5 MiB 的导入不展示进度，仅通过已有 Snackbar 反馈结果；大于等于 5 MiB 保留可取消进度弹窗。

完成成果：小于 5 MiB 的导入直接执行，不展示进度或发送进行中通知，仅用已有 Snackbar 反馈成功/失败；大于等于 5 MiB 保留可取消进度弹窗。删除页内导入块、ImportStatusMessage、ApplyImportProgress、测试夹具字段及 ILibraryImportCoordinator 的 inlineProgress 参数，所有调用方直接适配，没有兼容接口或持久化变化；保留编码选择、替换/离页取消、迟到结果抑制、刷新及 IsBusy 生命周期。同步 AGENTS.md、UI 合同与项目 Review 清单，T012 成果保留为历史记录。保留全部核心测试，扩展既有导入分流测试覆盖阈值两侧及精确边界（增加 2 个用例），现有导入测试核对唯一结果通知与取消，无新增永久布局测试。format verify、Release build（零警告/错误）、完整 Presentation 287 项、相关 Library/Feedback WPF 6 项与 git diff --check 全部通过（零跳过）；隔离 Desktop 临时主题验证覆盖 Light/Dark 首次创建、切换及切换后新建页面，两项通过，临时代码已清理。首次 Presentation 运行因临时文件的零参数 Show 调用触发共享窗口宿主静态检查，清理临时文件后完整复测通过，未弱化门禁；主题前景色与 Snackbar 两项清单核对通过。临时规格已删除，无环境受限检查、遗留临时产物或长期合同冲突；依赖未变未重复 restore，未运行其余无关 solution 测试项目，保留此前全部未提交修改，未提交或执行远端操作，未推进 T004。

---

## [x] T014（P1）：补齐书库与元数据规则多选 ESC 退出

用户追加修复；在 T004 前执行。修复书库进入批量管理后 ESC 无法退出的问题，并补齐文件名/正文头部元数据规则页的同类遗漏。

完成成果：LibraryViewModel 与 MetadataRuleWorkbenchViewModel 接入已有 ITransientEscapeHandler，复用各自页面级选择控制器的 Exit；零选择及非零选择均可退出并清空批量选择，元数据规则恢复正常编辑项装饰并保留编辑内容，普通模式不消费 ESC，继续由现有 Shell 快捷键策略处理返回。没有新增键盘路由、状态 owner、兼容层或持久化变化，也没有需要删除的旧实现。扩展已有书库管理及两类元数据规则草稿/管理核心行为测试，覆盖接口接线、零/非零选择退出、普通模式与编辑内容保留；全部既有核心测试保留，没有新增测试用例或临时产物。format verify、Release build（零警告/错误）、完整 Presentation 287 项、相关 Library/ShortcutContext 隔离 WPF 6 项及 git diff --check 通过，零跳过。项目清单 NS-01 不适用（无文字/图标/主题变更），NS-02 不适用（无结果通知、进度或反馈路径变更）。依赖未变未重复 restore，未运行其余无关 solution 测试项目；无环境受限检查或长期合同冲突，未提交或执行远端操作，未推进 T004。

---

## [x] T015（P1）：以当前导航页面统一 ESC 路由

用户追加修复；在 T004 前执行。处理书架 Ctrl/Shift 多选及页头管理入口的焦点依赖问题，统一当前页面 ESC 消费、瞬时 surface、文本编辑与返回的优先级。

完成成果：定位到书卡 Ctrl/Shift PreviewMouseLeftButtonDown 提前处理事件，焦点保留在导航区域或搜索框；页头管理按钮隐藏后也会失去页面内焦点，旧焦点祖先查找与文本编辑保护使 ESC 无法到达已补齐的 ViewModel。Shell 每次按键从当前导航内容取得 ITransientEscapeHandler，由八个现有业务页面桥接到原有 ViewModel，统一“瞬时 surface → 当前页面局部交互 → 文本编辑保护 → 返回”优先级；普通文本编辑和其它快捷键保持原策略，CacheManagement 继续 Extended Selection。删除焦点 DataContext 祖先消费者查找以及 PlayerView/CacheManagementPage 的独立 ESC 分支，没有新增 registry、页面缓存、选择状态 owner、兼容层、强制焦点转移或持久化变化。导航及批量管理长期合同同步补充规则。新增八个快捷键优先级核心用例和一个真实 MainWindow 页面切换/返回核心用例，重写已有焦点祖先用例为当前页面独立于焦点及旧 DataContext 的合同，扩展既有组合根测试覆盖八个页面 ESC 边界；其余核心测试全部保留。临时隔离 Desktop 验证真实书架页头入口、Ctrl/Shift 与导航项/搜索框焦点四种组合、书卡焦点及零/非零选择退出全部通过，验证代码与诊断产物已清理。locked restore、format verify、Release build（零警告/错误）、默认并行完整 969 项测试（零跳过）及 git diff --check 通过。首轮完整运行新增 Shell 用例失败（MainWindowNavigationTests.cs:67，退出断言失败）；诊断发现测试在导航内容尚未加载时发出按键，改为等待实际 Loaded 事件后 focused 及完整门禁通过，不使用固定延时或弱化断言。清单 NS-01 不适用（无产品主题文字/图标变更），NS-02 不适用（无通知/反馈路径变更）。无环境受限的未执行检查或长期合同冲突；临时实施规格已删除，未提交或执行远端操作，未推进 T004。

---

# Phase B：通用 Book/Source 模型

## [ ] T004（P0）：建立 Book/Source 持久化模型并完成 v0.8.0 schema migration

依赖：T003。

目标：建立 Book、Source、Local Source typed persistence、Source-owned Catalog 的最终数据结构；追加新的 SQLite migration，把现有每本本地书转换为一个 Local Source，并保留 BookId、Chapter 技术 ID、ReadingProgress、Speech Plan 与可安全保留的音频缓存关系。删除旧 Book 上已经失去语义的 Local TXT 字段/约束。

详细规格：`tasks/T004_BOOK_SOURCE_SCHEMA_MIGRATION.md`

## [ ] T005（P0）：重构 Local TXT 导入与重新导入更新

依赖：T004。

目标：直接导入改为创建 `Book + LocalSource + Catalog`；使用严格 Title+Author 自动匹配，唯一候选时更新该 Book 的唯一 Local Source，多候选时要求用户选择目标 Book 或新建；更新采用完整 snapshot 原子替换，失败保留旧 Source。

详细规格：`tasks/T005_LOCAL_SOURCE_IMPORT_UPDATE.md`

## [ ] T006（P0）：把 Query、正文读取、Playback 与 ReadingProgress 接到 ActiveSource Catalog

依赖：T004、T005。

目标：移除运行时“Book 直接拥有 StoredFilePath/Chapters”的假设；Library/BookDetails/Player/Cache/Speech Plan 通过 ActiveSource Catalog 和 Source content port 工作；ReadingProgress 保持 Book 级并在 Catalog 更新时只做边界截断。

详细规格：`tasks/T006_ACTIVE_SOURCE_RUNTIME_INTEGRATION.md`

## [ ] T007（P1）：收口 Source/Book 生命周期并删除旧模型残留

依赖：T006。

目标：完成 Local Source 更新/删除、Book 删除、ActiveSource=None 安全处理、文件/SQLite/音频缓存/ReadingProgress/Speech Plan 协调；删除旧 SourceHash-based duplicate path、Book-owned content contract 和仅为旧 schema 存在的代码/测试。

详细规格：`tasks/T007_SOURCE_LIFECYCLE_AND_LEGACY_CLEANUP.md`

---

# Phase C：最终集成收口

## [ ] T008（P0）：恢复完整可运行状态并执行阶段级验收

依赖：T001–T007。

目标：完成跨模块接线、测试收敛、架构审计和文档一致性检查；确保没有旧/新 Book 模型双路径、没有遗漏的 active-cache selection 专名、没有隐藏 selected item 参与批量操作，最终执行完整标准门禁。

详细规格：`tasks/T008_INTEGRATION_CLOSURE.md`
