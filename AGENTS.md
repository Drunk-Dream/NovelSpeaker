# AGENTS.md

## 文件定位

本文件只定义 NovelSpeaker 仓库的开发约束和 Agent 工作规则，不重复保存产品范围、当前功能、技术栈版本、页面清单、数据库 schema 或发布能力。

项目信息统一由 `docs/` 提供：

- 文档入口和阅读顺序：`docs/README.md`
- 产品范围：`docs/00_PROJECT_BRIEF.md`、`docs/01_PRODUCT_SCOPE.md`
- 架构与代码组织：`docs/02_TECH_STACK_AND_ARCHITECTURE.md`
- 专项设计：`docs/03_HTTP_TTS_COMPATIBILITY.md` 至 `docs/08_RUNTIME_AND_LIFECYCLE.md`、`docs/12_REGEX_REPLACEMENT_PIPELINE.md`、`docs/13_VISUAL_DESIGN_SYSTEM.md`
- 测试与工程约定：`docs/09_TESTING_AND_QUALITY.md`、`docs/10_ENGINEERING_CONVENTIONS.md`
- 决策和风险：`docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`
- 当前任务、依赖和状态：`docs/TASK_BACKLOG.md`

代码、测试和文档不一致时，先核对实际行为与现有测试，再修正相应文档；不要在本文件新增一份“当前实现基线”来掩盖差异。

## 开始工作前

1. 先阅读 `docs/README.md`，再按任务选择对应设计、测试、工程约定、决策和 backlog。
2. 阅读将要修改的生产代码、直接调用者和现有测试；不要只根据文件名或文档推断实现。
3. 检查工作区已有改动并保留用户修改；不得覆盖、回滚或格式化无关文件。
4. 确认任务是否改变产品行为、数据格式、公共接口、安全边界或发布内容；若改变，先明确设计依据和验收条件。
5. 优先使用 `rg`、`rg --files` 和小范围读取，不遍历 `bin`、`obj`、`TestResults` 等生成物。

Codex 配置目录直接读取 `~/.codex/`。

## 修改范围

- 按可运行、可验证的纵向切片修改，不进行与当前任务无关的一次性重构。
- 不为迁移长期保留 `New`、`V2`、`Final`、`Refactored`、`Old`、`Compat` 等平行实现。
- 建立目标实现并迁移调用者后，在同一任务删除旧入口和临时适配器。
- 不新增重量级框架、通用事件总线、Service Locator 或万能 `Manager/Helper/Utils` 来绕过现有边界。
- 新增公共接口前写清职责、所有权、调用方和取消语义；没有真实替换或边界价值时不机械抽接口。
- 行为保持型移动与行为变化分开执行，避免把格式化、重命名、功能和依赖升级混为一个改动。

## 架构约束

- 严格遵循 `docs/02_TECH_STACK_AND_ARCHITECTURE.md` 定义的项目依赖和功能切片。
- Domain 不依赖 Application、Infrastructure、App 或具体技术包。
- Application 不暴露 SQLite、HTTP client、Jint、NAudio、WPF、Wpf.Ui 或 Infrastructure 类型。
- Infrastructure 只实现持久化、文件、HTTP、脚本、音频、设置和日志等技术适配，不承载页面工作区或业务状态机。
- App 的非 Bootstrap 代码不得依赖 Infrastructure；ViewModel 只调用 Application 用例。
- 业务逻辑不得写入 code-behind；code-behind 只处理 WPF 生命周期、焦点、拖放、虚拟化、滚动、动画和事件桥接。
- ViewModel 不得引用具体 Page、Window、Dispatcher 或 WPF/Wpf.Ui 视觉类型。
- 播放、页面激活、编辑会话和后台操作必须各有唯一状态所有者。

## 异步与生命周期

- 所有异步 I/O、文件、SQLite、HTTP 和可等待业务流程必须接收并传递 `CancellationToken`。
- 不用同步阻塞等待替代异步流程；不得在 UI Dispatcher 上无界等待。
- `CancellationToken.None` 只允许用于有明确理由的不可取消最终清理，并在代码附近说明。
- `OperationCanceledException` 是正常控制流，不得被通用 `catch` 转换为失败。
- 页面进入、离开、重复进入和操作替换遵循 `docs/08_RUNTIME_AND_LIFECYCLE.md` 的 activation/operation version 规则。
- `async void` 只限事件入口；入口必须捕获异常并立即转交可等待、可串行化的流程。
- fire-and-forget Task 必须有明确所有者、取消源和异常处理，禁止无登记的后台 `Task.Run`。

