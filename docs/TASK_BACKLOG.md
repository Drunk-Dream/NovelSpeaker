# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段重新建立长期稳定的视觉样式体系。现有全局视觉 Wave 1 从 `e8079000d4bd82f844e7741fb35cc6e63ad17e2f` 开始，其修改范围过大且样式所有权不稳定，将通过单个聚合 `git revert` 提交回到基线 `25a746c608bd46f87126b828b6c879104f3c08c6`，同时保留本次更新后的 `docs/`。

放弃阶段记录：

- `archives/2026-08-03_ABANDONED_GLOBAL_VISUAL_WAVE1.md`

新的实现以 `13_VISUAL_DESIGN_SYSTEM.md` 为最终形态依据。Wpf.Ui 保持标准控件模板所有权，NovelSpeaker 通过 palette、稳定 Design Token、Provider Style Bridge、具名样式、自有组件和页面局部布局逐步迁移。

本阶段不改变书籍、播放、缓存、规则、导出、托盘、媒体控制或持久化语义。

## 2. 状态与优先级

状态：

- `[ ]` 未开始
- `[~]` 进行中
- `[x]` 完成
- `[!]` 阻塞；必须记录自动检查证据和恢复条件

优先级：

- `P0`：Git 回退、资源所有权、主题稳定性、行为回归和发布阻塞项。
- `P1`：Style Gallery、公共组件和单页迁移。
- `P2`：最终一致性、清理和非阻塞增强。

## 3. Codex 执行规则

1. 默认每次调用只执行一个编号任务；完成该任务、更新状态、提交并报告后停止。只有用户明确指定多个编号时才串行执行多个任务。
2. 不把用户视觉确认写入任务完成条件。要求截图的任务必须自动生成浅色/深色 PNG 与 JSON manifest，并报告路径，供用户在任务结束后自行查看。
3. 每个任务只能修改其声明的组件族或页面。不得顺便调整其它控件、全局密度、其它页面布局或 Wpf.Ui 版本。
4. 新视觉先进入 Style Gallery；除明确写明“迁移到产品页面”的任务外，不修改正式页面。
5. 页面迁移使用显式 `App.*` 样式和应用自有组件，不新增 Application/global 标准控件隐式样式。
6. 不在全局字典中替换标准 WPF/Wpf.Ui 控件完整 `ControlTemplate`。
7. Design Token 只保存跨组件稳定标尺；页面专用列宽、Padding、Margin 和固定宽度保留在唯一布局 owner。
8. 缺陷修复先增加失败测试。截图只作为产物，自动关闭依赖构建、资源、几何、可访问性和行为测试。
9. 除任务 1 的用户指定聚合回退外，提交按逻辑目的保持小而可回退。不得推送、强制更新远端或删除远端分支。
10. 每项任务完成后将自身 `[ ]` 更新为 `[x]`；不得预先标记后续任务。

完整自动质量门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

视觉截图输出目录：

```text
artifacts/visual-review/<task-id>/
```

该目录不进入 Git 提交。每个 manifest 至少记录任务号、Git commit、主题、DPI、窗口尺寸、场景名和 PNG SHA-256。

## 4. 总体开发顺序

```text
1  聚合 revert 并切换实验分支
2  样式所有权守卫与旧审计清理
3  Style Gallery 与自动截图宿主
4  Wpf.Ui Provider Bridge 和稳定主题链路
5  语义 Palette
6  稳定 Token、排版和表面
7  按钮组件族
8  媒体控制组件族
9  迷你播放器窗口表面
10 迷你播放器内容与媒体控制
11 输入与选择控件族
12 列表、卡片和设置行
13 导航、菜单、进度与反馈组件
14 设置页试点
15 其余设置页
16 书库
17 书籍详情
18 播放页
19 TTS 规则工作台
20 章节规则工作台
21 正则替换工作台
22 缓存管理与缓存数据页
23 主窗口壳层
24 Dialog、Flyout、Snackbar 与状态视图
25 DPI、可访问性、资源清理与发布门禁
```

---

## [x] 1（P0）：聚合回退不稳定视觉 Wave，并切换到实验分支

目标：

- 保留已经提交的历史，不使用 `reset --hard` 或强推改写主分支历史。
- 将 `25a746c608bd46f87126b828b6c879104f3c08c6` 之后、当前 docs 更新提交之前的全部视觉 Wave 修改反向应用。
- 回退结果压缩为一个提交。
- 保留当前 docs 更新内容。
- 从回退后的主分支创建并切换到 `experiment/visual-system-v2`。

