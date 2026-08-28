# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段围绕发布目录与数据根目录合同收口，继续完成 transient UI Single Surface 初版的自动调试，并把历史视觉截图资产从默认质量门禁中解耦。

本轮目标：

- 将正式运行数据根目录切换为程序目录下的 `Data/`，不保留旧 `%LocalAppData%/NovelSpeaker` 的迁移、探测或回退逻辑。
- 将默认开发运行数据隔离到 `%LocalAppData%/NovelSpeaker.Dev`，自动测试继续使用测试自身的临时目录。
- 将发布主程序从 `NovelSpeaker.App.exe` 统一改名为 `NovelSpeaker.exe`，并清除运行时代码对主程序文件名的硬编码。
- 保持现有 SQLite schema migration 体系；本轮“无兼容”只针对数据根目录切换。
- 调试并收口 ContentDialog、StartupStatusWindow 与 `AppStatusView` Embedded 模式的 Single Surface 初版。
- 收口朗读清单生命周期：启动缓存维护后不长期保留任何无缓存索引章节的 `ChapterSpeechPlans` / `ChapterSpeechPlanSegments`。
- 将 `artifacts/visual-review/` 降级为显式 UI 开发/验收时按需生成的本地临时资产，保留视觉工具能力但移除默认测试对历史 PNG/manifest/hash 的依赖。
- 完成发布产物、数据安全边界、文档一致性和完整自动质量门禁验收。

稳定产品、架构、数据、测试与视觉约束分别以数字编号文档为准。本文件描述当前实施顺序、任务状态和自动验收；Codex 完成的任务在下一次任务规划前继续保留，便于直接查看本轮成果。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成；任务末尾必须附简短“完成成果”。
- `[!]`：存在阻塞，必须在任务结果中记录可复现原因。
- `P0`：数据安全、运行隔离、发布产物或完整质量门禁。
- `P1`：结构清理、视觉收口和长期可维护性。

Codex 执行任务时不得删除已完成任务：完成后将状态改为 `[x]`，并在任务末尾记录主要实现、测试/门禁结果和必要的限制说明。

删除、重写或插入任务只发生在新的任务规划阶段：

- **插入任务**：保留现有任务、状态和成果，在合适依赖位置新增任务；必要时可顺延尚未完成任务编号并同步依赖。本次规划采用该方式。
- **删除/重写任务**：仅在新的规划明确要求清理或重新制定计划时使用；不建立 `archives/`，被替换历史由 Git 保存。
- 已完成任务不会因为 Codex 刚刚完成就自动消失；是否在后续规划中清理，由该次规划决定。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 执行前至少阅读：
   - `AGENTS.md`
   - `docs/02_TECH_STACK_AND_ARCHITECTURE.md`
   - `docs/05_DATA_AND_PERSISTENCE.md`
   - `docs/09_TESTING_AND_QUALITY.md`
   - `docs/10_ENGINEERING_CONVENTIONS.md`
   - `docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`
   - 当前任务直接涉及的生产代码和测试。
3. 不为旧 `%LocalAppData%/NovelSpeaker` 增加自动发现、复制、迁移提示、fallback、双读/双写或兼容 wrapper；发现现有相关入口时直接按目标合同清理。
4. 不删除、合并或重编号现有 SQLite schema migration；数据根目录切换与数据库 schema 升级是独立问题。
5. 数据根目录选择不得依赖 `#if DEBUG`。开发/诊断行为必须通过明确运行配置或显式数据根覆盖表达。
6. 自动测试不得读取或写入正式 `Data/`、`%LocalAppData%/NovelSpeaker.Dev` 或旧 `%LocalAppData%/NovelSpeaker`；测试必须显式拥有临时数据根。
7. 运行时代码不得硬编码 `NovelSpeaker.exe` 或 `NovelSpeaker.App.exe` 来寻找当前进程；需要当前 executable 路径时使用平台/运行时提供的实际进程路径。
8. 根 `README.md` 只描述当前已经实现的行为。涉及 README 的更新必须与对应实现任务一起完成，不能提前声明未落地能力。
9. 每个任务都优先补充/修改已有自动测试，不为了形式制造大量细粒度重复 case。
10. 默认测试不得设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；视觉产物生成仍运行在隔离隐藏 Desktop。
11. 用户要求提交时，按逻辑目的拆分原子提交，不把整个编号任务机械压成一个大提交。
12. 任务完成时将自身状态更新为 `[x]`，在该任务末尾增加 `完成成果：`，用 1–4 个短项记录主要改动与自动验收；不要自行删除任务、重排其它任务或创建归档。

