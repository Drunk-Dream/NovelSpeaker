# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段处理 **书库响应式网格达到最大卡宽后的左对齐收口**。当前 `dev` 基线为 `3780d3888d04436eaa59e699b783bed733024d7d`。

T001–T004 均已完成并保留作为历史记录。本轮只调整 Library 响应式 Panel 的水平排列策略：原先在卡片达到 `360 px` 最大宽度后将 bounded grid 整体居中，现在改为始终从书库内容区左侧基线开始排列。

本轮输入与边界：

- 不改变 `300 px` 最小卡宽、`360 px` 最大卡宽、`16 px` 横纵间距和基于实际 viewport 的列数计算。
- 不改变 `BookCardView` 内部结构、封面尺寸、MoreButton 空间策略、书库搜索/排序/导入/滚动状态或主题快捷切换。
- 不引入新的 WindowWidth breakpoint、NavigationView 特判、DPI 特判或全局 Design Token。

本轮终态：

- 每一行书卡都从内容区左侧开始排列，第一列 `X=0`（相对于响应式 Panel）。
- 当计算卡宽超过 `360 px` 时只截断为 `360 px`，剩余空间留在右侧，不再计算居中的 `startX`。
- 最后一行不足完整列数时仍从左侧开始，不按该行实际书籍数量重新居中。
- 窄 viewport 的安全收缩和无横向滚动行为保持不变。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成；任务末尾必须附简短“完成成果”。
- `[!]`：存在阻塞，必须记录可复现原因。
- `P0`：影响公共交互所有权、主题一致性、核心页面操作或最终质量门禁。
- `P1`：补充视觉覆盖、长期维护性或非阻塞清理。

Codex 完成任务后保留条目并标记 `[x]`；只有新的规划阶段才允许再次删除或重写 Backlog。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 开始前至少阅读：`AGENTS.md`、`docs/06_UI_AND_USER_FLOWS.md`、`docs/09_TESTING_AND_QUALITY.md`、`docs/10_ENGINEERING_CONVENTIONS.md`、`docs/13_VISUAL_DESIGN_SYSTEM.md`、`LibraryResponsivePanel.cs`、`LibraryResponsivePanelTests.cs` 及 Library 页面相关测试。
3. 修改范围优先限制在 `LibraryResponsivePanel` 与直接相关测试；若无实际需要，不改 `LibraryPage`、`BookCardView`、Shell、Theming 或公共资源。
4. 保持现有 viewport 驱动的列数/卡宽计算，只改变 Arrange 的水平起点策略；不得退回固定 `WrapPanel.ItemWidth` 或窗口宽度 breakpoint。
5. 所有行都使用同一个左侧基线，不为单本书、最后一行或达到最大宽度后的 bounded group 建立单独居中逻辑。
6. 响应式 Panel 仍必须在极窄 viewport 下安全收缩，不能为了左对齐重新引入横向滚动或超出 Arrange bounds。
7. 测试应验证用户可观察的稳定几何合同，不只把测试名称从 centered 改成 left-aligned；至少检查最大卡宽场景第一列 `X=0`、相邻卡片间距和最后一行左对齐。
8. 不使用负 Margin、Transform、额外容器 Padding 或页面补偿来实现左对齐；应在真正拥有排列位置的 Panel 中修正根因。
9. 如进行视觉验收，允许生成临时截图/脚本/manifest，但验收结束前必须全部删除。
10. WPF 自动测试默认运行在隐藏 Desktop，不自行设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
11. 视觉任务允许生成临时截图、截图脚本、VisualTree dump、manifest 或 fixture，但验收结束必须全部删除；任务关闭前执行 `git status --short` 和生成目录审计。
12. 缺陷属于最终像素/Provider 模板行为时，不能只用 Setter、Style key 或静态 VisualTree 证明通过；应使用真实 View/最终渲染像素验证。
13. 每个任务完成后更新自身状态并写“完成成果”，记录主要实现、测试与任何实际发现；不得自行删除其它任务。

