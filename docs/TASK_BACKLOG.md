# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段重构 NovelSpeaker 的公共视觉资源体系，并按页面逐步迁移正式 UI。

目标：

- 将 Palette、Token、标准控件 Style、自有控件模板、Feature 组件和页面布局明确分层。
- 将相同控件族的 Style 集中到同一资源字典，消除综合性资源文件和重复定义。
- 删除生产代码中硬编码 Style Gallery 示例内容的伪公共组件。
- 建立可跨页面复用的正式自有控件。
- 通过临时 Legacy 字典保持页面迁移期间可运行，并在最后删除全部旧资源。
- 每次只迁移一个页面或一个明确资源族；除任务明确列出的已确认产品行为变化外，保持功能、导航和状态语义不变。

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
8. 不擅自改变导航、播放、缓存、选择、Dirty State、持久化、确认顺序和生命周期语义；任务明确列出的已确认产品行为变化按对应设计文档实施，并以自动测试固定新语义。
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
→ 设置页 Canvas 层级与公共 Settings 组件视觉优化
→ 设置首页
→ Shell 圆角与 ComboBox Popup 视觉修正确认
→ 常规设置
→ 已迁移设置子页扁平化修正
→ 播放设置
→ 导入与文本
→ 缓存与数据
→ 诊断与关于
→ 书库
→ 书籍详情
→ Rules 管理交互与 TTS 选择语义
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

## [x] 11（P0）：重构 Style Gallery fixture 并删除伪公共组件

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

结果：删除 `AppComponentBase`、`FeedbackSurfaceBase`、`ComponentStyles.xaml` 及其生产/Gallery 引用；外观页改用 `AppSettingsRow`，Gallery 的 `list-components` 与 `feedback` 场景只组合正式控件和资源，示例内容全部留在 fixture。资源合并字典收敛为 23 个，生产控制源和伪组件目录无 fixture 内容，既有 scene ID 与 Gallery family 输出目录保持不变；Light/Dark 全场景 manifest 已在本地 `artifacts/visual-review/gallery/manifest.json` 重生成并完成重复渲染校验。最终完整门禁通过：Domain 2、Application 208、Presentation 382、Infrastructure 343、WPF 294；`artifacts/` 未加入 Git。

## [x] 12（P1）：重新迁移外观设置页

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

结果：`AppearanceSettingsPage` 改用 `AppPageHeader`、`AppSettingsGroup`、`AppSettingsRow`、Typography 与 `App.Input.ComboBox.Standard`；页面自带 24 Padding 与滚动，删除 `PagePadding`、`SectionSpacing`、`AppBackgroundBrush`、`CanvasSurfaceBrush` 等 Legacy/兼容键引用。保留主题 ComboBox 双向绑定、返回命令与即时生效/持久化语义；新增正式控件契约和窄/宽几何非重叠测试，Legacy 页面引用固定指纹同步更新。Light/Dark 100/125/150% 截图与 manifest 已在本地 `artifacts/visual-review/pages/appearance-settings/` 重生成并完成重复渲染校验；`artifacts/` 未加入 Git。

## [x] 13（P1）：优化设置页 Canvas 层级与公共 Settings 组件视觉

前置：12。

实现：

- 修正正式页面的背景所有权：`App.Brush.Window.Background` 只属于 Window/Shell，已迁移的正式 Page 根区域完整使用 `App.Brush.Canvas`；页面 Padding 只是 Canvas 上的留白，不再使用“Page=Window Background + 带 Margin 子容器=Canvas”的双层背景结构。
- 修正 `AppearanceSettingsPage` 当前外围色环：Page 根背景直接使用 Canvas，移除内部 Grid 对 Canvas 的重复绘制；保留现有 24 px 页面 Padding、滚动结构和横向宽度策略，不新增统一 `MaxWidth`。
- 优化 `AppSettingsGroup` 默认模板：继续使用 Primary Surface 和圆角分组，但默认不绘制完整外框；只有未来出现真实调用点时才允许增加显式具名描边变体，本任务不预建无调用点变体。
- 在 `Styles/Typography.xaml` 增加并集中维护 `App.Typography.GroupTitle`，用于设置分组 Header；其视觉权重低于 `AppSettingsRow` 标题，且不复用页面级 `SectionTitle`。
- 优化 Settings 密度所有权：Group 保留主要内容 Padding；ItemContainer 不再增加额外上下 Padding；`AppSettingsRow` 成为行级纵向 Padding 的唯一 owner，并移除与 Group 重复的横向缩进，使 Group Header 与 Row Title 左侧基线对齐。
- 保持 SettingsGroup 的行分隔线所有权、首尾边界判定、SettingsRow 的窄宽度纵向布局、DataContext、命令、Focus 和 Automation 合同不变。
- 将相近视觉能力继续集中在既有资源族中，不为本次修正建立页面专属 Style 文件：Typography 变化写入 `Typography.xaml`，Settings 控件变化写入 `ControlThemes/Settings.xaml`。
- 更新 Gallery 的稳定 `typography` 与 `settings-controls` family，展示 GroupTitle、无外框 SettingsGroup、单行/多行组、不同右侧控件、长说明和窄宽度场景。

自动验收：