前置自动检查：

- 当前分支必须是 `main`，工作区和 index 必须干净。
- 当前 `HEAD` 必须是用户刚提交的 docs-only 提交；`git diff-tree` 结果不得包含 `docs/` 以外的文件。
- `e8079000d4bd82f844e7741fb35cc6e63ad17e2f^` 必须等于基线 `25a746c608bd46f87126b828b6c879104f3c08c6`。
- 基线必须是回退终点的祖先。
- 回退范围不得包含 merge commit；若包含则标记 `[!]` 并停止，不猜测 `-m` mainline。
- `archive/visual-wave1-before-revert-2026-08-03` 和 `experiment/visual-system-v2` 不得已指向其它提交。

执行算法：

```bash
BASE=25a746c608bd46f87126b828b6c879104f3c08c6
DOCS_HEAD=$(git rev-parse HEAD)
ROLLBACK_END=$(git rev-parse HEAD^)

git branch archive/visual-wave1-before-revert-2026-08-03 "$DOCS_HEAD"
git revert --no-commit "$BASE..$ROLLBACK_END"
git restore --source="$DOCS_HEAD" --staged --worktree -- docs
```

随后：

- 只允许在 `docs/TASK_BACKLOG.md` 中将任务 1 标为完成，并填写实际回退提交/分支结果。
- `git diff "$BASE" -- . ':(exclude)docs/**'` 必须为空，证明非 docs 工作树等于基线。
- `git diff "$DOCS_HEAD" -- docs` 只能包含 `docs/TASK_BACKLOG.md` 的任务状态和结果记录。
- 运行完整自动质量门禁。
- 将全部反向代码差异和任务 1 状态压缩为一个提交：

```text
revert(ui): roll back unstable visual system wave
```

- 提交后再次验证该提交之外的非 docs 树与基线完全一致。
- 从该提交创建并切换：

```bash
git switch -c experiment/visual-system-v2
```

- 不推送任何分支。

自动验收：

- `main` 新增且只新增一个聚合回退提交。
- 回退提交是普通单父提交。
- 非 docs 文件树与基线提交一致。
- 当前 docs 仍存在，只有任务 1 跟踪内容发生预期变化。
- 归档分支指向回退前 docs 提交。
- 当前分支为 `experiment/visual-system-v2`，其起点为聚合回退提交。
- 完整自动质量门禁通过。

回退结果（由执行任务的 Codex 自动填写）：

```text
Revert commit: the single-parent commit containing this result (`revert(ui): roll back unstable visual system wave`), directly on top of `57d4dfe`
Archive branch: archive/visual-wave1-before-revert-2026-08-03
Working branch: experiment/visual-system-v2
```

## [x] 2（P0）：建立样式所有权守卫并清理放弃阶段审计资产

前置：1。

实现：

- 删除不再代表当前实现的 `docs/VISUAL_ASSET_AUDIT.md` 和 `docs/VISUAL_ASSET_AUDIT.json`；历史原因由归档文档保留。
- 新增 `VisualStyleArchitectureTests` 或等价测试，扫描 App.xaml、全局合并字典和主题运行时代码。
- 禁止 Application/global 范围的标准 WPF/Wpf.Ui 控件隐式 NovelSpeaker 样式；允许项必须位于自有组件局部并有显式白名单。
- 禁止主题运行时代码向 `Application.Resources` 写入 Style/ControlTemplate 类型键。
- 禁止全局资源替换标准控件完整模板。
- 禁止全局 Design Token 使用页面专用命名，例如 `PagePaneWidth`、`SettingsControlWidth`、`WorkbenchListWidth`、`RuleActionGap`。
- 生成 `artifacts/visual-review/02/style-ownership-audit.json`，列出 provider、全局字典、隐式样式、模板覆盖和页面局部资源。

自动验收：

- 架构测试能够用内置故障 fixture 证明每条禁令会失败。
- 基线代码通过全部守卫。
- 审计 manifest 可重复生成且路径分类完整。
- 完整质量门禁通过。

结果：

- `VisualStyleArchitectureTests` 已覆盖隐式全局样式、未白名单全局模板、主题运行时资源写入和页面专用 Design Token 故障 fixture。
- 审计 manifest：`artifacts/visual-review/02/style-ownership-audit.json`。
- 已删除放弃阶段资产：`docs/VISUAL_ASSET_AUDIT.md`、`docs/VISUAL_ASSET_AUDIT.json`。

## [x] 3（P0）：建立独立 Style Gallery 与自动截图宿主