## Phase A：公共 Floating Button 收口

## [x] T001（P0）：建立统一 FloatingIcon Button owner 并迁移全部生产调用方

目标：

- 将定位/返回类悬浮操作从“`App.Button.Floating` + `App.Surface.FloatingAction`”双层 owner 收口为 Button family 中的单一交互 owner。
- 让悬浮按钮的 Hover / Pressed / Keyboard Focus / Disabled / Theme 行为与当前克制型 Fluent 交互语言一致。

实施：

1. 审计 `App.Button.Floating`、`App.Surface.FloatingAction` 的全部生产与测试调用方，确认实际范围。当前至少包含：
   - `BookDetailsPage`：定位到当前章节。
   - `PlayerView`：定位到当前章节。
   - `PlayerView`：返回当前段落。
2. 在 `Buttons.xaml` 建立统一 `App.Button.FloatingIcon`：
   - Button 自己拥有 Background、Border、CornerRadius、Elevation、Foreground、Hover、Pressed、Keyboard Focus 与 Disabled。
   - Rest：弱 `Surface.Raised` + `Border.Subtle` + 固定 `Elevation.Low`。
   - Hover：`Interaction.Surface.Hover` + `Interaction.Foreground.Hover`，Elevation 不提高。
   - Pressed：`Interaction.Surface.Pressed` + `Interaction.Foreground.Pressed`，不切到 `Accent.Subtle`。
   - Keyboard Focus 使用独立 Focus 语义；Mouse Hover/Click 不制造长期 Focus Ring。
   - 图标颜色由 owning Button 控制；不得在页面内为 `SymbolIcon` 写状态 Foreground。
3. 保持当前约 `44 × 44` 命中区与约 `40 × 40` 视觉尺度的产品观感；若统一为 Button 本体圆形 Surface，应确保不缩小现有有效命中范围。
4. 将上述全部页面调用迁移为单层 Button + Icon 结构。页面只保留 Position/Margin/Visibility/Command 或 Click/Tooltip/AutomationName。
5. 更新 Style Gallery 的现有 Button scene，覆盖 FloatingIcon 的 Rest/Hover/Pressed/Focus/Disabled；不新建任务专属 scene。
6. 迁移全部调用和测试后，若 `App.Button.Floating`、`App.Surface.FloatingAction` 已无合法用途，则直接删除；同步清理相关 Gallery 条目、测试白名单和架构合同，不保留 alias/compat。

自动测试/验收：

- 静态调用方审计证明生产代码只使用新 FloatingIcon owner，不再出现旧的 Button + FloatingAction Border 组合。
- Button family 测试证明 Hover/Pressed 不改变布局尺寸，Hover 不提高 Elevation，Pressed 不使用 Accent Surface。
- Light/Dark 下最终图标 Foreground 在 Rest/Hover/Pressed/Disabled 均可读；High Contrast 资源可解析并有可辨识 Focus/边界。
- Book Details / Player 原有定位、Visibility、Command/Click、Tooltip、Automation 测试保持通过。

完成成果：建立 `App.Button.FloatingIcon` 作为唯一 `Wpf.Ui.Controls.Button` 交互 owner，迁移 Book Details 与 Player 的三个定位/返回调用方，删除旧 Button/Surface 双层资源并同步 Gallery、架构和调用方合同。T001 组合回归与架构检查通过（18/18）；Book Details 几何、Player 内容和返回按钮契约通过。Book Details 两个结构/异步 catalog 测试在隐藏 WPF 宿主的 20 秒 hang timeout 内触发 testhost 崩溃且无失败断言，已记录并留待后续真实页面验收与最终门禁复核。

## Phase B：真实页面视觉验收

## [x] T002（P0）：完成 FloatingIcon 的真实页面像素与可访问性验收

依赖：T001。

目标：

