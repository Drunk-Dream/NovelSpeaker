# NovelSpeaker 视觉设计系统

## 1. 设计定位

NovelSpeaker 是适合长期驻留桌面的轻量听书工具。最终界面采用“柔和表面层级、克制强调色、轻量悬浮质感”的统一语言，并保持 Windows/Wpf.Ui 的原生交互习惯。

界面不模拟纸质书籍，不使用大面积装饰插画，也不采用音乐软件式的强封面视觉。书籍内容、当前播放状态和用户正在执行的操作始终是视觉中心。

视觉统一不等于把所有标准控件替换成同一自定义模板。稳定性优先于一次性覆盖范围：Wpf.Ui 负责标准控件模板，NovelSpeaker 通过主题颜色、具名样式、自有组件和页面布局逐层扩展。

## 2. 核心原则

### 2.1 内容优先

- 阅读正文、章节信息和当前播放状态优先于装饰。
- 页面只保留完成任务所需的信息，不通过更多卡片、标签或颜色增加“丰富感”。
- 主操作明确，次操作克制，低频操作放入菜单、Flyout 或次级区域。

### 2.2 单一强调色

Accent 只用于：

- 当前选中或激活状态。
- 页面唯一主操作。
- 播放/暂停等核心媒体操作。
- 当前进度。
- 键盘焦点。

普通文本、普通图标和结构边界使用中性色。成功、警告和错误色只表达真实状态，不作为装饰。

### 2.3 表面层级代替边框堆叠

视觉分组优先通过背景表面差异、间距和留白实现。边框只用于窗口外轮廓、输入边界、选中/焦点和深浅表面不足以区分的场景。

同一区域最多出现三级可见表面。页面不得出现多层带完整边框和阴影的嵌套卡片。

### 2.4 主题结构一致

浅色和深色模式使用同一控件树、布局、尺寸和组件结构，只切换 Wpf.Ui 主题与 NovelSpeaker palette。页面不复制两套 XAML。

### 2.5 工具感而非展示感

- 阴影、渐变和动画只用于建立层级或反馈。
- 不使用持续呼吸、旋转封面、动态波形等无功能价值的效果。
- 不使用明显拟物纹理、玻璃高光或大面积彩色渐变。
- 动效短、可取消，并尊重系统减少动画设置。

## 3. 样式所有权模型

### 3.1 Wpf.Ui Provider 层

Wpf.Ui 持有：

- 标准 WPF/Wpf.Ui 控件的默认模板。
- NavigationView、ToggleSwitch、ComboBox、CheckBox、Dialog、Snackbar 和 FluentWindow 的基础交互。
- Fluent 主题资源和标准 Visual State。

Provider dictionaries 在应用启动时加载，并在进程生命周期内保持稳定。NovelSpeaker 不复制其完整模板，也不通过主题切换代码重新插入标准控件 Style。

### 3.2 Palette 与 Design Token 层

NovelSpeaker 全局资源只包含跨窗口稳定的语义：

- 背景、表面、文本、边框、Accent、Danger、Warning、Success。
- `4/8/12/16/20/24/32/40/48` 间距标尺。
- 小/中/大圆角。
- 常用图标尺寸。
- 紧凑/标准控件最小高度。
- 三档动效时长和三档阴影。

以下内容不是全局 Token：

- 页面列宽。
- 规则列表宽度。
- 设置控件固定宽度。
- 页面专用按钮间距。
- 某个编辑器的标签列宽。
- Shell 与页面重复拥有的 Page Padding。

### 3.3 Provider Style Bridge

确实需要扩展 Wpf.Ui 基础样式时，通过专用资源字典建立显式桥接键，例如：

```text
Provider.TextBox
Provider.ComboBox
Provider.ToggleSwitch
Provider.NavigationItem
```

桥接层只引用已加载的 Provider 资源，不设置页面语义，不替换模板。应用具名样式只依赖桥接键，不在各页面重复猜测 Wpf.Ui 类型资源和加载顺序。

### 3.4 NovelSpeaker 具名样式

公共变体使用明确 `x:Key`，例如：

