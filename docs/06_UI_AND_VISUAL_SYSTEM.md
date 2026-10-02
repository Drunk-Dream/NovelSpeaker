# UI 与视觉系统

## 1. 总体原则

NovelSpeaker 是桌面听书工具，UI 优先信息效率、清晰操作和稳定响应。

- 页面结构服务于内容，不用多层装饰容器替代信息层级。
- Light / Dark 保持相同布局和交互语义。
- 标准 WPF/Wpf.Ui 控件优先使用 Provider 正式能力。
- 页面状态与业务命令来自 ViewModel/Application owner；code-behind 只处理 WPF 特有生命周期与交互桥接。
- 大页面先可交互，再做次级 enrichment。
- 瞬时操作结果、警告和失败通知统一复用已有 Snackbar，通过 `IAppFeedbackService` 展示；禁止用页面内嵌通知块替代或重复展示。进行中进度、字段校验、空状态按其页面职责展示。
- Library 导入保持轻量分流：小于 5 MiB 直接导入，不展示进度、不发送进行中通知；大于等于 5 MiB 使用可取消进度对话框。导入结果统一使用已有 Snackbar 反馈，不在页面内嵌导入通知块。

## 2. 页面信息架构

主窗口保留清晰一级入口：

- Library；
- Playback；
- Settings；
- 必要的长期后台状态入口。

设置首页为入口列表，设置子页面保持扁平，不对设置项做分组，不增加分组标题和多层 Card。子页面中前往规则、管理或其他子页面的导航入口统一放在普通设置项之后，连续排列在设置项列表末尾。

设置首页分组长期按以下信息架构组织：

```text
常用
  常规
  播放设置
  语音服务

导入与文本
  导入
  正则替换

应用
  缓存与数据
  外观
  实验性功能
  诊断与关于
```

“导入”是所有 TXT 导入相关配置的统一入口，设置项按以下顺序排列：

```text
拆分长段落
长段落阈值
空行分章
文件名元数据规则 >
正文头部元数据规则 >
章节规则 >
```

“正则替换”不再嵌在导入页，作为“导入与文本”分组的独立入口。

章节规则、正则规则、元数据规则等 Rules 页面采用统一工作台语义：列表 + 选择 + draft/editor + Save/Cancel。文件名元数据规则与正文头部元数据规则是两个独立工作台，不建立 SourceType 万能页；元数据规则编辑器只编辑名称与 Regex Pattern，不增加测试输入区。

“语音服务”页面同样使用桌面双栏工作台，但 Provider 不是普通 Rule：左侧统一管理 Provider Instance，右侧由 Provider Type 决定具体 Editor。

## 3. 全局列表状态语义

所有需要表达 Current、Selected、Hover、Focus 的列表/卡片统一使用类似桌面文件管理器的视觉语义。四类状态分别占用不同视觉通道，可以组合，而不是互相覆盖。

### Current

`Current` 表示系统当前正在使用、播放或定位的对象，例如当前章节、当前段落、CurrentProvider。

- 使用左侧窄 Accent 指示条作为主要视觉标记；默认宽度约 3 DIP。
- Current 本身不占用 Selected 背景或 Accent 边框。
- Current 不等同于编辑选择，也不等同于批量选择。

### Hover

- Hover 只使用轻量瞬时背景。
- Light 默认继续使用 `App.Brush.Interaction.Surface.Hover = #EEF0F6`。
- Dark 默认继续使用 `App.Brush.Interaction.Surface.Hover = #2B303A`。
- Hover 不改变 Current/Selected 的持久状态事实。

### Selected

`Selected` 表示用户当前选中、准备编辑或批量操作的对象。

- 使用比 Hover 明显更强、但弱于 Primary Button 的中性选择背景。
- Light `App.Brush.Interaction.Surface.Selected = #E0E4EE`
- Light `App.Brush.Interaction.Surface.Selected.Hover = #D7DCE8`
- Dark `App.Brush.Interaction.Surface.Selected = #363D48`
- Dark `App.Brush.Interaction.Surface.Selected.Hover = #404855`
- Selected 默认保持正常可读前景，不使用 Accent 边框作为主要选择信号。
- 普通单选和批量选择复用同一 Selected surface；行为模式不同，但不另造一套颜色体系。