- 静态资源/页面契约确认 `AppearanceSettingsPage` 不再引用 `App.Brush.Window.Background`，Page 根背景解析为 `App.Brush.Canvas`，且不存在仅为露出外围色环而设置的内部 Canvas 背景层。
- `AppSettingsGroup` 默认模板外框为 0，Primary Surface 与圆角仍存在；分隔线只出现在相邻设置行之间，最后一行无多余分隔线。
- `App.Typography.GroupTitle` 只定义于 `Styles/Typography.xaml`，视觉权重低于 Row Title，并进入现有 Typography Gallery family。
- Settings 几何测试覆盖单行/多行、长说明、ToggleSwitch、ComboBox、TextBox、窄宽度以及 100/125/150% DPI，验证不存在 Group ItemContainer + Row 的双重纵向 Padding，Header 与 Row Title 左侧基线一致，右侧控件不重叠。
- 外观页主题选择、即时生效、持久化、返回导航和 Light/Dark 热切换行为回归通过。
- 更新 `artifacts/visual-review/gallery/typography/`、`artifacts/visual-review/gallery/settings-controls/`，并使用正式 `AppearanceSettingsPage` 重生成 `artifacts/visual-review/pages/appearance-settings/` 的 Light/Dark、100/125/150% DPI 截图与 manifest。
- 完整质量门禁通过。

结果：`AppearanceSettingsPage` 根背景改为 `App.Brush.Canvas` 并移除内部 Grid 的 Canvas 重复绘制，保留 24 px 留白、滚动结构与横向宽度策略；页面契约测试固定“不再引用 `App.Brush.Window.Background`、根背景解析为 Canvas、无内部背景层”。`AppSettingsGroup` 默认模板外框为 0，保留 Primary Surface 与圆角；分隔线仅出现在相邻设置行之间且末行清空；ItemContainer 不再叠加纵向 Padding，`AppSettingsRow` 成为行级纵向密度唯一 owner 并移除横向缩进，Group Header 与 Row Title 左侧基线对齐。`Styles/Typography.xaml` 集中新增 `App.Typography.GroupTitle`（14 SemiBold，低于 Row Title），SettingsGroup Header 改用该样式。Gallery 的 `typography` 与 `settings-controls` family 新增 GroupTitle、无外框组、单行/多行组、ToggleSwitch/ComboBox/TextBox、长说明与窄宽度场景；新增页面背景契约与 Settings 几何测试（含 100/125/150% DPI）。`artifacts/visual-review/gallery/typography/`、`settings-controls/` 与 `artifacts/visual-review/pages/appearance-settings/` 已重生成并完成 manifest-PNG SHA 校验；`artifacts/` 未加入 Git。完整质量门禁通过：Domain 2、Application 208、Presentation 382、Infrastructure 343、WPF 298。

## [x] 14（P1）：迁移设置首页

前置：13。

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

结果：`SettingsPage` 改用 `AppPageHeader`、`AppSettingsGroup` 和 `AppSettingsNavigationRow`，根区域使用 Canvas 背景与 24 px 页面留白；导航项继续按常用、文本处理、应用分组，保留原有命令、顺序和导航语义。正式 Settings 导航控件主题集中投影枚举图标并保留图标、标题、Chevron、AutomationName、TabStop 和键盘焦点合同；页面不再引用 PagePadding、SectionSpacing、SettingsGroupBorderStyle 或 SettingsNavigationRowButtonStyle 等 Legacy 键。新增设置首页正式控件、无 Legacy 引用、窄窗口/100% 与 150% DPI 几何回归、八项 Space 键激活回归，更新 Legacy 页面引用指纹；相关 WPF 43 项与 Presentation 22 项测试通过。已使用正式 `SettingsPage` 生成 `artifacts/visual-review/pages/settings-home/` 的 Light/Dark、100/125/150% 截图并完成 manifest-PNG SHA 校验；`artifacts/` 未加入 Git。

## [x] 14A（P0）：验证并提交 Shell 圆角与 ComboBox Popup 视觉修正

前置：14。

背景：

- 本任务开始前源码与设计文档已经直接应用一轮视觉修正，不再重新设计方案。
- Shell 内容背景所有权已调整为：`NavigationView` 内容宿主拥有 Canvas、边界和左上圆角；已迁移正式 Page 根节点透明，避免不透明 Page 背景遮住 Shell 圆角。
- ComboBox family 已在 `Inputs.xaml` 中接管经批准的局部模板：闭合态保持左文案、右 Chevron 和全表面命中；Popup 使用 Raised Surface、Subtle Border、Medium Radius、Medium Elevation、约 4 px 间隔；Item 使用统一 Hover/Selected/Disabled 状态。
- `Palette.Light.xaml` / `Palette.Dark.xaml` 已投影 Provider 所需 `NavigationViewContentBackground`、`NavigationViewContentGridBorderBrush`，并由 `SemanticPaletteRuntime` 纳入稳定主题键；两者复用现有 `App.Brush.Canvas` 与 `App.Brush.Border.Subtle`，不新增业务语义颜色。

实现：

- 审核上述直接修改与 `docs/13_VISUAL_DESIGN_SYSTEM.md`、`docs/06_UI_AND_USER_FLOWS.md`、`docs/09_TESTING_AND_QUALITY.md` 的最终合同一致；只修复测试、Gallery fixture、截图工具或实现缺陷，不扩大本任务视觉范围。
- 更新现有资源契约测试，使 Provider Bridge 接受 `Provider.ComboBoxItem`，Palette 稳定键接受 NavigationView 内容宿主投影；删除或改写仍要求“正式 Page 根背景必须为 Canvas”的旧断言。
- 为 Shell 内容边界增加自动合同：
  - 已迁移的 `AppearanceSettingsPage` 与 `SettingsPage` 根背景保持透明。
  - `NavigationViewContentBackground` 在 Light/Dark 分别与应用 Canvas 语义保持同步，`NavigationViewContentGridBorderBrush` 与应用边界语义同步。
  - 主窗口 `NavigationView` 内容宿主的左上圆角保持非零，页面不得通过不透明根背景遮挡该边界。
