# NovelSpeaker 视觉资产审计与行为基线

本文件记录任务 1 对当前 WPF 资产的可审查基线。机器可检查的清单在同目录的
[`VISUAL_ASSET_AUDIT.json`](VISUAL_ASSET_AUDIT.json)；JSON 的 `xamlAssets` 必须覆盖
`src/NovelSpeaker.App` 下全部 XAML，`migrationFindings` 的每一项必须有明确迁移目标。

审计日期：2026-08-02。

## 审计边界

本次只盘点现有资产和固定用户可观察行为，不改变生产行为，不新建视觉平行实现，也不把
任务 2–5 的执行状态写入数字设计文档。当前实现由实际 XAML、运行时对话框/通知服务和
现有 WPF/Presentation 测试共同确定；设计目标以
[`13_VISUAL_DESIGN_SYSTEM.md`](13_VISUAL_DESIGN_SYSTEM.md)、
[`06_UI_AND_USER_FLOWS.md`](06_UI_AND_USER_FLOWS.md) 和
[`07_SETTINGS_PAGES.md`](07_SETTINGS_PAGES.md) 为准。

## 资产覆盖清单

### 顶级窗口、应用入口和全局资源

| 资产 | 实际入口 | 当前表面/职责 | 现有行为契约 |
| --- | --- | --- | --- |
| 主窗口 | [`MainWindow.xaml`](../src/NovelSpeaker.App/Shell/MainWindow.xaml) | `FluentWindow`、标题栏、一级导航、主动缓存 Flyout、`ContentDialogHost`、`SnackbarPresenter` | `MainWindowNavigationTests`、`ShellActivationCoordinatorTests` |
| 启动状态窗口 | [`StartupStatusWindow.xaml`](../src/NovelSpeaker.App/Bootstrap/StartupStatusWindow.xaml) | 启动阶段、状态文本、无限进度 | `StartupCoordinatorTests` |
| 迷你播放器 | [`MiniPlayerWindow.xaml`](../src/NovelSpeaker.App/Desktop/MiniPlayer/MiniPlayerWindow.xaml) | 无系统标题栏、播放上下文、段落进度、媒体控制、音量 Popup | `MiniPlayerWindowTests`、`MiniPlayerViewModelTests` |
| 应用资源入口 | [`App.xaml`](../src/NovelSpeaker.App/Bootstrap/App.xaml) | `ThemeResources.xaml`（初始 Light Palette）、Light `ThemesDictionary`、`ControlsDictionary`、令牌和语义资源 | `ThemeResourceTests`、主题协调器测试 |
| 设计令牌 | [`DesignTokens.xaml`](../src/NovelSpeaker.App/Shared/Theming/Resources/DesignTokens.xaml) | 标准间距与语义布局间距、控件尺寸、字体栈/文字层级、圆角、描边、Elevation 资源、动效时长和导航宽度 | `ThemeResourceTests` |
| 语义资源 | [`SemanticStyles.xaml`](../src/NovelSpeaker.App/Shared/Theming/Resources/SemanticStyles.xaml) | 文本、设置行、卡片/Popup、Button、Slider、列表状态和媒体按钮 | `ThemeResourceTests`、`IconButtonStyleTests` |

任务 3 的令牌当前状态如下：`DesignTokens.xaml` 已提供唯一基础间距
`Spacing4/8/12/16/20/24/32/40/48`，并以这些分量组成页面、区块、字段、列表、卡片、
工具条和按钮的语义 Thickness；同时提供控件高度、图标尺寸、`AppFontFamily` 及
Window/Page/Section/Card/Body/Secondary/Caption 文字层级、圆角和描边令牌。
`SemanticStyles.xaml` 已引用字体、圆角、描边、进度和公共控件尺寸令牌；启动状态、主窗口、
迷你播放器、书库、书籍详情、缓存、规则工作台、设置子页和 `PlayerView` 已引用相应的
页面/字段/列表/工具条令牌。书籍封面的专属几何仍保留在 `BookCoverView.xaml`。

