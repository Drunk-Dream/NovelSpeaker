# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段重构 NovelSpeaker 的公共视觉资源体系，并按页面逐步迁移正式 UI。

目标：

- 将 Palette、Token、标准控件 Style、自有控件模板、Feature 组件和页面布局明确分层。
- 将相同控件族的 Style 集中到同一资源字典，消除综合性资源文件和重复定义。
- 删除生产代码中硬编码 Style Gallery 示例内容的伪公共组件。
- 建立可跨页面复用的正式自有控件。
- 通过临时 Legacy 字典保持页面迁移期间可运行，并在最后删除全部旧资源。
- 每次只迁移一个页面或一个明确资源族，保持功能、导航和状态语义不变。

最终形态以 `13_VISUAL_DESIGN_SYSTEM.md` 为准。本文件只描述实施顺序、依赖和自动验收。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成并通过自动验收。
- `[!]`：存在阻塞，必须在任务结果中记录可复现原因。
- `P0`：资源架构、行为安全或最终清理门禁。
- `P1`：公共资源、公共控件和正式页面迁移。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 执行前阅读：
   - `docs/13_VISUAL_DESIGN_SYSTEM.md`
   - 当前任务对应的页面设计文档。
   - 将修改的 XAML、code-behind、ViewModel、直接调用者和测试。
3. 用户在分配任务时提出的新增视觉需求，作为该任务的补充设计约束；不得因此顺手重做其它页面。
4. 页面迁移任务采用纵向切片：
   - 先在正确的公共字典或自有控件中补齐该页面真实需要的能力。
   - 将同类 Style 放入既定控件族文件，不建立页面同义资源。
   - 更新对应稳定资源族的 Style Gallery scene 和自动契约。
   - 迁移一个正式页面。
   - 删除该页面不再使用的旧键；只有全仓零引用时才删除 Legacy 定义。
5. 不为假想需求创建公共 Style 或控件。公共抽取至少满足以下之一：
   - 两个以上正式调用点具有相同结构和状态语义。
   - 属于稳定的平台级视觉合同，例如 Button、Input、Focus、Dialog 内容表面。
6. 标准 WPF/Wpf.Ui 控件只使用显式 `App.*` Style；NovelSpeaker 自有控件允许按类型自动应用默认 Style。
7. Style Gallery fixture 只能位于 `tools/NovelSpeaker.StyleGallery`，生产控件不得硬编码示例文本、命令、进度或状态。
8. 不改变导航、播放、缓存、选择、Dirty State、持久化、确认顺序和生命周期语义。
9. 不把用户人工视觉验收写入任务关闭条件。任务必须通过自动构建、契约、几何、可访问性、截图生成和发布检查关闭。
10. 视觉截图用于用户后续查看：
    - Gallery 截图按稳定 `family-id` 保存，用于集中检查资源族、控件族和样式族。
    - 正式页面和窗口截图按稳定 `page-id`/`window-id` 保存，不得使用任务编号、日期或提交号命名。
    - 每个页面任务至少更新 Light/Dark 基线；存在密度风险的页面还更新 100/125/150% DPI 场景。
    - 页面截图必须实例化正式 View 与确定性脱敏 fixture，不得用 Gallery 中的页面仿制品替代。
11. 每个任务完成后：
    - 将状态改为 `[x]`。
    - 在任务末尾追加简短“结果”，记录实际文件边界、测试和生成的视觉目录。
    - 运行任务要求的定向测试和完整质量门禁。
12. 用户要求提交时，仍按可回溯性拆分多个原子提交，不把整个编号任务机械压成一个大提交。

## 4. 目标资源图

```text
Shared/Theming/
├─ Provider/
├─ Palettes/
└─ Resources/
   ├─ Tokens/
   ├─ Styles/
   └─ ControlThemes/

Shared/Presentation/Controls/
├─ Common/
├─ Settings/
├─ Forms/
└─ Feedback/
```

迁移期间允许存在：

```text
Shared/Theming/Resources/Legacy/LegacyStyles.xaml
```

Legacy 只保存尚未完成迁移的旧键，必须最后整体删除。不得在 Legacy 中新增新的产品语义。

## 5. 总体顺序

```text
资源审计与架构守卫
→ 临时 Legacy 与稳定资源加载图
→ Palette/Token/Typography/Surface
→ Button/Input/Selection/Navigation/Menu/Progress/Media/Feedback
→ 正式公共控件与 Gallery fixture 重构
→ 外观设置
→ 设置首页
→ 常规设置
→ 播放设置
→ 导入与文本
→ 缓存与数据
→ 诊断与关于
→ 书库
→ 书籍详情
→ Rules 共享视图
→ TTS 规则
→ 章节规则
→ 正则替换
→ 缓存管理
→ 播放页
→ 主窗口与启动窗口
→ Dialog/Flyout/Snackbar 与状态视图
→ 删除 Legacy、旧聚合字典和未使用资源
```

## 6. 稳定视觉截图注册表

视觉产物目录与 backlog 编号完全解耦。任务重排、拆分、合并或归档时，不得修改既有截图身份。

Gallery 资源族目录：

```text
artifacts/visual-review/gallery/<family-id>/
```

正式页面和窗口目录：

```text
artifacts/visual-review/pages/<page-id>/
artifacts/visual-review/windows/<window-id>/
```

截图文件使用 `<scenario-id>.<theme>.<dpi>.png`。每个目录包含自身 `manifest.json`，根目录包含汇总清单 `artifacts/visual-review/manifest.json`。

当前稳定页面 ID：

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

当前稳定窗口 ID：

- `mini-player`
- `main-window`
- `startup-status-window`

页面特有的 Dialog、Flyout、Snackbar、空状态和错误状态写入所属页面/窗口目录中的独立 scenario。共享 Style 状态只写入对应 Gallery family，不建立 `feedback-hosts`、`task-xx` 等临时目录。

---

## [x] 1（P0）：建立资源清单与架构守卫

前置：无。

实现：