- 为 ComboBox family 补齐专项测试：
  - Standard/Compact 保持 `HorizontalContentAlignment=Stretch`、左侧选中文案、右侧 Chevron 和覆盖整块表面的 ToggleButton 命中结构。
  - Popup `VerticalOffset` 约 4 px，背景为 `App.Brush.Surface.Raised`，边界为 `App.Brush.Border.Subtle`，圆角使用 `App.Radius.Medium`，Effect 使用 `App.Elevation.Medium`，最小宽度不小于闭合态。
  - `App.Input.ComboBox.Item` 的 Normal/Hover/Selected/Disabled 状态分别符合透明、Secondary、Accent.Subtle + 左侧 Accent 状态条、Tertiary 文本合同，Item 圆角使用 `App.Radius.Small`。
  - 纯字符串长选中项单行省略且不挤压 Chevron；对象项/自定义模板继续由调用方承担等价截断。
  - 键盘展开、上下选择、Enter/Escape、Focus、Disabled、Editable（若现有产品调用支持）和主题热切换行为不因局部模板接管而回归。
- 更新稳定 `inputs` Gallery family，至少覆盖 Standard、Compact、宽控件、长文本、Disabled、Open Popup、Hover Item 和 Selected Item；不得新建按任务号命名的 Gallery scene。
- 重新生成并校验以下稳定视觉产物：
  - `artifacts/visual-review/gallery/inputs/`：Light/Dark，包含展开 Popup 的确定性场景。
  - `artifacts/visual-review/pages/appearance-settings/`：Light/Dark、100/125/150% DPI。
  - `artifacts/visual-review/pages/settings-home/`：Light/Dark、100/125/150% DPI。
  - 若现有截图工具已经有稳定 `main-window` window-id，则同步更新 `artifacts/visual-review/windows/main-window/`；若尚未建立该稳定入口，不为本任务临时创建任务号截图目录，以自动 Shell 几何合同覆盖圆角验证，并在结果中记录原因。
- 运行定向测试后执行完整质量门禁；不得以用户人工视觉验收作为任务关闭条件。

提交：

- 用户已明确授权本任务创建 Git 提交。完成实现与自动验收后，按可回溯性拆分原子提交，不把全部变化机械压成一个大提交。
- 源码与直接耦合测试应在同一原子提交中；设计文档/测试合同可单独提交；任务完成状态与最终结果记录最后提交。
- 使用 Conventional Commits。建议提交边界可为：
  1. `fix(ui): restore shell rounding and theme combo box popup`（源码 + 直接耦合测试）。
  2. `test(ui): refresh stable visual review coverage`（Gallery/截图生成器与测试；`artifacts/` 若按仓库规则忽略则不得强制提交）。
  3. `docs(ui): record shell and combo box visual contracts`（文档与 backlog 结果）。
- 若实际修改边界更适合拆为更多小提交，可调整，但不得把无关后续页面迁移混入本任务。

自动验收：

- Shell 资源/页面合同、ComboBox 模板/Item 状态、键盘交互、主题热切换与 Provider Bridge/Palette 精确键测试全部通过。
- `inputs` Gallery family 与两个已迁移设置页面的稳定截图成功重生成，manifest 与 PNG 校验通过；不存在以 `14A`、日期或提交号命名的视觉目录。
- 完整质量门禁通过。
- 工作树只剩仓库策略允许忽略的本地视觉产物或明确记录的无关预存改动；本任务修改已按上述原子边界提交。
- 将本任务状态改为 `[x]`，末尾追加“结果”，记录测试数量、截图目录、提交哈希和任何未生成 `main-window` 截图的原因。

结果：保留 `NavigationViewContentBackground` 与 `NavigationViewContentGridBorderBrush` 两个 Provider 适配投影键，但未新增业务语义颜色；Light/Dark Palette 通过现有 `App.Brush.Canvas` 与 `App.Brush.Border.Subtle` 的 Color 绑定定义投影，运行时再映射到 canonical Brush。Shell 内容宿主的 Canvas、边界和左上圆角合同已固定，`AppearanceSettingsPage`、`SettingsPage` 根节点保持透明。ComboBox 控件族模板补齐 Popup、Item 状态、键盘、Focus、Disabled、Editable 文本双向同步和主题热切换合同；Gallery Hover fixture 使用实际 `IsHighlighted` 状态，不再直接伪造背景。

自动验收证据：真实 `MainWindow` 在启动类 System 主题下从 SettingsPage 点击“外观”进入 AppearanceSettingsPage，并捕获 DispatcherUnhandledException；Release/Debug 均通过。完整门禁通过：Domain 2、Application 208、Presentation 382、Infrastructure 343、WPF 319，共 1,254 项测试；锁定还原无 packages.lock.json 变化，format、Release build 均通过。稳定产物已重生成并校验：`artifacts/visual-review/gallery/`（21 scenes × Light/Dark）、`artifacts/visual-review/gallery/inputs/`、`artifacts/visual-review/pages/appearance-settings/`、`artifacts/visual-review/pages/settings-home/`；所有 manifest 的 PNG 尺寸与 SHA 校验通过。仓库没有既有稳定 `main-window` window-id，因此未创建临时窗口截图目录，Shell 圆角由自动 WPF 几何合同覆盖。原子提交：`5ef1b06`（源码与直接测试）、`25e28a4`（Gallery 与视觉测试）；本条文档记录随独立 Conventional Commit 提交。

