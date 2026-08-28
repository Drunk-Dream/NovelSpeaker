# NovelSpeaker 视觉设计系统

## 1. 设计定位

NovelSpeaker 是适合长期驻留桌面的轻量听书工具。界面采用柔和表面层级、克制强调色和轻量悬浮质感，并保持 Windows 与 Wpf.Ui 的原生交互习惯。

视觉系统的目标不是让所有页面使用同一套固定布局，也不是把所有标准控件替换成自定义模板，而是建立稳定、可搜索、可组合的公共资源，使相同语义只定义一次，同时允许页面根据真实任务调整密度和结构。

界面遵循以下定位：

- 书籍内容、当前播放状态和用户正在执行的操作是视觉中心。
- 不模拟纸质书籍，不使用大面积装饰插画，也不采用音乐软件式的强封面视觉。
- Wpf.Ui 继续拥有标准控件的基础模板和交互。
- NovelSpeaker 通过语义 Palette、稳定 Token、具名 Style、自有控件和 Feature 组件逐层扩展。
- 公共资源只抽取已经具有稳定复用价值的内容，不为假想需求提前建立万能组件。

## 2. 核心原则

### 2.1 内容优先

- 页面只保留完成任务所需的信息。
- 主操作明确，次操作克制，低频操作进入菜单、Flyout 或次级区域。
- 不通过更多卡片、标签、边框或颜色增加“丰富感”。

### 2.2 单一强调色

Accent 只用于：

- 当前选中或激活状态。
- 页面唯一主操作。
- 当前进度。
- 键盘焦点。

普通文本、普通图标和结构边界使用中性色。Success、Warning 和 Danger 只表达真实状态，不作为装饰。

### 2.3 表面层级优先于边框堆叠

视觉分组优先通过背景表面差异、间距和留白实现。边框只用于窗口外轮廓、输入边界、选中、焦点和深浅表面不足以区分的场景。

同一区域最多出现三级可见表面。页面不得出现多层带完整边框和阴影的嵌套卡片。

### 2.4 主题结构一致

浅色和深色模式使用同一控件树、布局、尺寸和组件结构，只切换 Wpf.Ui theme 与 NovelSpeaker Palette。页面不复制两套 XAML。

### 2.5 单一资源所有者

同一视觉语义只允许一个定义位置：

- 一个资源键只有一个正式定义。
- 一个标准控件族只在一个 Style 字典中维护。
- 一个自有控件的默认模板只在一个 ControlTheme 字典中维护。
- 页面专用布局值不进入全局 Token。
- 正式代码中不存在长期别名、兼容键或并行新旧实现。

## 3. 资源与控件的职责边界

### 3.1 Palette

Palette 定义会随主题变化的语义颜色和 Brush。它不包含控件尺寸、Padding、模板或页面布局。

典型资源：

```text
App.Brush.Window.Background
App.Brush.Canvas
App.Brush.Surface.Primary
App.Brush.Surface.Secondary
App.Brush.Surface.Raised
App.Brush.Text.Primary
App.Brush.Text.Secondary
App.Brush.Text.Tertiary
App.Brush.Border.Subtle
App.Brush.Border.Strong
App.Brush.Accent
App.Brush.Accent.Hover
App.Brush.Accent.Pressed
App.Brush.Accent.Subtle
App.Brush.Focus
App.Brush.Success
App.Brush.Warning
App.Brush.Danger
App.Brush.Danger.Subtle
App.Brush.Danger.Text
App.Brush.Danger.Pressed
App.Brush.Danger.Pressed.Text
```

页面不得直接引用 Wpf.Ui 的主题色来表达 NovelSpeaker 业务语义，也不得写业务无关的十六进制颜色。

`App.Brush.Window.Background` 只属于 Window/Shell 壳层。主窗口中的 `NavigationView` 内容宿主拥有 `App.Brush.Canvas`、Shell 内容边界和左上圆角；正式 `Page` 根节点保持透明，只拥有页面 Padding、滚动和内容布局。页面不得再绘制一块不透明 Canvas 覆盖 Shell 内容宿主，否则会遮住 Shell 的圆角边界。`NavigationViewContentBackground` 与 `NavigationViewContentGridBorderBrush` 是 Provider 模板所需的适配投影键，分别跟随 `App.Brush.Canvas` 与应用边界语义，并由主题 Palette 保持 Light/Dark 同步；它们不是新增业务语义色：Palette 通过现有语义 Brush 的 `Color` 绑定定义投影，运行时再将投影解析为对应的 canonical Brush。正式页面不得直接引用这些 Provider 键。

### 3.2 Token

Token 是跨窗口稳定的标尺或效果，不包含控件结构。

允许进入全局 Token 的内容：

- `4/8/12/16/20/24/32/40/48` 间距标尺。
- 小、中、大圆角。
- 常用图标尺寸。
- 紧凑、标准和媒体控件最小高度或命中区。
- 字号、字重和行高。
- 动效时长。
- 阴影等级。
- 通用禁用透明度。

禁止进入全局 Token 的内容：

- 页面 Padding。
- 规则列表宽度。
- 设置页右侧控件固定宽度。
- 编辑器标签列宽。
- 页面专用按钮间距。
- Shell 与页面之间的补偿性 Margin。
- 只服务于一个页面或一个 Feature 的尺寸。

Token 使用 `App.` 前缀和分层名称，例如：

```text
App.Space.8
App.Radius.Medium
App.Size.Icon.Standard
App.Size.Control.Compact
App.Size.MediaButton
App.Text.Size.PageTitle
App.Text.Weight.SemiBold
App.Motion.Standard
App.Elevation.Medium
```

### 3.3 Style

Style 适合描述单个现有控件的外观变体，例如 Button、TextBox、ListBoxItem、ProgressBar 或 Border。

Style 应满足：

- 不新增业务语义内容。
- 不硬编码示例文本或命令。
- 不要求多个内容槽。
- 不持有页面状态机。
- 可以通过 `BasedOn` 复用同控件族的基础样式。

标准 WPF/Wpf.Ui 控件的 NovelSpeaker Style 必须使用显式 `x:Key`。全局资源不得为标准控件定义无键隐式 Style。

### 3.4 NovelSpeaker 自有控件

自有控件是 NovelSpeaker 定义、可在多个正式页面复用的控件类。它们用于封装稳定的重复结构、内容槽和视觉状态，而不是封装 Gallery 示例。

正式公共控件位于：

```text
src/NovelSpeaker.App/Shared/Presentation/Controls/
```

控件模板位于：

```text
src/NovelSpeaker.App/Shared/Theming/Resources/ControlThemes/
```

自有控件采用按类型自动生效的默认 Style。页面通常直接使用控件：

```xml
<controls:AppSettingsRow
    Title="朗读章节标题"
    Description="开启后，每章正文前先朗读章节标题。">
    <ui:ToggleSwitch IsChecked="{Binding ReadChapterTitle}" />
</controls:AppSettingsRow>
```

