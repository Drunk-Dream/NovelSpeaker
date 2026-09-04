# AGENTS.md

## 文件定位

本文件只定义 Agent 开发约束，不保存产品功能清单、任务过程或重复架构说明。

项目文档入口：

- 文档索引：`docs/README.md`
- 产品范围：`docs/00_PRODUCT_AND_SCOPE.md`
- 架构：`docs/01_ARCHITECTURE.md`
- 运行时与状态：`docs/02_RUNTIME_AND_STATE.md`
- 数据：`docs/03_DATA_AND_PERSISTENCE.md`
- UI/导航/性能：`docs/04_UI_NAVIGATION_AND_PERFORMANCE.md`
- HTTP TTS：`docs/05_HTTP_TTS_COMPATIBILITY.md`
- Regex：`docs/06_REGEX_REPLACEMENT_PIPELINE.md`
- 视觉系统：`docs/07_VISUAL_DESIGN_SYSTEM.md`
- 测试：`docs/08_TESTING_AND_QUALITY.md`
- 工程约定：`docs/09_ENGINEERING_CONVENTIONS.md`
- 已确认决策：`docs/10_DECISIONS.md`
- 当前任务：`docs/TASK_BACKLOG.md`

## 开始工作前

1. 阅读 `docs/README.md` 和当前 `TASK_BACKLOG.md`。
2. 按任务阅读对应 owner 文档，不遍历所有 docs 当作需求集合。
3. 阅读将修改的生产代码、调用者和测试。
4. 保留工作区已有用户修改，不格式化/回滚无关文件。
5. 先确认任务是否改变数据、用户行为、公共技术边界或发布内容。

## 当前架构优化阶段

- 四层项目结构保持不变。
- 允许内部 namespace/API/目录/DI/fixture 的破坏性重构。
- 不为内部兼容性长期保留 Old/New/V2/Compat/forwarding wrapper。
- 目标实现迁移完成后，在同一任务/当前 Phase 删除旧入口。
- 普通 Page/ViewModel 默认 transient。
- Feature-local controller 默认 `internal sealed`，不机械抽 interface。
- 不新增通用 EventBus/Messenger、Service Locator、万能 Manager/Helper/Utils。
- 不通过 Page Singleton/NavigationCache 掩盖页面加载问题。

## 架构约束

- 严格遵循 `docs/01_ARCHITECTURE.md`。
- Domain 不依赖外层。
- Application 不暴露 SQLite/HTTP/Jint/NAudio/WPF/Infrastructure 类型。
- Infrastructure 不承载页面工作区或 UI 状态机。
- App 非 Bootstrap 代码不依赖 Infrastructure。
- Shared 不依赖 Feature；Feature 不形成双向依赖。
- ViewModel 不引用具体 Page/Window/Dispatcher/WPF 视觉类型。
- 播放、页面 activation、编辑会话和后台任务各有唯一 owner。
- 页面不得直接写 ReadingProgress。

## 生命周期与异步

- 所有可取消异步流程传递 `CancellationToken`。
- cancellation 是正常控制流。
- `async void` 只限事件入口。
- fire-and-forget 必须登记 owner、取消和异常观察。
- `Task.Yield()` 不视为后台化，也不能作为性能修复证据。
- 大规模 DTO/projection/sort/group/hash 不得无界阻塞 UI Dispatcher。
- 复杂页面遵守 staged loading。

## 大列表

涉及 BookDetails、Player chapter catalog、CacheManagement 等列表时必须遵守：

- 目标至少 10,000 条连续 catalog；
- Immutable Catalog + Sparse Mutable Decoration；
- 禁止首屏 `Clear + N × Add`；
- current item O(1) 定位；
- cache/status 只更新必要 index/viewport；
- WPF virtualization 不视为 data virtualization。

## UI

UI/导航任务阅读：

- `docs/04_UI_NAVIGATION_AND_PERFORMANCE.md`
- `docs/07_VISUAL_DESIGN_SYSTEM.md`

正则相关另读 `docs/06_REGEX_REPLACEMENT_PIPELINE.md`。

业务逻辑不得写入 code-behind。Dialog/Flyout/Popup 遵守 Single Surface。主题图标使用语义资源，禁止 Dark Mode 硬编码黑色。

## 数据与安全

- 已发布 migration 只能追加。
- 不增加旧数据根双读/隐式迁移路径，除非用户明确重新决策。
- 永不修改用户外部 TXT。
- 规则脚本是不可信输入，不放宽文件、进程、反射、CLR 或任意宿主权限。
- 日志/诊断不包含小说正文、Token、API Key、完整请求/响应秘密。
- 测试只使用脱敏 fixture。

## 测试

- 先保护用户可观察行为，再重构实现。
- 允许删除/重写与旧内部结构绑定的测试；测试总数量可以减少。
- Presentation tests 不实例化真实 WPF。
- WPF tests 只验证 WPF 特有行为。
- 自动 WPF 测试默认隐藏隔离 Desktop；失败时 fail closed。
- 未经用户当前任务明确授权，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
- 不使用固定延时猜测异步完成。
- Architecture Fitness Tests 是长期门禁，不为过渡代码绕过。

## 文档规则

- 编号文档已经由规划阶段维护；Codex 默认不重组/扩写编号文档。
- 任务执行只更新 `TASK_BACKLOG.md` 中自身状态和“完成成果”。
- 如果实现证明稳定架构文档存在错误，记录证据和阻塞，不自行建立第二套架构文档。
- 不创建 docs/archive、任务归档或临时诊断长期文档。

## Backlog 规则

- 默认一次只执行一个编号任务。
- 完成后保留任务，标记 `[x]`，追加简短完成成果。
- 不自行开始下一任务。
- 删除/重写 Backlog 只发生在新的任务规划阶段。
- 任务中声明的旧实现/compat wrapper 必须按计划删除，不能“先留着以后再说”。

## 质量门禁

标准顺序：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

单个任务可运行 focused tests，但完成时必须满足该任务在 Backlog 中的门禁。不能执行的检查如实记录。

## Git

- 未经用户明确授权，不提交、推送、创建 PR/Release/tag。
- 不使用会丢弃用户改动的 reset/checkout。
- 用户要求提交时按逻辑目的拆原子 commit，使用 English Conventional Commits。
- 纯 move/rename 与行为变化尽量分开提交。

## 交付

完成任务后说明：

- 修改内容与职责边界；
- 删除了哪些旧实现；
- 测试/ArchitectureTests；
- 实际执行命令；
- 环境限制和剩余风险。