## [x] 15（P1）：迁移常规设置页

前置：14A。

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

结果：`GeneralSettingsPage` 迁移为正式 `AppPageHeader`、`AppSettingsGroup`、`AppSettingsRow` 与 `App.Input.ComboBox.Standard`/`App.Input.ToggleSwitch.Standard` 结构；页面根背景透明、保持 24 px 留白、关闭主窗口三选项与启动后最小化到托盘设置语义不变，设置仍经 ViewModel 即时持久化。按用户要求“启动后最小化到托盘”由 CheckBox 改为 ToggleSwitch，`docs/07_SETTINGS_PAGES.md` 的常规页条目同步更新。页面不再引用任何 Legacy 键（`PagePadding`、`BackIconButtonStyle`、`SettingsRow*` 等），资源图 legacy 引用指纹随迁移重定。新增 `GeneralSettingsPageTests` 契约覆盖正式结构、绑定、AutomationName、Canvas 背景、窄宽度/150% DPI 不重叠与主题热切换；`SettingsSubpageViewTests` 移除该页的旧行断言。稳定视觉产物已生成并校验：`artifacts/visual-review/pages/general-settings/` 共 6 张 Light/Dark × 100/125/150% 截图与 manifest；`artifacts/` 按仓库规则不入 Git。完整门禁通过：Domain 2、Application 208、Presentation 382、Infrastructure 343、WPF 327，共 1,262 项测试。任务未获用户提交授权，工作树改动未提交，提交哈希待后续授权。

## [x] 15A（P1）：将已迁移设置子页面改为扁平列表

前置：15。

背景：

- 最终设置视觉规则调整为：只有 `SettingsPage` 首页按导航类别保留 `AppSettingsGroup`；具体设置子页面不再显示“主题”“关闭主窗口时”“启动”等分组 Header，也不再为普通设置类别绘制独立圆角分组卡片。
- 已迁移的 `AppearanceSettingsPage` 与 `GeneralSettingsPage` 仍使用了 `AppSettingsGroup`，需要在继续迁移其它设置子页面前先纠正，以免旧模式扩散。

实现：

- 调整 `AppearanceSettingsPage`：移除 `ThemeGroup`/“主题”分组层，保留 `AppPageHeader` 与“应用主题” `AppSettingsRow`，主题 ComboBox、即时生效、持久化、返回导航和 AutomationName 语义不变。
- 调整 `GeneralSettingsPage`：移除“关闭主窗口时”“启动”两个 `AppSettingsGroup` 及重复 Group Header，将“关闭行为”“启动后最小化到托盘”按现有业务顺序直接排列为一个扁平设置列表；ComboBox/ToggleSwitch 绑定和即时持久化语义不变。
- `SettingsPage` 首页保持现状，继续使用 `AppSettingsGroup` 对导航入口分类；不得把本任务误扩展为取消首页分组。
- 确认 `AppSettingsRow` 在不处于 `AppSettingsGroup` 时仍具有正确的宽/窄布局、纵向密度、右侧控件对齐、Focus、Automation 和整行几何；如公共模板存在对 Group 的隐式依赖，只在 `ControlThemes/Settings.xaml` 中做最小通用修正，不增加页面专属 Style。
- 子页面行之间只使用现有稳定间距或必要分隔线，不新增分组 Header、Primary Surface 卡片或用 `AppSectionSurface` 伪装新的分组。
- 更新 `settings-controls` Gallery family，加入“standalone/flat settings rows”稳定场景，并保留首页 Group 场景，从 Gallery 上同时可检查“首页分组”和“子页扁平列表”两种明确用途。
- 更新现有静态资源/视觉树契约，明确 `AppearanceSettingsPage`、`GeneralSettingsPage` 不再含 `AppSettingsGroup`，而 `SettingsPage` 仍含分组。

自动验收：

- 外观页与常规页的设置绑定、即时保存、返回导航、主题热切换、关闭/托盘偏好全部回归通过。
- XAML/视觉树合同确认两个设置子页面无 `AppSettingsGroup`、无旧分组 Header；设置首页的 `AppSettingsGroup` 分类仍存在。
- 独立 `AppSettingsRow` 在宽/窄窗口、长说明、ComboBox/ToggleSwitch 与 100/125/150% DPI 下不重叠，右侧控件可点击，Tab/Automation 行为不退化。
- 更新 `artifacts/visual-review/gallery/settings-controls/`，并使用正式 View 重新生成：
  - `artifacts/visual-review/pages/appearance-settings/`
  - `artifacts/visual-review/pages/general-settings/`
  的 Light/Dark、100/125/150% DPI 截图与 manifest；不得建立 `15A` 命名的视觉目录。
- 完整质量门禁通过。