前置：2。

实现：

- 新增 `tools/NovelSpeaker.StyleGallery` 或等价独立 WPF 工具项目。
- 工具只引用 UI 资源和测试数据，不读取用户数据库、设置、书籍或缓存。
- 工具不注册到正式导航，不被生产 App 引用，不进入 publish 输出。
- 提供场景注册表、浅色/深色切换、固定窗口尺寸、固定 DPI 和自动退出截图模式。
- 初始场景至少包含 Provider 标准控件、主题资源探针和占位分区。
- 输出 PNG 和 JSON manifest 到 `artifacts/visual-review/03/`。
- WPF 测试复用同一场景注册表，验证每个场景可构造、Measure/Arrange、Render 且无 Dispatcher 未观察异常。

自动验收：

- Light/Dark 各生成至少一张非空 PNG。
- PNG 尺寸、DPI、场景名和 SHA-256 与 manifest 一致。
- 连续运行两次场景清单和尺寸一致。
- self-contained publish 自动检查确认不包含 Style Gallery 程序集和资产。
- 完整质量门禁通过。

结果：

- 独立工具 `tools/NovelSpeaker.StyleGallery` 已加入 solution；仅引用 `wpf-ui`，不引用生产 App、Application、Infrastructure 或数据层。
- 场景注册表、Provider 标准控件、主题资源探针、占位分区、Light/Dark 资源切换、固定 1280×820/96 DPI 宿主和自动退出截图入口已完成。
- `artifacts/visual-review/03/manifest.json` 与 6 张 PNG 已生成；PNG 为 1280×820、非空，manifest 含场景名、尺寸、DPI 和 SHA-256；连续两次运行哈希与清单一致。
- WPF 场景、manifest 和 PNG 契约测试 5/5 通过；架构/发布边界测试通过；Style Gallery 与完整 solution Release build 均 0 警告、0 错误；`dotnet format --verify-no-changes --no-restore` 通过。
- 完整 `dotnet test -c Release --no-build`、self-contained `win-x64` publish 和发布包隔离检查通过；首次全量测试中的 PlaybackCoordinator 超时经单测重跑通过，确认是时序波动。
- Release workflow 已增加 Style Gallery 程序集与 `visual-review` 资产排除检查；新增 Gallery lock 文件，WPF 测试 lock 仅记录新增项目依赖及还原后的项目版本。

## [ ] 4（P0）：建立 Wpf.Ui Provider Style Bridge 和稳定主题链路

前置：3。

实现：

- 固定 Wpf.Ui theme/provider dictionaries 的加载顺序和生命周期。
- 新增只含显式键的 `ProviderStyleBridge.xaml`，为后续需要扩展的 Button、TextBox、PasswordBox、ComboBox、CheckBox、ToggleSwitch、NavigationViewItem 和 Slider 提供 provider alias。
- Provider Bridge 不设置页面语义、不替换模板。
- 主题切换只调用 Wpf.Ui 主题入口和 NovelSpeaker palette 入口；删除或禁止任何 Style 恢复/重新注入路径。
- Style Gallery 增加 provider probe，显示每个桥接样式的模板、MinWidth、MinHeight、内容对齐、Focus 和 Disabled 状态。

自动验收：

- 主题切换前后 provider/bridge Style 对象可解析，资源字典数量和类型键集合不漂移。
- 标准控件 Template 非空，内容对齐和 Disabled/Focus 状态可触发。
- 运行时代码扫描不存在 Style 重新写入。
- 生成 `artifacts/visual-review/04/` 浅色/深色截图与 manifest。
- 完整质量门禁通过。

## [ ] 5（P1）：建立语义 Palette，不迁移正式页面

前置：4。

实现：

- 建立 Light/Dark palette 和语义 Brush：AppBackground、CanvasSurface、PrimarySurface、SecondarySurface、RaisedSurface、三档文本、两档边框、Accent 族和状态色。
- palette 键在两个主题中完全一致。
- Brush 使用 `DynamicResource` 链路，Style Gallery 热切换后更新。
- 不修改任何正式页面、窗口布局、控件高度、Padding 或模板。
- Gallery 展示全部颜色及文本/图标对比样例。

自动验收：

- Light/Dark 键集合和资源类型一致。
- 热切换后所有 probe Brush 更新且 Style/Template 实例未被替换。
- 对主要文本/背景组合执行自动对比度检查。
- 生成 `artifacts/visual-review/05/`。
- 完整质量门禁通过。

