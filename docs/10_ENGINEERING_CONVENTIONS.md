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
- 纯图标按钮统一使用 `Wpf.Ui.Controls.Button` + `App.Button.Icon`/`App.Button.DangerIcon`（媒体操作使用 `App.Media.Button`）并通过 `Button.Icon` 提供 `SymbolIcon`；不得把 `SymbolIcon` 直接放进 Button.Content，也不得在页面为 Button.Icon 内的图标单独设置 `Foreground` 或绑定祖先前景色。按钮样式是图标交互态颜色的唯一 owner，包含 `PressedForeground` 与最终 Icon 视觉树；Dark Mode 下不得让 Provider Pressed VisualState 回落为黑色/低对比度默认前景。
- 非 Button.Icon 的应用自有 `SymbolIcon` 使用显式 `App.Icon.Primary`/`Secondary`/`Accent`/`Danger` 语义样式；不得建立全局隐式 `SymbolIcon` Style。由 Wpf.Ui Navigation/Provider 模板拥有颜色的图标不额外覆盖。
- Style Gallery 的 fixture、示例文本和演示状态不得进入生产控件构造函数。
- Style Gallery 按稳定资源族组织场景；同一 Style/控件族只能有一个主要 Gallery scene，不按任务编号创建展示区。
- Gallery 截图使用稳定 `family-id`；正式页面和窗口截图使用稳定 `page-id`/`window-id`，目录和文件名不得包含 backlog 任务编号。
- 页面截图必须实例化正式 View 和确定性脱敏 fixture，不得以 Gallery 中的相似布局替代真实页面截图。
- 全局 Design Token 只保存稳定标尺：颜色语义、间距刻度、圆角、图标尺寸、最小控件高度和动效时长。
- Hover、Pressed、Selected/Checked、Focus 等交互颜色只能通过 `App.Brush.Interaction.*`、`App.Brush.Accent.*`、`App.Brush.Focus` 等公共语义资源表达；页面和 Feature XAML 不得硬编码交互色或建立同义局部 Brush。
- 一个控件树只允许一个主要 Hover owner。外层 Button/ListItem 与内部 Border/Pill 不得同时绘制大面积 Hover/Pressed Surface；确有独立行内按钮时，局部反馈必须限制在自身命中区。
- 当 ListBox/ListView 的业务视觉由内部 `App.Selection.*` Surface 持有时，外层 ItemContainer 必须使用共享的 chrome-free 容器样式/模板，只保留布局、虚拟化、选择和 Automation 所需行为；不得在多个页面复制 `<ControlTemplate><ContentPresenter/></ControlTemplate>`，也不得依赖默认 ItemContainer 模板“恰好透明”。
- “透明命中宿主”与“悬浮操作按钮”是不同语义。透明宿主若基于 Provider Button，即使 `Background=Transparent` 仍必须验证 Provider 模板最终像素；一旦 Provider 内部 VisualState 无法关闭额外状态层，应在 Button family 中使用受控的具名模板/自有控件集中接管，而不是在页面叠加透明 Brush。
- 定位/返回类悬浮操作统一由 `App.Button.FloatingIcon` 自己拥有 Background、Border、CornerRadius、Elevation、Foreground、Hover、Pressed、Keyboard Focus 与 Disabled；Surface family 不通过 Ancestor Button 绑定承担交互状态。页面不得再使用 `Button + App.Surface.FloatingAction` 双 owner 结构。FloatingIcon 的 Rest 可使用固定 Low Elevation，Hover 不提高阴影等级，Pressed 使用 `Interaction.Surface.Pressed` 而不是 `Accent.Subtle`；迁移后无引用的旧 Floating/FloatingAction 键直接删除。
- Mouse Hover/Pressed 与 Keyboard Focus 分离；不得用 IsMouseOver 触发 Focus Ring，也不得为了隐藏鼠标焦点而破坏 Tab/方向键导航和 Automation 语义。
- Menu 分组使用独立 Separator，不把分隔线伪装为 MenuItem Border；媒体 Slider 的 Track/Thumb 状态必须回到 `Media.xaml` 统一维护，不在 Player/MiniPlayer 页面复制模板。媒体 Slider 的交互 Track 与视觉 Rail 必须分离：`DecreaseRepeatButton` / `IncreaseRepeatButton` 只作为透明命中区，可见填充/剩余轨道由独立 VisualTrack 绘制并延伸到 Thumb 中心下方；不用负 Margin、局部缩窄、分段圆头或其它几何补丁隐藏接缝。
- `App.Feedback.PopupSurface` 是 Popup/Flyout 唯一可见圆角 Surface；PopupWindow/Wpf.Ui Flyout host、Provider bridge 与外层 chrome 必须透明且无第二份方形背景/Effect。WPF `Popup` 是矩形原生窗口，因此 `App.Feedback.PopupSurface` 不直接使用 `DropShadowEffect`。其它 Popup 控件族若确需阴影，必须显式预留透明 shadow extent 后再绘制，并用真实 Popup host 的像素渲染证明四角不会产生矩形残影；禁止用 Opacity、Margin、遮罩等局部补丁掩盖宿主裁切问题。
- 对原生 Popup、阴影、裁切、Slider Track/Thumb 等易产生“结构正确但像素错误”的 WPF 视觉问题，修复时先定位最终渲染 owner，再决定是否需要结构拆层。自动验收必须覆盖真实宿主或最终像素；仅验证 Style/Resource/VisualTree 属性不能作为关闭此类视觉缺陷的充分条件。
- 页面 Padding、列宽、规则列表宽度、设置编辑控件宽度等布局值由 Shell、页面或复合组件中的唯一 owner 管理。
- 只服务于单一 Feature 的响应式布局算法可以实现为 Feature 内部 Panel/布局组件。例如 Library 卡片列数与卡宽由 Library 自己的布局组件依据实际 viewport 计算；不得把 `300/360/16` 等页面专用尺寸提升为全局 Design Token，也不得让 ViewModel 返回 Width、Thickness 或其它 WPF 几何类型。
- 响应式网格使用实际布局可用宽度作为输入，不根据主窗口宽度、NavigationView 展开状态或 DPI 建立彼此独立的硬编码断点表。窗口、导航栏、滚动条和 DPI 变化应通过同一 Measure/Arrange 计算自然收敛。
- Library 书架属于从左到右扫描的内容集合，响应式 Panel 的排列起点固定为内容区左侧基线。达到最大卡宽后只停止继续拉伸，不通过居中整个 bounded group 重新分配剩余空间；最后一行同样从左侧开始，避免书籍数量变化时首列发生横向跳位。
- 页面不得复制通用 Trigger/VisualState，但可以保留真实页面专用的 Grid、Margin、MinWidth 和滚动结构。
- ViewModel 不返回 Brush、Style、ControlTemplate、Thickness、CornerRadius 或其它 WPF 视觉类型。
- UI 平台能力通过 presentation port/adapter 暴露给可测试代码。
- Shell 的 Light/Dark 快捷入口属于全局 presentation 行为，不在 MainWindow code-behind 直接调用 Wpf.Ui 主题 API或单独写 settings 文件。它必须复用正式主题偏好服务，并通过主题 runtime 的“当前实际生效 Light/Dark”能力处理 System 状态；快捷入口只能持久化显式 Light/Dark，重新进入 System 仍由 Appearance 设置页负责。
- 页面视觉变更按纵向切片执行：先确认已有公共资源是否足够；确有复用价值时在正确控件族中补齐公共资源和 Gallery fixture，再修改一个窗口或页面。不得因局部调整重新建立页面同义 Style 或把同类 Style 分散到多个字典。
- WPF 自动测试默认不在用户当前 Desktop 显示窗口；无窗口布局/渲染使用 `WpfControlHost`，真实 Window/Popup 生命周期只通过共享 TestKit 宿主进入隔离 Desktop。可见调试只在用户明确授权后启用。