结果：`AppearanceSettingsPage` 移除 `ThemeGroup`/“主题”分组，`GeneralSettingsPage` 移除“关闭主窗口时”“启动”两个 `AppSettingsGroup`，两页均改为 `AppPageHeader` 下直接排列 `AppSettingsRow` 的单一扁平列表；ComboBox/ToggleSwitch 绑定、AutomationName、返回导航与即时持久化语义不变，常规页两行保持原业务顺序并沿用 16 px 稳定间距。`ControlThemes/Settings.xaml` 仅做一处最小通用修正：`AppSettingsRow` 共享样式补齐 `IsTabStop=False`（此前独立于 Group 使用时依赖页面逐行显式设置）。`SettingsPage` 首页分组保持现状。契约更新：外观/常规页测试改为断言无 `AppSettingsGroup`、无 `App.Typography.GroupTitle` 分组 Header，并固定行直接位于扁平列表；常规页 DPI 覆盖补 100/125/150%；`settings-controls` Gallery family 新增 standalone/flat rows 稳定场景（宽 ComboBox/ToggleSwitch 行 + 360 px 窄行）并保留首页 Group 场景，新增 Light/Dark × 100/125/150% DPI 几何、Focus/Tab/Automation 与无 Group 祖先契约测试。视觉产物已重生成并校验：`artifacts/visual-review/gallery/settings-controls/`、`artifacts/visual-review/pages/appearance-settings/`、`artifacts/visual-review/pages/general-settings/`（Light/Dark × 100/125/150%，manifest 与 PNG SHA/尺寸校验通过）；不存在以 `15A` 命名的视觉目录。完整质量门禁通过：Domain 2、Application 208、Presentation 382、Infrastructure 343、WPF 330，共 1,265 项测试；锁定还原无 packages.lock.json 变化，format 与 Release build（0 警告/0 错误）通过。任务未获用户提交授权，工作树改动未提交，提交哈希待后续授权。

后续直接视觉修正：子页面“无分组”只取消分类 Header，不取消列表 Surface。新增 `AppSettingsList` 作为无 Header 的设置列表容器，与 `AppSettingsGroup` 共享 Primary Surface、Medium Radius、20 px Padding、ItemContainer 和相邻行 Divider；`AppSettingsGroup` 改为在该基础上增加 Header/Description/Footer。`AppearanceSettingsPage` 与 `GeneralSettingsPage` 已改为单一 `AppSettingsList`，普通 Row 不提供整行 Hover/Pressed。同期修正 `App.Input.ToggleSwitch.Standard/Compact`，删除人为 `MinWidth`，无标签开关按可见本体收缩，避免不可见横向 Focus/HitTest 空白。该直接修正不新增任务编号。


## [x] 16（P1）：迁移播放设置页

前置：15A。

实现：

- 使用正式 PageHeader、SettingsList、SettingsRow、Input 和 Feedback 资源迁移 `PlaybackSettingsPage`；设置项进入单一无 Header `AppSettingsList`，不使用 SettingsGroup 或分类 Header。
- 覆盖默认语速、预取数量和朗读章节标题。
- 错误信息使用 FormField 或 Feedback Style，不复制局部错误文本样式。
- 保持即时保存、朗读清单按需重算和主动缓存批次快照语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 输入校验、设置保存、配置通知和缓存批次隔离回归通过。
- Light/Dark、长说明、错误和 150% DPI 场景通过。
- 使用正式 `PlaybackSettingsPage` 更新 `artifacts/visual-review/pages/playback-settings/`。
- 完整质量门禁通过。

结果：`PlaybackSettingsPage` 改用透明页面根、`AppPageHeader`、单一无 Header 的 `AppSettingsList`、`AppSettingsRow`、正式 TextBox/ToggleSwitch Input Style 与 Validation Feedback Style，覆盖默认语速、播放预取数量和朗读章节标题；移除该页重复的 TTS 规则入口及其无调用命令，TTS 规则继续由设置首页直接导航。回车/失焦提交、500 ms 即时保存、播放中变速、设置通知、朗读清单按需重算与主动缓存批次快照边界保持不变。新增页面结构、绑定、Automation、Legacy 清零、宽/窄布局、长说明、错误投影、Light/Dark 与 100/125/150% DPI 回归，并新增可复用的正式页面视觉产物测试工具；`artifacts/visual-review/pages/playback-settings/` 已生成 Light/Dark × 默认/长说明/错误 × 100/150% DPI 的 10 张 PNG 与 manifest，重复生成哈希一致，`artifacts/` 仍未加入 Git。定向 Presentation 8 项、WPF/资源图 28 项和显式视觉生成测试通过；锁定还原、format、Release build（0 警告/0 错误）与完整测试门禁通过。

## [x] 17（P1）：迁移导入与文本设置页

前置：16。

实现：

- 使用正式 PageHeader、SettingsList、SettingsRow、SettingsNavigationRow、Input 和 Feedback 资源迁移 `ImportTextSettingsPage`；普通设置与三级入口按业务顺序进入同一无 Header `AppSettingsList`，不使用 SettingsGroup 或分类 Header。
- 覆盖长段落切分、阈值、文件名提取设置和正则替换三级入口。
- 保持即时保存、校验和导航语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 设置校验、保存、正则替换导航和错误投影测试通过。
- 窄宽度与 150% DPI 下字段和导航入口可用。
- 使用正式 `ImportTextSettingsPage` 更新 `artifacts/visual-review/pages/import-text-settings/`。
- 完整质量门禁通过。

结果：`ImportTextSettingsPage` 改用透明页面根、`AppPageHeader` 和单一无 Header 的 `AppSettingsList`，按“拆分长段落、长段落阈值、文件名提取模板、正则替换”业务顺序组合正式 `AppSettingsRow` 与 `AppSettingsNavigationRow`；TextBox、ToggleSwitch 和阈值错误分别使用正式 Input/Feedback Style，页面 Legacy 键引用清零。模板/阈值 500 ms 防抖即时保存、回车/失焦提交、拆分开关即时保存及正则替换三级导航语义保持不变。新增页面结构、业务顺序、绑定、Automation、Tab/Focus、Legacy 清零、宽/窄布局、错误投影、Light/Dark 与 100/125/150% DPI 回归，并补充非整数阈值拒绝和正则路由测试；`artifacts/visual-review/pages/import-text-settings/` 已生成 Light/Dark × 默认/长说明/错误 × 100/150% DPI 的 10 张 PNG 与 manifest，重复生成哈希一致，`artifacts/` 仍未加入 Git。定向 Presentation 6 项、WPF/资源图 29 项和显式视觉生成测试通过；锁定还原、format、Release build（0 警告/0 错误）与完整测试门禁通过。

