# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段只围绕 **UI 交互样式收口** 展开，以 `docs/13_VISUAL_DESIGN_SYSTEM.md` 定义的“克制型 Fluent”为最终视觉合同。上一轮已完成任务不在本文件继续保留，历史由 Git 追溯。

本轮目标：

- 建立统一的 Hover / Pressed / Keyboard Focus / Selected/Checked 交互语义与 Light / Dark / High Contrast 主题资源。
- 消除播放器媒体按钮悬浮时出现的大面积方框、ToolbarValue 外层 Button 与内部 Pill 的双层反馈等典型问题。
- 统一播放进度、竖向音量、语速、定时停止等播放器交互样式，并保持现有 TTS 变量、缓存和播放业务语义不变。
- 修复最新视觉验收发现的 Dark Mode Pressed 图标变黑、Popup/Flyout 四角残留直角半透明宿主层，以及竖向音量 Track 在 Thumb 附近局部收缩的问题。
- 收口 TextBox、ComboBox、ToggleSwitch、列表项、ContextMenu/MenuItem、Separator 等公共控件族的状态边界。
- 消除页面局部硬编码交互色、重复 Hover owner 和不完整/依附 MenuItem 的分隔线。
- 通过 WPF 契约、Style Gallery、确定性页面渲染和完整质量门禁建立可重复验收证据；人工视觉判断可以用于后续设计反馈，但不是任务关闭条件。

稳定产品、架构、测试与视觉终态分别以数字编号文档为准。本文件只描述实施顺序、任务状态和自动验收。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成；任务末尾必须附简短“完成成果”。
- `[!]`：存在阻塞，必须记录可复现原因。
- `P0`：会影响全局交互一致性、主题/可访问性、核心播放器操作或完整质量门禁。
- `P1`：局部样式迁移、视觉收口和长期可维护性。

Codex 完成任务后保留条目并标记 `[x]`；只有新的规划阶段才允许再次删除或重写 Backlog。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. UI 任务至少阅读：
   - `AGENTS.md`
   - `docs/06_UI_AND_USER_FLOWS.md`
   - `docs/09_TESTING_AND_QUALITY.md`
   - `docs/10_ENGINEERING_CONVENTIONS.md`
   - `docs/13_VISUAL_DESIGN_SYSTEM.md`
   - 当前任务直接涉及的生产 XAML、code-behind/ViewModel 和现有 WPF 测试。
3. 不重做整套应用主题，不引入新的 UI 框架；继续以 Wpf.Ui Provider + NovelSpeaker 公共资源层为基础。
4. 先修正公共 owner，再迁移页面调用方。不得在页面局部复制 Hover/Pressed/Focus/Selected Trigger 来快速绕过公共样式缺口。
5. 页面和 Feature XAML 不得新增硬编码交互颜色；主题差异必须由 Palette/语义 Brush 处理。
6. 一个控件树只允许一个主要 Hover owner。发现 Button + Border/Pill、ListItem + Card 等双层反馈时，明确唯一 owner 后删除重复状态层。
7. Mouse Hover/Pressed 不得伪装 Keyboard Focus；键盘导航、Automation、Disabled、Validation 和 High Contrast 语义不能为了“更干净”被弱化掉。
8. 不改变 `speakSpeed` 的领域范围、步进、TTS 规则变量语义、缓存身份和现有异步提交逻辑；本轮只调整交互呈现和必要的 presentation state。
9. 不改变播放、定时停止、音量和规则选择的业务行为；若实现新视觉需要新增 ViewModel 状态，只允许加入纯 presentation 状态，并增加对应测试。
10. WPF 自动测试默认运行在隐藏 Desktop，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。需要视觉产物时使用现有显式视觉生成机制。
11. 每个任务优先扩展已有 Gallery scene 和聚合契约测试，避免为每个控件/页面复制大量等价 case。
12. 用户要求提交时按逻辑目的拆分原子提交，不把整个编号任务机械压成一个大提交。
13. 任务完成时将自身状态更新为 `[x]`，在末尾增加 `完成成果：`，记录主要实现与自动验收结果；不得自行删除其它任务或创建任务归档。

## Phase A：交互资源与状态模型

## [x] T001（P0）：建立 Interaction Palette、Motion 与状态优先级合同

目标：

- 为后续所有控件提供唯一的 Hover / Pressed / SelectedHover / Focus / Disabled 主题资源入口。
- 保持 Light、Dark 的感知强度一致，并为 Windows High Contrast 提供可辨识 fallback/映射。
- 将现有零散动效时长收敛到 Fast / Standard / Slow Token，不在控件族中继续出现近似但不同的硬编码时长。

实施：