只有 Compact、Emphasized 等真实变体才显式指定具名 Style。无键隐式 Style 只允许作用于 NovelSpeaker 自有控件，不允许接管标准控件。

### 3.5 Feature 组件

只在某个业务域或相近页面族中复用的视图不进入全局控件目录，而位于对应 Feature 的 `Components` 或 `Shared` 子目录。

典型所有权：

- `BookCardView` 属于 Library Feature。
- 规则列表项属于 Rules 页面族。
- `PlayerView` 属于 Playback Feature。
- 缓存章节项属于 Cache Feature。

Feature 组件可以使用全局 Token、Style 和自有控件，但不得把领域对象、命令或业务状态反向写入全局主题层。

### 3.6 页面局部资源

页面继续拥有：

- 页面内部 Padding。
- 分栏比例和最小列宽。
- 页面专用工具栏排列。
- 虚拟化列表和滚动宿主。
- 只在该页面成立的 Trigger、DataTemplate 和状态投影。
- 页面特有的固定或自适应宽度。

页面不得复制已经属于公共 Style 或自有控件模板的 Hover、Focus、Selected、Disabled、Validation 和排版规则。

### 3.7 Style Gallery fixture

Style Gallery fixture 是开发测试数据和场景构造，不是生产控件。

- fixture 文本、按钮、进度值和状态组合只存在于 `tools/NovelSpeaker.StyleGallery`。
- 生产程序集中的公共控件不得在构造函数中创建示例内容。
- Gallery 可以实例化正式 Style 和控件，但不能把 Gallery 专用类型放入产品页面。

## 4. Wpf.Ui 与 NovelSpeaker 的所有权

### 4.1 Wpf.Ui Provider 层

Wpf.Ui 持有：

- 标准 WPF/Wpf.Ui 控件的默认模板。
- NavigationView、ToggleSwitch、ComboBox、CheckBox、ContentDialog、Snackbar 和 FluentWindow 的基础交互。
- Fluent 主题资源和标准 Visual State。

Provider dictionaries 在应用启动时加载，并在进程生命周期内保持稳定。默认情况下 NovelSpeaker 不复制标准控件完整模板，也不通过主题切换代码重新插入标准控件 Style。应用级 Style 优先通过字体、颜色、尺寸、Padding、对齐等模板输入属性完成定制，并保留 Provider 的交互与可访问性语义。

允许存在**受控的控件族级模板例外**：当已确认的应用交互/视觉合同无法通过 Provider 暴露的 Style 输入属性完成，且为整个稳定控件族接管一个局部模板比新增包装控件更简单时，可以在该控件族所属资源字典中维护经过裁剪的应用模板。例外必须保留键盘、Focus、Disabled、Editable、滚动、Popup 定位等原有行为，并由专项契约测试覆盖；不得把模板复制到页面资源中。

当前 `ComboBox` 是这一例外。`Inputs.xaml` 的 `App.Input.ComboBox.Standard` / `Compact` 维护基于 Wpf.Ui 4.3.0 结构适配的闭合态与 Popup 模板，以统一全表面命中、左右布局、Raised Popup、圆角、间距和选中状态；仍复用 Provider 的基础交互辅助资源。`NavigationView` 不属于该例外：Shell 保留 Provider 内容宿主模板和其左上圆角，页面通过透明根背景避免遮挡。无论是否使用控件族模板，`App.Input.ComboBox.*` 都必须保留 `HorizontalContentAlignment=Stretch` 语义。
`ToggleSwitch` 也不接管 Provider 模板。对于 Wpf.Ui 4.3.0 无标签模板仍保留 `*` Content 列的问题，应用只在三个内容属性全部为空时通过 Style Trigger 将 Width 收敛到 Provider 当前 40 px 可见轨道；一旦存在任何标签内容，Width 回到 `Auto`。如果未来 Provider 改变模板或轨道宽度，升级审计必须同步复核这一局部兼容约束。

### 4.2 Provider Style Bridge

确实需要扩展 Wpf.Ui 基础 Style 时，通过专用桥接资源建立稳定别名，例如：

```text
Provider.Button
Provider.UiButton
Provider.TextBox
Provider.PasswordBox
Provider.ComboBox
Provider.ComboBoxItem
Provider.CheckBox
Provider.ToggleSwitch
Provider.NavigationItem
Provider.MenuItem
```

桥接层只引用已加载的 Provider 资源：

- 不设置页面语义。
- 不写 NovelSpeaker 颜色。
- 不替换完整模板。
- 不包含控件变体。
- 不被页面直接引用。

NovelSpeaker 的具名 Style 通过 `BasedOn` 依赖桥接键，避免各字典分别猜测 Wpf.Ui 资源名和加载顺序。

需要使用 Wpf.Ui 专属控件状态属性的样式（例如 `MouseOverBackground` 和
`PressedBackground`）必须基于对应的 Provider bridge；标准 `Button` 的
`Background` 状态值不会覆盖 Wpf.Ui 模板内部的状态层。

### 4.3 Dialog、Flyout 与 Snackbar

Dialog、Flyout 和 Snackbar 的宿主、焦点管理、Escape、默认按钮和生命周期由 Wpf.Ui 提供。

NovelSpeaker 只提供：

- 内容区域排版。
- Raised Surface、Padding 和边界样式。
- 主、次、危险操作按钮样式。
- 验证、加载、空状态和错误状态控件。
- 页面或 ViewModel 提供的真实标题、说明、命令和状态。

不建立硬编码内容的 `DialogShell`、`FlyoutSurface` 或 `SnackbarContent` 生产控件。

## 5. 最终目录结构

```text
src/NovelSpeaker.App/Shared/
├─ Presentation/
│  └─ Controls/
│     ├─ Common/
│     │  ├─ AppPageHeader.cs
│     │  └─ AppSectionSurface.cs
│     ├─ Settings/
│     │  ├─ AppSettingsList.cs
│     │  ├─ AppSettingsGroup.cs
│     │  ├─ AppSettingsRow.cs
│     │  └─ AppSettingsNavigationRow.cs
│     ├─ Forms/
│     │  └─ AppFormField.cs
│     └─ Feedback/
│        └─ AppStatusView.cs
└─ Theming/
   ├─ Provider/
   │  └─ ProviderStyleBridge.xaml
   ├─ Palettes/
   │  ├─ Palette.Light.xaml
   │  └─ Palette.Dark.xaml
   └─ Resources/
      ├─ Tokens/
      │  ├─ Metrics.xaml
      │  ├─ TypographyTokens.xaml
      │  ├─ Motion.xaml
      │  └─ Elevation.xaml
      ├─ Styles/
      │  ├─ Typography.xaml
      │  ├─ Surfaces.xaml
      │  ├─ Buttons.xaml
      │  ├─ Icons.xaml
      │  ├─ Inputs.xaml
      │  ├─ Selection.xaml
      │  ├─ Navigation.xaml
      │  ├─ Menus.xaml
      │  ├─ Progress.xaml
      │  ├─ Media.xaml
      │  └─ Feedback.xaml
      └─ ControlThemes/
         ├─ Common.xaml
         ├─ Settings.xaml
         ├─ Forms.xaml
         └─ Feedback.xaml
```