### Focus

- Keyboard Focus 使用现有 `App.Brush.Focus` 边框/Focus chrome。
- Focus 可以叠加在 Current、Selected 或 Current+Selected 上，不用背景颜色替代。

### 组合规则

```text
Rest
Hover                         = Hover surface
Selected                      = Selected surface
Selected + Hover              = Selected.Hover surface
Current                       = Current accent rail
Current + Hover               = Current rail + Hover surface
Current + Selected            = Current rail + Selected surface
Current + Selected + Focus    = Current rail + Selected surface + Focus border
```

Pressed 仍是瞬时交互态，但不得抹掉 Current rail 或 Selection 事实。

共享 Selection style 应以这些稳定语义为 primitive，避免 `CurrentItem`、`MultiSelectItem`、普通 `IsSelected` 分别复制同一套 Setter。一个可点击区域仍只有一个 Surface owner。

## 4. 页面级批量管理模式

普通业务页面的批量选择不再统一套用“始终像文件管理器一样多选”的行为。对于 Library、Playback 章节、Speech Provider、Rules 等页面，选择应服务于明确的“管理对象”模式。

精确合同见 `specs/BATCH_MANAGEMENT.md`。长期摘要：

- Selection scope 是页面级，不跨页面；离开页面退出管理模式并清空。
- 正常模式下，普通点击继续执行页面原本的“使用对象”动作，例如打开 Book、跳转章节、切换编辑对象。
- Ctrl/Shift 点击可以进入管理模式；页面还必须提供一个显式、可发现的“选择/批量管理”入口。
- 一旦进入管理模式，普通点击只切换该项是否选中，不再执行打开、跳转、激活、编辑切换等动作。
- SelectedCount 降到 0 不自动退出管理模式；只有显式 Cancel/退出或离开页面才退出。
- Select All 只作用于当前可见、当前可管理的同类对象。
- 过滤/搜索/数据刷新导致条目不再可见时，该条目必须从选择集中移除；稳定排序和同一对象的普通刷新可以保留选择。
- 管理模式中的右键：右键已选项作用于整个选择集；右键未选项先把选择集切成该项单选，再打开单项菜单。
- Page Header 与 Context Menu 可以提供同一业务动作，但不要求按钮布局完全一致；Context Menu 不提供“取消管理模式”。
- 不同对象类型不得混在一个 selection scope。
- Dirty Draft/未保存编辑不能因为进入管理模式而被隐式丢弃；复用页面已有保存/放弃/取消保护。
- 批量动作逐项执行，单项失败不终止剩余项；最终汇总成功/失败/跳过。
- 破坏性批量动作只做一次批次级确认；非破坏性动作不重复确认。
- 选择集中存在不支持某动作的项时，允许保留选择；执行时处理支持项、跳过不支持项并汇报。
- 一次批量导出产生一个批次级结果，不弹出多个独立保存对话框。
- 删除不提供 Undo/回收站。

### 页面能力

- Library：Select All、Delete、Export。
- Playback 章节：通用章节管理模式；当前第一项业务动作是 Cache，后续可增加其它章节批量动作。
- Speech Provider：Select All、Export、Delete；不同 Provider Type 可以按能力跳过不支持动作。
- Rules：每个规则工作台独立 selection scope，提供 Select All、Export、Delete。
- Provider 与 Rule 永远不共享一个 selection scope。
- CacheManagement 是明确例外，继续保持当前文件管理器式 Extended Selection 语义，不强制套入页面级管理模式。

## 5. 语音服务管理页

桌面端保持：

```text
Provider 列表 | Provider Editor Host
```

规则：