- 审计 `Palette.Light.xaml`、`Palette.Dark.xaml`、Provider bridge、主题切换和 `Motion.xaml`，新增/整理 `App.Brush.Interaction.*` 与 `App.Brush.Accent.Subtle.Hover` 等公共语义资源。
- High Contrast 下优先投影到系统 Highlight/WindowText/GrayText/Focus 等可辨识语义；不得只依赖低透明度 Brush。
- 将公共 Hover/Pressed/Selected/Focus 的颜色所有权从页面/Feature 局部资源迁回 Palette/Style owner，并删除已失效同义键。
- 将公共动画统一到约 `100 ms` Fast、`160 ms` Standard、`220 ms` Slow；Popup/Flyout 的出现/退出只保留克制的淡入与轻微位移。
- 更新 Style Gallery 的 Palette/Motion/基础状态场景，用稳定 family-id 展示 Light/Dark 与关键状态。

自动测试/验收：

- 资源唯一性和主题热切换合同通过；已打开测试控件在 Light/Dark 切换后不保留旧 Brush。
- High Contrast 模拟/系统资源投影测试证明 Focus、Selection、Disabled 仍可区分。
- `rg` 不再发现生产 Page/Feature 为通用 Hover/Pressed/Focus 写死十六进制色或同义局部 Brush。
- 受影响 WPF/Style Gallery 定向测试通过。

完成成果：

- 新增 Light/Dark/High Contrast Interaction Palette 与系统颜色投影，统一公共 Hover、Pressed、Selected+Hover、Focus、Disabled 资源；移除已无生产引用的旧同义键。
- 保留 Fast/Standard/Slow 为唯一视觉动效时长 owner，并将 ComboBox 动效及播放器/章节自动居中时长接入共享 Token；新增 `interaction-states` Gallery family-id 场景。
- 自动验收：`dotnet format --verify-no-changes --no-restore`、Release 全量 build、Interaction/Style Gallery/Input/Semantic Palette/Provider 定向测试通过；虚拟化选择数据测试在隔离 WPF Desktop 中仍触发既有测试主机挂起，最小回归确认与本次组合状态触发器无关。

## Phase B：按钮与播放器样板

## [x] T002（P0）：收口 Button family 与播放器顶部/媒体按钮交互

依赖：T001。

目标：

- 消除媒体按钮 Hover 时出现的大面积方框。
- 建立“内容反馈型媒体按钮”和“弱 Surface 型工具按钮”的稳定差异。
- 去除播放器语速/定时入口中外层 Button + 内层 Pill 的双层 Hover/Pressed。

实施：

- 调整 `Buttons.xaml` / `Media.xaml`：`App.Media.Button` Rest 透明、Hover 只增强 Foreground、Pressed 使用轻微内容反馈；Keyboard Focus 保留独立 Focus Ring。
- `App.Button.Icon` / PageHeader 工具按钮继续使用弱 Hover Surface，但不使用明显 Hover Border。
- 保持播放/暂停、上一/下一段、上一/下一章、音量入口图标尺寸一致；播放/暂停仅通过中心位置表达主操作。
- 将 `App.Button.ToolbarValue` 与播放器语速/定时 Pill 重构为单一交互 Surface，避免宿主与内部 Border 分别高亮。
- 同步迷你播放器的共享媒体 Button 行为，不改变其现有布局尺寸与窗口行为。
- 更新 Button/Media Gallery scene 与 Player/MiniPlayer WPF 契约。

自动测试/验收：

- 媒体 Button Hover/Pressed 状态下 Background 保持透明或无大面积 Surface，Foreground 状态正确；Keyboard Focus 仍可见。
- Player 的 Speed/Timer 入口只有一个主要 Hover owner，不存在外层与内层同时切换背景的视觉树合同。
- Light/Dark/High Contrast 下按钮前景、Disabled、Focus 资源可解析。
- Player/MiniPlayer 相关 WPF 定向测试通过。

完成成果：

- `App.Media.Button` 收口为透明命中区、前景 Hover/Pressed 反馈，并让 `App.Button.ToolbarValue` 由单一 Wpf.Ui 按钮拥有圆角工具表面；播放器语速/定时入口移除内部 Pill。
- 同步 Button/Media Gallery 文案与按钮样板，以及 Player/MiniPlayer 相关 WPF 契约测试。
- 自动验收：按钮、媒体、Player/MiniPlayer 与 Gallery 定向 WPF 测试 15/15 通过，覆盖 ToolbarValue Icon+数值内容及统一媒体图标尺寸契约。

## [x] T003（P0）：统一播放进度、竖向音量与语速/定时 Flyout

依赖：T002。

目标：

- 建立播放器 Media Slider 统一视觉语言。
- 完成用户确认的竖向音量、隐藏式播放进度 Thumb、语速原始整数和定时 Choice 交互。
- 保持 Flyout/Popup Single Surface，不重新引入卡片嵌套。

实施：