最终结构中不存在：

- 综合性的 `SemanticStyles.xaml`。
- 同时容纳多个无关控件族的 `ComponentStyles.xaml`。
- 同时容纳导航、菜单、进度和反馈的 `NavigationFeedbackStyles.xaml`。
- 生产用 `Shared/Theming/Components` 控件类目录。
- Legacy、Compat、Old、V2 或其它长期兼容目录。

文件拆分按“同一控件族或同一资源职责集中维护”进行，不采用每个 Style 一个文件，也不把同一控件族分散到多个字典。

## 6. 资源加载顺序

应用资源按以下顺序稳定加载：

1. Wpf.Ui theme/provider dictionaries。
2. `ProviderStyleBridge.xaml`。
3. 当前主题 Palette。
4. Token 字典。
5. Typography 与 Surface Style。
6. 标准控件族 Style。
7. NovelSpeaker 自有控件 ControlTheme。
8. 窗口或页面局部资源。

依赖方向固定为：

```text
Provider → Bridge
Palette + Tokens → Styles
Styles + Tokens → ControlThemes
Global resources → Feature components → Pages
```

禁止反向依赖：

- Palette 不引用 Style。
- Token 不引用控件资源。
- Style 不引用页面资源。
- ControlTheme 不引用 Feature 或页面资源。
- 全局资源不引用具体 ViewModel、命令或业务模型。

主题切换只更新 Wpf.Ui theme 和 Palette。Token、Style、ControlTheme 和资源字典实例保持稳定。

## 7. 资源命名规则

### 7.1 通用规则

- 所有 NovelSpeaker 公共键使用 `App.` 前缀。
- 名称表达语义和控件族，不表达页面名。
- 相同词序保持一致，例如 `App.Button.DangerIcon`，不混用 `Danger.IconButton`。
- 同一键只在一个字典中定义。
- 最终代码不保留旧键别名。

### 7.2 Typography

```text
App.Typography.PageTitle
App.Typography.SectionTitle
App.Typography.GroupTitle
App.Typography.ItemTitle
App.Typography.Body
App.Typography.Secondary
App.Typography.Caption
App.Typography.FormLabel
App.Typography.Validation
```

### 7.3 Surface

```text
App.Surface.Canvas
App.Surface.Section
App.Surface.Card
App.Surface.Secondary
App.Surface.Raised
App.Surface.FloatingAction
App.Surface.Popup
```

Surface Style 只处理背景、边界、圆角、Padding 和效果，不包含业务内容。
`App.Surface.FloatingAction` 是透明悬浮 Button 内部的圆形视觉表面，统一承载 Normal、Hover 与 Pressed 状态；外层 Button 只保留命中区域、焦点和可访问性语义，不绘制方形外框。

### 7.4 Button

```text
App.Button.Primary
App.Button.Secondary
App.Button.Subtle
App.Button.Icon
App.Button.Danger
App.Button.DangerIcon
App.Button.ToolbarValue
App.Button.Floating
```

`App.Button.DangerIcon` 的默认图标和背景保持中性；Hover 时背景进入 `App.Brush.Danger`，Pressed 时进入 `App.Brush.Danger.Pressed` 并切换到可读的危险文本色。它表达危险动作，而不是让危险色常驻。

`App.Button.Icon` 与 `App.Button.DangerIcon` 的宿主统一为 `Wpf.Ui.Controls.Button`。纯图标按钮必须通过 `Button.Icon` 提供 `SymbolIcon`，图标的 Normal、Hover、Pressed、Disabled 前景色由 owning Button 的 `Foreground`/Provider 状态属性统一拥有；页面只负责 Symbol、Command、Tooltip 和 AutomationName，不在 `SymbolIcon` 上手工绑定、硬编码或覆盖 `Foreground`。`App.Media.Button` 继承同一 owner-foreground 合同。

应用自有、且不位于 Button.Icon 中的独立 `SymbolIcon` 使用显式语义样式：`App.Icon.Primary`、`App.Icon.Secondary`、`App.Icon.Accent`、`App.Icon.Danger`。NavigationView 等由 Wpf.Ui Provider 自己拥有颜色的图标继续交给 Provider，不强制覆盖。禁止增加全局隐式 `SymbolIcon` Style，以免破坏 Button、Navigation 和状态图标各自的颜色所有权。

媒体按钮从 Button 基础变体派生，不在 Media 字典复制完整 Button 模板。

### 7.5 Input

```text
App.Input.TextBox.Standard
App.Input.TextBox.Compact
App.Input.PasswordBox.Standard
App.Input.PasswordBox.Compact
App.Input.ComboBox.Standard
App.Input.ComboBox.Compact
App.Input.ComboBox.Item
App.Input.CheckBox.Standard
App.Input.CheckBox.Compact
App.Input.ToggleSwitch.Standard
App.Input.ToggleSwitch.Compact
```

`App.Input.ComboBox.Standard` 与 `App.Input.ComboBox.Compact` 遵循以下统一契约：

- 整个控件表面都是同一个点击/按压目标，不得只允许选中文案和 Chevron 附近响应。
- 选中文案占据左侧可用空间并左对齐，Chevron 固定靠右；控件变宽时，新增空间进入文案与 Chevron 之间，而不是留在 Chevron 右侧。
- Hover、Pressed/Open、Focus、Disabled 与 Validation 反馈作用于整个控件表面，Chevron 不形成独立按钮底色。
- Popup 是 ComboBox 控件族的一部分，统一使用 `App.Brush.Surface.Raised`、`App.Brush.Border.Subtle`、1 px 边界、`App.Radius.Medium` 和 `App.Elevation.Medium`；闭合态与 Popup 之间保留约 4 px 视觉间隔。
- Popup 最小宽度不得小于闭合态 ComboBox；选项内容更长时允许 Popup 在合理范围内自然扩展，不强制压缩到闭合态宽度。
- `App.Input.ComboBox.Item` 的 Normal 背景透明，Hover 使用 `App.Brush.Surface.Secondary`，Selected 使用弱 `App.Brush.Accent.Subtle` 背景并保留左侧 `App.Brush.Accent.Default` 状态条，Disabled 文本使用 `App.Brush.Text.Tertiary`；Item 使用 `App.Radius.Small`。
- 纯字符串选项在闭合态空间不足时保持单行，并使用 `CharacterEllipsis`；Chevron 的位置不得随文案长度变化。该行为由 `Inputs.xaml` 中 ComboBox Style 自身的局部 `String` DataTemplate 提供，不建立页面专属模板。
- 使用对象项、`DisplayMemberPath` 或自定义 `ItemTemplate` 的页面，若显示文本可能超长，则对应显示模板必须提供等价的单行截断；不得为此复制 ComboBox 控件族模板。
- `App.Input.ComboBox.*` 必须保持 `HorizontalContentAlignment=Stretch`。将其改为 `Left` 会使 Provider 内部布局按内容宽度收缩，造成 Chevron 靠近文案、右侧出现无效空白以及空白区域无法点击。
- 页面不得覆盖 ComboBox Popup Palette、Popup CornerRadius、ItemContainerStyle 或 Selection 状态；新的 ComboBox 视觉能力必须回到 `Inputs.xaml` 的同一控件族中维护。