- 证明新公共 Button 不只在 Style 属性层正确，而且在真实 Book Details / Player 最终渲染中符合统一交互语言。
- 防止 Provider chrome、父子状态层、阴影或裁切重新制造旧式视觉效果。

实施与验收：

1. 使用正式 View 和确定性脱敏 fixture，覆盖以下三个生产场景：
   - Book Details：定位到当前章节。
   - Player：定位到当前章节。
   - Player：返回当前段落。
2. 至少验证 Light / Dark 的 Rest、Hover、Pressed、Keyboard Focus；Disabled/Collapsed 若该调用点可达则验证对应可观察状态。
3. 最终像素重点检查：
   - 只有一个圆形/统一圆角可见 Surface，没有内部第二层 Border 或方形 Provider PointerOver。
   - Rest 有克制的悬浮可发现性；Hover 仅弱 Surface/Foreground 增强，不出现明显“抬高一层”的阴影跳变。
   - Pressed 使用普通 Interaction Pressed 语义，不突然变成 Accent 持续态。
   - Dark Mode 下图标没有按下变黑或低对比度闪烁。
   - Focus Ring 只在键盘焦点时出现，且不因 Mouse Hover 或普通点击长期残留。
   - 状态切换不改变 Button 的布局中心、命中区或造成裁切。
4. 可以增加最终像素/真实宿主回归测试；若已有公共 Button 像素测试能充分证明公共状态，则页面测试只保留调用结构与关键最终像素，避免三处重复大量等价 case。
5. 视觉验收生成的截图、脚本、manifest、VisualTree dump、临时 fixture 与 `TestResults/wpf-diagnostics` 在验收结束前全部清理，不提交到 Git。

验收：

- 三个实际场景在 Light/Dark 下视觉一致，且符合 `docs/13_VISUAL_DESIGN_SYSTEM.md` 中 FloatingIcon 合同。
- 不改变滚动定位、Visibility、Command、ToolTip 和 Automation 行为。

完成成果：新增正式 Book Details/Player View 的隐藏窗口视觉回归，覆盖三个生产按钮场景在 Light/Dark 下的 Rest、Hover、Pressed、Keyboard Focus 最终位图、glyph 前景、命中区、按钮/图标中心和可访问性语义，并验证定位按钮的 Collapsed→Visible 可达状态。主题、焦点、窗口与内存位图均完成清理；真实页面测试通过 2/2，格式检查通过。

## Phase C：最终清理与质量门禁

## [x] T003（P0）：清理旧 Floating 资源并完成最终质量门禁

依赖：T001、T002。

目标：

- 确认旧交互方案没有残留在生产资源、页面、Gallery、测试或文档中。
- 证明本轮仅改变视觉交互，不引入业务回归和验收副产物。

实施与验收：

1. 全仓审计：
   - `App.Button.Floating`
   - `App.Surface.FloatingAction`
   - 页面级定位按钮 Hover/Pressed/Focus Trigger
   - 对 FloatingIcon 的硬编码 Background/Border/Foreground/Effect
   - 旧测试白名单与 Gallery fixture
2. 若旧键已经无正式用途，必须删除定义和引用；不得为了旧测试或兼容性继续保留零引用公共资源。
3. 更新相关架构测试，确保未来新定位/返回悬浮按钮复用统一 FloatingIcon owner，而不是重新建立 Button + Surface 双层状态。
4. 确认所有视觉验收副产物已清除，`git status --short` 只包含正式任务修改。
5. 按固定顺序执行完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

完成标准：

- 完整门禁 0 失败；Release build 0 warning / 0 error。
- 旧 Floating 双 owner 资源与调用模式不存在。
- FloatingIcon 的主题、最终像素、Focus、Automation 和命中区合同均有稳定自动回归证据。
- 没有截图、截图脚本、manifest、临时 fixture、VisualTree dump 或诊断目录残留。