- 在 `Media.xaml` 建立共享 Track/Thumb 基础，提供 `App.Media.ProgressSlider` 与 `App.Media.VolumeSlider`；不在 Player/MiniPlayer 页面复制 Slider 模板。
- 播放进度使用 Accent 已播放轨道 + 弱中性未播放轨道；Thumb Rest 隐藏，Hover/Keyboard Focus/Dragging 显示，Dragging 时略强化。
- Player 与 MiniPlayer 的音量 Flyout 统一为竖向 Slider，Thumb 默认显示，已设置/剩余轨道具有对比，0 音量图标保持静音语义。
- 语速 Flyout 保留原始整数输入与 `−/+` 步进 1；Step Button 改为轻量紧凑反馈，TextBox 保留现有范围校验和提交逻辑。
- 定时 Flyout 将 `15/30/45/60/90` 改为轻量 Choice；Active Timer 入口使用弱 Accent 持续状态，自定义时间和关闭定时保持次级/中性层级。
- Popup/Flyout 内不增加完整 Card；仅使用现有 PopupSurface + 内容控件。

自动测试/验收：

- ProgressSlider 在 Rest/Hover/Focus/Dragging 的 Thumb 可见性与 Track 两段语义通过 WPF 契约。
- VolumeSlider 为 Vertical、Thumb 默认可见、轨道两段资源独立，Player/MiniPlayer 使用同一公共样式。
- `speakSpeed` 的 `1..20` 范围、默认值、步进和现有 TTS/cache 相关测试保持通过；UI 不出现 `1.0×` 等伪倍速映射。
- 定时预设 Choice、Active 状态、自定义输入和取消命令保持原有业务结果。
- Player/MiniPlayer 确定性渲染与定向测试通过。

完成成果：

- 在 `Media.xaml` 集中定义 Track/Thumb、播放进度和竖向音量 Slider 模板；Progress 使用配对 ProgressBar 表达已播放/未播放轨道，Volume 在 Player/MiniPlayer 共用竖向样式。
- 将速度步进与定时预设收口为轻量控件，保留原始整数、范围校验、取消/自定义提交和 Active Timer 弱 Accent 状态；Flyout 继续使用单一 PopupSurface。
- 后续视觉修正：媒体 Slider 的悬浮只增强 Thumb，不改整条轨道颜色；共享 Track 保留圆形 Thumb 的布局横截面，Player/MiniPlayer 音量 Flyout 仅在竖向控制条上方居中显示百分比。
- 音量 Flyout 进一步收窄至 96 DIP，并相对 48 DIP 音量按钮居中；宿主取消重复边界/阴影，仅保留 PopupSurface 的圆角表面，竖向窄轨道向 Thumb 延伸 1 DIP 消除接缝。
- 自动验收：播放器媒体/进度/音量、定时、Gallery 与视觉架构 WPF 定向测试 18/18 通过；PlayerViewModel 速度与定时回归 25/25 通过。包含整个 `PlayerViewTests` 的组合命令因仓库既有 WPF 测试主机挂起未完成，已停止该进程，未以其作为通过依据。

## Phase C：交互回归修正与输入、列表、菜单统一

## [x] T004（P0）：修复 Popup chrome、Dark Pressed Icon 与音量轨道几何回归

依赖：T003。

背景：

- 最新播放器视觉验收仍可在语速调节、规则切换、音量控制等浮层四角看到高透明度的直角宿主层，说明 Single Surface 合同只在内容 Border 上成立，Popup/Flyout host、Provider chrome 或 elevation 仍可能留下第二层矩形轮廓。
- Dark Mode 下部分 Icon Button 按下时图标会瞬间变黑，实际 `Button.Icon` 视觉树仍可能被 Provider Pressed VisualState 覆盖，不能只以 Palette/Style setter 已定义为通过依据。
- 竖向音量 Slider 的填充轨道在 Thumb 附近仍出现局部收缩/“掐腰”，当前为隐藏接缝加入的几何修补不能作为最终形态。

目标：

- 所有播放器 Popup/Flyout 只保留一个圆角内容 Surface 和与圆角轮廓一致的柔和阴影，四角外真正透明，不再出现可辨识的直角半透明底板。
- Dark Mode 的 Icon/Toolbar/Media Button 在 Pressed 状态继续使用主题可读前景，实际 SymbolIcon 不回落为黑色或低对比度 Provider 默认色。
- 竖向音量 Slider 的已填充/未填充 Track 全程保持同一固定厚度，并在 Thumb 下连续衔接，不通过局部缩窄、负 Margin 或其它“补缝”几何形成视觉收缩。

实施：