- Provider 不按 HTTP / Edge / Local 分组。
- Provider 列表读取一份统一 SortOrder。
- 正常模式下普通点击左侧项表示“正在编辑”，使用全局 Selected 视觉。
- CurrentProvider 是独立业务状态，使用全局 Current 左侧 Accent rail，不显示“当前”文字，也不允许通过该标识切换 Provider。
- 正在编辑 CurrentProvider 时，自然组合为 Current rail + Selected surface。
- Provider 没有 Enabled Toggle。
- HTTP Provider 支持复制、单项/批量导出、删除等类型适用动作。
- Microsoft Edge 不显示创建、复制、导入、导出、删除等不适用动作；批量动作通过能力判断跳过不支持项。
- 页首提供“新建”和“导入”；当只有 HTTP 可由用户创建时，不显示无意义 Provider Type 选择器。
- HTTP 模板帮助只属于 HTTP 编辑器语境。
- Provider 编辑器之间共享 Draft/Dirty/Save/Cancel/Test 生命周期，不强行共享字段布局。
- 新建 HTTP Provider 先进入右侧 Draft，保存成功后才进入左侧列表。
- 切换左侧 Provider、进入管理模式或离开页面时，Dirty Draft 使用已有保存 / 放弃 / 取消保护。
- 管理模式中的点击不改变右侧 Editor 当前对象；退出管理模式后恢复正常编辑选择行为。
- 删除 CurrentProvider 被允许；删除后 CurrentProvider 清空为 None，不自动切到其它 Provider。

## 6. 播放页 Provider 选择器

Provider 选择器是高频操作入口：

- 只显示已完成必要配置且当前可见的 Provider。
- 只显示名称，不显示 Provider Type 副标题。
- 不提供“None / 不使用语音服务”选项。
- Provider 顺序与管理页完全一致。
- 每个可选择项等宽并横向 Stretch，整行都是点击区域。
- Provider 行使用约 50 DIP 的稳定最小高度和约 16 DIP 水平内边距；文字垂直居中并对过长名称使用 CharacterEllipsis。
- Provider 项之间保持紧凑间距；列表过长时在浮窗内部滚动。
- 当前 Provider 只使用全局 Current 左侧 Accent rail，不显示“当前”文字，也不把 Current 伪装成 Selected。
- CurrentProvider=None 时没有任何项显示 Current rail。
- 底部管理入口与 Provider 选择列表通过轻量 Divider 分隔，使用“设置/语音”图标 + `语音服务管理` + Chevron 的导航行。
- Popup 继续遵守 Single Surface，不再嵌套额外完整 Card。

## 7. 章节标题显示

ChapterIndex 只属于内部数据模型，面向用户的章节列表和当前章节位置不再额外格式化“第 N 章”。

统一显示规则：

- 显式章节规则识别出的标题直接显示原始 `Chapter.Title`；
- 由空行分章产生的无标题章节显示其系统生成 Title，例如“第 2 节”；
- BookDetails、Player、CacheManagement、Export 选择等所有章节列表遵守同一规则；
- 不因为标题文本已经含“第 N 章”而再次添加 UI 自动编号。

## 8. 拖拽排序

所有支持手动排序的列表统一使用“插入槽位”语义，而不是目标卡片 Before/After 两套边界。

```text
────────  slot 0
Item A
────────  slot 1
Item B
────────  slot 2
```

要求：

- N 个当前可见 item 只有 N+1 个可见插入槽。
- 插入指示横线位于两张卡片的 gap 中，不压在卡片上/下边界。
- 相邻卡片之间只有一个候选位置。
- 列表顶部和底部也各有一个唯一槽位。
- 保留已有边缘自动滚动等成熟拖拽体验。
- Provider、章节规则、正则替换规则、元数据规则等排序列表统一采用这一交互原则。

Provider 存在隐藏项时，可见列表只是完整排序的投影：

- 隐藏 Provider 不因为隐藏而主动改序。
- 拖到两个可见 Provider 之间时，按可见插入槽映射回完整排序。
- 用户显式拖动其它 Provider 可以跨过隐藏 Provider。
- 隐藏 item 不产生用户看不见的额外命中边界。

## 9. 响应式 Library

Library 使用响应式多列卡片：