`ElevationLow`、`ElevationMedium`、`ElevationHigh` 已作为应用资源定义，参数符合视觉系统的
低/中/高层级范围；当前没有把阴影擅自应用到普通静态卡片或所有 Popup。后续由共享 Flyout、
Dialog 和窗口表面归属这些资源。`AnimFast`、`AnimNormal`、`AnimSlow` 和
`AnimReducedMotion` 已定义，`PlayerView` 与 `BookDetailsPage` 的定位逻辑读取 `AnimSlow`，
并保留系统 Reduce Motion 检测；其它页面尚未建立独立的动效消费者。

### 可导航页面

| 页面 | 实际 XAML | 当前主要结构和交互面 | 直接契约 |
| --- | --- | --- | --- |
| 书库 | [`LibraryPage.xaml`](../src/NovelSpeaker.App/Features/Library/LibraryPage.xaml) | 搜索、排序、导入、书籍网格、空状态、无结果状态 | `LibraryPageTests`、`LibraryViewModelTests` |
| 书籍详情 | [`BookDetailsPage.xaml`](../src/NovelSpeaker.App/Features/BookDetails/BookDetailsPage.xaml) | 编辑副本、书籍操作、虚拟化章节目录、当前章节定位 | `BookDetailsPageTests`、`BookDetailsViewModelTests` |
| 播放页 | [`PlayerPage.xaml`](../src/NovelSpeaker.App/Features/Playback/PlayerPage.xaml) | `PlayerView` 页面宿主 | `AppRouteNavigationTests`、`PlayerViewLayoutTests` |
| 设置首页 | [`SettingsPage.xaml`](../src/NovelSpeaker.App/Features/Settings/SettingsPage.xaml) | 分组设置导航行 | `SettingsPageViewTests`、`SettingsViewModelTests` |
| 播放设置 | [`PlaybackSettingsPage.xaml`](../src/NovelSpeaker.App/Features/PlaybackSettings/PlaybackSettingsPage.xaml) | 语速、预取、朗读标题、TTS 规则入口 | `SettingsSubpageViewTests`、`PlaybackSettingsViewModelTests` |
| TTS 规则 | [`TtsRulesPage.xaml`](../src/NovelSpeaker.App/Features/TtsRules/TtsRulesPage.xaml) | 规则工作台、启用/当前操作、菜单、编辑器、帮助抽屉 | `TtsRulesPageTests`、`TtsRulesViewModelTests` |
| 导入与文本 | [`ImportTextSettingsPage.xaml`](../src/NovelSpeaker.App/Features/ImportTextSettings/ImportTextSettingsPage.xaml) | 文件名模板、长段落设置、正则替换入口 | `SettingsSubpageViewTests`、`ImportTextSettingsViewModelTests` |
| 正则替换规则 | [`RegexReplacementRulesPage.xaml`](../src/NovelSpeaker.App/Features/RegexReplacementRules/RegexReplacementRulesPage.xaml) | 规则工作台、拖拽排序、更多菜单、编辑器、帮助抽屉 | `RegexReplacementRulesPageTests`、`RegexReplacementRulesViewModelTests` |
| 章节规则 | [`ChapterRulesPage.xaml`](../src/NovelSpeaker.App/Features/ChapterRules/ChapterRulesPage.xaml) | 规则工作台、拖拽排序、更多菜单、编辑器、帮助抽屉 | `ChapterRulesPageTests`、`ChapterRulesViewModelTests` |
| 缓存与数据 | [`CacheAndDataPage.xaml`](../src/NovelSpeaker.App/Features/Cache/CacheAndDataPage.xaml) | 缓存概览、上限、策略、清理全部缓存、缓存管理入口 | `CachePagesViewTests`、`CacheAndDataViewModelTests` |
| 缓存管理 | [`CacheManagementPage.xaml`](../src/NovelSpeaker.App/Features/Cache/CacheManagementPage.xaml) | 单书选择、章节 Extended 多选、清理、导出和进度 | `CachePagesViewTests`、`CacheManagementViewModelTests`、`DesktopSelectionControllerTests` |
| 常规 | [`GeneralSettingsPage.xaml`](../src/NovelSpeaker.App/Features/GeneralSettings/GeneralSettingsPage.xaml) | 关闭行为、启动最小化 | `SettingsSubpageViewTests`、`GeneralSettingsViewModelTests` |
| 外观 | [`AppearanceSettingsPage.xaml`](../src/NovelSpeaker.App/Features/Appearance/AppearanceSettingsPage.xaml) | System/Light/Dark 主题选择 | `SettingsSubpageViewTests`、`AppearanceSettingsViewModelTests` |
| 诊断与关于 | [`DiagnosticsAboutPage.xaml`](../src/NovelSpeaker.App/Features/Diagnostics/DiagnosticsAboutPage.xaml) | 版本、目录、日志级别、脱敏摘要、许可证入口 | `SettingsSubpageViewTests`、`DiagnosticsAboutViewModelTests` |