`App.Input.ToggleSwitch.Standard` 与 `App.Input.ToggleSwitch.Compact` 遵循以下尺寸与布局契约：

- ToggleSwitch 是内容驱动的状态控件，不是字段型控件；应用级 Style 不设置全局固定 `Width` 或大于 Provider 模板本体需求的 `MinWidth`。Wpf.Ui 4.3.0 模板内部存在 `Auto` 开关列与 `*` Content 列，透明根 Grid 会让无标签场景仍可能保留不可见横向 HitTest 区域，因此 `Inputs.xaml` 在 `Content`、`OnContent`、`OffContent` 全部为空时，条件式将控件宽度收敛到与 Provider 可见轨道一致的 40 px。该约束只修正纯开关几何，不改变 Provider 模板所有权。
- 带 `Content`、`OnContent` 或 `OffContent` 的 ToggleSwitch 仍使用同一 Standard/Compact Style，条件宽度约束不生效，由 Provider 模板按“开关本体 + Content”自然计算宽度；不得仅因是否带标签而派生 `SwitchOnly`、`WithContent` 等重复视觉变体。
- Standard/Compact 可以继续通过 `MinHeight` 维持一致的纵向可操作尺寸；纯开关的横向 Focus/HitTest 边界必须贴合 40 px 可见轨道，带标签时再随真实内容扩展，不以 Gallery 对齐或页面排版为理由人为拉宽。
- ToggleSwitch 自身不强制 `HorizontalAlignment=Right`。控件负责自身 DesiredSize，`AppSettingsRow`、表单、Dialog 等宿主负责决定其 Left/Center/Right 布局；设置项右侧纯开关由 ValuePresenter 右对齐后，应以开关可见本体的右边缘与其他字段的右边缘对齐。
- Input family 的宽度所有权按交互语义区分：ComboBox、TextBox、PasswordBox 等字段型控件可以由页面/表单提供明确宽度或合理 MinWidth；ToggleSwitch、CheckBox 等内容型状态控件默认内容自适应；Icon Button 等固定点击目标由对应控件族定义方形尺寸。不得仅为“看起来整齐”向内容型控件加入无语义的横向空白命中区域。

### 7.6 Selection 与列表容器

```text
App.Selection.ListItem
App.Selection.CardItem
App.Selection.CurrentItem
App.Selection.DropTarget
App.Selection.MultiSelectItem
```

这些 Style 只表达容器状态，不承载书籍、章节或规则内容。

### 7.7 Navigation 与 Menu

```text
App.Navigation.Entry
App.Navigation.SettingsEntry
App.Menu.Surface
App.Menu.ContextSurface
App.Menu.Item
App.Menu.DangerItem
App.Menu.GroupHeader
```

### 7.8 Progress 与 Media

```text
App.Progress.Standard
App.Progress.Compact
App.Progress.MediaTrack
App.Media.Slider
App.Media.ProgressSlider
App.Media.Button
App.Media.ControlSurface
```

ProgressBar 与 Slider 保持不同控件语义和测试，不共用模板。
`App.Progress.MediaTrack` 用于播放页和迷你播放器：已播放部分使用 Accent，未播放部分使用可见的 Subtle Border 轨道；`App.Media.ProgressSlider` 只保留拖动和滑块交互，Provider 自带轨道保持透明。
播放页和迷你播放器的媒体按钮使用统一尺寸和中性状态；播放/暂停不建立独立的 Accent 主按钮变体。

### 7.9 Feedback

```text
App.Feedback.PopupSurface
App.Feedback.FlyoutHost
App.Feedback.DialogBody
App.Feedback.DialogTitle
App.Feedback.DialogMessage
App.Feedback.ValidationText
App.Feedback.InlineMessage
App.Feedback.SnackbarBody
App.Feedback.SnackbarTitleTemplate
App.Feedback.SnackbarMessageTemplate
App.Feedback.Snackbar
```

Dialog、Flyout 和 Snackbar 的宿主及生命周期由 Wpf.Ui 持有。`App.Feedback.DialogBody` 只是透明、无边框、无阴影的 Dialog 内容布局容器，不再拥有独立 Surface；Flyout/Popup 的 `App.Feedback.PopupSurface` 继续作为其唯一可见 Surface。加载、空状态、无结果和错误使用 `AppStatusView`，不分别建立四套硬编码控件类。

## 8. 基础视觉规范

### 8.1 颜色

| 语义 | 浅色主题建议 | 深色主题建议 | 用途 |
|---|---|---|---|
| Window Background | `#F4F5F9` | `#101218` | 窗口壳层 |
| Canvas | `#F8F9FC` | `#15181F` | 页面画布 |
| Primary Surface | `#FFFFFF` | `#1B1F27` | 卡片、输入、工具区 |
| Secondary Surface | `#F1F3F8` | `#232832` | 次级控制条和分组 |
| Raised Surface | `#FFFFFF` | `#272C36` | Flyout、Dialog、浮窗 |
| Primary Text | `#20242C` | `#F2F4F8` | 标题和正文 |
| Secondary Text | `#626A77` | `#AEB5C1` | 元数据和说明 |
| Tertiary Text | `#8A919D` | `#7F8794` | 占位和弱提示 |
| Accent | `#5B6FD8` | `#7C8CFF` | 主操作、当前状态、进度 |
| Danger | `#C83C4A` | `#FF7A86` | 危险操作的 Hover 背景和错误 |
| Danger Subtle | `#FBE6E9` | `#4A2028` | 低强调危险状态 |
| Danger Text | `#FFFFFF` | `#160B0D` | 文字或图标位于 Danger 背景上 |
| Danger Pressed | `#A82F3D` | `#B83E4B` | 危险操作的 Pressed 背景 |
| Danger Pressed Text | `#FFFFFF` | `#F2F4F8` | 文字或图标位于 Danger Pressed 背景上 |
| Warning | `#A66A00` | `#F2B84B` | 风险提示 |
| Success | `#2E7D5B` | `#66C99A` | 完成和健康状态 |

Accent 至少提供 Default、Hover、Pressed、Subtle 和 Focus。浅色 AccentSubtle 约为 Accent 的 `10%–14%`，深色约为 `16%–20%`。

### 8.2 表面、描边与阴影

只使用以下表面层级：

1. Window Background。
2. Canvas。
3. Primary/Secondary Surface。
4. Raised Surface。

所有权规则：