## [ ] 6（P1）：建立稳定 Token、排版和表面组件，不迁移正式页面

前置：5。

实现：

- 定义稳定间距标尺、圆角、图标尺寸、最小控件高度、字体层级、动效时长和 Elevation。
- 新增 PageHeader、SectionSurface 和 StatusView 的 Style Gallery 组件样例。
- 不定义页面列宽、设置控件宽度、规则列表宽度或页面专用 Padding。
- 不调整正式页面密度和布局。

自动验收：

- Token 命名架构测试通过且不存在页面专用几何。
- 组件在 Light/Dark、100/125/150% DPI 下 Measure/Arrange 无负值、NaN、零宽关键内容或裁剪异常。
- 生成 `artifacts/visual-review/06/`。
- 完整质量门禁通过。

## [ ] 7（P1）：建立具名按钮组件族，仅用于 Style Gallery

前置：4–6。

实现：

- 建立 Primary、Secondary、Subtle、Icon 和 Danger 具名样式。
- 样式通过 Provider Bridge 保留 Wpf.Ui 模板和基础状态，不设置完整 ControlTemplate。
- Gallery 覆盖 Default、Hover、Pressed、Focus、Disabled、图标+文本和长中文文本。
- 不修改正式页面中的 Button Style 引用。

自动验收：

- 全局隐式样式守卫通过。
- 每个状态可通过 WPF 输入/VisualState 驱动并保持内容可见。
- 点击区域不小于 `32 × 32`，状态变化不改变外部布局尺寸。
- 生成 `artifacts/visual-review/07/`。
- 完整质量门禁通过。

## [ ] 8（P1）：建立媒体控制组件族，仅用于 Style Gallery

前置：7。

实现：

- 建立主播放按钮、段落按钮、章节按钮、窗口动作按钮和媒体 Slider 的具名样式/自有组件。
- 明确上一章与上一段、下一章与下一段图标差异。
- Gallery 覆盖播放/暂停、置顶激活、长 Tooltip、Focus、Disabled 和拖动状态。
- 不修改正式播放页或迷你播放器。

自动验收：

- 主按钮、段落按钮和章节按钮最小尺寸与视觉权重合同通过。
- Slider Tooltip 投影 `x/y`，拖动不触发真实播放命令。
- 生成 `artifacts/visual-review/08/`。
- 完整质量门禁通过。

## [ ] 9（P1）：只迁移迷你播放器窗口表面与窗口动作

前置：8。

实现：

- 只调整迷你播放器 RaisedSurface、轻描边、圆角、阴影、标题文本层级和置顶/恢复/关闭窗口动作。
- 保留现有媒体按钮、进度条、内部布局尺寸和所有命令语义。
- 不修改主窗口或其它页面。
- 自动构造有播放上下文、无播放上下文、长书名/章节名和置顶状态。

自动验收：

- 修改文件白名单只允许 MiniPlayer 窗口、专属局部资源、直接测试和 Backlog。
- 隐藏/恢复、关闭等价恢复、置顶、拖动空白区和位置记忆测试通过。
- Light/Dark、长文本、100/125/150% DPI 截图生成到 `artifacts/visual-review/09/`。
- 完整质量门禁通过。

## [ ] 10（P1）：迁移迷你播放器内容布局与媒体控制

前置：9。

实现：

- 按最终横向媒体面板结构迁移章节标题、书名/段落信息、进度和五个媒体按钮。
- 应用任务 8 的显式媒体组件，不复制模板。
- 窗口尺寸约束为 `440–500 × 130–160` 的可用范围，并保留长标题省略与 Tooltip。
- 不修改播放页媒体控件。

自动验收：

- 主窗口与迷你播放器共享 PlaybackSnapshot，不增加第二状态机。
- 上下章、上下段、播放/暂停、Slider 拖动和 Tooltip 回归通过。
- 最小尺寸与 150% DPI 下按钮不重叠、文本不覆盖窗口动作。
- 生成 `artifacts/visual-review/10/`。
- 完整质量门禁通过。

## [ ] 11（P1）：建立输入与选择控件族，仅用于 Style Gallery

前置：4–6。

实现：

- 建立 Standard/Compact TextBox、PasswordBox、ComboBox、CheckBox 和 ToggleSwitch 具名样式。
- 保留 Provider 模板、内容对齐、Popup、键盘、Focus、Disabled 和验证状态。
- Gallery 覆盖空内容、长内容、无标签 ToggleSwitch、错误、只读和禁用。
- 不迁移正式页面。

自动验收：