- 审计 `App.Feedback.PopupSurface`、`App.Feedback.FlyoutHost`、`App.Surface.Popup`、`Provider.Flyout`、WPF `Popup` 与 elevation Effect 的实际视觉树；分别验证 `SpeedMenuPopup`、`RuleMenuPopup`、`VolumeFlyout`、`StopTimerFlyout`，定位第二层矩形 chrome 的真正 owner。
- 以公共 Feedback/Surface/Flyout owner 修复浮层；不得在三个播放器浮层分别增加遮罩、CornerRadius 补丁或背景色。宿主和 bridge 保持透明，只有内容 Surface 绘制背景/边界；阴影必须跟随圆角内容轮廓。
- 在 Dark Palette 下真实构造 `App.Button.Icon`、`App.Button.ToolbarValue`、`App.Media.Button` 并进入 Pressed 状态，检查 Wpf.Ui Button template 中最终 `Button.Icon`/`SymbolIcon.Foreground`。若 Provider 状态覆盖应用 setter，在 `Buttons.xaml`/必要的控件族模板或 bridge 层统一修复，不允许页面级设置 Icon Foreground。
- 重构 `App.Media.PlaybackSliderControlTemplate` 的竖向轨道连接方式：Decrease/Increase 两段使用同一固定轨道宽度，并连续延伸至 Thumb 中心区域，由 Thumb 覆盖接缝；移除会造成轨道局部收缩或鼓包的 Margin/尺寸修补。Player 与 MiniPlayer 继续复用 `App.Media.VolumeSlider`。
- 不改变语速、规则切换、定时、音量数值和播放业务行为。

自动测试/验收：

- Dark Mode 下三类按钮的 Normal/Hover/Pressed/Disabled 最终 Icon 前景 WPF 合同通过；Pressed 实际 SymbolIcon 与背景保持可辨识对比，不以资源键存在代替视觉树验证。
- Popup/Flyout 视觉树合同证明内容 Border 是唯一不透明圆角 Surface，外层 host/bridge 无第二份 Background/Border/Effect；语速、规则、音量至少生成一次确定性 Light/Dark 渲染，四角外不出现直角半透明板。
- VolumeSlider 几何合同验证 Decrease/Increase 轨道厚度一致、Thumb 上下连接连续；确定性渲染中控制柄附近不出现收缩、鼓包或断缝。
- Player/MiniPlayer/Feedback/Button/Media 相关 WPF 定向测试通过；仅执行本任务必要的视觉产物生成，不将 PNG 基线纳入仓库。

完成成果：

- 新增公共 `App.Feedback.PopupHost`，Player 的规则/语速 Popup 与 Flyout 宿主保持透明。后续根因验证确认：圆角 `PopupSurface` 直接使用 `DropShadowEffect` 时，阴影会被矩形 WPF Popup 原生窗口裁切并形成四角半透明直角残影；最终由 `App.Surface.Popup` 取消该 Effect，内容 Surface 只保留圆角背景和边界。
- Icon、ToolbarValue、Media Button 的 Hover/Pressed 前景统一绑定语义交互色；音量 Slider 最终不再让 Decrease/Increase RepeatButton 直接绘制可见轨道，而是将交互 Track 透明化，由独立 `App.Media.VisualTrack` 绘制连续固定厚度的 Accent/neutral Rail，从结构上消除 Thumb 附近分段圆头造成的“掐腰”。
- 自动验收补充真实 Popup host 捕获和像素级回归：直接检查圆角外像素透明度，并比较音量 Thumb 上下相邻位置的轨道像素宽度；不再仅以 `Background/Effect` Setter、RepeatButton Width/Margin 或视觉树结构作为通过依据。
- 视觉验收：生成并检查 feedback、button-styles、media-controls、progress 的 Light/Dark 截图，确认圆角 Popup 四角真正透明、Dark pressed 图标对比度与音量轨道连续性；验收后已删除截图及临时生成代码。

## [x] T005（P0）：收口 Input、Selection 与设置行状态边界

依赖：T001、T004。

目标：

- 让输入控件主要通过 Border 表达 Hover/Focus。
- 消除 Selected 被普通 Hover 覆盖以及设置行空白区域产生错误 Hover 暗示的问题。
- 保持 ToggleSwitch 已修复的 40 px 纯开关 Focus/HitTest 边界。

实施：

- TextBox/PasswordBox/ComboBox Rest 使用 Subtle Border，Hover 只增强 Border 并最多使用极弱 Surface，Open/Keyboard Focus 使用统一 Focus/Accent Border，Validation Error 优先级最高。
- ToggleSwitch Hover/Pressed 只增强轨道/Thumb；On + Hover 在 Accent 持续状态上增强，不添加外围矩形。
- Selection family 统一 `Disabled > Selected/Current+Hover > Selected/Current > Hover > Rest`，新增/复用 SelectedHover Accent 资源，禁止普通 Hover 把选中项改成中性灰。
- 整行可点击列表由行容器拥有唯一 Hover；普通 `AppSettingsRow` 不提供整行 Hover，`AppSettingsNavigationRow` 保留整行导航反馈。
- 审计规则卡片、章节列表、缓存管理、书库/详情等调用方，删除与公共 Selection/Input 重复的局部 Trigger。

自动测试/验收：

- ComboBox/TextBox/ToggleSwitch 的 Hover/Focus/Validation/Checked 状态 WPF 合同通过。
- 纯 ToggleSwitch 的可见轨道、Focus 和 HitTest 宽度继续保持 40 px 合同；带 Content 时自然扩展。
- Selected/Current + Hover 不回退为普通 Hover Brush。
- 设置行仅真实控件/导航行产生交互反馈；普通设置空白不成为可点击目标。
- 受影响设置、规则、列表 WPF 定向测试通过。