- Window Background 只由 Window/Shell 使用，用于窗口壳层、导航壳层或窗口边缘区域。
- 主窗口 `NavigationView` 内容宿主拥有完整 Canvas、Shell 内容边界和左上圆角；正式 Page 根节点透明，页面 Padding、滚动留白和内容间距位于该 Canvas 上。
- 页面不得通过不透明根背景覆盖 Shell 内容边界，也不得为了制造层级在 Canvas 外再露出一圈 Window Background。
- Primary/Secondary Surface 只用于 Canvas 内部真实需要分组的内容块。

规则：

- 普通静态卡片默认不带阴影。
- Hover 卡片可使用低抬升。
- Menu/Flyout 使用中抬升。
- Dialog 使用高抬升。
- transient UI 遵循 **Single Surface**：一个 Dialog、Flyout、Popup 或独立状态浮窗默认只允许一个主可见 Surface。宿主已经提供完整 Surface 时，内容不得再次套 `Card`、`Section`、`Raised` 或其它带完整背景、边框、圆角和阴影的 Surface。
- ContentDialog 自身是 Dialog 的唯一主 Surface；内部使用透明的 `App.Feedback.DialogBody`，通过 Typography、间距和必要的 Divider 建立信息层级，不使用 Card-in-Dialog。
- StartupStatusWindow 等独立 transient Window 由 Window 自身承担 Raised Surface；内部状态内容使用 `AppStatusView` 的 Embedded 模式，不再增加第二层 Section Surface。
- 复杂 Dialog 确有二级信息分组时，可以使用无阴影的弱背景或 Divider；完整 Card-in-Dialog 只能作为有明确独立语义的例外，不能成为默认布局。
- FocusRing 独立于普通 Border，不通过改变尺寸造成布局抖动。
- 深色主题降低纯黑阴影强度，并用低透明度浅色描边补充边缘。

### 8.3 尺寸和圆角

| 语义 | 建议值 |
|---|---:|
| 紧凑图标按钮 | 不小于 `32 × 32` |
| 普通工具按钮 | `36–40` 高 |
| 媒体按钮命中区 | `48 × 48` |
| 列表行 | `48–56` 高 |
| 设置导航行 | `52–60` 高 |
| 输入控件圆角 | `6–8` |
| 列表行、普通卡片圆角 | `10` |
| 分组工具条、Dialog 圆角 | `12` |
| 主表面、迷你播放器圆角 | `14–16` |

页面最终宽度、Padding 和间距由页面拥有，不由这些最小合同替代。

### 8.4 排版

默认字体栈：

```text
Segoe UI Variable Text
Microsoft YaHei UI
Segoe UI
sans-serif
```

| 语义 | 字号 | 字重 |
|---|---:|---|
| Page Title | `24` | SemiBold |
| Section Title | `18` | SemiBold |
| Group Title | `13–14` | SemiBold，低于设置行标题的视觉权重 |
| Item Title | `15–16` | SemiBold |
| Body | `14` | Regular |
| Secondary | `12–13` | Regular |
| Caption | `11–12` | Regular |

正文阅读字体、字号、行距和段间距由阅读设置控制，不与表单字体绑定。

### 8.5 状态与动效

- Hover 与 Selected 必须能同时表达。
- Error、Selected 和 Disabled 不能只依赖颜色。
- Pressed 和图标切换使用 `80–100 ms`。
- Hover、展开和状态层使用 `140–180 ms`。
- Flyout、Dialog 和局部进入使用 `200–240 ms`。
- 减少动画模式保留最终状态，移除位移和缩放。

## 9. 正式自有控件

### 9.1 AppPageHeader

统一页面顶部的返回入口、标题、副标题和右侧操作区。

职责：

- 提供 `Title`、`Description`、`BackCommand` 和 `Actions` 内容槽。
- 搜索、排序、导入、新建等页面级顶部操作放入 `Actions`，与标题垂直居中对齐；页面不得在 Header 下方再建立一行同语义工具栏。
- 处理标题省略、间距、焦点和 Automation 属性。
- 不拥有页面 Padding、ScrollViewer 或主体布局。
- 无返回语义的一级页面不显示返回按钮。

### 9.2 AppSectionSurface

用于需要标题、说明和内容区域的稳定区块。

职责：

- 提供 Header、Description、Content 和 Footer/Actions 槽。
- 统一区块表面、内部 Padding 和标题间距。
- 不承担页面级 Grid、分栏或滚动。
- 不允许在其内部再次机械嵌套同等级 Section Surface。

### 9.3 AppSettingsList

设置子页面使用的无标题设置列表表面。它负责保留与设置首页一致的列表视觉，但不表达分类语义。

职责：

- 提供 Items 槽，不提供 Header、Description 或 Footer。
- 默认使用 Primary Surface、`App.Radius.Medium`、统一 20 px 内容 Padding 和相邻条目之间的 Subtle Divider。
- 最后一项不绘制底部分隔线；首尾边界由列表容器统一处理，页面不得再使用 `SettingsLastRow...` 一类样式。
- 所有普通设置子页面只使用一个 `AppSettingsList` 承载页面主要设置项；单项页面同样使用该 Surface，不退化为裸 `AppSettingsRow`。
- 不提供整行 Hover/Pressed。普通 `AppSettingsRow` 的可操作目标仍是右侧真实控件；需要整行导航时使用 `AppSettingsNavigationRow`。
- 不负责页面 Padding、ScrollViewer、MaxWidth、保存时机或业务分组。

### 9.4 AppSettingsGroup

组织设置首页等确实需要分类的信息集合。`AppSettingsGroup` 继承 `AppSettingsList` 的列表容器行为，并在同一 Surface 之上增加分组 Header/Description/Footer。**普通设置子页面不使用该控件。**

职责：

- 与 `AppSettingsList` 共享 Primary Surface、圆角、20 px Padding、ItemContainer 和行分隔线规则，不复制另一套列表视觉。
- 额外提供 Header、Description 和 Footer 槽。
- 设置体系中的主要正式调用点是 `SettingsPage` 首页，用于“常用 / 文本处理 / 应用”等导航类别；不得因为子页面有多个设置项就机械套用 Group。
- Header 使用低于设置行标题的 `App.Typography.GroupTitle` 视觉层级，不使用页面级 `SectionTitle`。
- Header 与设置行标题保持稳定左侧基线；Items 区与 Header 之间保留稳定间距。

### 9.5 AppSettingsRow

封装设置标题、说明和右侧值/控件区域。它是 `AppSettingsList` 与 `AppSettingsGroup` 的行内容，不自行拥有外层卡片 Surface。

职责：

- 提供 Title、Description 和 Value/Content 槽。
- 支持 ToggleSwitch、ComboBox、TextBox、Button 和只读值。
- 设置行是行级纵向密度的唯一 owner；列表 ItemContainer 不叠加上下 Padding。
- 默认横向 Padding 为 0 或仅保留最小必要值；列表 Surface 的 20 px Padding 负责统一左右基线。
- 只规定最小高度和内部布局，不规定页面统一右侧固定宽度。
- 窄宽度下允许右侧内容换行或转为纵向布局。