- 扫描 App、Style Gallery 和 WPF 测试中的所有 ResourceDictionary、资源键定义和引用。
- 建立自动测试，持续验证：
  - 正式公共键必须使用 `App.` 前缀。
  - 一个正式键只能有一个定义位置。
  - 标准 WPF/Wpf.Ui 控件不存在 NovelSpeaker 全局隐式 Style。
  - 生产控件构造函数不得包含 Gallery fixture 文本或固定演示状态。
  - 页面专用几何不得进入 Token 字典。
  - App.xaml 的资源加载顺序可被测试读取。
- 将当前旧键、正式新键、页面局部键和 Provider 键分类为测试 fixture，不在文档中维护手工清单。
- 本任务不改变运行时视觉和页面资源引用。

自动验收：

- 新增资源图/键唯一性/隐式 Style/生产 fixture 守卫测试。
- 现有应用、Style Gallery 和全部测试项目可构建。
- 完整质量门禁通过。

结果：

- 新增 VisualResourceGraphTests，扫描 App、Style Gallery 和 WPF 测试 XAML，校验正式键前缀/唯一性、正式引用闭合、生产控件 fixture 和 App 资源层级顺序；保留现有样式所有权守卫作为标准控件与 Token 的专项检查。迁移期间的旧 Shared/Theming/Components 伪控件 fixture 单独记录为 legacy debt，由任务 11 删除，不作为最终控件基线。
- 验证通过：`dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、`dotnet build -c Release --no-restore`（0 警告/0 错误）、`dotnet test -c Release --no-build`（Domain 2、Application 208、Infrastructure 343、Presentation 382、WPF 247），以及最新定向架构测试 19/19。
- 本切片未改变运行时资源加载、视觉引用或截图工具，也未生成视觉产物；任务 2 继续负责目录骨架和 Legacy 迁移。

## [x] 2（P0）：建立最终目录骨架和临时 Legacy 层

前置：1。

实现：

- 创建 `Resources/Tokens`、`Resources/Styles`、`Resources/ControlThemes` 和临时 `Resources/Legacy`。
- 将当前综合资源中的旧键集中迁入 `Legacy/LegacyStyles.xaml`，保持键名和运行时行为不变。
- 将已经属于明确控件族的新资源移动到目标字典；移动期间不改变视觉属性。
- 固定 App.xaml 合并顺序：Provider → Palette → Tokens → Styles → ControlThemes → Legacy。
- Legacy 必须最后加载，且架构测试禁止新增页面对 Legacy 键的引用。
- 保留页面当前可运行状态，不迁移正式页面。

自动验收：

- 所有现有资源引用均可解析。
- Light/Dark 热切换前后字典实例和 Style/Template 类型资源保持稳定。
- 资源加载顺序、Legacy 单一入口和依赖方向测试通过。
- 完整质量门禁通过。

结果：

- 新增 `Resources/Tokens`、`Resources/Styles`、`Resources/ControlThemes` 和 `Resources/Legacy`；DesignTokens、Typography、输入/按钮/Slider、组件/导航资源分别归入目标层，旧综合键集中到 `Legacy/LegacyStyles.xaml`，键名与样式内容保持不变。
- App 与 Style Gallery 均固定为 Wpf.Ui → Provider → Palette → Tokens → Styles → ControlThemes → Legacy；Legacy 只通过 App 资源链最后一个入口加载，现有页面暂不迁移以保持运行时行为。
- 新增资源骨架、Legacy 单一入口、加载顺序、主题切换实例稳定性和迁移期页面引用基线回归验证；资源迁移相关定向 WPF 测试 89/89 通过。
- 完整质量门禁通过：`dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、`dotnet build src/NovelSpeaker.App/NovelSpeaker.App.csproj -c Release --no-restore`（0 警告/0 错误）、`dotnet test -c Release --no-build`（Domain 2、Application 208、Infrastructure 343、Presentation 382、WPF 258）。

## [x] 3（P1）：重构 Palette 与基础 Token

前置：2。

实现：

- 将主题 Brush 统一为 `App.Brush.*` 语义键。
- 将间距、圆角、图标尺寸、控件最小尺寸、排版标尺、动效和阴影拆分到 Tokens 子目录。
- 移除 `PagePadding`、`SettingsRowControlWidth` 等页面或组件族专用几何；仍被旧页面使用的键只在 Legacy 中保留。
- Theme runtime 只更新 Wpf.Ui theme 和 Palette，不替换 Style/ControlTheme 字典。
- 更新 Palette Gallery scene，覆盖所有语义 Brush 和基础对比度样例。

自动验收：

- Palette Light/Dark 键集合完全一致。
- 所有公共 Token 使用 `App.` 前缀且不包含页面专用名称。
- 主题热切换、Brush DynamicResource 链路、对比度和资源实例稳定测试通过。
- 更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/foundations/`。
- 完整质量门禁通过。

结果：

- Palette 统一为 28 个 `App.Brush.*` 语义键（表面/文字/边框/强调/状态），Light/Dark 键集合与 `SemanticPaletteRuntime.Keys` 完全一致；未迁移页面仍直接引用的旧 Brush 键作为迁移期兼容键保留在同一主题切换字典中（保证热切换仍更新），页面迁移完成时逐个删除。
- DesignTokens 拆分为 `Tokens/Metrics.xaml`、`Tokens/TypographyTokens.xaml`、`Tokens/Motion.xaml`、`Tokens/Elevation.xaml`，间距/圆角/图标尺寸/控件最小尺寸/排版标尺/动效/阴影/禁用透明度全部使用 `App.` 前缀；`PagePadding`、`SettingsRowControlWidth` 等页面或组件族几何键移入 `Legacy/LegacyStyles.xaml`（旧页面仍引用），零引用旧键（`ItemSpacing`、`DialogCornerRadius`、`Cover*`、`AnimFast/AnimNormal`）删除。
- 正式样式字典、Style Gallery 与 GalleryThemeRuntime 全部迁移到新键；Theme runtime 仍只更新 Wpf.Ui theme 与 Palette，不替换 Style/ControlTheme 字典。
- Palette Gallery scene 按表面/文字/边框/强调/状态分组覆盖全部语义 Brush，并新增 5 组基础对比度样例；已生成 `artifacts/visual-review/gallery/foundations/palette-probe.{light,dark}.png` 与 `manifest.json`。
- 新增/更新资源契约测试：Token 键集合与 `App.` 前缀、Legacy 页面引用指纹、Palette 键集合/对比度/热切换、Gallery scene 28 swatch + 5 contrast 契约；资源迁移相关定向测试 107/107 通过。
- 完整质量门禁通过：`dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、`dotnet build -c Release --no-restore`（0 警告/0 错误）、`dotnet test -c Release --no-build`（Domain 2、Application 208、Infrastructure 343、Presentation 382、WPF 258）。