完成成果：

- TextBox/PasswordBox 的 Hover/Focus/Validation 通过 Border 统一表达；ToggleSwitch 的 On+Hover 保持 Accent 轨道，保留 40 px 纯开关边界。
- 新增共享 Selection 内容文字样式，统一详情、缓存、播放器、主窗口和规则卡片的 Selected/Current/ActiveCache/Disabled 投影；普通设置行不拥有整行 Hover，导航设置行保留行级反馈。
- 自动验收：Input、Selection、Settings、RuleListItem、详情页章节模板和 Release build 定向检查通过；增加调用方不重复声明 Selected/Disabled Foreground 的静态契约。
- 视觉验收：生成并检查 input-controls、selection、list-components、settings-controls、rules-shared 的 Light/Dark 截图，确认字段边界、选择态 Accent、列表文字对比度、设置行 Hover 所有权和规则卡片状态；验收后已删除截图、manifest 及临时生成代码。

## [x] T006（P1）：统一 ContextMenu/MenuItem 与独立 Separator

依赖：T001、T004。

目标：

- 建立菜单项单层 Hover/Pressed/Checked 反馈。
- 修复当前菜单分组线长度/透明度不稳定、依附菜单项导致“分隔线不完整”的问题。

实施：

- 在 `Menus.xaml` 统一 Menu Surface、Item、DangerItem、Checked/Selected 和 Disabled 状态；MenuItem 不再叠加内部按钮式 Hover。
- 新增/整理 `App.Menu.Separator`：独立 Separator、统一左右 inset、与文字列视觉对齐，不依附前/后 MenuItem Border。
- Separator 不继承相邻项的 Disabled/Opacity/Hover 状态；首尾和连续分隔线按菜单结构规整。
- ContextMenu、规则菜单、章节/缓存菜单和其它下拉菜单迁移到公共 Menu/Separator 资源；不在页面继续手工画分组底边。
- 更新 Menu Gallery scene，覆盖普通、Hover、Pressed、Checked、Disabled、Danger 和多组 Separator。

自动测试/验收：

- Visual tree/资源合同证明 Separator 为独立元素并使用统一 inset，不是 MenuItem Bottom Border。
- Disabled 菜单项不会改变相邻 Separator 的 Opacity/长度。
- MenuItem 只有一个主要 Hover owner，Checked/Selected + Hover 保持持续状态。
- ContextMenu 键盘打开、Focus、`Shift+F10`/Menu Key 等既有交互测试保持通过。

完成成果：

- 新增 `Provider.Separator` 与 `App.Menu.Separator`，公共菜单统一处理 Hover/Pressed/Checked、Checked+Pressed、Disabled 和 Danger 优先级；Separator 使用固定 inset、1 px 独立模板和稳定不透明度。
- 规则菜单、书卡 ContextMenu、托盘菜单及 Gallery 迁移到公共菜单/分隔线资源，托盘代码构造项也绑定同一资源链。
- 自动验收：菜单资源/状态/Provider bridge 合同、真实 ContextMenu Separator 视觉树与几何、规则 ContextMenu、托盘菜单和全 Gallery 渲染定向测试通过。
- 视觉验收：生成并检查 `menus.light.png`、`menus.dark.png`，覆盖普通、Hover、Pressed、Checked、Checked+Hover、Checked+Pressed、Disabled、Danger 和多组 Separator；验收后已删除截图、manifest 及临时生成代码。

## Phase D：全项目迁移与质量收口

## [x] T007（P1）：审计并迁移剩余交互样式调用方

依赖：T002–T006。

目标：

- 将本轮交互语言从播放器样板扩展到全项目，清除局部同义样式、硬编码颜色和重复状态层。
- 不进行与交互样式无关的页面重构。

实施：

- 全仓库审计 Button、IconButton、ListBoxItem、Card、Input、Menu、Popup/Flyout、Slider、ToggleSwitch 的局部 Trigger/Brush/Border。
- 对可点击整行、不可点击设置行、持续 Selected/Current、行内按钮分别按设计系统迁移。
- PageHeader、规则页、缓存管理、书库/详情、设置页和其它页面统一复用公共交互资源。
- 检查 Light/Dark/High Contrast、Disabled、长文本、窄宽度、100/125/150% DPI 下的布局和状态，不为修视觉扩大 HitTest 区域。
- 删除迁移后零引用的旧键、局部 Style 和兼容资源；不保留 V2/Legacy 别名。

自动测试/验收：

- `rg`/架构测试确认生产页面不存在通用交互色硬编码、旧同义资源和明显的 Button+Border 双 Hover 模式。
- 公共资源键唯一、主题热切换、Icon Foreground、Input、Selection、Menu、Media 聚合合同通过。
- 主要正式页面使用确定性 fixture 完成 Light/Dark 渲染；High Contrast 关键控件状态合同通过。
- WPF/Presentation 定向测试通过。

