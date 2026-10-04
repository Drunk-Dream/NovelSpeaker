# AGENTS.md

## 1. 文件定位与信息优先级

本文件只定义 Agent 在 NovelSpeaker 仓库中的开发约束。产品最终形态由 `docs/` 定义，当前开发调度由根目录 `TASK_BACKLOG.md` 定义，每个未完成任务的详细实施合同位于 `tasks/`。

发生冲突时按以下优先级处理：

1. `AGENTS.md`：仓库级硬约束与 Agent 工作方式。
2. 当前任务对应的 `tasks/Txxx_*.md`：当前任务的权威实施合同。
3. `docs/`：长期产品、架构、协议与质量合同。
4. `TASK_BACKLOG.md`：任务顺序、依赖、状态和简要成果，不替代详细实施规格。

当前任务规格可以描述从现状向目标终态的增量变化；任务完成后应删除该临时规格，`docs/` 重新成为长期事实来源。

## 2. 开始工作前

1. 阅读根目录 `TASK_BACKLOG.md` 中当前任务。
2. 阅读该任务对应的 `tasks/Txxx_*.md`。
3. 只阅读任务规格明确引用的长期文档；不要遍历全部 `docs/` 后自行综合需求。
4. 阅读将修改的生产代码、调用者和相关测试，以真实代码为实现基线。
5. 保留工作区已有用户修改，不格式化、回滚或重写无关文件。
6. 如果源码现状与任务规格存在会改变产品行为、架构边界、隐私边界或后续路线的冲突，停止该冲突部分并记录证据；普通类名、目录、局部实现差异由 Agent 自行适配。

## 3. 文档职责

- `docs/`：只保存长期有效的最终产品/架构/协议/质量合同。
- `docs/specs/`：只保存确实需要精确定义的长期协议或流水线合同。
- `tasks/`：只保存尚未完成任务的临时实施规格。
- `TASK_BACKLOG.md`：只负责任务调度、依赖、状态和完成成果。
- Git：保存已完成任务规格、旧设计和历史演进，不建立 `docs/archive/`。

未经当前任务明确要求，Codex 不重组或扩写长期文档。如果实现证明长期文档无法成立，应记录冲突并停止相关扩张，不自行建立第二套解释。

## 4. 任务执行与完成

- 默认一次执行一个编号任务；如果调用明确要求连续执行一个 Phase 或整个 Backlog，可按依赖顺序继续。
- 人工验收永远是可选补充，不是任务完成或下一任务启动的前置条件。
- 必须以与任务风险匹配的自动化测试、构建、静态检查、架构检查以及任务规格中的可自动验证条件作为主要完成标准。
- 可选人工视觉/交互验收未执行或尚未执行，不得将任务标记为阻塞。
- 若后续人工发现问题，创建新的修复任务，不重新激活已经完成并删除的旧任务规格。
- **默认每个任务结束时仓库应保持可构建、可测试。只有当 `TASK_BACKLOG.md` 明确声明进入“staged breaking migration window”，且当前 task spec 明确列出允许暂时失效的调用方/测试时，任务才可以在预期的中间破坏态结束。**
- staged breaking migration 期间，当前任务仍必须完成自己声明的静态检查、focused tests 或结构验证；不得把“阶段最终会修好”当成跳过当前任务验证的理由。
- staged breaking migration 的最终 Integration/Closure 任务必须恢复标准完整门禁；如果完整门禁仍失败，不得把该阶段标记完成。

任务完成时必须：

1. 满足任务规格中的强制自动验收。
2. 清理任务声明的一次性代码、脚本、截图、trace、兼容层、迁移残留和临时测试。
3. 在 `TASK_BACKLOG.md` 中将任务标记为 `[x]`，追加简短“完成成果”。
4. 删除该任务对应的 `tasks/Txxx_*.md`。
5. 停止或按调用要求继续下一任务；不等待人工验收。

## 5. 架构约束

严格遵守 `docs/01_SYSTEM_ARCHITECTURE.md`：

- 保持 Domain / Application / Infrastructure / App 四层。
- Domain 不依赖外层。
- Application 不暴露 SQLite/HTTP/Jint/NAudio/WPF/Infrastructure 类型。
- App 非 Bootstrap 代码不直接依赖 Infrastructure。
- Application 模块和 App Feature 不形成循环依赖。
- Shared 不依赖 Feature，不通过 Shared 隐藏模块循环。
- 同一 mutable state 只有一个 owner。
- 普通 Page/ViewModel 默认 transient。
- Feature-local controller/projector 默认 `internal sealed`，除非存在真实边界。
- 不新增通用 EventBus/Messenger、Service Locator、CommandBus、万能 Manager/Helper/Utils。
- 不通过 Page Singleton/NavigationCache 掩盖生命周期或性能问题。
- 优先复用 .NET / Windows / WPF / Wpf.Ui、当前技术栈和项目已有稳定能力；不重复实现框架级基础设施。

## 6. 生命周期与异步

遵守 `docs/02_RUNTIME_AND_NAVIGATION.md`：

- 所有可取消异步流程传递 `CancellationToken`。
- cancellation 是正常控制流。
- `async void` 只限 WPF 事件入口。
- fire-and-forget 必须有明确 owner、异常观察和生命周期。
- 页面离开只取消 page-owned work，不取消 process/session/background owner。
- `Task.Yield()` 不视为后台线程切换，也不能作为性能修复证据。
- 大规模 projection/sort/group/hash 不得无界阻塞 Dispatcher。
- 复杂页面使用 staged loading。