## 7. 数据与文件

- 正式运行的数据根目录固定为 `AppContext.BaseDirectory/Data`，不得重新隐式回退到 `%LocalAppData%/NovelSpeaker`。
- 默认开发启动必须使用独立的 `%LocalAppData%/NovelSpeaker.Dev`；需要特殊根目录时通过 `NOVELSPEAKER_DATA_ROOT` 显式覆盖，不使用 `#if DEBUG` 决定持久化位置。
- 数据根目录解析与根目录内部的路径布局是两个职责：前者决定“数据放在哪里”，后者只负责 `app.db`、`settings.json`、`Books/`、`Cache/`、`Operations/` 和 `Logs/` 等稳定布局。
- 自动测试必须显式注入临时数据根，不得读取或写入正式 `Data/`、开发目录或旧数据目录。
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
- Codex 完成任务后保留 `TASK_BACKLOG.md` 中的任务条目，标记 `[x]` 并简要记录完成成果；不得把“完成任务”解释为删除任务。
- 删除、重写或插入任务属于新的任务规划阶段操作。规划阶段若决定清理旧任务，可直接更新当前 Backlog，历史状态由 Git 记录；仓库不维护任务归档目录或归档文档。
- 行为/架构/数据边界变化时同步对应文档；纯重命名不制造无意义文档改动。

## 10. 依赖与格式

- 使用仓库固定的 .NET SDK 和中央包版本。
- 依赖变化后审查所有 `packages.lock.json`。
- 不为清理代码顺带升级无关依赖。
- `dotnet format --verify-no-changes` 必须通过。