```text
App.Button.Primary
App.Button.Secondary
App.Button.Subtle
App.Button.Icon
App.Button.Danger
App.Input.Standard
App.Input.Compact
App.Media.Primary
App.Media.Secondary
```

规则：

- 页面显式选择样式，影响范围可以通过搜索确定。
- 样式优先覆盖颜色、前景、Padding、MinHeight、圆角语义和状态层。
- 不在 Application/global 作用域为标准 WPF/Wpf.Ui 控件定义 NovelSpeaker 隐式样式。
- 不在全局资源中替换标准控件完整 `ControlTemplate`。

### 3.5 NovelSpeaker 自有组件

复杂且需要稳定内部结构的视觉单元使用应用自有组件：

- `AppPageHeader`
- `AppSectionSurface`
- `AppBookCard`
- `AppSettingsRow`
- `AppRuleListItem`
- `AppMediaControlBar`
- `AppEmptyState`

自有组件拥有自己的内部布局和状态，可以在组件局部使用受控隐式样式。局部资源不得逃逸到页面或 Application。

### 3.6 页面布局层

页面负责：

- 页面内部边距。
- 分栏比例和最小列宽。
- 页面专用工具栏排列。
- 虚拟化列表和滚动宿主。
- 页面特有的固定或自适应宽度。

同一几何属性只允许一个 owner：Shell 不替页面重复增加外边距，NavigationView 不与页面同时提供 FrameMargin，组件内部 Margin 不补偿外部布局错误。

## 4. 禁止项

- 禁止在 `Application.Resources` 或全局合并字典中声明无 `x:Key` 的标准 WPF/Wpf.Ui 控件样式。
- 禁止主题切换时执行 `Application.Current.Resources[typeof(...)] = ...` 或等价 Style 恢复逻辑。
- 禁止在全局字典中复制 Wpf.Ui 控件模板。
- 禁止一次改动同时调整 palette、公共模板、页面密度和多个页面布局。
- 禁止用全局 Token 保存页面专用列宽和补偿性 Margin。
- 禁止 ViewModel 返回 WPF 视觉类型。
- 禁止通过整体 `Opacity` 弱化复杂容器导致文字对比度不足。
- 禁止为了视觉统一改变导航、播放、缓存、选择、Dirty State 或持久化语义。

## 5. 颜色系统

页面只能引用语义 Brush，不直接写业务无关的十六进制颜色。

| 语义 | 浅色主题建议 | 深色主题建议 | 用途 |
|---|---|---|---|
| `AppBackground` | `#F4F5F9` | `#101218` | 窗口壳层 |
| `CanvasSurface` | `#F8F9FC` | `#15181F` | 页面画布 |
| `PrimarySurface` | `#FFFFFF` | `#1B1F27` | 卡片、输入、工具区 |
| `SecondarySurface` | `#F1F3F8` | `#232832` | 次级控制条和分组 |
| `RaisedSurface` | `#FFFFFF` | `#272C36` | Flyout、Dialog、浮窗 |
| `PrimaryText` | `#20242C` | `#F2F4F8` | 标题和正文 |
| `SecondaryText` | `#626A77` | `#AEB5C1` | 元数据和说明 |
| `TertiaryText` | `#8A919D` | `#7F8794` | 占位和弱提示 |
| `SubtleBorder` | 深色约 10% | 白色约 14% | 轻描边 |
| `Accent` | `#5B6FD8` | `#7C8CFF` | 主操作、当前状态、进度 |
| `Danger` | `#C83C4A` | `#FF7A86` | 不可逆操作和错误 |
| `Warning` | `#A66A00` | `#F2B84B` | 风险提示 |
| `Success` | `#2E7D5B` | `#66C99A` | 完成和健康状态 |

Accent 至少提供 Default、Hover、Pressed、Subtle 和 FocusRing。浅色 AccentSubtle 约为 Accent 的 `10%–14%`，深色约为 `16%–20%`。

主题切换更新 Wpf.Ui theme 和 palette 值；Style、ControlTemplate 和组件字典不随主题替换。

## 6. 表面、描边与阴影

### 6.1 表面层级

只使用四级表面：

1. `AppBackground`
2. `CanvasSurface`
3. `PrimarySurface` / `SecondarySurface`
4. `RaisedSurface`