### 9.6 AppSettingsNavigationRow

用于设置首页和二/三级入口。

职责：

- 提供 Icon、Title、Description、Command 和右侧 Chevron。
- 整行可点击并具备键盘 Focus。
- Hover、Pressed、Disabled 和 Focus 由控件模板统一处理。

### 9.7 AppFormField

统一规则编辑和其它表单中的 Label、说明、输入区域和错误信息。

职责：

- 提供 Label、Description、Content、Error 和 Required 状态。
- Error 文案位于字段下方，不只改变边框颜色。
- 不决定输入控件类型和页面列宽。
- 字段自身不保存业务值，不包含验证逻辑。

### 9.8 AppStatusView

统一加载、空状态、无结果、错误和轻量成功提示。

职责：

- 通过状态种类和页面提供的 Title、Description、图标及操作槽渲染。
- 支持 PrimaryAction 和 SecondaryAction。
- 默认模式拥有一个 `App.Surface.Section`，适合页面内 Loading、Empty、NoResult、Error 和轻量 Success 状态。
- `IsEmbedded=true` 时移除自身背景、边框、圆角、Padding 和阴影，只保留状态图标、Title、Description 与操作槽；只用于外层已经拥有唯一主 Surface 的 Dialog/Window/Popup 内容。
- Embedded 模式不得被页面用来规避正常 Section Surface；是否嵌入由外层 Surface 所有权决定。
- 不硬编码“导入”“重试”等业务文案。
- 不替代 Snackbar 或需要立即决策的 Dialog。

## 10. 不进入全局控件层的内容

以下内容即使可以复用，也不默认成为 `Shared/Presentation/Controls`：

- BookCard：包含书籍信息、播放状态和书籍命令，属于 Library Feature。
- RuleListItem：包含规则名称/摘要、独立 ToggleSwitch、选择状态、右键菜单入口和可选排序状态，属于 Rules 页面族；不承载 TTS 当前规则语义。
- Chapter/Cache row：包含章节与缓存语义，属于对应 Feature。
- 完整 RuleWorkbench：三个规则页面字段、列宽和操作不同，不建立万能工作台。
- 完整 MediaControlBar：播放页和迷你播放器结构、尺寸与操作密度不同，优先共享 Button、Slider 和 Surface Style。
- DialogShell/FlyoutSurface/SnackbarContent：宿主由 Wpf.Ui 提供，内容由调用页面提供。

只有当两个以上正式调用点具有相同结构、相同状态语义和相同可访问性合同，并且差异不需要大量可选属性时，才升级为全局自有控件。

## 11. 页面资源应用矩阵

| 页面/区域 | 公共资源 | 自有控件 | Feature/页面所有内容 |
|---|---|---|---|
| StartupStatusWindow | Typography、Progress、Feedback | AppStatusView（Embedded） | Window 自身作为唯一 Raised Surface；启动阶段文本与状态切换 |
| MainWindow | Navigation、Button、Surface、Menu | 无强制页面壳控件 | Window Chrome、一级导航、内容宿主、托盘入口 |
| Settings 首页 | Typography、Navigation、Surface | AppPageHeader、AppSettingsGroup、AppSettingsNavigationRow | 导航项集合和页面 Padding |
| 各设置子页 | Typography、Input、Button、Feedback | AppPageHeader、AppSettingsList、AppSettingsRow；需要三级入口时可用 AppSettingsNavigationRow | 无标题列表 Surface、设置绑定、保存时机、危险操作语义 |
| Library | Typography、Button、Surface、Progress | AppPageHeader、AppStatusView | BookCardView、自适应网格、搜索与排序 |
| Book Details | Typography、Input、Selection、Progress | AppPageHeader、AppSectionSurface、AppStatusView | 摘要、编辑区、目录模板、虚拟化与定位 |
| TTS/Chapter/Regex Rules | Typography、Input、Selection、Menu、Feedback | AppPageHeader、AppSectionSurface、AppFormField、AppStatusView | Rules 共享列表项、各自字段、分栏和 Dirty State |
| Cache And Data | Typography、Input、Button、Feedback | AppPageHeader、AppSettingsList、AppSettingsRow、AppSettingsNavigationRow | 无标题列表 Surface、数据操作、确认和路径信息 |
| Cache Management | Typography、Selection、Menu、Feedback、Button | AppPageHeader、AppSectionSurface、AppStatusView | 单书分栏、全宽章节项和 PageHeader 多选动作；导出后台状态由 Shell 投影 |
| Player | Typography、Surface、Button、Media、Progress、Feedback | AppPageHeader、AppSectionSurface、AppStatusView | PlayerView、正文、侧栏、滚动追随和 Flyout 内容 |
| Mini Player | Typography、Surface、Button、Media | 无强制复合控件 | 固定横向布局、窗口动作和尺寸约束 |
| Dialog/Flyout/Snackbar | Button、Surface、Typography、Feedback | AppStatusView 仅在已有主 Surface 时使用 Embedded 模式 | Wpf.Ui host、真实文案、命令和生命周期；ContentDialog 内部保持 Flat Body |

## 12. 页面最终视觉要求

### 12.1 主窗口

- 侧栏使用中性壳层，当前项使用 AccentSubtle。
- 页面标题和页面级主操作位于内容区，由页面拥有。
- Shell 不向页面重复注入 Padding 或 FrameMargin。
- 窄窗口可收缩导航，但不隐藏核心入口。

### 12.2 书库与书籍详情

- 书库使用自适应书籍卡片网格。
- 卡片重点显示书名、作者、当前章节和剩余章节。
- 低频操作进入更多菜单。
- 书卡以单一整卡状态层表达 Hover，除“更多”按钮外整卡均可点击，不在卡片表面再叠加按钮 Hover 背景。
- 目录使用虚拟化列表。
- 目录项的序号与标题靠左，缓存百分比靠右，不把整组内容居中排列。
- 当前章节使用 AccentSubtle 和可访问状态，不额外显示“当前”标签。
- 目录页的 0% 或异常缓存完整度不显示。

### 12.3 播放页

- 正文为视觉中心，章节侧栏和媒体控制保持克制。
- 播放/暂停、上一段/下一段、上一章/下一章和音量使用统一 `48 × 48` 的中性媒体按钮。
- 播放/暂停不通过 Accent 背景或更大的按钮制造额外层级；媒体语义由图标、位置和 Tooltip 表达。
- 语速、定时停止、缓存等低频控制使用 Flyout。
- 当前段使用轻微 AccentSubtle，不使用高饱和背景。

### 12.4 规则工作台

