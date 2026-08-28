# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段围绕发布目录与数据根目录合同收口，并继续完成 transient UI Single Surface 初版的自动调试。

本轮目标：

- 将正式运行数据根目录切换为程序目录下的 `Data/`，不保留旧 `%LocalAppData%/NovelSpeaker` 的迁移、探测或回退逻辑。
- 将默认开发运行数据隔离到 `%LocalAppData%/NovelSpeaker.Dev`，自动测试继续使用测试自身的临时目录。
- 将发布主程序从 `NovelSpeaker.App.exe` 统一改名为 `NovelSpeaker.exe`，并清除运行时代码对主程序文件名的硬编码。
- 保持现有 SQLite schema migration 体系；本轮“无兼容”只针对数据根目录切换。
- 调试并收口 ContentDialog、StartupStatusWindow 与 `AppStatusView` Embedded 模式的 Single Surface 初版。
- 收口朗读清单生命周期：启动缓存维护后不长期保留任何无缓存索引章节的 `ChapterSpeechPlans` / `ChapterSpeechPlanSegments`。
- 完成发布产物、数据安全边界、文档一致性和完整自动质量门禁验收。

稳定产品、架构、数据、测试与视觉约束分别以数字编号文档为准。本文件只描述尚未完成的实施顺序和自动验收。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[!]`：存在阻塞，必须在任务结果中记录可复现原因。
- `P0`：数据安全、运行隔离、发布产物或完整质量门禁。
- `P1`：结构清理、视觉收口和长期可维护性。

任务完成后直接从本文件删除，不保留仓库内历史归档；历史追溯使用 Git。

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

## Phase A：数据根目录与开发隔离

## [ ] T002（P0）：切换生产 Bootstrap 并隔离默认开发运行

依赖：T001。

目标：

- 将 NovelSpeaker 正式运行真正切换到 `<application-directory>/Data`。
- 确保仓库默认 `dotnet run` / IDE 开发启动使用 `%LocalAppData%/NovelSpeaker.Dev`，不会污染正式便携数据。
- 清理旧 `%LocalAppData%/NovelSpeaker` 的生产依赖和兼容残留。

实施：

- 在组合根中使用 T001 的统一 resolver 构建 `IAppDataDirectoryProvider`，删除直接实例化旧 `LocalAppDataDirectoryProvider` 的路径。
- 增加/调整 `Properties/launchSettings.json`，让仓库默认开发 profile 明确设置 `NOVELSPEAKER_ENVIRONMENT=Development`。
- 默认开发 profile 不设置固定绝对数据路径；实际路径继续由 resolver 计算为 `%LocalAppData%/NovelSpeaker.Dev`。
- 首次正式运行时按需创建 `Data/` 与必要子目录；不要求发布 ZIP 预置空 `Data/`。
- 全仓检查并删除旧 `%LocalAppData%/NovelSpeaker` 的探测、迁移、复制、fallback 或兼容入口；没有此类入口时不要为了“迁移”新增任何代码。
- 审计数据根安全检查：允许数据根作为已选定的根边界正常工作，但数据库记录或根目录内部的子级路径仍不得通过 `..`、绝对路径替换或 reparse point 逃逸根目录。
- 不改变用户外部 TXT 永不写入的既有边界。

自动测试：

- Bootstrap/组合根测试证明正式默认路径解析到 `AppContext.BaseDirectory/Data`。
- 开发 profile 可由静态/配置测试证明设置了 Development 环境，不依赖人工运行应用观察。
- 测试证明旧 `%LocalAppData%/NovelSpeaker` 即使存在也不会被读取或作为 fallback。
- 路径安全回归覆盖便携根内正常路径与子级逃逸/reparse-point 拒绝场景。
- 测试代码静态守卫确保没有测试访问正式、开发或旧数据目录。

验收：

- `rg` 不存在生产代码直接拼接旧 `%LocalAppData%/NovelSpeaker` 的路径。
- `dotnet run` 的默认开发配置与正式数据根隔离合同可由自动检查证明。
- SQLite migration 测试仍通过，且 migration 文件没有因本任务被改写/重编号。
- 受影响项目 build/test 通过。

## Phase B：主程序命名与发布产物

## [ ] T003（P0）：统一主程序为 `NovelSpeaker.exe`

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

## Phase C：Transient UI 自动调试收口

## [ ] T004（P1）：调试并收口 transient UI Single Surface 初版

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

## Phase D：朗读清单持久化收口

## [ ] T005（P0）：清理无缓存索引的残留章节朗读清单

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

## Phase E：整体质量与发布合同验收

## [ ] T006（P0）：执行跨模块审阅并完成阶段质量门禁

依赖：T001–T005。

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
