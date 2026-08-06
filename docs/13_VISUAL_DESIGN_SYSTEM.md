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

Provider dictionaries 在应用启动时加载，并在进程生命周期内保持稳定。NovelSpeaker 不复制其完整模板，也不通过主题切换代码重新插入标准控件 Style。

### 4.2 Provider Style Bridge

确实需要扩展 Wpf.Ui 基础 Style 时，通过专用桥接资源建立稳定别名，例如：

```text
Provider.Button
Provider.TextBox
Provider.PasswordBox
Provider.ComboBox
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
App.Surface.Popup
App.Surface.DialogContent
```

Surface Style 只处理背景、边界、圆角、Padding 和效果，不包含业务内容。

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

媒体按钮从 Button 基础变体派生，不在 Media 字典复制完整 Button 模板。

### 7.5 Input

```text
App.Input.TextBox.Standard
App.Input.TextBox.Compact
App.Input.PasswordBox.Standard
App.Input.PasswordBox.Compact
App.Input.ComboBox.Standard
App.Input.ComboBox.Compact
App.Input.CheckBox.Standard
App.Input.CheckBox.Compact
App.Input.ToggleSwitch.Standard
App.Input.ToggleSwitch.Compact
```

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
App.Media.Slider
App.Media.Button
App.Media.ControlSurface
```

ProgressBar 与 Slider 保持不同控件语义和测试，不共用模板。
播放页和迷你播放器的媒体按钮使用统一尺寸和中性状态；播放/暂停不建立独立的 Accent 主按钮变体。

### 7.9 Feedback

```text
App.Feedback.PopupSurface
App.Feedback.ValidationText
App.Feedback.InlineMessage
App.Feedback.SnackbarBody
```

加载、空状态、无结果和错误使用 `AppStatusView`，不分别建立四套硬编码控件类。

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

规则：

- 普通静态卡片默认不带阴影。
- Hover 卡片可使用低抬升。
- Menu/Flyout 使用中抬升。
- Dialog 使用高抬升。
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

### 9.3 AppSettingsGroup

组织一组设置项，并统一处理分组表面、行分隔线和首尾圆角。

职责：

- 提供 Header、Description、Items 和 Footer 槽。
- 分隔线由 Group 模板拥有。
- 页面不再通过 `SettingsLastRow...` 之类样式手动区分最后一行。

### 9.4 AppSettingsRow

封装设置标题、说明和右侧值/控件区域。

职责：

- 提供 Title、Description 和 Value/Content 槽。
- 支持 ToggleSwitch、ComboBox、TextBox、Button 和只读值。
- 只规定最小高度和内部布局，不规定页面统一右侧固定宽度。
- 窄宽度下允许右侧内容换行或转为纵向布局。

### 9.5 AppSettingsNavigationRow

用于设置首页和二/三级入口。

职责：

- 提供 Icon、Title、Description、Command 和右侧 Chevron。
- 整行可点击并具备键盘 Focus。
- Hover、Pressed、Disabled 和 Focus 由控件模板统一处理。

### 9.6 AppFormField

统一规则编辑和其它表单中的 Label、说明、输入区域和错误信息。

职责：

- 提供 Label、Description、Content、Error 和 Required 状态。
- Error 文案位于字段下方，不只改变边框颜色。
- 不决定输入控件类型和页面列宽。
- 字段自身不保存业务值，不包含验证逻辑。

### 9.7 AppStatusView

统一加载、空状态、无结果、错误和轻量成功提示。

职责：

- 通过状态种类和页面提供的 Title、Description、图标及操作槽渲染。
- 支持 PrimaryAction 和 SecondaryAction。
- 不硬编码“导入”“重试”等业务文案。
- 不替代 Snackbar 或需要立即决策的 Dialog。

## 10. 不进入全局控件层的内容

以下内容即使可以复用，也不默认成为 `Shared/Presentation/Controls`：

- BookCard：包含书籍信息、播放状态和书籍命令，属于 Library Feature。
- RuleListItem：包含规则启用、当前规则、排序和菜单，属于 Rules 页面族。
- Chapter/Cache row：包含章节与缓存语义，属于对应 Feature。
- 完整 RuleWorkbench：三个规则页面字段、列宽和操作不同，不建立万能工作台。
- 完整 MediaControlBar：播放页和迷你播放器结构、尺寸与操作密度不同，优先共享 Button、Slider 和 Surface Style。
- DialogShell/FlyoutSurface/SnackbarContent：宿主由 Wpf.Ui 提供，内容由调用页面提供。

只有当两个以上正式调用点具有相同结构、相同状态语义和相同可访问性合同，并且差异不需要大量可选属性时，才升级为全局自有控件。

## 11. 页面资源应用矩阵

| 页面/区域 | 公共资源 | 自有控件 | Feature/页面所有内容 |
|---|---|---|---|
| StartupStatusWindow | Typography、Surface、Progress、Feedback | AppStatusView | 启动阶段文本与状态切换 |
| MainWindow | Navigation、Button、Surface、Menu | 无强制页面壳控件 | Window Chrome、一级导航、内容宿主、托盘入口 |
| Settings 首页 | Typography、Navigation、Surface | AppPageHeader、AppSettingsGroup、AppSettingsNavigationRow | 导航项集合和页面 Padding |
| 各设置子页 | Typography、Input、Button、Feedback | AppPageHeader、AppSettingsGroup、AppSettingsRow | 设置绑定、保存时机、危险操作区 |
| Library | Typography、Button、Surface、Progress | AppPageHeader、AppStatusView | BookCardView、自适应网格、搜索与排序 |
| Book Details | Typography、Input、Selection、Progress | AppPageHeader、AppSectionSurface、AppStatusView | 摘要、编辑区、目录模板、虚拟化与定位 |
| TTS/Chapter/Regex Rules | Typography、Input、Selection、Menu、Feedback | AppPageHeader、AppSectionSurface、AppFormField、AppStatusView | Rules 共享列表项、各自字段、分栏和 Dirty State |
| Cache And Data | Typography、Input、Button、Feedback | AppPageHeader、AppSettingsGroup、AppSettingsRow | 数据操作、确认和路径信息 |
| Cache Management | Typography、Selection、Progress、Menu、Feedback | AppPageHeader、AppSectionSurface、AppStatusView | 单书分栏、章节项、多选工具栏和后台状态 |
| Player | Typography、Surface、Button、Media、Progress、Feedback | AppPageHeader、AppSectionSurface、AppStatusView | PlayerView、正文、侧栏、滚动追随和 Flyout 内容 |
| Mini Player | Typography、Surface、Button、Media | 无强制复合控件 | 固定横向布局、窗口动作和尺寸约束 |
| Dialog/Flyout/Snackbar | Button、Surface、Typography、Feedback | AppStatusView 仅用于内容状态 | Wpf.Ui host、真实文案、命令和生命周期 |

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
- 目录使用虚拟化列表。
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
- 左侧为规则列表、状态和更多菜单。
- 右侧为字段编辑、帮助、试听、取消和保存。
- 页面各自拥有列宽和字段布局。
- Dirty State 通过标题提示、命令启用和导航守卫表达。

### 12.5 设置页

- 设置入口使用 `图标 + 标题 + Chevron` 整行导航。
- 二级页使用 Setting Group 和 Settings Row。
- 普通布尔项使用 ToggleSwitch，枚举项使用 ComboBox。
- 危险数据操作独立位于页面底部。

### 12.6 缓存管理

- 左侧单书选择，右侧章节列表。
- 使用文件管理器式多选和统一 Selected 状态。
- 工具栏只保留作用于选中项的操作。
- 缓存管理页显示所有有缓存章节，包括当前配置完整度为 0% 的章节。

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
- 同一资源族的截图始终写入同一目录；任务重排、拆分或归档不得导致目录改名。
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
- 页面不引用已归档或兼容资源键。
- Style Gallery 在浅色/深色下可重复渲染，并按稳定 family-id 生成 PNG/manifest。
- 正式页面和窗口按稳定 page-id/window-id 生成截图，路径不依赖 backlog 任务编号。
- 页面截图使用正式 View，而不是 Gallery 中的页面仿制品。
- 根视觉 manifest 可以唯一索引全部 family、页面、窗口和 scenario。
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
- 禁止建立依赖大量可选属性的万能 BookCard、RuleWorkbench 或 MediaControlBar。
- 禁止 ViewModel 返回 Brush、Style、Thickness、CornerRadius、Icon 或其它 WPF 视觉类型。
- 禁止通过整体 `Opacity` 弱化复杂容器导致文字对比度不足。
- 禁止为了视觉统一改变导航、播放、缓存、选择、Dirty State 或持久化语义。