- 三个规则页面共享视觉零件，不共享万能页面壳。
- 新建、从文件导入、从剪切板导入、恢复默认值和帮助等页面级顶部操作统一放入 `AppPageHeader.Actions`，与标题平齐；页面 Header 不提供导出。
- 左侧规则卡片统一采用左右布局：左侧名称与摘要占剩余空间，右侧 ToggleSwitch 使用自身内容宽度并保持独立命中区域，不得 Stretch 形成不可见横向点击区。
- 卡片不显示 `⋮` 更多按钮。单规则“导出到文件”“复制到剪切板”、删除及章节/正则的上移/下移使用 ContextMenu；右键不改变当前编辑对象。
- 页面进入时列表不默认选中规则；单击卡片才打开右侧编辑器。未选择规则时右侧使用轻量空状态。
- 章节规则和正则替换不显示拖动手柄，使用整卡长按约 `300 ms` 后拖动；拖动态只使用轻量状态反馈、插入线和边缘自动滚动，不实现相邻卡片位移动画。
- 右侧为字段编辑、帮助、试听、取消和保存。
- 编辑器打开后“取消”保持可用并承担关闭编辑器语义；“保存”只由草稿 Dirty/校验状态控制。
- 页面各自拥有列宽和字段布局。
- ToggleSwitch 的启用状态独立即时持久化，不进入编辑 Draft/Dirty State；Dirty State 通过标题提示、保存命令和导航守卫表达。
- TTS 页面不显示当前规则状态或切换入口；当前 TTS 规则只在播放页表达。

### 12.5 设置页

- 设置首页入口使用 `图标 + 标题 + Chevron` 整行导航，并保留 `AppSettingsGroup` 对“常用 / 文本处理 / 应用”等导航类别的分组。
- 所有正式设置页根节点保持透明；Canvas 由 Shell 的 `NavigationView` 内容宿主统一提供，因此页面 Padding 周围既不会出现 Window Background 色环，也不会遮住 Shell 左上圆角。
- **除设置首页外，具体设置子页面统一使用一个无 Header 的 `AppSettingsList`：保留设置首页同源的 Primary Surface、圆角、Padding 和行分隔线，但不显示分组 Header，也不按逻辑类别拆成多个卡片。**
- 子页面在 `AppPageHeader` 下放置单一 `AppSettingsList`，其中排列 `AppSettingsRow`；存在三级入口时可在同一列表中排列 `AppSettingsNavigationRow`。标题、说明和控件本身提供足够语义，不再重复显示“主题”“启动”等上层分类标题。
- `AppSettingsRow` 的独立布局、窄宽度适配、Focus/Automation 与纵向密度不得依赖 Group Header；Surface、圆角、统一 Padding 和 Divider 由 `AppSettingsList`/`AppSettingsGroup` 的共同列表合同提供，Row 自身不画外层卡片。
- 普通布尔项使用 ToggleSwitch，枚举项使用 ComboBox。
- 危险数据操作通过危险按钮样式、说明与确认流程表达风险；即使位于页面底部，也不因此恢复普通 Settings Group。
- 缓存与数据页把 LRU 说明归入容量上限行；应用数据目录使用文件夹图标按钮，清理全部缓存使用带 Tooltip 和可访问名称的 DangerIcon，缓存管理继续使用整行导航。
- 页面内容宽度继续由各页面拥有；本轮视觉规范不新增统一 `MaxWidth`，也不改变既有设置页的横向铺展策略。

### 12.6 缓存管理

- 左侧单书选择；右侧 `AppSectionSurface` 使用无 Header 状态，直接显示当前书籍摘要和章节列表，不重复“章节缓存 / 当前书名”。无 Header/Description 时 Surface 内容区不得留下标题间距。
- 使用文件管理器式多选和统一 Selected 状态；右键未选项先变为唯一选择，右键已选项保留多选集合。
- `AppPageHeader.Actions` 显示“已选择 N 章”以及清理 DangerIcon、导出 IconButton；按钮无文字但必须有 Tooltip/AutomationName。页面正文不再放多选工具栏或导出进度卡片。
- 章节 `ListBoxItem` 的 ContentPresenter 必须继承 HorizontalContentAlignment，使卡片铺满 ScrollViewer 可用宽度；禁止通过固定 Width 伪造统一宽度，横向不出现滚动条。
- 缓存管理页显示所有有缓存章节，包括当前配置完整度为 0% 的章节。
- 章节导出运行态/完成态属于 Shell Footer/Flyout 的进程级投影，不属于 CacheManagement Surface。

### 12.7 迷你播放器

- 宽度 `440–500 px`，高度固定为 `150 px`。
- Raised Surface 背景，外轮廓清晰，右侧边缘只调整宽度。
- 章节标题一行省略，书名与段落信息使用 Secondary Text。
- 五个媒体按钮和音量按钮使用统一 `48 × 48` 命中区及中性状态。
- 播放/暂停不单独使用 Accent 背景或更大按钮。
- 关闭按钮仍使用 DangerIcon 表达退出应用，但默认保持中性；鼠标悬浮时背景变为 Danger 色，按下时使用 Pressed Danger 状态。
- 不显示封面、作者、缓存、规则或语速。

## 13. Style Gallery 与视觉工具

### 13.1 Gallery 定位

`NovelSpeaker.StyleGallery` 是公共视觉资源的长期展示目录、开发检查工具和截图入口。它按稳定的资源族组织，而不是按开发任务、提交或页面迁移阶段组织。

Gallery 展示三类内容：

1. 基础资源族：Palette、Token、Typography、Surface。
2. 标准控件 Style 族：Button、Input、Selection、Navigation、Menu、Progress、Media、Feedback。
3. NovelSpeaker 自有控件族：PageHeader、SectionSurface、StatusView、Settings、FormField 等正式可复用控件。

Gallery 的目标是让用户在少量稳定场景中快速查看同一资源族的全部变体和关键状态，并直接截图比较。它不用于模拟完整业务页面，也不按每个正式页面重复展示同一套 Style。

每个资源族拥有唯一、稳定的 `family-id` 和 Gallery scene：

- 相近 Style、控件及其状态集中在同一资源族 scene 中维护。
- 新增变体时更新既有 scene，不因 backlog 新增任务而创建同义 scene。
- 只有出现新的独立视觉语义和明确所有权时，才新增资源族。
- `family-id` 不包含任务编号、日期、版本号或本地化显示名称。

Gallery 必须：

- 不进入正式应用导航。
- 不依赖用户数据库和真实书籍。
- 不进入 self-contained 发布包。
- fixture 只使用虚构、脱敏内容。
- 直接实例化正式 Style 和正式自有控件。
- 覆盖适用的 Default、Hover、Pressed、Focus、Disabled、Selected、Error、长文本和窄宽度状态。
- 支持浅色/深色、固定窗口尺寸和固定 DPI 自动截图。

正式页面与 Style Gallery 使用同一组公共资源；Gallery 只增加 fixture 和状态组合，不建立第二套产品 Style、控件实现或页面副本。

### 13.2 Gallery 截图目录

Gallery 截图按资源族长期保存：