### 可复用局部组件

| 组件 | 实际 XAML | 当前职责 | 直接契约 |
| --- | --- | --- | --- |
| 播放工作区 | [`PlayerView.xaml`](../src/NovelSpeaker.App/Features/Playback/Components/PlayerView.xaml) | 播放工具、四个 Popup、章节/段落列表、媒体控制和无规则状态 | `PlayerViewLayoutTests`、`PlayerProgressInteractionControllerTests`、键盘快捷键测试 |
| 书籍卡片 | [`BookCardView.xaml`](../src/NovelSpeaker.App/Features/Library/BookCardView.xaml) | 封面、书籍摘要、阅读进度、更多菜单 | `BookCardViewTests`、`LibraryPageTests` |
| 生成封面 | [`BookCoverView.xaml`](../src/NovelSpeaker.App/Shared/Presentation/Books/BookCoverView.xaml) | 确定性封面背景、装饰几何形状和标题行 | `BookCoverGeneratorTests`、`BookCardViewTests` |

### 主题资源字典

| 资源 | 实际入口 | 当前职责 | 直接契约 |
| --- | --- | --- | --- |
| 浅色 Palette | [`Palette.Light.xaml`](../src/NovelSpeaker.App/Shared/Theming/Resources/Themes/Palette.Light.xaml) | 应用浅色语义 Brush | `ThemeResourceTests` |
| 深色 Palette | [`Palette.Dark.xaml`](../src/NovelSpeaker.App/Shared/Theming/Resources/Themes/Palette.Dark.xaml) | 应用深色语义 Brush，与浅色键集合一致 | `ThemeResourceTests` |
| 主题资源外壳 | [`ThemeResources.xaml`](../src/NovelSpeaker.App/Shared/Theming/Resources/Themes/ThemeResources.xaml) | 稳定资源入口；运行时由 `ThemePaletteRuntime` 直接替换当前 Palette 语义键 | `ThemeResourceTests` |

## Dialog、Flyout、Snackbar 和菜单

### Dialog

应用没有独立 Dialog XAML；所有应用内模态对话框由 `ContentDialog` 运行时创建，并由主窗口的
`RootContentDialogHost` 承载。没有 Host 时才使用原生 `MessageBox` 回退。