## Phase A：数据根目录与开发隔离

## [x] T001（P0）：建立统一的数据根目录解析合同

目标：

- 建立唯一的数据根目录解析入口，明确正式运行、开发运行和显式覆盖三种路径来源。
- 将“数据根在哪里”与“根目录内部有哪些文件/子目录”拆成两个职责，避免目录 provider 同时承担环境判断和目录布局。

实施：

- 数据根 resolver 使用 `NOVELSPEAKER_DATA_ROOT` → Development `%LocalAppData%/NovelSpeaker.Dev` → 默认 `<base-directory>/Data` 的明确优先级。
- 不使用编译符号决定运行数据位置；Debug/Release 不改变根目录合同。
- 将旧 LocalAppData 专用 provider 重构为与具体宿主位置无关的应用数据目录 provider，并保留测试可注入根目录边界。
- 根目录内部稳定布局保持 `app.db`、`settings.json`、`Books/`、`Cache/`、`Operations/`、`Logs/`。

完成成果：

- 已引入 `AppDataRootResolver` 与通用 `AppDataDirectoryProvider`，统一数据根选择与内部目录布局职责。
- 已补充 resolver/provider 定向测试并迁移相关测试调用方；未引入旧数据目录兼容逻辑。

## [x] T002（P0）：切换生产 Bootstrap 并隔离默认开发运行

依赖：T001。

目标：

- 将 NovelSpeaker 正式运行切换到 `<application-directory>/Data`。
- 确保仓库默认 `dotnet run` / IDE 开发启动使用 `%LocalAppData%/NovelSpeaker.Dev`，不会污染正式便携数据。
- 清理旧 `%LocalAppData%/NovelSpeaker` 的生产依赖和兼容残留。

实施：

- 组合根使用统一 resolver 构建 `IAppDataDirectoryProvider`，不再直接依赖旧 LocalAppData provider。
- 默认开发 profile 通过 `NOVELSPEAKER_ENVIRONMENT=Development` 表达开发模式，不硬编码机器绝对路径。
- 正式数据目录按需创建，不要求发布包预置空 `Data/`。
- 保持路径逃逸/reparse-point 防护与 SQLite schema migration 合同不变。

完成成果：

- 已切换 Bootstrap 到统一数据根解析，并新增 `launchSettings.json` 开发 profile 隔离默认开发数据。
- 已增加 Launch Profile、Bootstrap 与数据根相关自动合同测试，并同步当前 README 的开发运行说明。

## Phase B：主程序命名与发布产物

## [x] T003（P0）：统一主程序为 `NovelSpeaker.exe`

目标：

- 将发布后的主程序文件名从 `NovelSpeaker.App.exe` 改为 `NovelSpeaker.exe`。
- 项目目录、项目名和命名空间保持现状，不为输出文件名做无关大规模重命名。
- 消除源码、测试和发布脚本对旧 executable 名称的硬编码。

实施：

- 在 `NovelSpeaker.App.csproj` 通过正式项目属性统一 assembly/output 名为 `NovelSpeaker`，不要在 publish 后执行临时文件重命名。
- 审计托盘、图标、进程路径、启动/诊断等所有需要 executable path 的代码；需要当前进程路径时改用 `Environment.ProcessPath` 或同等可靠运行时入口，不把硬编码从旧名称替换成新名称。
- 更新 `.github/workflows/release.yml` 的发布包内容校验，使其要求 `NovelSpeaker.exe` 且拒绝旧 `NovelSpeaker.App.exe`。
- 保持发布 ZIP 的既有版本化文件名合同，除非源码中已有与主程序名直接冲突的错误逻辑。
- 实现完成后同步根 `README.md` 的启动文件名和数据目录说明，使 README 与实际发布行为一致。

自动测试/检查：

- 增加或调整项目/发布合同测试，确认 publish 输出包含 `NovelSpeaker.exe`。
- 确认 publish 输出不包含 `NovelSpeaker.App.exe`。
- `rg` 确认生产源码、测试、workflow 和当前 README 不再把旧 executable 名称作为运行合同。
- 托盘/图标路径相关测试不依赖硬编码文件名。

验收：

- self-contained `win-x64` publish 成功。
- 自动包内容检查通过：主程序、LICENSE、THIRD-PARTY-NOTICES 和既有运行时依赖完整，且不包含测试/开发资产。
- 受影响项目 build/test 通过。