## [x] 4（P1）：集中 Typography 与 Surface Style

前置：3。

实现：

- 建立 `Styles/Typography.xaml` 和 `Styles/Surfaces.xaml`。
- 集中定义 `App.Typography.*` 与 `App.Surface.*`。
- Surface 只处理背景、边界、圆角、Padding 和 Effect，不包含业务内容。
- 将旧排版、卡片、Popup 和分组表面键保留在 Legacy，等待页面逐步移除。
- Style Gallery 覆盖长中文/英文、禁用、浅深主题、不同表面嵌套和最大三级表面层级。

自动验收：

- Typography 和 Surface 正式键只在对应字典定义。
- 不存在综合字典中的重复定义。
- 文本省略、非零布局、主题切换和深浅表面识别测试通过。
- 分别更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/typography/` 与 `artifacts/visual-review/gallery/surfaces/`。
- 完整质量门禁通过。

结果：

- `Styles/Typography.xaml` 集中维护 8 个 `App.Typography.*` 角色，使用共享排版 Token、语义文字 Brush、自动换行和统一 Disabled 投影；新增 `Styles/Surfaces.xaml` 集中维护 7 个 `App.Surface.*` 表面，仅负责背景、边界、圆角、Padding 和 Effect。
- 应用与 Style Gallery 的加载顺序固定为 Typography → Surface → 标准控件族；资源图契约阻止重复定义、未解析引用和 Surface 混入业务内容。
- Gallery 新增稳定场景 `typography` 与 `surfaces`，覆盖长中文/英文、禁用、验证文字、浅深主题、6 个 Surface 变体和最多三级嵌套；已生成 `artifacts/visual-review/gallery/typography/` 与 `artifacts/visual-review/gallery/surfaces/` 下的 Light/Dark PNG 与 manifest。
- 新增 Typography/Surface WPF 契约与布局/主题切换测试；定向资源测试 35/35 通过。
- `dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、`dotnet build -c Release --no-restore`（0 警告/0 错误）和切片定向 WPF 测试 35/35 通过；经用户授权，在切片提交后仅刷新 `VisualResourceGraphTests` 的 Legacy 引用指纹基线，未修改页面 XAML。

## [x] 5（P1）：集中 Button Style 族

前置：3、4。

实现：

- 在 `Styles/Buttons.xaml` 集中维护 Primary、Secondary、Subtle、Icon、Danger、DangerIcon、ToolbarValue 和 Floating。
- 通过 Provider Bridge 继承 Wpf.Ui Button 基础 Style，不复制完整模板。
- Icon Button 统一最小命中区、Focus、Tooltip 和 AutomationName 合同。
- Media 变体只允许在后续 Media 字典通过 `BasedOn` 扩展，不复制 Button 基础逻辑。
- Gallery 覆盖 Default、Hover、Pressed、Keyboard Focus、Disabled、长文本和图标场景。

自动验收：

- 所有 `App.Button.*` 只定义于 Buttons.xaml。
- 鼠标点击不会留下错误的常驻键盘焦点边框，键盘导航仍可见。
- 最小命中区、内容非零和 Automation 属性测试通过。
- 更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/buttons/`。
- 完整质量门禁通过。

结果：

- `Styles/Buttons.xaml` 集中维护 8 个 `App.Button.*` 角色；全部通过 Provider Bridge 继承 Wpf.Ui Button 基础 Style，不声明完整 `ControlTemplate`。ToolbarValue 使用紧凑控件尺寸，Floating 使用 Raised 表面和低层级 Effect，DangerIcon 保持中性默认态并通过 Provider.UiButton 投影危险 Hover/Pressed 状态。
- Icon Button 统一 32 px 最小命中区、Provider Focus 行为和禁用 Tooltip 展示；Gallery fixture 为图标按钮补齐 Tooltip 与 AutomationProperties.Name，避免以视觉图标替代可访问名称。
- `Media.xaml` 继续仅通过 `BasedOn="{StaticResource App.Button.Icon}"` 扩展媒体命中区，没有复制 Button 基础逻辑；资源加载、资源图层和 Provider Bridge 契约同步切换到 `Buttons.xaml`。
- `button-styles` Gallery 覆盖 8 个变体的 Default、Hover、Pressed、Keyboard Focus、Disabled，另含图标、图标 + 文本和长中文文本；已生成 `artifacts/visual-review/gallery/buttons/` 下的 Light/Dark PNG 与 manifest。
- Button/Provider/Gallery/资源图契约定向测试 49/49 通过；最终完整质量门禁通过：restore 无 lock 文件差异，format/build 通过（0 警告/0 错误），全量测试 1203/1203 通过（WPF 268）。

## [x] 6（P1）：集中 Input Style 族

前置：3、4。

实现：

- 在 `Styles/Inputs.xaml` 集中维护 TextBox、PasswordBox、ComboBox、CheckBox 和 ToggleSwitch 的 Standard/Compact 变体。
- 保留 Provider 模板、Popup、键盘、只读、禁用和验证行为。
- 输入字段标签和错误结构由后续 `AppFormField` 提供，不在每个输入 Style 中重复。
- Gallery 覆盖空内容、长内容、只读、禁用、错误、无标签 ToggleSwitch 和 ComboBox Popup。

自动验收：

- 所有 `App.Input.*` 只定义于 Inputs.xaml。
- Light/Dark 热切换后 Style/Template 不漂移。
- Measure/Arrange、Popup 资源、键盘 Focus、ReadOnly/Disabled 差异和验证状态测试通过。
- 更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/inputs/`。
- 完整质量门禁通过。