## [x] 18（P1）：迁移缓存与数据页

前置：17。

实现：

- 使用正式 PageHeader、SettingsList、SettingsRow、SettingsNavigationRow、Button 和 Feedback 资源迁移 `CacheAndDataPage`；设置、只读信息和导航入口采用单一无 Header `AppSettingsList`，不使用 SettingsGroup 或分类 Header。
- 显示缓存占用、容量上限、使用率、随容量上限展示的 LRU 说明、应用数据目录、清理全部缓存和缓存管理入口；不保留独立“缓存策略”条目。
- 应用数据目录使用文件夹图标按钮；清理全部缓存使用删除图标和 DangerIcon 资源，并保留 Tooltip、可访问名称和确认流程。
- 危险操作保持独立区域和确认流程。
- 保持容量调低后的确认、LRU 清理、保护 registry 和朗读清单同步回收语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 容量校验、确认/取消、清理、目录打开和导航测试通过。
- 危险按钮、错误状态和 150% DPI 几何测试通过。
- 使用正式 `CacheAndDataPage` 更新 `artifacts/visual-review/pages/cache-data/`。
- 完整质量门禁通过。

结果：`CacheAndDataPage` 改用透明页面根、`AppPageHeader`、单一无 Header 的 `AppSettingsList`、四个 `AppSettingsRow` 和末尾 `AppSettingsNavigationRow`；缓存总览使用正式 Typography/Progress/Button/Feedback 资源，缓存上限输入使用正式 TextBox/ComboBox 资源，该页 Legacy 键引用清零。按用户确认删除独立“缓存策略”条目，将 LRU 说明并入缓存上限行；应用数据目录改为独立文件夹图标入口，清理全部缓存改用 `Delete24` 与 `App.Button.DangerIcon`，并保留 Tooltip、AutomationName、命令禁用及确认流程。容量校验、调低确认/取消、LRU 清理、保护 registry、朗读清单回收、目录打开与缓存管理导航语义保持不变。新增正式结构、业务顺序、图标与绑定、Legacy 清零、错误状态、宽/窄布局及 100/125/150% DPI 回归，并更新 Legacy 页面引用指纹。`artifacts/visual-review/pages/cache-data/` 已生成 Light/Dark × 默认/长说明/校验错误/加载错误的 10 张 PNG 与 manifest，重复生成哈希一致；`artifacts/` 按仓库规则不入 Git。定向 Presentation 44 项、WPF 22 项和显式视觉生成测试通过；锁定还原、format、Release build（0 警告/0 错误）与完整测试门禁通过：Domain 2、Application 208、Presentation 383、Infrastructure 343、WPF 355，共 1,291 项测试。

## [x] 19（P1）：迁移诊断与关于页

前置：18。

实现：

- 使用正式 PageHeader、SettingsList、SettingsRow、Button、Typography 和 Feedback 资源迁移 `DiagnosticsAboutPage`；版本、目录、诊断摘要与操作按单一无 Header `AppSettingsList` 组织，不使用 SettingsGroup 或分类 Header。
- 覆盖版本、目录入口、数据库 schema、安全诊断摘要、许可证和复制脱敏诊断信息。
- 只读值使用 SettingsRow 内容槽，不建立专用 Value TextBlock 旧样式。
- 保持日志与诊断脱敏边界。
- 删除该页全部 Legacy 键引用。

自动验收：

- 目录打开、许可证、复制和脱敏测试通过。
- 长版本号、长路径、窄宽度和 150% DPI 场景通过。
- 使用正式 `DiagnosticsAboutPage` 更新 `artifacts/visual-review/pages/diagnostics-about/`。
- 完整质量门禁通过。

## [x] 20（P1）：迁移书库与 Feature BookCard

前置：19。

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

## [x] 21（P1）：迁移书籍详情与目录

前置：20。

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

## [ ] 22（P0）：统一 Rules 管理交互与 TTS 当前规则语义

前置：21。

实现：

- 先固定三个规则 Feature 的新行为合同，再建立共享视觉：页面进入时不自动打开规则；单击卡片才进入编辑状态；显式“取消”直接丢弃草稿、清除选择并关闭编辑器，只有切换规则或离开页面时继续使用 Dirty State 导航保护。
- 将规则启用状态从编辑 Draft 中解耦为列表级即时设置。Toggle 失败时回滚 UI；保存其它字段不能用旧草稿值覆盖刚刚切换的 `IsEnabled`。
- 删除 TTS Rules 页“设为当前”及其特殊保护逻辑。当前 TTS 规则只允许播放页切换，播放页规则列表只投影已启用规则；当前规则被禁用或删除时清空选择，不自动回退，导入、新建或重新启用规则也不自动成为当前规则。
- 为 TTS、章节规则和正则替换建立明确的单规则 JSON 导出/复制合同，完整保留 `IsEnabled` 等可移植字段；不要把内部持久化 Id 当作导入覆盖键。
- 三类规则均支持从文件和剪切板导入单条对象或规则数组，并采用合并语义：完全重复跳过，同名不同内容作为新规则，不覆盖现有规则；章节/正则新增项按导入源顺序追加到现有排序末尾。
- 为页面层准备统一的文件选择、剪切板读写和导入结果反馈入口，复用既有 presentation port/错误投影边界，不在各 Page code-behind 重复实现平台访问。
- 本任务只完成行为、用例、状态模型和可测试命令边界；最终卡片布局、ContextMenu、长按拖动视觉和 PageHeader 排版由后续 Rules 共享视图及页面迁移任务完成。