完成成果：

- 已通过项目 `AssemblyName` 统一发布输出为 `NovelSpeaker.exe`，并将托盘图标解析切换为实际当前进程路径。
- 已更新发布包校验、README 与托盘路径合同测试，发布目录及 ZIP 均拒绝旧 executable 名称。
- 已执行 T003 定向 build/test 与 self-contained `win-x64` publish 检查；详细结果见交付说明。

## Phase C：Transient UI 自动调试收口

## [x] T004（P1）：调试并收口 transient UI Single Surface 初版

目标：

- 对当前 ContentDialog、StartupStatusWindow 和 `AppStatusView` Embedded 模式的初版实现进行自动调试、修复和代码审阅。
- 保证去除 Card-in-Dialog / Card-in-Window 后，不引入布局裁切、主题失配、Focus/Automation 回归或新的重复 Surface。
- 不把人工视觉验收作为任务关闭条件；如需要截图，只能通过现有隐藏 Desktop 视觉产物宿主生成。

实施：

- 复查 `AppDialogVisuals.CreateBody(...)`、`App.Feedback.DialogBody` 及所有调用点，确认 ContentDialog 内容容器保持透明、无边框、无阴影、无额外 Surface Padding，同时保留稳定的 MinWidth/MaxWidth 和现有按钮语义。
- 复查删除书籍、普通确认、未保存修改、编码选择和导入进度 Dialog，在长中文/英文、输入控件、CheckBox、进度条和 Danger 主按钮场景下都保持合理布局；不得为单个 Dialog 重新引入专属 Card。
- 复查 `AppStatusView.IsEmbedded` 的模板触发器，确认 Embedded 只移除自身 Section chrome，不改变图标状态、Title、Description、Action、Automation 或状态颜色语义；默认非 Embedded 状态仍保持原有 Section Surface。
- 复查 `StartupStatusWindow` 的单 Surface 结构、Light/Dark、Loading/Error、100%/125%/150% DPI 和紧凑尺寸；窗口自身是唯一 Raised Surface，内部不得出现第二层完整卡片。
- 复查 Style Gallery 的 `surfaces` 与 `feedback` 场景，删除已失效的旧 Dialog Surface 展示，并让 Dialog 示例表达“host surface + flat body”的最终结构。
- 保留 Flyout/Popup/Snackbar 当前唯一 Surface 边界，不为了统一结构做无关重构。
- 增补或修正现有 WPF 契约测试，优先在已有聚合测试中覆盖 Single Surface、DialogBody flat chrome、Embedded 状态和 Startup 布局。
- 执行代码审阅，检查资源键唯一性、旧键零引用、Theme DynamicResource、命名、可访问性和窗口测试隔离边界；发现问题直接修复。

验收：

- `rg` 证明稳定源码、测试和稳定设计文档中不存在 `App.Surface.DialogContent`、`App.Feedback.DialogContent`、`AppDialogVisuals.Wrap` 或旧 `StartupSurface` 引用。
- ContentDialog 自动契约证明 Body 无可见 chrome，StartupStatusWindow 自动契约证明 Embedded 状态没有第二层 Section Surface。
- 默认测试不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；视觉产物如需生成仍运行在隐藏 Desktop。
- WPF/Presentation 定向测试通过。

完成成果：

- 已保留 ContentDialog、AppStatusView Embedded、StartupStatusWindow 与 Style Gallery 的 Single Surface 契约，并修复默认 WPF 测试错误读取被忽略视觉产物的问题。
- 已让视觉 manifest 校验仅在显式 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 时运行，默认测试不依赖本地 PNG 或子 manifest。
- 已执行 T004 定向及完整 WPF/Presentation 测试；详细结果见交付说明。

## Phase D：朗读清单持久化收口

## [x] T005（P0）：清理无缓存索引的残留章节朗读清单

目标：

- 避免仅加载/生成过朗读计划但从未留下音频缓存的章节长期占用 `ChapterSpeechPlans` 与 `ChapterSpeechPlanSegments`。
- 保持现有“删除最后一条缓存时同步删除计划”的即时回收机制，同时增加启动维护的最终兜底收敛。
- 不在计划提交到音频缓存落盘之间的活动窗口执行即时孤立计划删除，避免播放、预取、主动缓存或导出产生竞态。

实施：