完成成果：补强调用方静态审计，覆盖生产/Gallery XAML 与源代码中的旧资源键、页面级视觉属性元素及嵌套 Setter/Trigger，并修正 Player 返回按钮测试对本地 Visibility 样式的 BasedOn 合同。完整门禁通过：restore、format、Release build（0 警告/0 错误）及全量测试（851/851，通过）；未发现本任务新增视觉或诊断产物。

## Phase D：书库响应式布局与主题快捷切换验收

## [x] T004（P0）：应用、验收、修复并提交 `core.patch`

依赖：T001–T003 已完成；用户提供本轮 `core.patch`；当前文档补丁已应用或与本任务一并位于工作树。

目标：

- 将 `core.patch` 安全应用到 `44e8dc701a6382e92af37a16ba68b4ac993c43b0` 对应源码基线。
- 验证响应式书库和 Shell Light/Dark 快捷切换完全符合当前文档合同。
- 主动发现并修复补丁中的编译、逻辑、布局、主题同步、可访问性、视觉或测试问题，而不是只证明补丁能够应用。
- 完成专项测试、真实 UI 验收、全量质量门禁、临时产物清理，并提交正式修改。

执行：

1. **输入与基线检查**
   - 查看 `git status --short` 和当前 HEAD/工作树，确认没有与本任务无关的未提交修改。
   - 确认正式源码仍以 `44e8dc701a6382e92af37a16ba68b4ac993c43b0` 为代码基线；文档补丁可以是额外的已提交或未提交修改。
   - 定位用户提供的 `core.patch`。若它位于仓库内部，将其视为临时输入：不得加入 Git index，任务结束前删除。
   - 先执行 `git apply --check <core.patch>`；通过后再 `git apply <core.patch>`。若检查失败，定位真实冲突原因并人工移植等价修改，必须确认没有遗漏 hunk。

2. **源码审阅**
   - 审阅 `LibraryPage`、新增响应式 Panel/布局组件、`BookCardView`、Theme runtime/preference service、`MainWindowViewModel`、`MainWindow.xaml` 与 DI 注册。
   - Library 必须依据实际可用 viewport 计算列数和卡宽；不得使用一组 WindowWidth breakpoint 模拟响应式。
   - 固定合同：最小卡宽 `300 px`、最大 `360 px`、间距 `16 px`；达到最大宽度后整组居中；极窄 viewport 单列安全收缩且不出现横向滚动。
   - `BookCardView` 保持 `104 × 140 px` 封面和既有主要命中语义；MoreButton 只占标题安全区，不能继续让作者/章节/ProgressBar 全列保留旧 `42 px` 右 Margin。
   - 主题快捷入口固定在 NavigationView Footer 最后一项；展开/Compact 均可访问，图标与文案表达“切换后的目标主题”。
   - 快捷入口只写入显式 Light/Dark。设置为 System 时必须读取实际生效主题再取反；不得把字符串 `System` 自身当成 Light/Dark，也不得通过该入口回到 System。
   - 设置页和快捷入口必须共享正式主题持久化路径；失败回滚、并发/迟到结果语义不得比现有 `ThemePreferenceService` 退化。

3. **专项自动测试**
   - 为响应式 Panel 建立确定性几何测试，至少覆盖单列安全收缩、`300 px` 下限附近、两列、默认目标 viewport 三列、达到 `360 px` 上限后的居中、多列宽窗口；断言列数、卡宽范围、间距和无横向溢出。
   - 更新 `LibraryPageTests`，不再只验证“能够渲染”；至少证明默认主窗口对应 viewport 可以形成目标 3 列，并验证实际 viewport 变化后布局重新计算。
   - 更新 `BookCardViewTests`，证明正文信息与 ProgressBar 不再被 MoreButton 的整列右侧预留无谓压缩，并保持 MoreButton 的 Tooltip/Automation/ContextMenu/局部 Hover 行为。
   - 更新主题服务/Presentation 测试，覆盖 Light→Dark、Dark→Light、System+实际 Light→Dark、System+实际 Dark→Light、保存失败回滚、迟到请求和设置外部变化后的 Shell 投影同步。
   - 更新 MainWindow WPF 测试，覆盖 Footer 最后一项、展开/Compact 的 Content/Icon/Tooltip/AutomationName、主题切换后状态刷新，以及不破坏现有缓存/导出/播放 Footer 项。
   - 若 `core.patch` 新增接口导致既有 Fake/Stub 编译失败，只做必要同步，不为了测试方便放宽生产接口语义。