结果：

- `Styles/Inputs.xaml` 收敛为设计文档规定的 10 个 `App.Input.*` 规范键（TextBox/PasswordBox/ComboBox/CheckBox/ToggleSwitch × Standard/Compact），全部通过 Provider Bridge 继承 Wpf.Ui 模板并保留 Popup、键盘、只读、禁用和验证状态；删除无生产引用的旧别名 `App.Input.Standard`/`App.Input.Compact`，契约测试同步收紧为 10 键。
- Input/Provider/资源图/Gallery 定向测试 59/59 通过；`NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 下 `input-controls` 截图契约校验 manifest 与 PNG 尺寸/DPI/SHA-256 一致。
- 已生成 `artifacts/visual-review/gallery/inputs/` 下的 Light/Dark PNG 与 manifest。
- 最终完整质量门禁通过：restore 无 lock 文件差异，format/build 通过（0 警告/0 错误），全量测试 1203/1203 通过（WPF 268）。

## [x] 7（P1）：集中 Selection、Navigation 与 Menu Style

前置：3–6。

实现：

- 建立 `Styles/Selection.xaml`、`Styles/Navigation.xaml` 和 `Styles/Menus.xaml`。
- Selection 集中 ListItem、CardItem、CurrentItem、DropTarget 和 MultiSelect 状态。
- Navigation 集中一级入口和设置导航入口的标准控件 Style。
- Menu 集中 Menu/ContextMenu 表面、普通项、危险项和分组标题。
- Selected、Current、Hover、Focus、Disabled 和 DropTarget 必须可组合，不依赖虚拟化容器实例保存业务事实。
- Gallery 集中展示这些状态，不创建页面专属列表样式。

自动验收：

- 三个控件族的键只定义于各自字典。
- 虚拟化回收后选择事实保持正确。
- 键盘导航、Focus、危险项分组和关闭项中性语义测试通过。
- 分别更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/selection/`、`artifacts/visual-review/gallery/navigation/` 与 `artifacts/visual-review/gallery/menus/`。
- 完整质量门禁通过。

结果：

- 新建 `Styles/Selection.xaml`（5 个 `App.Selection.*` 容器状态样式）、`Styles/Navigation.xaml`（`App.Navigation.Entry` 与 `App.Navigation.SettingsEntry`）和 `Styles/Menus.xaml`（5 个 `App.Menu.*`）；Navigation/Menu 键从 `NavigationFeedbackStyles.xaml` 迁出，`Provider.MenuItem` 移入 `ProviderStyleBridge.xaml`，App 与 Style Gallery 资源链同步增加三个字典。
- Selection 样式只表达容器状态：Selected/Current/MultiSelect/DropTarget 通过数据事实绑定（`IsSelected`/`IsCurrent`/`IsSelectedForActiveCache`/`IsDropTarget`），Hover 状态层与状态边框可组合，不替换标准列表容器模板，也不在回收容器上保存业务事实；契约测试用虚拟化回收 ListBox 验证滚动回收后状态仍跟随数据。
- Gallery 新增稳定场景 `selection`、`navigation`、`menus`，`navigation-feedback` 裁剪为 Progress/Feedback；已生成对应 `artifacts/visual-review/gallery/` 下的 Light/Dark PNG 与 manifest。新增字典所有权、Provider 链、键盘 Focus、危险项分组与中性 Close 等契约测试；ProviderStyleBridge 契约为 10 键、资源链 20 个字典，StyleGalleryScene 注册表为 15 个场景。
- 定向 WPF/架构测试 67/67 通过；`NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 下新场景与 navigation-feedback 截图契约通过。
- 最终完整质量门禁通过：restore 无 lock 文件差异，format/build 通过（0 警告/0 错误），全量测试 1213/1213 通过（WPF 278）。

## [x] 8（P1）：集中 Progress、Media 与基础 Feedback Style

前置：3–7。

实现：

- 建立 `Styles/Progress.xaml`、`Styles/Media.xaml` 和 `Styles/Feedback.xaml`。
- ProgressBar 与 Slider 保持独立模板和行为合同。
- Media Button 基于 `App.Button.Icon`，Media Surface 基于 `App.Surface.*`；播放页与迷你播放器的媒体按钮统一为 `48 × 48` 命中区和中性状态，不建立 Accent 主媒体按钮变体。
- Feedback 只定义 Popup/InlineMessage/Validation/Snackbar 内容样式，不建立宿主控件。
- 将播放页和迷你播放器迁移到新 Media、Progress、Surface 和 Typography 键，删除其对旧公共样式键的引用；不改变布局、窗口动作和媒体命令。
- Gallery 覆盖媒体按钮、音量、段落 Slider、进度、验证和轻量反馈内容。

自动验收：

- `App.Progress.*`、`App.Media.*`、`App.Feedback.*` 分别只定义于对应字典。
- 迷你播放器 PlaybackSnapshot、上下章/段、播放暂停、拖动、音量、置顶、恢复和退出测试通过。
- ProgressBar 与 Slider 类型边界、Tooltip 和 100/125/150% DPI 几何测试通过。
- 分别更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/progress/`、`artifacts/visual-review/gallery/media/` 与 `artifacts/visual-review/gallery/feedback/`。
- 使用正式迷你播放器窗口更新 `artifacts/visual-review/windows/mini-player/`，不得以 Gallery 组合样例替代窗口截图。
- 完整质量门禁通过。

结果：