普通静态卡片默认不带阴影。Hover 卡片可使用低抬升；Flyout 使用中抬升；Dialog 和迷你播放器使用高抬升。

### 6.2 描边

- 窗口、Dialog、Flyout 和迷你播放器使用 `1 px` 轻描边。
- 卡片默认无边框；背景差异不足时才使用 SubtleBorder。
- FocusRing 独立于普通 Border，不通过改变尺寸造成布局抖动。
- Close 按钮默认中性，只有 Hover/Pressed 使用 DangerSubtle。

### 6.3 阴影

| 等级 | 用途 | 建议效果 |
|---|---|---|
| `ElevationLow` | Hover 卡片、轻悬浮按钮 | 偏移 1–2，模糊 8–12 |
| `ElevationMedium` | Flyout、菜单 | 偏移 3–4，模糊 16–20 |
| `ElevationHigh` | Dialog、迷你播放器 | 偏移 5–6，模糊 22–28 |

深色主题降低纯黑阴影强度，并用低透明度浅色描边补充边缘。

## 7. 尺寸、圆角与排版

### 7.1 稳定标尺

- 图标与文字：`8`
- 同组字段：`12`
- 控件内部：`12–16`
- 卡片内部：`16–20`
- 页面区块：`24–32`

以上是可复用标尺，不代表所有页面统一使用同一 Page Padding。页面根据内容选择标尺值并拥有唯一布局责任。

### 7.2 圆角

| 语义 | 圆角 |
|---|---:|
| 小型状态层、输入控件 | `6–8` |
| 列表行、普通卡片 | `10` |
| 分组工具条、Dialog | `12` |
| 主表面、迷你播放器 | `14–16` |
| 圆形媒体按钮 | `50%` |

### 7.3 最小交互尺寸

- 紧凑图标按钮：不小于 `32 × 32`
- 普通工具按钮：`36–40` 高
- 主媒体按钮：`44–48`
- 列表行：`48–56`
- 设置导航行：`52–60`

最小尺寸是自动几何合同；页面最终宽度、Padding 和间距允许独立调整。

### 7.4 字体

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

## 8. 状态与动效

### 8.1 状态层

| 状态 | 中性控件 | 强调控件 |
|---|---|---|
| Default | 透明 | Accent |
| Hover | 前景色 6%–8% | AccentHover |
| Pressed | 前景色 10%–12% | AccentPressed |
| Selected | AccentSubtle | Accent |
| Disabled | 保留结构、降低强调 | 同左 |
| Focus | 独立 FocusRing | 独立 FocusRing |

Hover 与 Selected 必须能同时表达。错误、选中和禁用不能只依赖颜色。

### 8.2 动效

- `80–100 ms`：Pressed、图标切换。
- `140–180 ms`：Hover、展开、状态层。
- `200–240 ms`：Flyout、Dialog、局部进入。

页面切换只使用轻微淡入或小距离位移。减少动画模式保留最终状态，移除位移和缩放。

## 9. 通用组件最终形态

### 9.1 按钮

- Primary：同一操作区最多一个 Accent 实色主按钮。
- Secondary：中性表面或轻描边，用于取消和次级提交。
- Subtle：透明默认状态，用于工具栏和低频动作。
- Icon：无边框，Hover 使用圆角状态层，必须有 Tooltip 和 AutomationName。
- Danger：只在最终不可逆确认中使用 Danger 强调。

公共按钮由具名样式或自有组件实现，不通过隐式 Button Style 改变全应用。

### 9.2 输入与选择控件

- 保留 Wpf.Ui 默认模板和键盘行为。
- 标签位于控件上方，不用 Placeholder 代替字段名称。
- Error 文案位于字段下方，不只显示红色边框。
- Disabled 与 ReadOnly 视觉不同。
- 页面通过 `App.Input.Standard`、`App.Input.Compact` 等显式样式选择密度。

### 9.3 卡片与列表

- 卡片承载完整对象或独立任务，不用于每个字段。
- 连续对象优先使用统一列表行。
- Selected 使用 AccentSubtle、细强调边缘或状态图标，不用饱和 Accent 填满大面积。
- 多选事实独立于虚拟化容器和视觉 item。