- 为 SQLite 朗读清单 store/repository 增加集合式清理能力，删除所有不存在任何 `AudioCacheEntries` 的 `ChapterSpeechPlans`；优先使用单条集合 SQL / 等价常数级数据库操作，不逐章查询。
- `ChapterSpeechPlanSegments` 继续依靠既有 `ON DELETE CASCADE` 回收，不新增第二套手工段删除流程。
- 将该清理接入启动缓存维护流程，并明确顺序：先完成缺失/损坏缓存索引修复及容量/LRU 淘汰，再清理最终无任何缓存索引的残留计划。
- 保持现有缓存删除事务语义：某章最后一条缓存索引在运行时被删除时仍立即同步删除计划，不等待下一次启动。
- 只要章节仍存在任意缓存索引（包括旧合成配置或当前受保护条目），启动维护不得删除其朗读清单。
- 不读取章节正文、不执行正则、不重新生成朗读清单，也不为了判断孤立状态扫描音频目录；孤立判断只基于 SQLite `AudioCacheEntries`。
- 清理必须幂等；数据库为空、无孤立计划或重复执行均安全。

自动测试：

- seed 仅有 `ChapterSpeechPlans` + `ChapterSpeechPlanSegments`、无 `AudioCacheEntries` 的章节，启动维护后计划与段均删除。
- seed 至少一条缓存索引的章节，启动维护后计划与段保留；覆盖旧 synthesis profile / 非当前配置缓存仍属于“有缓存”。
- 缓存文件缺失或损坏导致索引在同轮健康维护中被删除后，对应计划随后被孤立清理回收，证明维护顺序正确。
- 重复执行维护保持幂等，不产生异常或额外数据变化。
- 保持既有“删除最后一条缓存索引时同事务删除计划”的测试，不把它退化为仅启动时清理。
- 测试不依赖真实用户数据目录或可见窗口。

验收：

- 数据库不会因反复加载但从未形成缓存的章节跨启动无限积累朗读清单。
- 清理实现为集合式/常数级数据库维护，不形成逐章 N+1。
- 相关 Infrastructure/Application 定向测试通过。

完成成果：

- 为朗读清单 store 增加单条 `DELETE ... NOT EXISTS` 集合清理，并将其接入索引健康修复与 LRU 淘汰之后、Shell 交互前的启动维护流程。
- 保留运行时删除最后一条缓存索引时的同事务计划回收，并覆盖无缓存清理、旧配置/受保护缓存保留、LRU 后清理和重复维护幂等回归。
- 已通过相关 Infrastructure 集成测试和 Application 单元测试。

## Phase E：视觉验收资产与默认测试解耦

## [ ] T006（P1）：将 `visual-review` 降级为按需生成资产

目标：

- 让普通 `dotnet test` 与仓库历史视觉 PNG、manifest 和截图哈希完全解耦，解决 UI 历史副产物导致全量测试在常规功能开发后失败的问题。
- 保留 Style Gallery、正式 Page/Window fixture、截图 harness、稳定场景 ID 和显式视觉生成能力，确保以后重新进行 UI 开发时仍能快速恢复视觉验收流程。
- 保留 WPF 失败诊断能力；不要把“成功视觉验收资产”和“测试失败诊断”混成一套机制。

实施：