4. **真实 UI 与视觉验收**
   - 使用正式 `LibraryPage` / `MainWindow` 与确定性脱敏 fixture；默认仍在隐藏 Desktop 中执行。
   - 至少覆盖 Light/Dark 下的书库 Rest/Hover 与 Shell Footer 主题入口。
   - 书库至少检查窄、默认、宽三个 viewport。重点确认：默认窗口不再只有 2 张卡并在右侧留下大块空白；卡片之间间距一致；达到最大卡宽后空白左右均衡；卡片内部不存在明显无意义右侧空白。
   - 视觉验收不得仅看截图：同时用布局几何/像素或 VisualTree 合同证明实际列数、卡宽、组居中和无裁切。
   - 主题入口至少验证显式 Light/Dark 两态的目标图标和文案；System 的取反逻辑使用可控 runtime/测试替身建立确定性证据，不依赖测试机当前系统主题。

5. **发现问题后的修复原则**
   - 可直接修改 `core.patch` 应用后的正式源码和测试，不需要等待人工确认。
   - 优先修复布局 owner、主题 service/runtime 或 Shell projection 的根因；不在页面加入一次性 Trigger、硬编码颜色、负 Margin、透明遮罩或只针对某个 DPI/窗口宽度的特判。
   - 如果原 `core.patch` 的实现方式与文档合同冲突，以本文档终态为准，并在“完成成果”中简述实际调整。

6. **清理**
   - 删除验收过程中生成的截图、截图脚本、临时 manifest、VisualTree dump、临时 fixture、`TestResults/wpf-diagnostics` 等副产物。
   - 删除仓库内部的 `core.patch` 临时副本；补丁文件本身不得提交。
   - 执行 `git status --short`，确保只剩正式源码、测试和文档修改。

7. **完整质量门禁**

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

   - Release build 必须 0 warning / 0 error。
   - 全量测试必须 0 失败。
   - 默认不得设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。

8. **完成记录与提交**
   - 全部验收通过后，将 T004 改为 `[x]`，在任务末尾增加简短“完成成果”，至少记录：响应式网格最终实现、主题快捷切换最终逻辑、主要专项测试、全量门禁结果和是否修复了原补丁问题。
   - 将正式源码、测试和文档提交为一次清晰提交；若当前文档补丁尚未提交，可与本轮正式修改一起提交。
   - 建议提交信息：`feat(ui): refine library layout and theme switching`。
   - `core.patch`、截图及其它验收副产物不得进入提交。
   - 提交后再次执行 `git status --short`；工作树应保持干净，并在最终回复中报告提交哈希和关键验收结果。

完成标准：

- `core.patch` 的目标功能已被真正验收，而不是仅成功应用。
- 默认书库布局明显改善，响应式算法在窄/默认/宽 viewport 下满足 `300–360 / 16 px / 最大宽度后居中` 合同。
- BookCard 的有效文字宽度被合理利用，MoreButton 不再造成整列无意义右侧留白。
- Shell 快捷入口只执行 Light/Dark；System 根据实际主题正确取反，返回 System 只能通过设置页。
- 专项测试、真实 UI 验收和完整门禁全部通过。
- 临时补丁与视觉验收副产物已清理，正式修改已经提交，工作树干净。