### 9.4 Slider 与 Progress

- 轨道高度 `3–4 px`。
- Slider 的 Thumb 可在 Hover、Focus 或拖动时增强。
- 已完成部分使用 Accent，未完成部分使用中性低透明度。
- 媒体段落进度 Tooltip 显示“第 x / y 段”，不伪造时间。
- 不使用渐变进度和轨道阴影。

### 9.5 Menu、Flyout、Dialog 与 Snackbar

- Menu/Flyout 使用 RaisedSurface、轻描边和 ElevationMedium。
- Dialog 只承载单一决策；主确认在右侧，删除确认使用 Danger。
- Snackbar 用于非阻塞结果，不替代需要立即决策的 Dialog。
- 空、加载、错误和无结果视图使用统一自有组件。

## 10. 窗口与页面最终形态

### 10.1 主窗口

- 自定义标题栏、一级侧边导航、内容工作区和全局反馈层。
- 侧栏使用中性壳层，当前项使用 AccentSubtle。
- 页面标题和页面级主操作在内容区内部，由页面拥有。
- Shell 不与页面重复提供内容 Padding。
- 窄窗口可收缩导航，但不隐藏核心入口。

### 10.2 书库

- 自适应书籍卡片网格。
- 卡片重点显示书名、作者、当前章节和剩余章节。
- 低频操作进入 `⋮` 菜单。
- 无封面时使用简洁中性识别块，不生成装饰插画。
- 空书库只提供一个主要导入入口。

### 10.3 书籍详情与目录

- 顶部显示书籍摘要和主要操作。
- 目录使用虚拟化列表。
- 当前章节使用 AccentSubtle 和可访问状态，不显示“当前”文字标签。
- 缓存百分比遵循 UI 与缓存文档中的稳定显示规则。
- “定位到当前章节”只在当前项离开可见区域时显示。

### 10.4 播放页

- 正文为视觉中心，章节侧栏和媒体控制保持克制。
- 底部或固定区域使用 SecondarySurface 媒体控制条。
- 播放/暂停是唯一 Accent 主媒体按钮。
- 语速、定时停止、缓存等低频控制使用 Flyout。
- 当前段使用轻微 AccentSubtle，不使用高饱和背景。

### 10.5 规则工作台

TTS、章节规则和正则替换共享双栏交互骨架，但页面各自拥有真实列宽和字段布局。

- 左侧：规则列表、状态和更多菜单。
- 右侧：字段编辑、帮助、试听、取消和保存。
- Dirty 状态通过标题提示、命令启用和导航守卫表达。
- 不通过全局 Token 强制三个页面采用固定像素宽度；共享组件只提供最小可用尺寸。

### 10.6 设置

- 设置入口使用 `图标 + 标题 + Chevron` 整行导航。
- 二级页使用轻量 Setting Group 和 AppSettingsRow。
- 普通布尔项使用 ToggleSwitch；枚举项使用 ComboBox。
- 危险数据操作独立位于页面底部。

### 10.7 缓存管理

- 左侧单书选择，右侧章节列表。
- 文件管理器式多选和统一 Selected 状态。
- 工具栏只保留作用于选中项的清理、导出等动作。
- 物理缓存大小、条目数和当前配置完整度使用中性文本层级。
- 后台缓存状态使用全局入口与 Flyout，不在每章重复复杂进度。

### 10.8 迷你播放器

迷你播放器是中等尺寸横向媒体控制面板，不显示封面。

建议范围：

- 宽度 `440–500 px`
- 高度 `130–160 px`
- RaisedSurface 背景
- `1 px` 轻描边
- `14–16 px` 圆角
- ElevationHigh 阴影

结构：

```text
顶部：章节标题 + 置顶/恢复主窗口/关闭
次行：书名 · 第 x / y 段
中部：段落进度
底部：SecondarySurface 媒体控制条
      上一章 / 上一段 / 播放暂停 / 下一段 / 下一章
```

- 播放/暂停为 `44–48 px` Accent 圆形主按钮。
- 上一段/下一段为 `36 px` 中性按钮。
- 上一章/下一章为 `32–34 px`，视觉权重略低。
- 置顶激活使用 AccentSubtle。
- 关闭等价于恢复主窗口时，Tooltip 写明“返回主窗口”。
- 标题一行省略，书名和段落使用 SecondaryText。
- 不显示作者、缓存、规则、语速或封面。