## 7. 数据与安全

遵守 `docs/05_DATA_AND_COMPATIBILITY.md`：

- 开发中拟实施的数据库结构或持久数据变更，必须有明确的产品/架构授权边界。
- 对**尚未获授权**的新增、删除、重命名表/列/索引/约束、数据迁移或清理，Agent 必须在修改迁移代码前说明变更对象、原因、数据影响和回退/保留方案并取得用户同意。
- 如果当前 task spec 明确列出一个**已经由用户批准的具体持久化变更集合**，Agent 可以直接实施该集合，不得再次逐字段重复请示；task spec 必须同时写明数据影响与失败/回退策略。任何超出已批准集合的新持久化变化仍需停止并请求授权。
- 已发布 SQLite migration 只能追加。
- 不为内部 API/namespace 重构新增无意义数据 migration。
- 永不修改用户外部 TXT。
- 规则脚本是不可信输入，不放宽文件、进程、反射、CLR 或任意宿主权限。
- 测试 fixture 使用脱敏数据。
- 日志、普通遥测和结构化诊断不得保存小说正文、书名、章节标题、完整本地路径、完整 URL/Query、HTTP Header/Body、Token/API Key、TTS 文本、Regex 正文、SQL 参数或缓存音频。
- 诊断会话中的“截取当前窗口”仅由用户主动触发；这是允许包含当前 NovelSpeaker 界面内容的明确例外，禁止自动截图、连续截图或录屏。

## 8. Observability 约束

长期目标见 `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`。

- 生产日志、性能遥测、诊断会话职责独立。
- 业务模块只描述一次稳定操作语义，性能遥测和诊断会话可共享基础打点，但分别消费和持久化。
- 日志持久化独立于遥测和诊断会话 writer/store。
- 诊断/日志/遥测失败不得使业务操作失败。
- 第一版不引入完整 OpenTelemetry、远程 telemetry backend、Crash Dump/Minidump、自动截图或录屏。
- 不为“以后可能有用”增加高频、高基数或用户内容型诊断数据。

## 9. UI 与大列表

稳定 UI、主题、通知、设置页面、列表规模与视觉合同由 [docs/06_UI_AND_VISUAL_SYSTEM.md](docs/06_UI_AND_VISUAL_SYSTEM.md) 唯一维护；页面级批量管理合同见 [docs/specs/BATCH_MANAGEMENT.md](docs/specs/BATCH_MANAGEMENT.md)。

- WPF code-behind 只处理 WPF 特有生命周期与交互桥接。
- ViewModel 不引用具体 Page/Window/Dispatcher/Brush/Style/Thickness 等视觉类型。
- 瞬时操作结果、警告和失败通知使用已有 `IAppFeedbackService` / Snackbar，不在页面内嵌通知块替代或重复展示。
- 主题文字、图标或资源变更的验收遵守 UI owner 文档中的 Light/Dark 冷启动、主题切换和全量主题资源检查合同。
- 支持批量管理的页面遵守批量管理合同；CacheManagement 保持文档定义的文件管理器式选择语义例外。

## 10. 测试与自动验收

永久测试准入、测试分层和核心回归范围由 [docs/08_QUALITY_AND_TESTING.md](docs/08_QUALITY_AND_TESTING.md) 唯一维护。

每次代码审查（包括首审、复审、替换 reviewer，以及不通过 skill 发起的审查）必须读取仓库根目录 `.codex/review-checklist.md`，对最新完整 diff 逐项报告通过、失败或不适用及简短依据。该项目清单必须可读取；缺失或不可读取时不能形成可信审查结论。清单不替代本文件、当前任务规格和长期合同。

- 非核心修改默认不新增永久测试；临时测试、fixture、脚本和专用入口在任务完成前删除。
- Presentation tests 不实例化真实 WPF。
- 需要真实 Window/Popup/Focus/HWND 的 WPF tests 使用隔离 Desktop；隔离失败必须 fail closed。
- 未经当前任务明确授权，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
- 异步测试不使用固定延时猜测完成。

标准完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

开发阶段优先运行与当前风险匹配的 focused tests；阶段收口、发布以及任务规格明确要求时执行完整门禁。staged breaking migration 期间按当前 task spec 执行局部门禁，最终 Closure task 必须执行上述完整门禁。由于环境限制无法执行的检查必须如实记录，不得删除核心测试、弱化安全隔离或跳过关键架构约束制造绿色。

## 11. Git 与格式

- 本项目文本文件统一使用 LF。
- 未经用户明确授权，不推送、创建 PR/Release/tag。
- 不使用会丢弃用户已有改动的 reset/checkout。
- 用户要求提交时使用 English Conventional Commits，并按逻辑目的拆分。
- 行为保持型 move/rename 与行为变化尽量分开。
- 完成任务时不提交一次性诊断产物或临时测试。

## 12. 交付

每个任务完成成果至少说明：

- 实现了什么能力与职责边界；
- 删除了哪些旧实现/兼容残留；
- 新增、保留、删除或合并了哪些核心测试，以及依据；
- 执行了哪些自动验收以及结果；
- 因环境限制未执行哪些检查；
- 剩余风险或发现的长期文档冲突。

## 13. 用户提示

- 如果命令因沙箱权限、网络或其他沙箱限制运行失败，优先尝试在沙箱外运行；仍失败或确认并非沙箱原因时，必须如实保留并记录原错误和限制。
- 在沙箱外执行 `gh` 相关命令。