自动验收：

- TTS Rules 中不存在可执行“设为当前”入口；播放页只列出启用规则，禁用/删除当前规则会清空 `SelectedTtsRuleId`，且不会自动选择替代规则。
- 播放页无可用规则状态只引导用户前往规则管理页启用或导入规则，不再提示用户在规则管理页选择“当前规则”。
- 启用切换与编辑 Dirty State 相互独立；存在未保存草稿时切换启用状态并随后保存其它字段，不会回写旧启用值。
- 初始无选择、单击打开、显式取消关闭、Dirty 切换保护和页面离开保护均有 Presentation 测试。
- 三类规则的文件/剪切板导入、单对象/数组、重复跳过、同名不同内容新增、排序追加、失败不覆盖现有数据和单规则导出状态保真测试通过。
- TTS 导出与导入的 `IsEnabled` 对称性测试通过；导入、新建和重新启用均不改变当前播放规则。
- 完整质量门禁通过。

## [ ] 23（P1）：建立 Rules 页面族共享视图

前置：22。

实现：

- 在相近 Rules Feature 边界中建立共享规则列表项和必要的交互行为，不建立完整 `AppRuleWorkbench`，不固定三个页面的列宽和字段集合。
- 共享卡片采用左右布局：左侧名称/摘要占剩余空间，右侧 ToggleSwitch 使用自身内容宽度；Toggle 命中区域不能因 Stretch 或透明空白扩展，也不触发卡片选择或拖动。
- 单击卡片主体用于选择/打开编辑器；右键打开 ContextMenu 但不改变当前编辑对象，支持 `Shift+F10`/Menu Key。共享菜单能力允许 Feature 提供“导出到文件”“复制到剪切板”、删除以及可选上移/下移，不显示 `⋮` 按钮。
- 为章节规则和正则替换建立整卡长按拖动行为：固定约 `300 ms` 长按阈值，长按后移动才启动 Drag；Toggle/ContextMenu 区域排除；使用轻量拖动态反馈、明确插入线和列表边缘自动滚动，不实现相邻卡片位移动画。
- 插入索引由目标卡片垂直中心线决定前/后位置；DragOver 不写持久层，只在 Drop 后提交排序。上移/下移继续作为键盘和备用排序入口。
- 共享视图使用正式 Selection、Menu、Input、Typography 和 Surface 资源，并为 TTS、章节、正则 fixture 建立 Gallery 场景，但不迁移正式页面。

自动验收：

- 共享视图不依赖具体规则 ViewModel 类型或业务持久化类型，只消费明确的显示、状态和命令合同。
- Toggle 可见轨道与实际横向命中范围一致；Toggle 点击不改变选择，不启动拖动。
- 左键选择、右键不选择、键盘 ContextMenu、虚拟化回收和菜单能力状态测试通过。
- 长按阈值使用可确定测试的手势状态机，不以任意 `Thread.Sleep`/`Task.Delay` 猜测；插入前后、边缘自动滚动、取消拖动、Drop 后排序和备用上移/下移测试通过。
- Gallery 明确覆盖普通 TTS 项、可排序项、禁用项、Selected、Focus、ContextMenu、拖动态和插入线；更新 `artifacts/visual-review/gallery/rules-shared/`。
- 完整质量门禁通过。

## [ ] 24（P1）：迁移 TTS 规则工作台

前置：23。

实现：

- 使用正式 PageHeader、SectionSurface、AppFormField、Input、Button、Menu、Feedback 和 Rules 共享列表项迁移 `TtsRulesPage`。
- `AppPageHeader.Actions` 放置新建、从文件导入、从剪切板导入和帮助，与标题平齐；不保留 Header 下方并行工具栏，也不提供页面级导出。
- 单规则 ContextMenu 提供“导出到文件”“复制到剪切板”和删除；不显示当前规则状态、“设为当前”或 `⋮` 按钮。
- 页面拥有真实双栏比例、字段布局和滚动；初始右侧为空状态，单击规则后才打开编辑器。
- 保持任务 22 已固定的启用即时保存、试听编辑副本、显式取消关闭编辑器、Dirty 导航守卫和导入合并语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 最小工作区下关键字段有非零宽度且可滚动；初始空编辑区、选择和取消关闭状态正确。
- PageHeader 文件/剪切板导入、右键文件/剪切板单规则导出、删除、启用、试听、保存/取消和切换保护测试通过。
- 页面及其 Automation/ContextMenu 中不存在“设为当前”操作；右键其它规则不会切换当前编辑对象或触发现有草稿守卫。
- 使用正式 `TtsRulesPage` 更新 `artifacts/visual-review/pages/tts-rules/`。
- 完整质量门禁通过。

## [ ] 25（P1）：迁移章节规则工作台

前置：24。

实现：

