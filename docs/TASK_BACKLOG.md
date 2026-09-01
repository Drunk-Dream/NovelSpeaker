# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段只处理 **Floating Action / 定位类悬浮按钮的交互样式收口**，基线提交为 `0bdc1191ba70adda767bf19fa44b1353e4b044b9`。

上一轮 UI 交互整改已经完成，旧任务不再保留在本文件中，历史由 Git 追溯。本轮仅处理仍沿用旧交互方案的“定位到当前章节 / 返回当前段落”等悬浮定位操作，不扩散到播放、缓存、规则或其它业务逻辑。

当前已确认的旧实现特征：

- 页面使用 `App.Button.Floating` 作为外层 Button。
- Button.Content 再嵌套 `App.Surface.FloatingAction`。
- `App.Surface.FloatingAction` 通过 Ancestor Button 的 `IsMouseOver` / `IsPressed` 监听交互状态。
- Hover 通过 `Surface.Secondary + Elevation.Medium` 表达，Pressed 切换为 `Accent.Subtle`，与当前统一的 `Interaction.Surface.*` / `Interaction.Foreground.*` 状态语言不一致。

本轮终态：

- 悬浮定位操作仍保留“浮在滚动内容之上”的可发现性，但由 Button family 自己拥有完整视觉与交互状态。
- 建立单一 `App.Button.FloatingIcon`；不再使用“Button + 内部交互 Surface”双 owner 结构。
- Rest 使用弱 Raised Surface、Subtle Border 和固定 Low Elevation；Hover 使用 `App.Brush.Interaction.Surface.Hover` / `Interaction.Foreground.Hover`；Pressed 使用 `App.Brush.Interaction.Surface.Pressed` / `Interaction.Foreground.Pressed`；Keyboard Focus 独立表达。
- Hover 不再提升阴影等级，Pressed 不再切换为 `Accent.Subtle`。
- 页面只负责位置、Visibility、Command/Click、Tooltip、AutomationName 和图标语义，不声明 Hover/Pressed/Focus 视觉状态。
- 迁移完成后删除无生产用途的 `App.Button.Floating` / `App.Surface.FloatingAction`，不得长期保留并行旧实现或兼容别名。

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
2. 开始前至少阅读：`AGENTS.md`、`docs/09_TESTING_AND_QUALITY.md`、`docs/10_ENGINEERING_CONVENTIONS.md`、`docs/13_VISUAL_DESIGN_SYSTEM.md`、`Buttons.xaml`、`Surfaces.xaml` 及当前任务涉及的页面和 WPF 测试。
3. 不改变定位按钮的业务行为、显示条件、命令、滚动/定位逻辑和 Automation 语义。
4. 优先修正公共 Button owner，再迁移页面调用方；不得在页面局部增加 Trigger、透明遮罩、负 Margin、不对称偏移或其它补丁式视觉修复。
5. 新交互颜色只能使用现有语义 Brush；若确实缺少语义资源，先证明缺口，再在 Palette 公共层补齐，不写页面硬编码色。
6. 悬浮按钮只有一个可见交互 owner。Surface family 不得通过 Ancestor Button 绑定来承担 Button 的 Hover/Pressed/Focus 状态。
7. Mouse Hover/Pressed 与 Keyboard Focus 分离；Dark Mode Pressed 图标必须保持可读，不回落到 Provider 黑色/低对比度前景。
8. Rest 的悬浮感由位置、弱 Raised Surface 和固定轻阴影表达；Hover 不通过增大阴影制造“突然抬高”，Pressed 不使用 Accent 作为普通按压状态。
9. 若采用轻微 Pressed 缩放，必须保证命中区、布局包络和周围裁切不变化，并尊重减少动画设置；若无必要，优先只通过 Surface/Foreground 表达。
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