- 根据可用宽度决定列数和 card width。
- 常用窗口宽度下避免明显无意义空白。
- Row 内少量 Card，不建立第二套 virtualization。
- 外层列表使用 WPF 标准 recycling virtualization。
- resize 后保持稳定书籍顺序，并尽量保持逻辑 anchor 可见。
- 搜索继续覆盖书名与作者。
- 排序至少提供最近阅读、标题和最近导入。

跨页面滚动恢复保存逻辑 anchor（例如 BookId），不保存自定义虚拟画布状态。

## 10. 超长列表

BookDetails、Player、CacheManagement 等连续 catalog 至少按 10,000 条设计：

- 用户侧不显式分页；
- 连续 scrollbar；
- current item 可直接定位；
- catalog 与 dynamic decoration 分离；
- selection 与 WPF container 分离；
- 首屏不使用 `Clear + N × Add`；
- WPF virtualization 负责 container/layout，应用负责轻量数据 projection 与有界更新。

不自行实现 ItemContainerGenerator、recycling protocol、scroll extent/viewport/offset 状态机。

## 11. Staged Loading 与性能

复杂页面：

```text
Critical
→ First Interactive Frame
→ Secondary Enrichment
→ Background Enhancement
```

首个交互帧只等待必要内容，例如 Header、轻量 catalog、当前阅读位置和主要动作。

禁止在首屏 Dispatcher 同步执行：

- 大规模 DTO→复杂 VM；
- 全量 cache decoration；
- 全量 sort/group/hash；
- 无界 locator/layout 循环；
- 同步等待后台任务。

真实性能耗时用于诊断和跨版本观察，不以脆弱的绝对毫秒阈值作为主要 CI 合同。

## 12. 视觉资源

资源分层：

```text
Wpf.Ui provider
→ NovelSpeaker palette
→ design tokens
→ provider style bridge
→ explicit App.* styles
→ shared controls
→ feature components
→ page-local layout
```

- Palette 保存语义颜色。
- Token 只保存跨多个组件稳定的 spacing/radius/icon/min-size/typography/animation。
- 页面专用几何不进入全局 Token。
- 应用级不为标准控件建立 NovelSpeaker 隐式样式接管。
- 标准控件完整 ControlTemplate 替换只允许局部、明确且有 WPF 合同测试的场景。

文本框、密码框和下拉框统一采用桌面输入密度：Standard 最小高度 32 DIP、内边距 `10,4`，Compact 最小高度 28 DIP、内边距 `8,2`，由全局 Input Token 和显式 `App.Input.*` 样式控制。文字使用正文大小；文本输入保持左对齐，多行输入随内容增高，页面可以按编辑需求声明更大的最小高度。按钮与开关继续使用各自的点击区域约定。

### Button 样式语义

应用级 Button Style 按稳定交互职责命名，不按“透明”“某页面按钮”或某次视觉实现命名。

- Primary / Secondary / Subtle / Icon / Danger 等共享样式表达稳定动作语义。
- 当 Button 只负责命令、键盘焦点与命中区域，Hover / Selected / Current 等视觉由内部 Selection/Surface 统一表达时，应使用明确的 interaction-host 语义；宿主本身不得再绘制第二层 Hover/Pressed Surface。
- 相同交互语义跨 Feature 复用同一共享样式，不为 Provider、Book、Cache 等页面分别创建等价 Button Style。
- 只有控件族确实拥有独立、长期的交互模型时，才使用 `App.Media.*`、`App.Navigation.*` 等领域命名空间。
- 同一个可点击区域只允许一个视觉状态 owner。
- 样式整理或重命名时直接迁移调用方并删除旧 Key，不为内部资源名长期保留兼容 alias。

## 13. Shared Controls

全局 Shared 只包含真实跨 Feature 复用的应用自有 UI primitive，例如：

- AppPageHeader；
- Settings row/navigation row；
- form field；
- status view；
- section surface；
- selection surface；
- 通用页面级 batch selection/controller primitive（仅表达稳定选择行为，不包含业务动作）。

Book card、Provider card/editor、rule card、player content、cache chapter item 仍由 Feature 拥有。

共享拖拽排序只抽象稳定的“插入槽位/重排交互”和成熟的桌面选择手势，不把不同业务项的数据模型合并成万能列表模型。