完成成果：

- 迁移书库卡片、书籍详情章节、缓存书籍/章节、规则选择按钮、帮助抽屉关闭遮罩及播放器自定义输入，统一使用 `App.Button.Floating`、`App.Input.TextBox.Compact` 和既有 Selection owner；未改变播放、规则或缓存业务状态。
- 新增生产 XAML 交互调用方静态审计，覆盖显式公共样式、通用交互色硬编码和选择容器的单一 Hover owner，并补充对应 WPF 视觉树断言。
- 自动验收：交互调用方静态合同通过，受影响的书卡、规则项和缓存投影定向测试通过；`dotnet format --verify-no-changes --no-restore` 通过。

## [x] T008（P0）：完成交互样式阶段的跨模块审阅与完整质量门禁

依赖：T001–T007。

目标：

- 对“克制型 Fluent”实施结果进行一次跨控件族自动审阅。
- 确保视觉优化没有改变播放、TTS、定时、音量、规则、选择和设置业务语义。

审阅：

- 复查 Hover/Pressed/Focus/Selected 状态所有权和优先级，确认没有 Mouse Hover Focus Ring、Selected 被 Hover 覆盖或父子双层大面积高亮。
- 复查 Player/MiniPlayer 媒体按钮、ProgressSlider、VolumeSlider、Speed/Timer Flyout 与 Single Surface 合同。
- 复查 Dark Mode Pressed 最终 Icon Foreground、语速/规则/音量/定时浮层四角透明 chrome，以及 VolumeSlider Track 在 Thumb 附近的连续固定厚度。
- 复查 Input/ToggleSwitch、Settings Row、规则/章节/缓存列表以及 Menu Separator 的关键几何和键盘/Automation 行为。
- 复查 Light/Dark/High Contrast 资源映射和运行时主题切换。
- 复查数字编号文档、Gallery scene、生产资源和测试表达同一套终态合同。

完整验收：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

并执行交互样式专项静态/视觉合同检查，至少确认：

- 生产 Page/Feature 未重新引入通用交互色硬编码。
- 媒体按钮 Hover 无大面积背景方框，进度 Thumb/竖向音量状态符合合同。
- Dark Mode Icon/Toolbar/Media Button 的 Pressed 实际图标不变黑；播放器 Popup/Flyout 圆角外无直角半透明底板；VolumeSlider 控制柄附近无轨道收缩/鼓包/断缝。
- Separator 独立且完整，Disabled MenuItem 不影响分隔线。
- ToggleSwitch 纯开关宽度、Selected+Hover、Keyboard Focus 和 High Contrast 关键状态保持可访问。
- 默认测试未设置可见窗口授权；如生成视觉产物，仅通过隐藏 Desktop 的显式生成流程。

验收：

- 完整质量门禁全部通过。
- 若发现可自动修复的问题，在本任务内修复并重跑受影响门禁；真实阻塞标记 `[!]` 并记录可复现证据。

完成成果：

- 跨模块复查 Player/MiniPlayer、Feedback Popup/Flyout、Input/ToggleSwitch、Selection/Settings、Menu Separator 及正式页面调用方；补齐媒体资源键的 `App.Media.*` 形式，消除资源图前缀违规且保持现有模板/状态语义。
- 自动专项合同通过：交互调用方静态审计、资源图闭包与键唯一性、视觉资源所有权、媒体按钮/滑杆、Popup/Flyout、菜单、输入和主题状态测试均通过。
- 完整质量门禁按规定顺序通过：locked restore、format、Release build（0 警告/0 错误）及全量 test；5 个测试程序集共 846 项通过，0 失败、0 跳过，未设置可见窗口授权。

## Phase E：视觉残留回归与真实像素验收

## [x] T009（P0）：定位并修复播放页目录/正文段落的双层 Hover 与直角宿主层

依赖：T008。

背景：

- 用户在真实播放页视觉验收中确认：目录卡片和正文预览段落 Hover 时仍出现两层状态面，其中至少一层为直角矩形。
- 当前 `WideChaptersListBox` 与 `SegmentListBox` 的 `ItemContainerStyle` 只设置透明 Background/Border，没有像 Book Details、Cache 的部分列表那样显式移除 ItemContainer 默认 chrome；两处 DataTemplate 又使用 `App.Button.Floating` 点击宿主包裹 `App.Selection.MultiSelectItem` / `App.Selection.CurrentItem` 圆角 Surface。
- 因此静态结构上至少存在三个需要实测排查的状态 owner：`ListBoxItem` 默认/Provider Template、外层 Button Provider Template、内部 Selection Border。现有测试主要证明 Setter/Style 关系，不能证明 Hover 时最终像素只有一层。

目标：