- 主题热切换后 Style/Template 不漂移。
- Measure/Arrange 下输入内容区域非零，ToggleSwitch 不塌缩为窄条。
- Popup/ComboBoxItem 主题资源正确解析。
- 生成 `artifacts/visual-review/11/`。
- 完整质量门禁通过。

## [ ] 12（P1）：建立列表、卡片与设置行组件族，仅用于 Style Gallery

前置：6、7、11。

实现：

- 建立 BookCard、ListRow、SelectableRow、SettingsRow、RuleListItem 和 EmptyState 自有组件。
- 选择、当前播放、Hover、Focus 和 Disabled 使用独立状态。
- 组件只规定内部结构和最小尺寸，不规定页面列宽。
- 不迁移正式页面。

自动验收：

- 虚拟化和选择状态不依赖容器实例。
- 长标题、省略、Tooltip、AutomationName 和多状态组合测试通过。
- 生成 `artifacts/visual-review/12/`。
- 完整质量门禁通过。

## [ ] 13（P1）：建立导航、菜单、进度与反馈组件族，仅用于 Style Gallery

前置：4–7、12。

实现：

- 建立 Navigation Entry、ContextMenu/MenuItem、ProgressBar、Flyout surface、Dialog shell、Snackbar content 和 Loading/Error/NoResult 状态组件。
- Navigation 只通过显式样式扩展 Provider，不修改全局 NavigationViewItem。
- Danger 菜单项分组，Close 默认中性。
- 不迁移正式页面。

自动验收：

- 键盘导航、Focus、Escape、默认按钮和取消语义测试通过。
- 进度与 Slider 视觉和行为不混用。
- 生成 `artifacts/visual-review/13/`。
- 完整质量门禁通过。

## [ ] 14（P1）：以外观设置页作为首个正式页面试点

前置：11–13。

实现：

- 只迁移 AppearanceSettingsPage 及其直接自有组件。
- 使用显式输入/设置行/导航组件。
- 页面保持原有布局 owner，不调整 Shell、其它设置页或全局页面密度。
- 保持主题选择、即时生效、持久化和损坏恢复语义。

自动验收：

- 修改文件白名单和资源引用测试通过。
- 设置命令、持久化、导航和主题切换回归通过。
- 100/125/150% DPI 与 Light/Dark 截图生成到 `artifacts/visual-review/14/`。
- 完整质量门禁通过。

## [ ] 15（P1）：逐个迁移其余设置页面

前置：14。

实现：

- 按子页面分独立原子提交迁移 Settings 首页、General、Playback、ImportText、CacheAndData 和 Diagnostics/About。
- 每个提交只迁移一个页面及其直接组件。
- 不改变设置值、保存时机、导航层级或危险操作语义。
- 每个子页面分别生成 Light/Dark 截图目录。

自动验收：

- 每个子提交通过页面文件白名单、设置行为和键盘焦点测试。
- 最小窗口和 150% DPI 下右侧控件不遮挡标题/说明。
- 全部子页面完成后运行完整质量门禁。

## [ ] 16（P1）：迁移书库与书籍卡片

前置：12、13、15。

实现：

- 迁移 LibraryPage 和 BookCardView。
- 保持搜索、排序、拖放、打开、详情、删除和滚动位置语义。
- 卡片网格响应式列数由页面拥有，不进入全局 Token。

自动验收：

- 空书库、搜索无结果、长书名、不同书籍数量和多窗口宽度测试通过。
- 生成 `artifacts/visual-review/16/`。
- 完整质量门禁通过。

## [ ] 17（P1）：迁移书籍详情与目录

前置：12、13、16。

实现：

- 迁移 BookDetailsPage 的摘要、编辑区、目录和定位按钮。
- 保持虚拟化、Dirty State、章节跳转、缓存百分比和当前章节语义。
- 页面拥有目录/摘要布局，不修改 Shell。

自动验收：

- 长标题、0% 隐藏、定位、编辑保存/取消和导航守卫测试通过。
- 生成 `artifacts/visual-review/17/`。
- 完整质量门禁通过。

## [ ] 18（P1）：迁移播放页

前置：8、12、13、17。

实现：

- 迁移 PlayerPage、PlayerView 和直接媒体/正文组件。
- 复用已验证媒体组件，正文、章节侧栏和控制条布局由页面拥有。
- 保持播放状态机、进度、滚动追随、音量、定时停止、主动缓存和快捷键语义。

自动验收：

