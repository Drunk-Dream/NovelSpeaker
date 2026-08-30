# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段只围绕 **UI 交互样式收口** 展开，以 `docs/13_VISUAL_DESIGN_SYSTEM.md` 定义的“克制型 Fluent”为最终视觉合同。上一轮已完成任务不在本文件继续保留，历史由 Git 追溯。

本轮目标：

- 建立统一的 Hover / Pressed / Keyboard Focus / Selected/Checked 交互语义与 Light / Dark / High Contrast 主题资源。
- 消除播放器媒体按钮悬浮时出现的大面积方框、ToolbarValue 外层 Button 与内部 Pill 的双层反馈等典型问题。
- 统一播放进度、竖向音量、语速、定时停止等播放器交互样式，并保持现有 TTS 变量、缓存和播放业务语义不变。
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

## [ ] T003（P0）：统一播放进度、竖向音量与语速/定时 Flyout

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

## Phase C：输入、列表与菜单交互统一

## [ ] T004（P0）：收口 Input、Selection 与设置行状态边界

依赖：T001。

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

## [ ] T005（P1）：统一 ContextMenu/MenuItem 与独立 Separator

依赖：T001。

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

## Phase D：全项目迁移与质量收口

## [ ] T006（P1）：审计并迁移剩余交互样式调用方

依赖：T002–T005。

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

## [ ] T007（P0）：完成交互样式阶段的跨模块审阅与完整质量门禁

依赖：T001–T006。

目标：

- 对“克制型 Fluent”实施结果进行一次跨控件族自动审阅。
- 确保视觉优化没有改变播放、TTS、定时、音量、规则、选择和设置业务语义。

审阅：

- 复查 Hover/Pressed/Focus/Selected 状态所有权和优先级，确认没有 Mouse Hover Focus Ring、Selected 被 Hover 覆盖或父子双层大面积高亮。
- 复查 Player/MiniPlayer 媒体按钮、ProgressSlider、VolumeSlider、Speed/Timer Flyout 与 Single Surface 合同。
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
- Separator 独立且完整，Disabled MenuItem 不影响分隔线。
- ToggleSwitch 纯开关宽度、Selected+Hover、Keyboard Focus 和 High Contrast 关键状态保持可访问。
- 默认测试未设置可见窗口授权；如生成视觉产物，仅通过隐藏 Desktop 的显式生成流程。

验收：

- 完整质量门禁全部通过。
- 若发现可自动修复的问题，在本任务内修复并重跑受影响门禁；真实阻塞标记 `[!]` 并记录可复现证据。