- 播放页目录项和正文段落在 Rest/Hover/Pressed/Current/Selected 状态下只存在一个主要可见状态 Surface，Hover 为既定圆角，不出现第二层直角或圆角底板。
- 将“没有视觉 chrome 的 ItemContainer/点击宿主”收口为可复用公共能力，避免页面继续复制或猜测 Provider 模板行为。
- 保留章节选择、主动缓存多选、段落跳转、虚拟化、滚动、键盘焦点、Enter/Space 激活和 Automation 语义。

实施：

1. 先生成真实 `PlayerView` 的确定性 Light/Dark Hover 截图，并同时记录目标项实际 VisualTree/Template owner；分别让鼠标位于普通目录项、Current 目录项、普通正文段落和 Current 正文段落上，确认每一层可见背景来自哪个元素。不得先按猜测直接改 XAML。
2. 优先检查 `WideChaptersListBox` / `SegmentListBox` 的 `ListBoxItem` 默认模板。若矩形层来自 ItemContainer，建立 Selection family 的共享 chrome-free ItemContainer Style/Template，只保留 `ContentPresenter`、布局/选择/虚拟化所需语义，并迁移 Player；同时把 Book Details、Cache 中等价的页面级裸 ItemContainer Template 收口到该公共 owner，页面只保留真实差异。
3. 再验证外层 Button。若 `App.Button.Floating` 即使设置透明 Background 仍由 Provider Template 内部 VisualState 绘制额外状态层，新增/调整一个明确的 chrome-free interaction host（受控具名模板或应用自有控件），由 Button family 统一维护；不得在 Player 页面叠加透明 Brush/遮罩规避。`App.Button.Floating` 保留给真正包含 `App.Surface.FloatingAction` 的悬浮操作，不继续承担通用列表/卡片点击宿主语义。
4. 最终让 `App.Selection.MultiSelectItem` / `App.Selection.CurrentItem` 成为播放页目录/正文行唯一可见 Hover/Selected owner；Current/Selected + Hover 继续使用 AccentSubtle.Hover，不回退普通中性 Hover。
5. 扩展 WPF 测试：既要检查 VisualTree owner，也要渲染真实 Player 行 Hover 的最终像素，能够发现矩形第二层；不能只断言 `Background=Transparent`、`BorderThickness=0` 或 Style key。

自动/视觉验收：

- Light/Dark 下目录普通项、目录 Current 项、正文普通段、正文 Current 段 Hover 均只有一个圆角状态面，没有直角矩形或第二个大面积 Hover Surface。
- Tab/键盘焦点仍清晰且只在 Keyboard Focus 时显示；鼠标 Hover 不引出 Focus Ring。
- 章节点击、主动缓存多选、正文跳转、虚拟化和滚动相关测试保持通过。
- 若本任务生成截图、截图脚本、manifest、临时 fixture 或 VisualTree dump，只作为验收副产物；确认通过后全部删除，并以 `git status --short` 证明仓库只剩预期源码/测试/文档修改。

完成成果：

- 新增公共 `App.Selection.ChromeFreeItemContainer` 与 `App.Button.InteractionHost`，让 ListBoxItem 和整行点击宿主只保留布局、命令、焦点及 Automation 语义，不再绘制 Provider PointerOver/Pressed chrome；Player 目录/正文改由 Selection Border 唯一拥有圆角状态面。
- 书籍详情、缓存、书籍卡片和规则列表中的等价全表面点击调用同步迁移；`App.Button.Floating` 仅保留给带 `App.Surface.FloatingAction` 的定位/悬浮操作。
- 新增 Light/Dark 真实 Player 行最终像素回归，覆盖目录普通/Current 与正文普通/Current；新增样式、VisualTree owner、调用方审计契约。临时鼠标/VisualTree 诊断 fixture 已删除。
- 自动验收：App Release build 0 警告/0 错误；InteractionCallerAudit、ButtonStyle、Selection style、Player 内容/几何及最终像素回归通过。部分既有 WPF Window/动画测试在当前隔离宿主中测试进程崩溃，详见交付说明。

## [ ] T010（P0）：修复竖向音量 Thumb 在 Pressed/Dragging 时右侧裁切

依赖：T009 可并行；最终验收依赖两者均完成。

背景：

- 用户确认音量 Thumb 点击/拖动时会轻微放大，放大后圆形右侧出现遮挡/裁切。
- 当前 `App.Media.PlaybackSliderThumbStyle` Rest 为 `14 × 14`，`IsDragging=True` 时直接把 Thumb 改为 `16 × 16`；竖向 `PART_Track` 的固定宽度仍为 `14`。这是高可信的几何风险点，但必须通过真实渲染确认最终裁切 owner，不能只按属性差异推断。

目标：

- 保留克制的 Pressed/Dragging 反馈，但状态切换不改变 Track 测量布局，不让 Thumb 右侧/左侧被裁切，也不造成横向漂移。
- Player 与 MiniPlayer 共用同一 VolumeSlider 行为，100%/125%/150% DPI 下视觉对称。