## 11. 浅色与深色主题

### 11.1 浅色

- 使用柔和冷灰 AppBackground 和白色/近白表面。
- PrimaryText 使用深灰而非纯黑。
- 普通卡片主要依赖表面差异，阴影只用于浮层。
- Accent 保持克制，不为每张卡片增加彩色装饰。

### 11.2 深色

- 使用多级深灰，不使用纯黑底加纯白文字。
- 表面逐级提高亮度形成层级。
- 普通图标和正文避免满强度纯白。
- 阴影减弱，描边和表面亮度差承担更多边缘识别。
- Accent 适当提高亮度并降低发光感。

## 12. Style Gallery 与视觉工具

仓库包含独立开发工具 `NovelSpeaker.StyleGallery` 或等价测试宿主：

- 不进入正式应用导航。
- 不依赖用户数据库和真实书籍。
- 不进入 self-contained 发布包。
- 展示 palette、排版、按钮、媒体控件、输入、选择、卡片、列表、导航、进度、Menu、Flyout、Dialog 和状态视图。
- 每个组件覆盖 Default、Hover、Pressed、Focus、Disabled、Selected 和 Error 中适用的状态。
- 支持浅色/深色、固定窗口尺寸和固定 DPI 的自动截图。
- 自动输出 PNG 和 JSON manifest 到 `artifacts/visual-review/`。

Style Gallery 是开发和回归工具，不决定产品行为。实际页面仍需通过显式样式和自有组件逐页迁移。

## 13. WPF 资源组织

推荐结构：

```text
Shared/Theming/
├─ Provider/
│  └─ ProviderStyleBridge.xaml
├─ Palettes/
│  ├─ Palette.Light.xaml
│  └─ Palette.Dark.xaml
├─ DesignTokens.xaml
├─ Styles/
│  ├─ Buttons.xaml
│  ├─ Inputs.xaml
│  ├─ ListsAndCards.xaml
│  ├─ Navigation.xaml
│  ├─ MediaControls.xaml
│  └─ Feedback.xaml
└─ Components/
   ├─ PageHeader.xaml
   ├─ SettingsRow.xaml
   ├─ MediaControlBar.xaml
   └─ StatusViews.xaml
```

加载顺序：

1. Wpf.Ui theme/provider dictionaries。
2. Provider Style Bridge。
3. NovelSpeaker palette。
4. Design Tokens。
5. 具名 Styles 和自有 Components。
6. 窗口/页面局部资源。

Style 和组件字典保持稳定；主题切换只更新第 1 层主题状态和第 3 层 palette 值。

## 14. 可访问性与缩放

- 所有纯图标按钮有 Tooltip 和 AutomationName。
- 键盘 Focus 清晰，不能只依赖颜色。
- 文本和背景达到合理对比度目标。
- 列表、多选、Dialog 和 Flyout 支持完整键盘操作。
- 100%、125%、150% DPI 和文本缩放下核心操作不遮挡、不重叠。
- 高对比模式允许系统资源覆盖，不能依赖固定图片或渐变传达内容。
- 动效尊重系统减少动画偏好。

## 15. 自动验收标准

最终样式体系必须由自动检查证明：

- Wpf.Ui provider dictionaries 在主题切换前后保持加载稳定。
- 应用级资源没有禁止的标准控件隐式样式。
- 运行时代码不重新注入 Style/ControlTemplate 类型资源。
- NovelSpeaker 具名样式通过受测 Provider Bridge 或默认模板工作。
- 全局 Token 不包含页面专用几何。
- Style Gallery 在浅色/深色下可重复渲染全部场景并生成 PNG/manifest。
- 最小点击区域、关键非零宽度、不重叠和核心内容可见测试通过。
- 页面迁移不改变导航、播放、选择、缓存、规则和生命周期语义。
- 主题切换后已打开主窗口、Dialog、Flyout 和迷你播放器立即更新。
- 完整质量门禁和 self-contained 发布内容检查通过。