- 新建 `Styles/Progress.xaml` 与 `Styles/Feedback.xaml`，将可编辑位置 Slider 和媒体控制表面集中到 `Styles/Media.xaml`；ProgressBar 与 Slider 保持独立类型边界，媒体按钮继续基于 `App.Button.Icon`，统一使用 `App.Size.MediaButton` 的 48 × 48 命中区和中性状态。
- Player 与 MiniPlayer 已迁移到新的 Media、Progress、Feedback、Surface、Typography 和语义色键；删除 `SliderStyles.xaml`、`NavigationFeedbackStyles.xaml` 及其旧公共样式引用，未改变播放命令、窗口动作或播放状态所有者。
- Feedback 只提供 PopupSurface、InlineMessage、ValidationText、SnackbarBody 内容样式，不包含宿主控件、模板或生命周期；Gallery 新增 `progress`、`feedback` 场景并刷新 `progress/`、`media/`、`feedback/` 资源族。正式 MiniPlayer 窗口产物覆盖 Light/Dark、无上下文/长上下文和 100/125/150% DPI，共 12 张 PNG 与 manifest。
- 定向资源、Player、MiniPlayer、Gallery 与架构测试 89/89 通过；MiniPlayer 正式窗口视觉测试 1/1，通过全套 MiniPlayer 测试 16/16。
- 完整质量门禁通过：locked restore 无 lock 文件差异，format/build 通过（0 警告、0 错误），全量测试 1215/1215 通过（WPF 280）。

## [x] 9（P1）：实现 Common 与 Feedback 正式自有控件

前置：4、5、8。

实现：

- 在 `Shared/Presentation/Controls/Common` 实现 `AppPageHeader` 和 `AppSectionSurface`。
- 在 `Shared/Presentation/Controls/Feedback` 实现单一 `AppStatusView`。
- 默认模板分别集中在 `ControlThemes/Common.xaml` 和 `ControlThemes/Feedback.xaml`。
- 自有控件按类型自动应用默认 Style；只为真实变体建立具名 Style。
- 控件只提供内容槽和视觉状态，不硬编码页面文案、命令或 fixture 数据。
- Gallery 使用独立 fixture 展示返回/无返回、长标题、操作区、Loading/Empty/NoResult/Error 和操作按钮组合。

自动验收：

- 默认 Style 可按类型解析，模板部件和内容槽可用。
- 控件类不包含固定演示文案、固定命令和 Gallery AutomationId。
- 长文本、省略、键盘 Focus、AutomationName 和非零布局测试通过。
- 分别更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/page-header/`、`artifacts/visual-review/gallery/section-surface/` 与 `artifacts/visual-review/gallery/status-view/`。
- 完整质量门禁通过。

结果：

- 新增 `AppPageHeader`、`AppSectionSurface` 和 `AppStatusView` 正式自有控件，分别集中于 `ControlThemes/Common.xaml` 与 `ControlThemes/Feedback.xaml` 的隐式默认模板；控件只拥有内容槽和视觉状态，不包含页面文案、命令或 Gallery fixture。
- Gallery 新增稳定 `page-header`、`section-surface`、`status-view` 场景，覆盖返回/无返回、长标题与操作区、内容与 Footer 槽，以及 Loading/Empty/NoResult/Error 和操作按钮；已生成对应 family 目录的 Light/Dark PNG 与 manifest。
- 新增 Common/Feedback WPF 内容槽、模板、状态、Automation、Focus、DPI 布局和 Gallery fixture 契约测试；资源顺序、资源图、Style Gallery 相关定向测试 56/56 通过，format verify 通过。

## [x] 10（P1）：实现 Settings 与 Forms 正式自有控件

前置：4–6、9。

实现：

- 在 `Shared/Presentation/Controls/Settings` 实现 `AppSettingsGroup`、`AppSettingsRow` 和 `AppSettingsNavigationRow`。
- 在 `Shared/Presentation/Controls/Forms` 实现 `AppFormField`。
- 模板分别集中在 `ControlThemes/Settings.xaml` 和 `ControlThemes/Forms.xaml`。
- SettingsGroup 拥有行分隔线和首尾圆角，页面不再需要 LastRow Style。
- SettingsRow 不定义全局右侧固定宽度，窄宽度下允许自适应纵向布局。
- FormField 提供 Label、Description、Content、Error 和 Required 状态，不保存业务值或实现验证逻辑。
- Gallery 覆盖 ToggleSwitch、ComboBox、TextBox、Button、只读值、长说明、错误和窄宽度。

自动验收：

- 自有控件默认 Style、内容槽、DataContext 传递和命令绑定测试通过。
- 最小宽度和 150% DPI 下标题、说明、右侧控件和错误文案不重叠。
- SettingsGroup 无需最后一行特例即可正确绘制分隔线。
- 分别更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/settings-controls/` 与 `artifacts/visual-review/gallery/form-field/`。
- 完整质量门禁通过。

结果：

- 新增 `AppSettingsGroup`、`AppSettingsRow`、`AppSettingsNavigationRow` 和 `AppFormField`，分别集中于 `ControlThemes/Settings.xaml` 与 `ControlThemes/Forms.xaml`；分组模板统一拥有行分隔线、表面和首尾圆角，设置行不设置全局右侧宽度并在窄宽度切换为纵向布局，表单字段只投影标签、说明、必填标记、内容槽和错误文案。
- Gallery 新增稳定 `settings-controls` 与 `form-field` 场景，覆盖 ToggleSwitch、ComboBox、TextBox、Button、只读值、导航行、长说明、错误和窄宽度；Light/Dark PNG 与 manifest 仅生成在本地 `artifacts/`，未加入 Git。
- Common、Settings、Feedback 相关正式控件在辅助文案为空时保持主文案/图标/操作区的垂直居中，并由契约测试固定该行为；新增 Settings/Form WPF 默认 Style、内容槽、DataContext、命令绑定、分隔线、错误投影、窄宽度、560/561 边界、100/125/150% 等效缩放和 Gallery 场景契约测试；切片定向测试 50/50 通过，构建 0 警告、0 错误。

## [ ] 11（P0）：重构 Style Gallery fixture 并删除伪公共组件

前置：3–10。

实现：

