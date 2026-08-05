# 工程约定

## 1. 代码组织

- 生产代码按 feature/职责组织，不按“所有 Service/Model/ViewModel”建巨型技术目录。
- 文件名、命名空间和主公共类型保持一致。
- 一个类型只承担可解释的单一状态所有权或技术适配职责。
- 不新增 `New/V2/Final/Old/Compat` 平行主线；迁移完调用者后删除旧入口。
- 避免万能 `Manager/Helper/Utils`；通用代码必须有明确业务或平台语义。

## 2. 命名

- 用例/命令使用动作语义：`ImportBook`、`ExportChapters`、`StartActiveCache`。
- 查询模型使用结果语义：`CacheOverview`、`ActiveCacheSnapshot`。
- `Coordinator` 仅用于真正协调多个拥有明确边界的参与者。
- `Controller` 用于局部交互/状态协调，不作为业务服务的默认后缀。
- `Adapter` 明确表示平台/技术适配。

## 3. 异步

- I/O 和可等待业务流程接受 `CancellationToken` 并向下传递。
- 不使用 `.Result`、`.Wait()` 或同步 `Mutex.Wait` 阻塞异步路径。
- `async void` 仅限事件入口。
- fire-and-forget 必须有 owner、取消和异常观察。
- `OperationCanceledException` 是正常控制流。

## 4. 错误处理

- 不使用空 `catch`。
- 技术异常在边界处转换为稳定错误分类和脱敏用户信息。
- catch 只捕获能处理的范围；取消优先重新抛出或映射为 Cancelled。
- 资源在 `finally`/`await using`/`using` 中确定性释放。

## 5. Application/API

- Application 合同不引用 SQLite、HTTP message、Jint、NAudio、WPF、Wpf.Ui 类型。
- 不因为测试方便就为每个类机械建立接口。
- 新公共接口必须能回答：谁调用、谁实现、谁拥有状态、取消语义是什么。
- DTO 不与 UI 卡片/控件一一绑定；Presentation 自行投影。

## 6. WPF 与样式

- 业务逻辑不写在 code-behind。
- code-behind 可处理 WPF 必需的焦点、拖放、滚动、虚拟化、动画、窗口和事件桥接。
- Wpf.Ui provider dictionaries 先加载并在进程生命周期内保持稳定；主题切换不重新注入 Style。
- `Application.Resources` 和全局合并字典禁止为标准 WPF/Wpf.Ui 控件定义 NovelSpeaker 隐式样式。
- 公共外观使用带 `x:Key` 的 `App.*` 具名样式；需要继承 Wpf.Ui 时通过受测试的 Provider Style Bridge，而不是依赖不透明的加载顺序。
- 同一标准控件族的 Style 集中在一个职责明确的资源字典中，不把 Button、Input、Selection、Media 或 Feedback 分散到多个综合文件。
- 不在全局资源中替换标准控件完整 `ControlTemplate`。确需完全自定义时，使用应用自有 CustomControl/UserControl 或局部具名样式，并增加专项 WPF 测试。
- 跨 Feature 的正式自有控件类位于 `Shared/Presentation/Controls`，默认模板位于 `Shared/Theming/Resources/ControlThemes`；业务领域视图留在对应 Feature。
- 自有控件允许按类型应用默认 Style；标准 WPF/Wpf.Ui 控件仍必须显式引用 `App.*` Style。
- Style Gallery 的 fixture、示例文本和演示状态不得进入生产控件构造函数。
- Style Gallery 按稳定资源族组织场景；同一 Style/控件族只能有一个主要 Gallery scene，不按任务编号创建展示区。
- Gallery 截图使用稳定 `family-id`；正式页面和窗口截图使用稳定 `page-id`/`window-id`，目录和文件名不得包含 backlog 任务编号。
- 页面截图必须实例化正式 View 和确定性脱敏 fixture，不得以 Gallery 中的相似布局替代真实页面截图。
- 全局 Design Token 只保存稳定标尺：颜色语义、间距刻度、圆角、图标尺寸、最小控件高度和动效时长。
- 页面 Padding、列宽、规则列表宽度、设置编辑控件宽度等布局值由 Shell、页面或复合组件中的唯一 owner 管理。
- 页面不得复制通用 Trigger/VisualState，但可以保留真实页面专用的 Grid、Margin、MinWidth 和滚动结构。
- ViewModel 不返回 Brush、Style、ControlTemplate、Thickness、CornerRadius 或其它 WPF 视觉类型。
- UI 平台能力通过 presentation port/adapter 暴露给可测试代码。
- 页面视觉迁移按纵向切片执行：先在正确控件族中补齐该页真实需要的公共资源和 Gallery fixture，再迁移一个窗口或页面；不得把同类 Style 分散到页面局部资源或多个公共字典。

## 7. 数据与文件

- 所有内部路径经过集中 resolver；不直接相信数据库绝对路径。
- 文件写入使用 staging + atomic move/replace。
- 多资源操作设计补偿或 journal。
- migration 只追加。
- 导出永不静默覆盖用户已有文件。

## 8. 安全

- 外部规则和文本均视为不可信输入。
- 不放宽脚本沙箱、路径根限制和请求校验来“提高兼容率”。
- 日志/诊断/UI 错误不输出正文、完整请求或凭据。
- 测试 fixture 使用虚构、脱敏内容。

## 9. 文档

- 数字编号文档描述终态，不写执行 Wave。
- `TASK_BACKLOG.md` 是唯一任务计划。
- 根 `README.md` 只写当前已实现能力。
- 行为/架构/数据边界变化时同步对应文档；纯重命名不制造无意义文档改动。

## 10. 依赖与格式

- 使用仓库固定的 .NET SDK 和中央包版本。
- 依赖变化后审查所有 `packages.lock.json`。
- 不为清理代码顺带升级无关依赖。
- `dotnet format --verify-no-changes` 必须通过。