## UI 修改约束

涉及页面结构、导航、视觉、反馈、滚动、快捷键、可访问性或设置交互时，必须阅读并遵循：

- `docs/06_UI_AND_USER_FLOWS.md`
- `docs/07_SETTINGS_PAGES.md`
- `docs/13_VISUAL_DESIGN_SYSTEM.md`
- 正则替换相关任务另读 `docs/12_REGEX_REPLACEMENT_PIPELINE.md`

若需求与设计文档冲突，先更新设计或记录经用户确认的偏离原因，再修改代码。不得通过新增并行页面、局部导航或重复交互绕过既有设计。

UI 事件和平台能力通过可测试的 presentation port 转交；不得在不同页面分别直接实现文件选择、剪贴板、目录打开、错误投影或播放协调。

Dialog、Flyout、Popup 和独立状态浮窗遵循 `docs/13_VISUAL_DESIGN_SYSTEM.md` 的 Single Surface 约束：宿主已经拥有完整浮层时，内容不得再默认嵌套完整 Card/Section/Raised Surface；`AppStatusView` 只有在外层已拥有主 Surface 时才使用 Embedded 模式。

## 数据、安全与兼容约束

- 已发布 SQLite migration 只能追加，不能为整理代码而修改、合并或重编号。
- 数据格式、章节偏移、阅读进度和音频缓存键变化必须有独立迁移/兼容设计与回归测试；数据目录切换是否迁移以 `docs/05_DATA_AND_PERSISTENCE.md` 的明确合同为准，不得自行增加兼容路径。
- 不得绕过应用数据根目录约束读取、移动或删除任意路径；永不修改用户外部源文件。
- 规则脚本是不可信输入。不得放宽 CLR、文件、进程、反射、任意网络、宿主对象或资源限制，除非有明确设计和安全回归测试。
- 不直接复制或翻译 Legado 源代码，只参考公开行为和数据格式独立实现。
- 日志、异常、Snackbar、请求预览和诊断摘要不得包含小说正文、完整 URL、Header、Body、响应正文、Token、API Key 或其它凭据/认证状态。
- 不在测试、日志或提交中加入真实凭据和私人小说正文；测试只使用脱敏 fixture。
- 不得把未实现的兼容能力、安全能力或发布能力描述为已经提供。

## 缺陷与测试规则

- 修复缺陷时先增加能够复现问题的失败测试，再修改实现。
- 解析器、缓存键、限流器、播放状态机、迁移、路径安全和脚本边界的行为变化必须增加专项回归测试。
- 迁移/重构前先用特征测试固定用户可观察行为，不用测试照抄私有实现。
- 测试按 `docs/09_TESTING_AND_QUALITY.md` 分层；纯测试不得依赖 WPF STA 或真实第三方服务。
- 测试等待明确事件、状态版本或可控时间，不使用任意 `Task.Delay`/`Thread.Sleep` 猜测完成。
- migration、fixture、损坏音频、规则样本和 WPF Test Host 属于受保护测试资产，不能仅因无生产引用而删除。
- 自动 WPF 测试默认不得在用户当前交互桌面显示任何顶层窗口；普通 Page/UserControl 优先使用 `WpfControlHost`，真实 Window/Popup/Focus/HWND 生命周期只能通过 `tests/TestKit/Wpf` 的共享宿主进入隔离测试 Desktop。
- 测试宿主无法建立隔离 Desktop 时必须失败，不得回退到当前用户 Desktop。不得通过“移到屏幕外”作为长期无窗口保证。
- 未经用户在当前任务中明确授权，Codex 不得设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`，也不得设置旧的 `NOVELSPEAKER_TEST_SHOW_WINDOWS=1`、直接运行会显示 NovelSpeaker UI 的调试流程，或采用其它方式绕过隐藏 Desktop。
- `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 只授权生成确定性截图/manifest，不授权显示窗口。
- 当前阶段的测试数量目标只存在于 `docs/TASK_BACKLOG.md`；不得建立永久的测试总数上限，也不得通过把无关行为塞进单一测试来规避数量。

## 文件和文档规则