实施：

1. 在真实 `App.Media.VolumeSlider` 中分别捕获 Rest/Hover/Pressed-or-Dragging 状态，确认裁切发生在 Thumb Template、Track measure/arrange、Slider clip 还是 Flyout 宿主边界。
2. 首选固定最大尺寸的 Thumb layout envelope：外层 Thumb/Presenter 始终按最大交互尺寸预留，内部圆形在该固定区域内从 Rest 到 Dragging 轻微放大/变色；不要通过直接改变参与 Track 布局的 Thumb `Width/Height` 实现放大。若视觉上不需要放大，也允许改为仅颜色/描边增强，但不得牺牲可操作性。
3. 检查 Slider/Flyout 的水平 padding、Track 宽度和裁切设置，确保最大 Thumb 视觉在左右两侧有对称余量；不得通过偏移 Thumb、加不对称 Margin 或扩大整个 Flyout 来掩盖问题。
4. 保持既有 VisualTrack 与交互 Track 分离、轨道固定厚度、Thumb 下连续衔接合同。
5. 新增像素/几何回归：比较 Dragging 状态 Thumb 左右可见半径、中心位置和边界 alpha，确认无单侧削平；同时保护轨道不因状态变化产生新的掐腰/断缝。

自动/视觉验收：

- Player/MiniPlayer、Light/Dark、100%/125%/150% DPI 下 Rest/Hover/Dragging Thumb 均完整、居中且左右对称。
- Dragging 不改变 Slider 的有效水平位置或 Flyout 几何，不引入新的轨道接缝。
- 音量数值、静音图标、键盘调整和播放业务测试保持通过。
- 视觉验收生成的截图、调试脚本、manifest 和临时 fixture 在任务结束前全部删除。

## [ ] T011（P1）：审计 chrome-free 点击宿主与残余双层交互面

依赖：T009、T010。

目标：

- 将 T009 暴露的“透明 Setter 与最终 Provider/默认模板像素不等价”问题扩展审计到全项目，避免同类方框只在其它页面尚未被人工发现。
- 清理 `App.Button.Floating` 被当作通用透明点击宿主的语义混用，并消除重复页面级裸 ItemContainer Template。

重点范围：

- `BookCardView`、Book Details 章节列表、Cache Management 书籍/章节项、规则卡片选择宿主、Player 目录/正文项以及真正的定位 FloatingAction。
- 所有 `App.Button.Floating` 生产调用方、Button + `App.Selection.*` 组合、ListBox/ListView ItemContainerStyle、整卡 Hover + 行内按钮组合。

实施与验收：

- 按“最终可见 owner”而不是 Style 名称分类：真正 FloatingAction、chrome-free interaction host、Selection Surface、普通弱 Surface Button 各自使用明确公共语义。
- 页面不得复制等价的裸 `ListBoxItem` Template；共享模板进入 Selection family，页面只提供选择绑定、Margin、虚拟化等差异。
- 对 Book Library、Book Details、Cache、Rules、Player 各生成至少一个确定性 Light/Dark Hover 画面进行视觉核对，重点检查直角 PointerOver、双层 Hover、Selected 被 Hover 覆盖、行内按钮与父项同时大面积高亮。
- 若发现实际问题，在公共 owner 修复并补回归测试；不得为了通过截图逐页面加透明背景、负 Margin 或局部遮罩。
- 所有视觉验收副产物在确认通过后删除，不纳入 Git；任务结束必须检查工作树无截图/脚本/manifest 残留。

## [ ] T012（P0）：完成视觉残留修复的最终质量门禁

依赖：T009–T011。

目标：

- 证明上一轮“自动合同已通过但真实像素仍有残留”的测试缺口已经关闭。
- 确保本轮视觉修复没有改变业务行为，也没有把临时验收工具提交进仓库。

完整验收：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

并额外确认：

- Player 目录/正文真实 Hover 像素只有一个圆角 owner；ListBoxItem/Button Provider 状态不会再生成直角第二层。
- Volume Thumb 在 Rest/Hover/Dragging、Light/Dark、100%/125%/150% DPI 下完整对称且无右侧裁切，轨道仍连续。
- Book Library、Book Details、Cache、Rules 的代表性 Hover/Selected 状态不存在同源双层交互面。
- `App.Button.Floating` 只承担真正 FloatingAction 语义；通用透明点击宿主使用独立公共 owner（若 T009 验证确有需要）。
- `git status --short` 只包含任务要求的最终修改；仓库中不存在本轮生成的截图、截图脚本、临时 manifest、VisualTree dump 或其它验收副产物。

验收：

- 所有质量门禁和新增真实像素/宿主合同通过后，将 T009–T012 标记完成并记录成果。
- 若仍存在肉眼可见但自动测试无法证明的状态，任务不得以“Setter/VisualTree 符合预期”为由关闭；保留 `[!]` 并记录截图对应状态与可复现步骤。