| 资产 | 所有者 | 当前变体 | 契约和迁移归属 |
| --- | --- | --- | --- |
| 通用确认 | `Shared/Dialogs/AppDialogService.cs` | 确认、未保存修改（保存/放弃/取消） | `AppDialogService` 是唯一决策适配器；迁移到共享 Dialog Shell 和语义按钮资源 |
| 删除书籍 | `Features/BookDetails/BookDeleteDialogService.cs` | 删除、音频缓存复选框、播放中提示 | 保留外部 TXT 安全语义；迁移到共享 Danger Confirmation Shell |
| 编码选择 | `Features/Library/EncodingSelectionDialogService.cs` | 编码 ComboBox、继续导入/取消 | 迁移到共享表单 Dialog 和输入资源 |
| 导入进度 | `Features/Library/ImportProgressDialogService.cs` | 不定/确定进度、取消 | 迁移到共享 Progress Dialog；保留取消源、进度投影和关闭等待 |
| 启动错误 | `Bootstrap/App.xaml.cs`、`WpfStartupRuntime.cs` | 启动阶段错误、无法建立 Host 的错误 | 仍是 Bootstrap 边界回退；有 Host 时优先共享反馈层 |

### Flyout、Popup 和帮助抽屉

| 资产 | 实际位置 | 内容 | 现状 |
| --- | --- | --- | --- |
| 主动缓存进度 | `MainWindow.xaml#ActiveCacheFlyout` | 章节进度列表、取消任务 | 已复用 `PopupSurfaceBorderStyle`，列表行仍有局部状态样式 |
| 定时停止 | `PlayerView.xaml#StopTimerPopup` | 快捷时长、自定义分钟数、错误提示 | 已复用 Popup 表面，按钮和输入高度/间距仍有局部常量 |
| 规则切换 | `PlayerView.xaml#RuleMenuPopup` | 规则列表、当前标识、前往规则管理 | 列表行复用 BorderlessListItemButtonStyle，选中提示是局部 TextBlock 样式 |
| 语速调整 | `PlayerView.xaml#SpeedMenuPopup` | 减速、输入、加速 | 已复用 Popup 表面，输入和动作按钮仍有局部尺寸 |
| 播放音量 | `PlayerView.xaml#VolumeMenuPopup` | 音量百分比、Slider | 与迷你播放器共用 Slider 语义样式，但 Popup 结构重复 |
| 迷你播放器音量 | `MiniPlayerWindow.xaml#MiniPlayerVolumeMenuPopup` | 音量百分比、Slider | 与播放页音量 Popup 结构重复 |
| TTS 帮助 | `TtsRulesPage.xaml#HelpDrawerBorder` | 规则编写帮助 | 页内抽屉和遮罩，需共享 HelpDrawer 表面 |
| 章节规则帮助 | `ChapterRulesPage.xaml#HelpDrawerBorder` | 规则生效帮助 | 页内抽屉和遮罩，需共享 HelpDrawer 表面 |
| 正则替换帮助 | `RegexReplacementRulesPage.xaml#HelpDrawerBorder` | 正则语法和作用范围 | 页内抽屉和遮罩，需共享 HelpDrawer 表面 |

### Snackbar 和菜单

- Snackbar 只有主窗口 `RootSnackbarPresenter` 一个承载点；`AppNotificationService` 是统一适配器。
  页面/ViewModel 不直接拥有 Snackbar 队列。
- 书籍卡片菜单：书籍详情、删除书籍。
- TTS 规则菜单：导出、删除。
- 章节规则菜单：上移、下移、删除；内置规则按能力禁用删除。
- 正则替换菜单：上移、下移、删除。
- 菜单由 XAML 声明，规则菜单的拖拽/菜单事件只负责平台事件桥接；排序、删除和 dirty guard 仍由现有 ViewModel/页面协调。
- 文件/文件夹选择器和系统打开目录不属于应用自绘视觉资产，由统一 `IPresentationFileDialogService`/
  `IPresentationLauncher` 提供平台能力。

## 主题入口、Accent 和资源替换

当前链路如下：