- 使用与任务 24 相同的公共边界迁移 `ChapterRulesPage`。
- `AppPageHeader.Actions` 放置新建、从文件导入、从剪切板导入、默认规则导入/恢复和帮助；不提供页面级导出。
- 单规则 ContextMenu 提供“导出到文件”“复制到剪切板”、上移、下移和按能力控制的删除；右键不改变当前编辑对象。
- 页面保留自身字段、帮助、默认规则导入/恢复和布局；通用文件/剪切板导入与默认规则操作保持独立语义。
- 使用共享整卡长按拖动、插入线和边缘自动滚动，不再显示拖动手柄；不实现相邻卡片位移动画。
- 保持任务 22 已固定的初始空编辑区、启用即时保存、显式取消关闭编辑器、Dirty 导航守卫和合并导入语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- 默认规则操作、文件/剪切板合并导入、单规则导出、启用、长按 Drag/Drop、上移/下移、帮助、保存/取消和导航守卫测试通过。
- 长正则、错误、最小工作区、插入线和列表边缘自动滚动几何测试通过。
- 使用正式 `ChapterRulesPage` 更新 `artifacts/visual-review/pages/chapter-rules/`。
- 完整质量门禁通过。

## [ ] 26（P1）：迁移正则替换工作台

前置：25。

实现：

- 使用与任务 24 相同的公共边界迁移 `RegexReplacementRulesPage`。
- `AppPageHeader.Actions` 放置新建、从文件导入、从剪切板导入和帮助；不提供页面级导出。
- 单规则 ContextMenu 提供“导出到文件”“复制到剪切板”、上移、下移和删除；右键不改变当前编辑对象。
- 页面保留名称、Pattern、Replacement、作用目标、帮助和自身布局，错误统一通过 AppFormField/Feedback 投影。
- 使用共享整卡长按拖动、插入线和边缘自动滚动，不再显示拖动手柄；不实现相邻卡片位移动画。
- 保持任务 22 已固定的初始空编辑区、启用即时保存、显式取消关闭编辑器、Dirty 导航守卫、合并导入和播放刷新语义。
- 删除该页全部 Legacy 键引用。

自动验收：

- Pattern/Replacement 校验、错误投影、文件/剪切板合并导入、单规则导出、启用、长按排序、保存/取消、导航守卫和播放刷新测试通过。
- 长表达式、错误、最小工作区、插入线和列表边缘自动滚动几何测试通过。
- 使用正式 `RegexReplacementRulesPage` 更新 `artifacts/visual-review/pages/regex-replacement-rules/`。
- 完整质量门禁通过。

## [ ] 27（P1）：迁移缓存管理页

前置：18、21、26。

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

## [ ] 28（P1）：迁移播放页与 PlayerView

前置：8、21、27。

实现：

- 使用正式 PageHeader、SectionSurface、Typography、Surface、Button、Media、Progress、Selection、Feedback 和 AppStatusView 迁移 `PlayerPage` 与 Feature 所有的 `PlayerView`。
- 正文、章节侧栏、媒体控制条、Flyout 内容和页面 Padding 由 Playback Feature 拥有。
- 播放页和迷你播放器的媒体按钮使用统一尺寸和中性状态；播放/暂停不建立 Accent 主媒体操作层级。
- 保持 PlaybackSnapshot、播放状态机、上下章/段、拖动、音量、定时停止、主动缓存、滚动追随和快捷键语义。
- TTS 规则菜单只渲染已启用规则并保留播放页独占切换语义；无可用规则状态不恢复 Rules 页“设为当前”的旧文案或入口。
- 当前段使用统一轻量状态，不使用高饱和背景。
- 删除两个视图全部 Legacy 键引用。

自动验收：

- 播放/暂停、上下段/章、拖动、当前段居中、用户滚动暂停追随、音量、定时停止、主动缓存和页面离开测试通过。
- 长正文、空章节、错误、最小窗口和 100/125/150% DPI 场景通过。
- 使用正式 `PlayerPage` 更新 `artifacts/visual-review/pages/player/`。
- 完整质量门禁通过。

## [ ] 29（P1）：迁移主窗口与启动窗口

前置：12–28。

实现：

- 迁移 `MainWindow` 的 Window Chrome、一级导航、内容宿主和全局运行时入口。
- 迁移 `StartupStatusWindow` 到正式 Typography、Surface、Progress、Feedback 和 AppStatusView。
- Shell 只拥有标题栏、导航、内容边界和 Window Background，不向页面重复注入 Padding/FrameMargin；正式 Page 自身从根区域覆盖 Canvas。
- 保持最小化、最大化、恢复、关闭到托盘、真正退出、未保存导航守卫、播放和主动缓存入口语义。
- 删除两个窗口全部 Legacy 键引用。

自动验收：

- Window Chrome、拖动、最小化、最大化、恢复、关闭和托盘状态机测试通过。
- Startup loading/error 状态和脱敏错误投影测试通过。
- `960 × 640`、Light/Dark 和 125/150% DPI 下无重复外边距或核心内容遮挡。
- 分别使用正式窗口更新 `artifacts/visual-review/windows/main-window/` 与 `artifacts/visual-review/windows/startup-status-window/`。
- 完整质量门禁通过。

## [ ] 30（P1）：统一 Dialog、Flyout、Snackbar 和状态视图

前置：9、12–29。

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

## [ ] 31（P0）：删除 Legacy 与旧资源并完成发布门禁

前置：1–30。

实现：

- 确认正式页面、窗口、Style Gallery 和测试不再引用 Legacy 键。
- 删除 `Resources/Legacy`、旧 `SemanticStyles.xaml`、旧聚合字典、旧 alias、零引用 Token/Style/ControlTheme 和临时迁移测试。
- 删除所有 `PagePadding`、`SettingsRowControlWidth` 等全局页面几何键；页面值保留在唯一布局 owner。
- 扫描硬编码主题色、禁止隐式 Style、全局模板覆盖、运行时 Style 写入、重复资源键和生产 fixture；同时验证正式 Page 根区域统一使用 Canvas，页面不再引用 Window Background 形成外围壳层。
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