- 删除仓库当前跟踪的 `artifacts/visual-review/manifest.json`，调整 `.gitignore`，使整个 `artifacts/visual-review/` 仅作为本地生成目录，不再特例跟踪根 manifest、PNG 或子 manifest。
- 移除/重构 `VisualReviewManifestTests` 以及其它默认测试中“仓库必须存在历史 manifest/PNG/hash”的合同。默认测试在 `artifacts/visual-review/` 完全不存在时仍必须通过。
- 不删除 `PageVisualReviewHarness`、`WindowVisualReviewHarness`、MiniPlayer/Style Gallery 的显式截图能力，也不删除 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` guard；这些能力继续只在显式视觉流程中使用。
- 保留 `Generate-VisualReviewManifest.ps1` 或等价根 manifest 生成能力，使一次显式完整视觉生成仍能产出可校验的本轮索引；工具不得假设仓库预先存在历史根 manifest。
- 若需要继续自动验证截图/manifest/hash 生成器自身，把测试改为使用测试拥有的临时目录生成最小视觉资产，验证 schema、路径、hash 和可重复性后清理；不得读取仓库历史视觉资产。
- `StyleGallerySceneTests` 等稳定 scene registry、Measure/Arrange/Render、资源解析和显式 guard 合同继续留在默认测试，只清理其对固定仓库输出目录/历史生成物的不必要依赖。
- 保持 `TestResults/wpf-diagnostics/<test-name>/` 的失败截图、视觉树和窗口状态机制不变；它仍只在失败诊断时按测试策略生成。
- 更新 `tools/NovelSpeaker.StyleGallery/README.md` 及与视觉生成命令直接相关的开发说明，明确输出是可删除、可重建的本地验收资产，而不是提交到 Git 的 baseline。

自动测试/检查：

- 在删除整个本地 `artifacts/visual-review/` 后运行受影响 WPF/Presentation 测试，默认路径全部通过，且不会因为目录缺失自动生成视觉资产。
- `rg` 确认默认测试不存在要求 repository `artifacts/visual-review/manifest.json`、固定 PNG 或历史 SHA256 必须存在的断言。
- 显式视觉 guard 测试继续证明：未设置 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 时不生成资产；设置后可在测试临时目录或显式目标目录重复生成。
- 如保留 manifest 工具测试，证明同一组本轮 child manifests 可生成一致根索引，缺失引用 PNG 时仍给出明确错误。
- `.gitignore` 证明 `artifacts/visual-review/` 下的 PNG 和各级 manifest 均不会被 Git 跟踪。

验收：

- 默认 `dotnet test` 不依赖、读取或维护任何历史视觉截图基线。
- 显式 UI 开发仍可以生成 Light/Dark、页面/窗口和 Gallery 视觉资产进行人工比较。
- Style Gallery、fixture、截图宿主和失败诊断能力没有因清理历史资产而被删除。
- 受影响 WPF/Presentation 定向测试通过。

## Phase F：整体质量与发布合同验收

## [ ] T007（P0）：执行跨模块审阅并完成阶段质量门禁

依赖：T001–T006。

目标：

- 对数据根切换、开发隔离、主程序改名和 transient UI 收口做一次跨模块自动复查。
- 确保最终源码、测试、README、数字编号文档与发布产物表达同一套合同。

实施：

- 复查数据根解析只存在一个生产 owner，正式/开发/测试三类数据不会互相读取。
- 复查旧 `%LocalAppData%/NovelSpeaker` 没有迁移、探测、fallback 或兼容入口；同时确认 SQLite schema migration runner 与既有 migration 保持完整。
- 复查 `NOVELSPEAKER_DATA_ROOT` 和 Development 环境只承担明确的开发/诊断职责，不泄漏为隐藏的多套生产存储模式。
- 复查正式 Data 根的路径安全、书籍删除、缓存维护、Operations journal、日志和 settings 全部使用同一个根目录 provider。
- 复查启动缓存维护在索引修复/LRU 淘汰后会集合式清除无任何 `AudioCacheEntries` 的残留朗读清单，并且仍有缓存的章节不会被误删。
- 复查生产运行不依赖 `NovelSpeaker.App.exe` 硬编码；需要 executable path 的功能均使用实际当前进程路径。
- 复查根 `README.md` 只描述已经落地的 `NovelSpeaker.exe`、便携 `Data/` 与开发命令，不保留旧运行说明。
- 复查 `AGENTS.md`、`docs/README.md` 和 `TASK_BACKLOG.md` 不再要求创建任务归档；仓库中不存在 `docs/archives/`。
- 复查默认测试与 `artifacts/visual-review/` 历史生成物完全解耦；删除该目录后完整质量门禁仍可运行，显式视觉生成能力和失败诊断能力仍保留。
- 复查 transient UI Single Surface 自动合同仍通过。

完整验收：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

随后执行 self-contained `win-x64` publish 与自动包内容检查，至少确认：

- `NovelSpeaker.exe` 存在，`NovelSpeaker.App.exe` 不存在。
- 主程序可从 publish 根目录解析其正式数据根为同级 `Data/`。
- 发布包不预置用户数据库、设置、书籍、缓存、日志或开发 profile 产物。
- LICENSE、THIRD-PARTY-NOTICES 和既有必需 runtime assemblies 存在。
- 不包含测试程序集、TestAssets、Style Gallery、视觉调试输出或临时文件。

验收：

- 完整质量门禁和 publish 包合同全部通过。
- 默认测试未设置可见窗口授权变量。
- 稳定文档、README 与实现一致。
- 若发现可以自动修复的问题，在本任务内修复并重新执行受影响门禁；无法自动解决的真实阻塞标记为 `[!]` 并记录可复现证据。