完成成果：已应用并验收 `core.patch`；Library 采用实际 viewport 驱动的 `300–360 px / 16 px` 响应式网格，BookCard 仅在标题安全区避让 MoreButton；Shell Footer 提供基于实际生效主题的 Light/Dark 显式切换，System 仍由 Appearance 设置页负责。补回滚/失败/取消、迟到请求、外部设置同步、页面激活订阅、并发点击及 WPF Footer/布局/DI 契约测试，并修复了补丁中的主题通知、设置页同步、并发命令取消和 DI 边界问题。`dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、Release 全量 build（0 警告/0 错误）及全量测试（865/865，通过）均通过；临时 `core.patch` 已清理且未进入提交。

## Phase E：书库最大卡宽后的左对齐收口

## [x] T005（P0）：将响应式书架从 bounded-grid 居中改为稳定左对齐并提交

依赖：T004 已完成。

目标：

- 保留现有 `300–360 px / 16 px / viewport-driven` 响应式算法。
- 修复卡片达到最大宽度后整组居中导致的视觉问题，使书架始终从页面内容左侧基线开始。
- 保证单本书、少量书籍、完整多列和最后一行不足列数时都具有稳定的第一列位置。

实施：

1. 修改 `LibraryResponsivePanel.ArrangeOverride`：
   - 第一列起点固定为 `0`。
   - 删除 `(finalSize.Width - layout.GroupWidth) / 2` 一类 bounded-grid 居中计算。
   - 卡宽仍由现有 `CalculateLayout` 决定；达到 `MaxItemWidth=360` 后右侧自然保留剩余空间。
   - 每一行使用相同列坐标；最后一行不得根据剩余 item 数量重新居中。
2. 检查 `CalculateLayout`：
   - `MinItemWidth=300`、`MaxItemWidth=360`、`HorizontalSpacing=16`、`VerticalSpacing=16` 保持不变。
   - 列数仍按实际 available width 计算。
   - viewport `<300` 时的单列收缩行为保持不变。
3. 更新 `LibraryResponsivePanelTests`：
   - 将现有 `centers_the_bounded_grid` 合同改为左对齐合同。
   - `1172 px / 3 列 / 360 px` 场景第一列必须为 `X=0`，右侧保留约 `60 px`。
   - 保留 `632 px → 2 × 308`、`1012 px → 3 × ~326.67`、`1248 px → 4 × 300` 等已有响应式场景。
   - 增加或明确验证最后一行不足完整列数仍为 `X=0`，相邻列间距保持 `16 px`。
   - 极窄 `280 px` 场景继续证明无横向溢出。
4. 检查 Library 页面级测试是否存在“整体居中/左右空白均衡”的旧断言或视觉预期；如有，仅更新与本次对齐合同直接冲突的部分。

自动验收：

- `LibraryResponsivePanelTests` 全部通过。
- Library 相关 WPF 测试通过。
- 至少用真实 `LibraryPage` 验证单本书和达到最大卡宽的多卡场景：第一列与书库内容区左侧基线稳定，不再随 item 数量横向移动。
- Light/Dark 布局几何一致；本任务不改变主题颜色或交互状态。
- 若生成截图或视觉调试副产物，结束前全部删除。

完整质量门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

完成与提交：

- 门禁通过后将 T005 标记 `[x]` 并附简短“完成成果”，记录最终排列规则、专项测试与全量测试结果。
- 提交正式源码、测试和本文档修改。
- 建议提交信息：`fix(ui): left-align bounded library grid`。
- 提交后执行 `git status --short`，确认工作树干净且没有截图、脚本、manifest、诊断文件等副产物。

完成成果：`LibraryResponsivePanel` 保留原有 viewport-driven 的 `300–360 px / 16 px` 响应式算法，仅将所有行的排列起点固定为内容区左侧 `X=0`；最大卡宽后的剩余空间留在右侧，最后一行不足列数时仍保持左对齐。新增响应式 Panel 的最大卡宽、最后一行和 `280 px` 窄 viewport 几何回归，并补充真实 `LibraryPage` 单本/六本书场景的首列稳定性验证。专项 WPF 测试及 Library 页面测试通过（7/7）；完整质量门禁 `dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、Release build（0 警告/0 错误）和全量测试（867/867）均通过。