```text
App.xaml
  └─ Wpf.Ui ThemesDictionary(初始 Light) + ControlsDictionary
       └─ DynamicResource 主题 Brush
            └─ SemanticStyles.xaml / 页面 XAML

settings.json → IAppSettingsService.Current.Theme
  └─ AppThemeStartupCoordinator
       └─ WpfUiThemeRuntime
            └─ ApplicationThemeManager.Apply / ApplySystemTheme

AppearanceSettingsPage
  └─ AppearanceSettingsViewModel
       └─ ThemePreferenceService
            ├─ 先应用运行时主题
            └─ 再持久化；失败时应用旧主题回滚
```

应用的颜色入口由 `ThemeResources.xaml` 稳定持有，并初始合并 `Palette.Light.xaml`；
`Palette.Dark.xaml` 提供相同的语义 Brush 键集合。Wpf.Ui 的 `ThemesDictionary` 和
`ControlsDictionary` 仍只负责底层控件模板与系统主题 provider。应用 Accent 族由自有 Palette
暴露：

- `AccentBrush`、`AccentHoverBrush`、`AccentPressedBrush`：主媒体按钮、当前状态和进度。
- `AccentSubtleBrush`、`AccentSubtleHoverBrush`：选择与强调状态层。
- `AccentFocusRingBrush`：键盘焦点环。
- `AccentForegroundBrush`：强调色表面上的图标和文字。

页面不再消费 Wpf.Ui 的具体颜色键。运行时由 `ThemePaletteRuntime` 替换稳定外壳中的 Palette
语义键，并保持已打开窗口、Popup、Dialog 和迷你播放器的 `DynamicResource` 引用同步刷新；
两套 Palette 加载或键集合校验失败时回退到有效的 Light 或现有 Palette。

## 重复局部样式与明确迁移目标

下表是所有任务 1 发现的局部重复视觉关注点。每项都已在 JSON 的 `migrationFindings` 中有唯一
ID、实际来源和目标归属；“迁移目标”不是未归属的以后处理项。