## 14. Surface 与浮层

Dialog、Flyout、Popup、独立状态窗口遵守 **Single Surface**：

- 宿主已经提供完整 Surface 时，内容默认透明或弱分组。
- 不默认在 Popup/Flyout/Dialog 内再嵌完整 Raised Card。
- 四角不得因内外两层背景出现直角残影或双层边界。

诊断工具悬浮控制条属于明确的独立轻量工具 Surface，不嵌套多层 Card。

## 15. 主题与图标

- 支持 Light / Dark / System。
- System 跟随操作系统实际主题。
- 普通文本使用显式 `App.Typography.*` 或动态语义前景资源；共享内容宿主提供动态主题前景色，避免首次创建内容时落到 WPF 默认黑色。
- 产品 XAML 的文本样式必须能解析到主题前景色；前景色定义使用动态语义资源或显式绑定主题 owner，不使用静态画刷引用或硬编码可见颜色。C# 动态创建文本使用 `SetResourceReference` 设置语义样式/画刷。
- 图标使用主题语义资源，不硬编码黑色/白色。
- Hover/Pressed/Focus 不得让 Dark Mode 图标失去可读性。
- 主题切换不通过运行时代码重写标准控件完整模板。
- Light/Dark 快捷切换保持高频可达。
- High Contrast 下 Current/Selected/Focus 仍必须可区分；必要时优先使用系统 Highlight/Focus 资源。

## 16. 控件交互

- Icon Button 使用紧凑、可访问的 Tooltip/Automation Name。
- Slider thumb/track/filled track 保持几何连续。
- ToggleSwitch 的 focus/hit-test 范围与可见布局一致，不保留宽大隐藏点击区域。
- 列表 item 的 Hover/Selected/Current/Focus 使用本文件统一视觉语义，不在 Feature 内重新定义近似但冲突的状态颜色。
- Esc 优先由局部临时交互消费，否则进入统一页面返回语义。
- Reduced Motion / 系统动画设置保持可用。

## 17. Diagnostics UI

### 性能遥测

Settings 中使用一行轻量入口：

- 性能遥测 Toggle；
- 清除遥测数据的 Icon Button；
- 导出诊断信息的 Icon Button；
- 少量说明文字。

不展示数据大小、最近采集时间、窗口数量等监控面板信息。

### 诊断工具

从 Settings 打开诊断工具后显示悬浮控制条。

准备状态：

```text
[开始诊断] [容量上限/次级设置] [关闭]
```

采集中：

```text
诊断中 + 时长
[标记问题] [截取当前窗口] [结束]
```

结束后：

```text
诊断已保存
[重新开始] [导出] [完成]
```

要求：

- 在用户点击“开始诊断”前不创建/采集诊断会话。
- “标记问题”表示问题发生在该时间点附近，可在问题即将发生或刚发生后点击。
- Marker、截图、立即导出都是增强功能，不是完成诊断的必选步骤。
- 达到硬容量上限后明确显示采集已停止，用户可以调整上限后重新复现。
- 不加入长时间录制提示、复杂问题分类或问题描述表单。
- 截图只截 NovelSpeaker 自身窗口，且必须由用户主动触发。

## 18. 自动视觉验收

WPF tests 重点保护真正影响核心可用性的边界，不为样式微调建立永久测试。

对于 Selection 语义、Provider 选择器、拖拽插入线、CurrentProvider 状态等视觉任务，可以使用任务内临时测试、Style Gallery 或截图进行验证；必须覆盖 Light/Dark，并在涉及 Current+Selected 时至少验证组合态。通过后删除临时截图、脚本和临时测试。

主题相关验收须覆盖先应用主题再首次创建页面、已有页面切换主题、切换后新建页面。全量产品 XAML 的通用主题资源架构检查作为持续门禁，检查遗漏主题前景、静态画刷引用和硬编码颜色，不冻结具体颜色值或模板结构；页面专用视觉验证代码在任务完成后删除。

人工视觉验收始终是可选补充，不阻塞 Agent 完成任务。