- 将全部示例文本、示例按钮、示例进度和状态组合移入 Style Gallery fixture/scene builder。
- Gallery 直接实例化正式 Style 和正式自有控件。
- 删除 `Shared/Theming/Components/AppComponentBase.cs`、`FeedbackSurfaceBase.cs` 及其硬编码 BookCard、SettingsRow、RuleListItem、DialogShell、FlyoutSurface、SnackbarContent 等伪公共组件。
- 删除不再需要的 `ComponentStyles.xaml` 和 `NavigationFeedbackStyles.xaml` 正式内容；仍未迁移的旧键只能存在于 Legacy。
- 建立稳定 Gallery 场景注册表，确保相同资源族、控件族或样式族只在一个 scene 集中维护，scene ID 不包含任务编号。

自动验收：

- 生产程序集不存在 Gallery fixture 内容和伪公共组件类型。
- Gallery 全场景 Light/Dark 可重复渲染，manifest 哈希稳定。
- Style Gallery 不进入 self-contained 发布包。
- 更新 `artifacts/visual-review/gallery/manifest.json`，并确认全部既有 family 目录和 scene ID 保持稳定。
- 完整质量门禁通过。

## [ ] 12（P1）：重新迁移外观设置页

前置：10、11。

实现：

- 使用 `AppPageHeader`、`AppSettingsGroup`、`AppSettingsRow`、Typography 和 ComboBox Style 重整 `AppearanceSettingsPage`。
- 页面拥有自身 Padding 和滚动，不使用全局 PagePadding 或 SettingsRowControlWidth。
- 根据用户分配任务时的最新视觉要求调整该页密度和层级。
- 保持跟随系统/浅色/深色、即时生效、持久化和损坏恢复语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 页面只引用正式 `App.*` 资源和正式自有控件。
- 主题命令、持久化、导航和热切换回归通过。
- Light/Dark、100/125/150% DPI 截图和几何测试通过。
- 使用正式 `AppearanceSettingsPage` 更新 `artifacts/visual-review/pages/appearance-settings/`。
- 完整质量门禁通过。

## [ ] 13（P1）：迁移设置首页

前置：12。

实现：

- 使用 `AppPageHeader`、`AppSettingsGroup` 和 `AppSettingsNavigationRow` 迁移 `SettingsPage`。
- 按常用、文本处理、应用分组，整行导航使用图标、标题和 Chevron。
- 页面拥有分组排列和 Padding，不建立万能设置页面壳。
- 保持现有导航目标、返回栈和 guard 语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 所有导航项命令、AutomationName、Tab 顺序和键盘激活测试通过。
- 最小窗口与 150% DPI 下分组和导航项不重叠。
- 使用正式 `SettingsPage` 更新 `artifacts/visual-review/pages/settings-home/`。
- 完整质量门禁通过。

## [ ] 14（P1）：迁移常规设置页

前置：13。

实现：

- 使用正式 PageHeader、SettingsGroup、SettingsRow 和 Input Style 迁移 `GeneralSettingsPage`。
- 保持关闭主窗口行为三选项和启动后最小化到托盘设置。
- 设置仍即时持久化，不把托盘平台逻辑写入页面。
- 删除该页全部 Legacy 键引用。

自动验收：

- 设置绑定、即时保存、关闭/托盘偏好和导航回归通过。
- 窄宽度与 150% DPI 下右侧控件可用。
- 使用正式 `GeneralSettingsPage` 更新 `artifacts/visual-review/pages/general-settings/`。
- 完整质量门禁通过。

## [ ] 15（P1）：迁移播放设置页

前置：14。

实现：

- 使用正式 PageHeader、SettingsGroup、SettingsRow、Input 和 Feedback 资源迁移 `PlaybackSettingsPage`。
- 覆盖默认语速、预取数量和朗读章节标题。
- 错误信息使用 FormField 或 Feedback Style，不复制局部错误文本样式。
- 保持即时保存、朗读清单按需重算和主动缓存批次快照语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 输入校验、设置保存、配置通知和缓存批次隔离回归通过。
- Light/Dark、长说明、错误和 150% DPI 场景通过。
- 使用正式 `PlaybackSettingsPage` 更新 `artifacts/visual-review/pages/playback-settings/`。
- 完整质量门禁通过。

## [ ] 16（P1）：迁移导入与文本设置页

前置：15。

实现：

- 使用正式 PageHeader、SettingsGroup、SettingsRow、SettingsNavigationRow、Input 和 Feedback 资源迁移 `ImportTextSettingsPage`。
- 覆盖长段落切分、阈值、文件名提取设置和正则替换三级入口。
- 保持即时保存、校验和导航语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 设置校验、保存、正则替换导航和错误投影测试通过。
- 窄宽度与 150% DPI 下字段和导航入口可用。
- 使用正式 `ImportTextSettingsPage` 更新 `artifacts/visual-review/pages/import-text-settings/`。
- 完整质量门禁通过。

## [ ] 17（P1）：迁移缓存与数据页

前置：16。

实现：

- 使用正式 PageHeader、SettingsGroup、SettingsRow、SettingsNavigationRow、Button 和 Feedback 资源迁移 `CacheAndDataPage`。
- 显示缓存占用、容量上限、使用率、LRU 说明、应用数据目录、清理全部缓存和缓存管理入口。
- 危险操作保持独立区域和确认流程。
- 保持容量调低后的确认、LRU 清理、保护 registry 和朗读清单同步回收语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 容量校验、确认/取消、清理、目录打开和导航测试通过。
- 危险按钮、错误状态和 150% DPI 几何测试通过。
- 使用正式 `CacheAndDataPage` 更新 `artifacts/visual-review/pages/cache-data/`。
- 完整质量门禁通过。

## [ ] 18（P1）：迁移诊断与关于页

前置：17。

实现：