```text
artifacts/visual-review/gallery/<family-id>/
├─ manifest.json
├─ <scenario-id>.light.100.png
├─ <scenario-id>.dark.100.png
└─ <scenario-id>.<theme>.<dpi>.png
```

约束：

- 目录和文件名不得包含 `TASK_BACKLOG.md` 任务编号。
- `scenario-id` 描述稳定状态组合，例如 `states`、`long-content`、`validation` 或 `compact`。
- 同一资源族的截图始终写入同一目录；任务重排或拆分不得导致目录改名。
- family manifest 记录 family、scenario、主题、DPI、viewport、文本缩放、资源版本和 PNG 哈希。

### 13.3 正式页面与窗口截图

正式界面截图按真实页面或窗口保存，不按任务编号保存：

```text
artifacts/visual-review/pages/<page-id>/<scenario-id>.<theme>.<dpi>.png
artifacts/visual-review/windows/<window-id>/<scenario-id>.<theme>.<dpi>.png
```

页面截图必须实例化正式 View，并使用确定性的脱敏 visual fixture 或测试 ViewModel。不得在 Gallery 中重新拼装一个外观相似但结构不同的页面副本。

稳定页面 ID：

- `appearance-settings`
- `settings-home`
- `general-settings`
- `playback-settings`
- `import-text-settings`
- `cache-data`
- `diagnostics-about`
- `library`
- `book-details`
- `tts-rules`
- `chapter-rules`
- `regex-replacement-rules`
- `cache-management`
- `player`

稳定窗口 ID：

- `mini-player`
- `main-window`
- `startup-status-window`

页面和窗口 ID 表示长期产品界面身份，不随 backlog 编号、任务顺序或中文标题调整而变化。页面特有 Dialog、Flyout、Snackbar 和错误状态作为该页面目录中的独立 `scenario-id` 保存；跨页面共享的纯样式状态仍在 Gallery 对应资源族中展示。

`MainWindow` 的 `ContentDialogHost` 是所有页面模态决策的唯一宿主，因此页面发起的 Dialog 保存在 `windows/main-window/`，并用发起页面作为 scenario 前缀。反馈场景覆盖映射如下：

- `close-dialog` 覆盖通用双按钮确认结构，包括关闭、清理和导出确认。
- `tts-rules-unsaved-dialog` 覆盖 TTS、章节和正则规则编辑器共享的三按钮未保存修改结构。
- `book-details-book-delete-dialog` 覆盖书籍详情页发起的危险主按钮、说明段落和缓存复选框删除结构。
- `library-encoding-dialog` 与 `library-import-progress-dialog` 分别覆盖编码选择和无 Footer 的导入进度结构。
- `active-cache-flyout`、`chapter-export-flyout`、`snackbar` 归属 `windows/main-window/`；播放器定时停止和音量归属 `pages/player/`，迷你播放器音量归属 `windows/mini-player/`。

根清单 `artifacts/visual-review/manifest.json` 汇总全部 Gallery family、页面、窗口、scenario、主题、DPI 和截图哈希，供用户稳定比较。

## 14. 自动验收标准

视觉资源体系必须由自动检查证明：

- Wpf.Ui provider dictionaries 在主题切换前后保持加载稳定。
- 应用级资源不存在接管标准 WPF/Wpf.Ui 控件的 NovelSpeaker 隐式 Style。
- 运行时代码不重新注入 Style 或 ControlTemplate 类型资源。
- 所有 NovelSpeaker 公共键使用 `App.` 前缀。
- 一个正式资源键只有一个定义位置。
- Provider Bridge 不包含 NovelSpeaker 颜色、页面几何或模板复制。
- 全局 Token 不包含页面专用 Padding、列宽或补偿性 Margin。
- 生产公共控件不包含 fixture 文本、演示命令或固定演示状态。
- 自有控件默认 Style 可按类型解析，具名变体只用于真实差异。
- 页面不引用已废弃或兼容资源键。
- Style Gallery 在浅色/深色下可重复渲染，并按稳定 family-id 生成 PNG/manifest。
- 正式页面和窗口按稳定 page-id/window-id 生成截图，路径不依赖 backlog 任务编号。
- 页面截图使用正式 View，而不是 Gallery 中的页面仿制品。
- 根视觉 manifest 可以唯一索引全部 family、页面、窗口和 scenario。
- `App.Button.Icon`、`App.Button.DangerIcon` 与 `App.Media.Button` 的生产调用方只能使用 `ui:Button` + `Button.Icon`，不得退回标准 WPF Button 的直接 `SymbolIcon` Content；静态 XAML 契约必须阻止该模式回归。
- 浅色/深色运行时测试必须证明 Icon Button 的 `SymbolIcon.Foreground` 跟随 owning Button 的前景色；`button-styles` 与 `media-controls` Gallery 场景持续覆盖普通、危险、媒体 Icon Button 的主题与状态组合。
- 最小点击区域、关键非零宽度、不重叠和核心内容可见测试通过。
- 100%、125%、150% DPI 和文本缩放下核心操作可用。
- 视觉资源重构不得改变导航、播放、选择、缓存、规则、Dirty State 和生命周期语义。
- 主题切换后已打开窗口、Dialog、Flyout 和迷你播放器立即更新。
- Style Gallery、测试 fixture 和视觉产物不进入发布包。
- 最终资源图中不存在 Legacy 字典、旧聚合字典和零引用公共资源。

## 15. 禁止项

- 禁止在 `Application.Resources` 或全局合并字典中为标准 WPF/Wpf.Ui 控件声明无 `x:Key` Style。
- 禁止主题切换时执行 `Application.Current.Resources[typeof(...)] = ...` 或等价恢复逻辑。
- 禁止在全局资源中复制 Wpf.Ui 标准控件完整模板。
- 禁止把 Gallery 示例内容写入生产控件构造函数。
- 禁止同一控件族的 Style 分散在多个无关字典中。
- 禁止用综合资源文件承载排版、按钮、列表、媒体和反馈等多类无关资源。
- 禁止用全局 Token 保存页面专用几何。
- 禁止在纯 Icon Button 内把 `SymbolIcon` 作为 `Content`，或在页面上为 Button.Icon 内的 `SymbolIcon` 单独维护 `Foreground`/祖先 Foreground Binding；颜色由 owning Button 统一拥有。
- 禁止为 `SymbolIcon` 建立无 `x:Key` 的全局隐式 Style；应用自有独立图标使用 `App.Icon.*` 语义样式，Provider 所有图标保持 Provider 所有权。
- 禁止建立依赖大量可选属性的万能 BookCard、RuleWorkbench 或 MediaControlBar。
- 禁止 ViewModel 返回 Brush、Style、Thickness、CornerRadius、Icon 或其它 WPF 视觉类型。
- 禁止通过整体 `Opacity` 弱化复杂容器导致文字对比度不足。
- 禁止为了视觉统一改变导航、播放、缓存、选择、Dirty State 或持久化语义。