| ID | 类别 | 当前重复/局部实现 | 明确迁移目标 |
| --- | --- | --- | --- |
| `palette-ownership` | 颜色 | 页面和语义样式通过 `ThemeResources.xaml` 使用应用自有语义 Brush；Wpf.Ui 颜色名保留在底层主题/控件 provider 边界 | `Themes/Palette.Light.xaml`、`Themes/Palette.Dark.xaml`、`Themes/ThemeResources.xaml`，由语义 Brush 对外暴露 |
| `local-state-colors` | 颜色 | 主动缓存、当前章节、播放段、多选、置顶状态在页面局部 Trigger 写 Brush | `Components/ListsAndCards.xaml`、`Components/MediaControls.xaml`、`Windows/MiniPlayer.xaml` 的共享状态样式 |
| `cover-palette` | 颜色 | 生成封面使用独立的确定性渐变和内容色 | 保持 `BookCover` 专属资源边界，不并入全局 Accent |
| `inline-corner-radii` | 圆角 | Popup、播放列表、工具条胶囊、媒体按钮、书籍卡片和详情页的公共圆角已迁移到 `DesignTokens.xaml`，并由 `SemanticStyles.xaml`、`MainWindow.xaml`、`MiniPlayerWindow.xaml`、`PlayerView.xaml`、`BookDetailsPage.xaml` 和 `BookCardView.xaml` 引用；`BookCoverView.xaml` 的 `12` 仍是封面专属几何 | 保持现有 Card/Dialog/List/Toolbar/Media/Interactive 令牌作为公共所有者；后续若需要外层 MiniPlayer/Cover 表面令牌，由对应窗口/封面组件归属，不回退为页面字面量 |
| `unrounded-mini-player-surface` | 圆角 | 迷你播放器外层 Border 未使用窗口圆角 | `Windows/MiniPlayer.xaml` 的 MiniPlayerSurface + MiniPlayerCornerRadius |
| `shadow-ownership` | 阴影 | `DesignTokens.xaml` 已定义 `ElevationLow`/`ElevationMedium`/`ElevationHigh`；当前没有普通静态卡片或所有 Popup 的应用级阴影消费者，现有深度仍由窗口/控件平台表面提供 | 保持令牌由 `DesignTokens.xaml` 单一所有；后续在共享 Flyout、Dialog、MiniPlayer 表面接入，不在页面添加局部 `DropShadowEffect` |
| `shared-button-templates` | Button Template | 三个公共 Button ControlTemplate 仍由 `SemanticStyles.xaml` 所有；任务 3 已迁移公共按钮高度、内边距、圆角、描边和焦点环，RevealMore/CurrentRule 等行为包装仍是局部样式 | 任务 4 归属 `Components/Buttons.xaml` 的模板整理和行为语义样式迁移；本审计不把该模板迁移写成已完成 |
| `text-box-template` | TextBox Template | 仍没有应用自有 TextBox/PasswordBox ControlTemplate；任务 3 已迁移可共享的控件高度与字段 spacing，页面的一次性文本框几何和 Wpf.Ui 基础模板仍保留 | 任务 5 归属 `Components/Inputs.xaml` 的统一输入模板/状态迁移；本审计不把输入组件迁移写成已完成 |
| `slider-template` | Slider Template | Slider/Thumb/RepeatButton 仍由 `SemanticStyles.xaml` 共享；任务 3 已迁移进度轨道、滑块和媒体控件的公共尺寸，进度/音量 Popup 结构与模板所有权尚未合并 | 任务 5 归属 `Components/MediaControls.xaml` 的 Slider/Progress 模板和状态迁移；保留当前播放/音量行为 |
| `book-details-list-container` | 列表选择 | 详情页内联 ListBoxItem + ContentPresenter 模板仍保留；章节行的公共圆角、尺寸和 spacing 已使用令牌，状态仍在数据 Border | 任务 5 归属 `Components/ListsAndCards.xaml` 的虚拟化容器 + CurrentListItem 样式 |
| `cache-management-list-container` | 列表选择 | 缓存管理内联 ListBoxItem 模板和选中 Border Trigger 仍保留；列表公共间距、行尺寸和操作内边距已使用令牌 | 任务 5 归属共享 ExtendedSelection 容器 + SelectedCard 样式；选择事实仍由 `DesktopSelectionController` |
| `rules-list-container` | 列表选择 | 正则、章节、TTS 工作台仍分别声明容器/拖放/当前状态局部样式；字段、列表和工具条 spacing 已迁移到 `DesignTokens.xaml` | 任务 5 归属共享 RuleWorkbench、DropTarget、CurrentRule 样式 |
| `playback-list-selection` | 列表选择 | 播放章节、段落仍各自声明容器和当前/多选状态；公共行圆角、按钮尺寸和 spacing 已使用令牌 | 任务 5 归属共享 PlaybackChapter、PlaybackSegment、CurrentAndSelected 状态样式 |
| `shell-active-cache-row` | 列表选择 | 主窗口 Flyout 内联当前/失败行背景 | 共享 FlyoutListItem + ActiveCacheStatus 样式 |
| `local-popup-surfaces` | 表面/阴影 | 多个页面仍引用 `PopupSurfaceBorderStyle`；公共圆角、边框、控件尺寸和部分内边距已令牌化，宽度与一次性内容布局仍留在调用方，Elevation 资源尚未在这些表面统一应用 | 后续由共享 FlyoutSurface、DialogSurface、MiniPlayerSurface 统一表面与 Elevation；宽度仍是内容布局输入 |
| `settings-row-style` | 设置行 | 设置页面已统一复用四个语义样式，当前没有页面 Hover/Border 复制 | 保留语义样式单一所有者，仅把颜色映射到 ThemeResources 并补充 SettingGroup token |

### 控件模板现状结论