- 播放/暂停、上下段/章、拖动、当前段居中、用户滚动暂停追随和页面离开测试通过。
- 生成 `artifacts/visual-review/18/`。
- 完整质量门禁通过。

## [ ] 19（P1）：迁移 TTS 规则工作台

前置：11–13。

实现：

- 只迁移 TtsRulesPage。
- 左侧使用 RuleListItem，右侧使用显式输入与页面自有布局。
- 保持试听、当前规则、启用、排序、导出、删除、Dirty State 和导航守卫语义。

自动验收：

- 不使用全局固定工作台宽度 Token。
- 最小工作区下关键字段有非零可用宽度并可滚动。
- 生成 `artifacts/visual-review/19/`。
- 完整质量门禁通过。

## [ ] 20（P1）：迁移章节规则工作台

前置：19。

实现与自动验收遵循任务 19 的样式边界，只迁移 ChapterRulesPage，并覆盖默认规则、启用、排序、帮助、保存/取消和导航守卫。截图输出到 `artifacts/visual-review/20/`，完整质量门禁通过。

## [ ] 21（P1）：迁移正则替换工作台

前置：20。

实现与自动验收遵循任务 19 的样式边界，只迁移 RegexReplacementRulesPage，并覆盖 Pattern/Replacement、启用、排序、错误投影、保存/取消和播放刷新语义。截图输出到 `artifacts/visual-review/21/`，完整质量门禁通过。

## [ ] 22（P1）：迁移缓存管理与缓存数据页

前置：12、13、15。

实现：

- 迁移 CacheManagementPage 和 CacheAndDataPage，两个页面分独立原子提交。
- 保持单书选择、Ctrl/Shift/Ctrl+A、多选工具栏、清理、导出、0%、未计算和后台缓存状态语义。
- 页面分栏由 CacheManagementPage 拥有。

自动验收：

- 选择、确认、取消、导出、清理、Tooltip 和 AutomationName 回归通过。
- 生成 `artifacts/visual-review/22/`。
- 完整质量门禁通过。

## [ ] 23（P1）：迁移主窗口壳层与一级导航

前置：14–22。

实现：

- 最后迁移 MainWindow 标题栏、一级导航、内容宿主和全局运行时入口。
- Shell 只拥有标题栏、导航和内容边界，不向页面重复注入 Padding/FrameMargin。
- 保持关闭到托盘、真正退出、未保存导航守卫、正在播放和主动缓存入口语义。

自动验收：

- Window Chrome、拖动、最小化、最大化、恢复、关闭和托盘状态机测试通过。
- 页面在 `960 × 640` 和 125/150% DPI 下无重复外边距或核心内容遮挡。
- 生成 `artifacts/visual-review/23/`。
- 完整质量门禁通过。

## [ ] 24（P1）：迁移 Dialog、Flyout、Snackbar 和全局状态视图

前置：13、23。

实现：

- 迁移删除、清理、导出、未保存修改和关闭询问等现有 Dialog。
- 迁移音量、定时停止、主动缓存等 Flyout。
- 统一 Snackbar、Loading、Empty、NoResult 和 Error 投影。
- 不改变确认顺序、取消、默认按钮和脱敏错误语义。

自动验收：

- Dialog/Flyout 键盘、Escape、Focus trap、默认按钮和关闭守卫测试通过。
- Snackbar 不覆盖模态决策和关键操作。
- 生成 `artifacts/visual-review/24/`。
- 完整质量门禁通过。

## [ ] 25（P0）：DPI、可访问性、资源清理与发布门禁

前置：1–24。

实现：

- 对全部 Gallery 场景和关键页面执行 Light/Dark、100/125/150% DPI、文本缩放和减少动画测试。
- 补齐 Tooltip、AutomationName、Tab 顺序、Focus 可见性和颜色非唯一状态信号。
- 删除未使用 palette、Token、Style、组件、旧局部模板和临时桥接；不删除受保护测试资产。
- 扫描硬编码主题色、禁止隐式样式、全局模板覆盖、运行时 Style 写入和页面几何 Token。
- 执行完整质量门禁、self-contained `win-x64` publish 和发布内容检查。
- 生成最终 `artifacts/visual-review/25/manifest.json`，列出所有场景、页面、主题、DPI 和截图哈希。

自动验收：

- 全部样式架构、资源、几何、行为、可访问性和渲染测试通过。
- Style Gallery 不进入发布包。
- 主题切换不产生旧 Style、资源字典或窗口事件订阅泄漏。
- self-contained 发布内容完整且不包含测试/视觉工具资产。