- 使用正式 PageHeader、SettingsGroup、SettingsRow、Button、Typography 和 Feedback 资源迁移 `DiagnosticsAboutPage`。
- 覆盖版本、目录入口、数据库 schema、安全诊断摘要、许可证和复制脱敏诊断信息。
- 只读值使用 SettingsRow 内容槽，不建立专用 Value TextBlock 旧样式。
- 保持日志与诊断脱敏边界。
- 删除该页全部 Legacy 键引用。

自动验收：

- 目录打开、许可证、复制和脱敏测试通过。
- 长版本号、长路径、窄宽度和 150% DPI 场景通过。
- 使用正式 `DiagnosticsAboutPage` 更新 `artifacts/visual-review/pages/diagnostics-about/`。
- 完整质量门禁通过。

## [ ] 19（P1）：迁移书库与 Feature BookCard

前置：18。

实现：

- 迁移 `LibraryPage` 和 Feature 所有的 `BookCardView`。
- 使用正式 PageHeader、Typography、Button、Surface、Progress、Menu 和 AppStatusView。
- BookCard 继续属于 Library Feature，不升级为全局自有控件。
- 自适应网格列数、卡片尺寸和页面 Padding 由 LibraryPage 拥有。
- 保持搜索、排序、拖放导入、打开、详情、删除和滚动位置语义。
- 删除两个视图全部 Legacy 键引用。

自动验收：

- 空书库、搜索无结果、长书名、不同书籍数量和多窗口宽度测试通过。
- BookCard 命令、Tooltip、AutomationName 和进度显示测试通过。
- 使用正式 `LibraryPage` 更新 `artifacts/visual-review/pages/library/`。
- 完整质量门禁通过。

## [ ] 20（P1）：迁移书籍详情与目录

前置：19。

实现：

- 使用正式 PageHeader、SectionSurface、Input、Selection、Progress、Button 和 AppStatusView 迁移 `BookDetailsPage`。
- 页面继续拥有摘要/编辑/目录布局、虚拟化和滚动宿主。
- 当前章节使用统一 CurrentItem 状态。
- 目录页只显示正常且大于 0% 的缓存完整度；0% 和异常状态不显示。
- 保持编辑保存/取消、Dirty State、章节跳转和定位到当前章节语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 虚拟化、当前章节、定位、长标题、0% 隐藏、编辑保存/取消和导航守卫测试通过。
- 使用正式 `BookDetailsPage` 更新 `artifacts/visual-review/pages/book-details/`。
- 完整质量门禁通过。

## [ ] 21（P1）：建立 Rules 页面族共享视图

前置：20。

实现：

- 在相近 Rules Feature 边界中建立共享规则列表项和必要的帮助/命令区视图。
- 共享视图使用正式 Selection、Menu、Button、Typography 和 Surface 资源。
- 不建立完整 `AppRuleWorkbench`，不固定三个页面的列宽和字段集合。
- 规则启用、当前、排序、Drag/Drop、更多菜单和 Dirty 提示分别保留明确状态。
- 为 TTS、章节和正则 fixture 建立 Gallery 场景，但不迁移正式页面。

自动验收：

- 共享视图不依赖具体规则 ViewModel 类型之外的 WPF 视觉返回值。
- 虚拟化、选择、启用、当前、拖拽、键盘备用排序和菜单状态测试通过。
- 更新 Gallery 稳定资源族 `artifacts/visual-review/gallery/rules-shared/`。
- 完整质量门禁通过。

## [ ] 22（P1）：迁移 TTS 规则工作台

前置：21。

实现：

- 使用正式 PageHeader、SectionSurface、AppFormField、Input、Button、Menu、Feedback 和 Rules 共享列表项迁移 `TtsRulesPage`。
- 页面拥有真实双栏比例、字段布局和滚动。
- 保持试听、当前规则、启用、排序、导入、导出、删除、Dirty State 和导航守卫语义。
- 未修改时取消/保存禁用；试听针对当前编辑副本。
- 删除该页全部 Legacy 键引用。

自动验收：

- 最小工作区下关键字段有非零宽度且可滚动。
- 试听、保存/取消、切换保护、导入导出、删除和当前规则测试通过。
- 使用正式 `TtsRulesPage` 更新 `artifacts/visual-review/pages/tts-rules/`。
- 完整质量门禁通过。

## [ ] 23（P1）：迁移章节规则工作台

前置：22。

实现：

- 使用与任务 22 相同的公共边界迁移 `ChapterRulesPage`。
- 页面保留自身字段、帮助、默认规则导入/恢复和布局。
- 内置规则可删除性由能力字段决定，不新增标签。
- 排序以拖拽为主，菜单上移/下移作为键盘和备用入口。
- 删除该页全部 Legacy 键引用。

自动验收：

- 默认规则、启用、排序、Drag/Drop、帮助、保存/取消和导航守卫测试通过。
- 长正则、错误和最小工作区几何测试通过。
- 使用正式 `ChapterRulesPage` 更新 `artifacts/visual-review/pages/chapter-rules/`。
- 完整质量门禁通过。

## [ ] 24（P1）：迁移正则替换工作台

前置：23。

实现：

- 使用与任务 22 相同的公共边界迁移 `RegexReplacementRulesPage`。
- 页面保留名称、Pattern、Replacement、作用目标、帮助和自身布局。
- 错误统一通过 AppFormField/Feedback 投影。
- 保持启用、排序、删除、保存/取消、Dirty State 和播放刷新语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- Pattern/Replacement 校验、错误投影、排序、保存/取消、导航守卫和播放刷新测试通过。
- 长表达式、错误和最小工作区几何测试通过。
- 使用正式 `RegexReplacementRulesPage` 更新 `artifacts/visual-review/pages/regex-replacement-rules/`。
- 完整质量门禁通过。

## [ ] 25（P1）：迁移缓存管理页

前置：17、20、24。

实现：

- 使用正式 PageHeader、SectionSurface、Selection、Progress、Menu、Button、Feedback 和 AppStatusView 迁移 `CacheManagementPage`。
- 缓存书籍项和章节项留在 Cache Feature。
- 页面拥有左书右章分栏、虚拟化和多选工具栏。
- 保持单书选择、Ctrl/Shift/Ctrl+A、多选、清理、导出、取消和打开目录语义。
- 显示全部有缓存章节，包括 0%；计划更新中、计划计算中、规则不可用和无可播放内容使用明确状态。
- 删除该页全部 Legacy 键引用。