| 控件族 | 当前是否有应用自有模板 | 当前证据 | 迁移目标 |
| --- | --- | --- | --- |
| Button/Icon/Media/ListItem Button | 有 | `SemanticStyles.xaml` 的三个 `ControlTemplate` | 拆入 `Components/Buttons.xaml`，页面只引用语义样式 |
| TextBox/PasswordBox | 无 | 未发现应用自有 `ControlTemplate` | `Components/Inputs.xaml` 的统一输入模板/状态 |
| Slider/Thumb/RepeatButton | 有 | `PlaybackProgressSliderStyle`、`PlaybackSliderThumbStyle`、`PlaybackSliderTrackButtonStyle` | `Components/MediaControls.xaml`，增加 Hover/Focus/Dragging 语义 |
| ProgressBar | 无独立 ControlTemplate | `PlaybackProgressBarStyle` 只设置尺寸和 Brush | 与 MediaControls 的进度轨道/填充语义资源统一 |
| ListBoxItem | 局部多份 | 详情、缓存、正则、播放页有 ContentPresenter-only 容器 | 共享虚拟化容器样式，页面保留业务语义绑定 |
| ComboBox/CheckBox | 无应用自有模板 | 使用 Wpf.Ui 控件模板，页面只设置数据/布局属性 | `Components/Inputs.xaml` 统一高度、焦点、禁用和错误表现 |

## 行为基线与测试证据

行为基线只固定用户可观察结果，不复制播放、选择或编辑状态机。

| 范围 | 必须保持的行为 | 现有测试证据 |
| --- | --- | --- |
| Shell 导航 | 一级只有书库/设置；启动到书库；切换只留一个 active；关闭经过生命周期/guard | `MainWindowNavigationTests`、`AppRouteNavigationTests`、`GuardedNavigationServiceTests` |
| 书库与详情 | 搜索/导入/空状态可访问；章节目录虚拟化；当前章节定位；未保存元数据支持保存、放弃、取消 | `LibraryPageTests`、`BookDetailsPageTests`、`LibraryViewModelTests`、`BookDetailsViewModelTests` |
| 播放与键盘 | 播放命令启用矩阵、Space/Ctrl/Alt 快捷键、章节/段落虚拟化、进度拖动和当前项定位 | `PlayerViewLayoutTests`、`PlayerProgressInteractionControllerTests`、`KeyboardShortcutPolicyTests`、`WpfShortcutContextResolverTests`、`PlayerViewModelCommandTests` |
| 多选与缓存 | 单书、章节 Extended 选择、Ctrl/Shift/Ctrl+A、无选择禁用、清理/导出进度和取消 | `CachePagesViewTests`、`DesktopSelectionControllerTests`、`CacheManagementViewModelTests` |
| 规则工作台 | 左右滚动工作区、启用/当前、拖拽/菜单排序、dirty Save/Cancel、帮助抽屉 | `TtsRulesPageTests`、`ChapterRulesPageTests`、`RegexReplacementRulesPageTests` 及对应 ViewModel 测试 |
| 设置与主题 | 设置导航行、字段保存策略、Light/Dark/System 应用、保存失败回滚 | `SettingsPageViewTests`、`SettingsSubpageViewTests`、设置 ViewModel 测试、`AppThemeStartupCoordinatorTests`、`ThemePreferenceServiceTests` |
| 播放浮窗与反馈 | 迷你播放器关闭恢复主窗口；空白拖动不吞控件；进度/音量/媒体按钮有可访问名称；Dialog/Snackbar 走统一服务 | `MiniPlayerWindowTests`、`MiniPlayerViewModelTests`、`BookDeleteDialogServiceTests`、`FeedbackServicesTests` |

新增的 `VisualAssetAuditTests` 只检查本清单自身的完整性：全部 23 个 XAML 均已登记，所有
视觉发现均有迁移目标，且主题入口、Accent 来源、运行时替换和行为矩阵均有 owner/测试文件。
它不以截图坐标或私有实现细节作为断言。