- 使用 `apply_patch` 修改文本文件；避免用脚本或重定向重写用户文件。
- 目录、命名空间、文件名和主公共类型保持一致。
- 数字编号文档只描述稳定产品/架构终态；迁移过程、执行波次和任务状态只写入 `docs/TASK_BACKLOG.md`。
- 行为变化后同步对应设计、测试策略、决策、README 和 backlog；不要更新无关文档。
- Codex 完成 `docs/TASK_BACKLOG.md` 中的任务后保留该任务条目，将状态标记为 `[x]`，并在任务末尾用简短“完成成果”记录主要实现与自动验收结果；不要在执行任务时自行删除已完成条目。
- Backlog 的删除、重写或插入只发生在新的任务规划阶段，由用户当前规划要求决定。需要清理旧计划时直接重写当前 Backlog，历史仍由 Git 记录；仓库不创建或维护任务归档目录、归档文档或归档索引。
- 根目录 `README.md` 只描述当前已经实现的能力；规划中的功能不得提前写成可用能力。
- 新的 backlog/任务验收不得把“手动验证”作为关闭条件；尽量用自动测试、架构检查、WPF 契约测试和发布包检查建立可重复证据。
- Style Gallery 场景和截图按稳定资源族命名；正式界面截图按稳定页面/窗口身份命名。任何视觉产物路径不得使用 backlog 任务编号。
- `artifacts/visual-review/` 是显式 UI 视觉验收的本地生成目录，不是默认测试基线或仓库长期资产；默认 `dotnet test` 不得要求其中存在 PNG、manifest 或历史哈希。视觉生成工具、fixture 与测试宿主可以长期保留。
- 页面截图必须来自正式 View 与确定性脱敏 fixture；Gallery 只展示资源族、控件族和样式族，不制作正式页面副本。

## 日常验证

当前环境如无法从 `PATH` 找到 `dotnet`，使用：

```text
/mnt/c/Program Files/dotnet//dotnet.exe
```

在 Codex 帮我批准模式下，如果命令运行失败，先尝试在沙箱外以提权模式重新运行；若仍然失败，按实际错误报告，不绕过失败检查。
仓库通过 `Directory.Build.props` 的 `RuntimeIdentifiers` 为所有还原入口保留 `win-x64` 锁文件目标，普通 build/run 和 IDE 隐式还原不得覆盖该属性。完整质量门禁严格按以下顺序执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

手动启动使用：

```powershell
dotnet run --project src/NovelSpeaker.App
```

仓库默认开发启动配置使用 `%LocalAppData%/NovelSpeaker.Dev`，不得读写正式便携数据目录。`dotnet run` 会显示正式应用窗口，属于可见 UI 操作；Codex 只有在用户当前任务明确允许时才可执行。普通测试和视觉产物生成不得以此替代自动宿主。

只有依赖或版本确实变化时才允许：

```powershell
dotnet restore -r win-x64 --force-evaluate
```

执行后必须审查全部 `packages.lock.json` 差异，确认依赖变化符合任务且 `win-x64` 目标仍存在，再运行锁定还原验证。无法执行的检查必须在交付中如实说明。

## Git 约束

- 未经用户明确授权，不得提交、打标签、推送、创建 PR/Release 或修改远端内容。
- 不得使用 `git reset --hard`、`git checkout --` 等方式丢弃用户改动。
- 用户明确要求提交时，任务切片不是默认 commit 边界；即使一个任务已经完成，也必须继续按逻辑目的拆成多个原子提交。
- 每个 commit 只包含一个清晰目的的改动；不同性质的修改尽量分开提交。
- 实现与直接对应、用于固定同一行为的测试通常放在同一个 commit；不要为了“测试单独提交”人为拆散一个不可独立理解的改动。
- 纯重命名/移动尽量与行为变化分开提交，避免 diff 中同时混入大规模搬运和逻辑修改。
- 文档只有在与对应行为不可分割时跟随实现提交；独立的文档整理和规划使用独立 commit。
- commit message 简洁准确，描述该提交的实际目的，不使用“update/fix stuff”等模糊描述。
- Commit messages use English Conventional Commits, such as `type(scope): describe the change`.
- Unless the user explicitly requests otherwise, use fast-forward merge mode when merging branches.
- 禁止在任务结束时把该任务产生的全部文件修改一次性打包成一个大提交。
- NovelSpeaker 的版本判断、版本更新、发布分支/PR 处理、tag、Release CI、Release Note 和发布后分支整理统一由 `.codex/skills/release-version/SKILL.md` 定义，不在本文件或 `docs/` 重复维护流程。用户明确调用该 Skill 时，按 Skill 中的授权边界执行；用户在当前请求中的特殊要求优先。

## 交付说明

完成任务后简要说明：

- 修改了什么，以及职责边界或设计理由。
- 添加或更新了哪些测试；仅文档修改时明确写“未修改测试代码”。
- 实际执行的验证命令和结果。
- 未执行检查、环境限制、剩余风险和后续依赖。