自动验收：

- 文件管理器式选择、虚拟化回收、确认/取消、导出、清理、状态刷新和页面离开取消测试通过。
- 0%、未计算、长章节名、空列表和 150% DPI 场景通过。
- 使用正式 `CacheManagementPage` 更新 `artifacts/visual-review/pages/cache-management/`。
- 完整质量门禁通过。

## [ ] 26（P1）：迁移播放页与 PlayerView

前置：8、20、25。

实现：

- 使用正式 PageHeader、SectionSurface、Typography、Surface、Button、Media、Progress、Selection、Feedback 和 AppStatusView 迁移 `PlayerPage` 与 Feature 所有的 `PlayerView`。
- 正文、章节侧栏、媒体控制条、Flyout 内容和页面 Padding 由 Playback Feature 拥有。
- 播放页和迷你播放器的媒体按钮使用统一尺寸和中性状态；播放/暂停不建立 Accent 主媒体操作层级。
- 保持 PlaybackSnapshot、播放状态机、上下章/段、拖动、音量、定时停止、主动缓存、滚动追随和快捷键语义。
- 当前段使用统一轻量状态，不使用高饱和背景。
- 删除两个视图全部 Legacy 键引用。

自动验收：

- 播放/暂停、上下段/章、拖动、当前段居中、用户滚动暂停追随、音量、定时停止、主动缓存和页面离开测试通过。
- 长正文、空章节、错误、最小窗口和 100/125/150% DPI 场景通过。
- 使用正式 `PlayerPage` 更新 `artifacts/visual-review/pages/player/`。
- 完整质量门禁通过。

## [ ] 27（P1）：迁移主窗口与启动窗口

前置：12–26。

实现：

- 迁移 `MainWindow` 的 Window Chrome、一级导航、内容宿主和全局运行时入口。
- 迁移 `StartupStatusWindow` 到正式 Typography、Surface、Progress、Feedback 和 AppStatusView。
- Shell 只拥有标题栏、导航和内容边界，不向页面重复注入 Padding/FrameMargin。
- 保持最小化、最大化、恢复、关闭到托盘、真正退出、未保存导航守卫、播放和主动缓存入口语义。
- 删除两个窗口全部 Legacy 键引用。

自动验收：

- Window Chrome、拖动、最小化、最大化、恢复、关闭和托盘状态机测试通过。
- Startup loading/error 状态和脱敏错误投影测试通过。
- `960 × 640`、Light/Dark 和 125/150% DPI 下无重复外边距或核心内容遮挡。
- 分别使用正式窗口更新 `artifacts/visual-review/windows/main-window/` 与 `artifacts/visual-review/windows/startup-status-window/`。
- 完整质量门禁通过。

## [ ] 28（P1）：统一 Dialog、Flyout、Snackbar 和状态视图

前置：9、12–27。

实现：

- 将现有删除、清理、导出、未保存修改和关闭询问等 Dialog 统一为 Wpf.Ui host + NovelSpeaker 内容资源。
- 将音量、定时停止、主动缓存等 Flyout 统一为 Wpf.Ui host + 正式 Surface/Typography/Button/Feedback。
- 统一 Snackbar 内容排版，并在适合的页面采用 `AppStatusView` 表达 Loading、Empty、NoResult 和 Error。
- 不创建 DialogShell、FlyoutSurface 或 SnackbarContent 生产控件。
- 保持确认顺序、默认按钮、取消、Escape、焦点恢复和脱敏错误语义。

自动验收：

- Dialog/Flyout 键盘、Escape、Focus trap、默认按钮、取消和关闭守卫测试通过。
- Snackbar 不覆盖模态决策和关键操作。
- 状态视图不硬编码业务文案或命令。
- 更新 `artifacts/visual-review/gallery/feedback/` 的共享样式场景。
- 将每个 Dialog、Flyout、Snackbar 和状态场景写入其所属 `pages/<page-id>/` 或 `windows/<window-id>/`，不得建立跨页面的 `feedback-hosts` 截图目录。
- 完整质量门禁通过。

## [ ] 29（P0）：删除 Legacy 与旧资源并完成发布门禁

前置：1–28。

实现：

- 确认正式页面、窗口、Style Gallery 和测试不再引用 Legacy 键。
- 删除 `Resources/Legacy`、旧 `SemanticStyles.xaml`、旧聚合字典、旧 alias、零引用 Token/Style/ControlTheme 和临时迁移测试。
- 删除所有 `PagePadding`、`SettingsRowControlWidth` 等全局页面几何键；页面值保留在唯一布局 owner。
- 扫描硬编码主题色、禁止隐式 Style、全局模板覆盖、运行时 Style 写入、重复资源键和生产 fixture。
- 对全部 Gallery 场景和关键页面执行 Light/Dark、100/125/150% DPI、文本缩放和减少动画测试。
- 补齐 Tooltip、AutomationName、Tab 顺序、Focus 可见性和颜色非唯一状态信号。
- 执行 self-contained `win-x64` publish 和发布内容检查。
- 更新数字编号文档中因实现细节确认而需要精化的终态描述；不把迁移历史写回数字编号文档。

自动验收：

- 全仓 Legacy/旧键/旧聚合字典零匹配。
- ResourceDictionary 图、键唯一性、依赖方向和加载顺序测试通过。
- 全部样式架构、行为、可访问性、DPI 和渲染测试通过。
- Style Gallery、测试 fixture 和 `artifacts/visual-review` 不进入发布包。
- 主题切换不产生旧 Style、字典或窗口事件订阅泄漏。
- 更新根清单 `artifacts/visual-review/manifest.json`，列出所有 Gallery family、页面、窗口、scenario、主题、DPI 和截图哈希。
- 完整质量门禁与 self-contained 发布检查通过。
