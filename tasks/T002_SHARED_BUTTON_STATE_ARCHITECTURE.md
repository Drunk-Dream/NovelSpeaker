# T002 — 统一共享按钮样式的 Wpf.Ui 状态机制

**优先级：** P1  
**类型：** 共享样式架构与调用点迁移  
**前置：** T001

## 目标

从共享样式层消除标准 WPF `Button` 样式触发器与 Wpf.Ui 模板状态机制之间的冲突。不要以新增更多 `Background` 触发器或复制 Wpf.Ui 模板的方式修补截图。

## 权威合同

- `docs/06_UI_AND_VISUAL_SYSTEM.md` 的 Button 样式语义与状态 owner 合同。
- `docs/08_QUALITY_AND_TESTING.md` 的测试准入合同。
- T001 的 `BUTTON_STYLE_AUDIT_REPORT.md`（若存在）；实现前仍须核实真实源码与调用点。

## 实施范围

至少处理以下共享样式族及其全部调用点：

- `App.Button.Primary`
- `App.Button.Secondary`
- `App.Button.Subtle`
- `App.Button.Icon`
- `App.Button.Danger`
- `App.Button.DangerIcon`
- `App.Button.ContextAction`
- `App.Button.InteractionHost`（确认并保留其例外边界）

### 统一规则

1. 具有自身可见 Hover / Pressed / Focus / Disabled 外观的普通操作按钮，统一使用 `ui:Button`，基于 `Provider.UiButton` 或以它为基础的共享语义样式。
2. 将当前仍以 `System.Windows.Controls.Button` 为 TargetType 的普通视觉样式迁移到 `ui:Button`，并在同一个任务中迁移所有使用这些样式的调用点，避免留下 TargetType 不匹配或临时兼容层。
3. 对 Wpf.Ui 4.3.0（如 T001 核实的实际锁定版本仍为该版本）的普通状态，优先设置：
   - Default：`Background`、`Foreground`、`BorderBrush`；
   - Hover：`MouseOverBackground`、`MouseOverBorderBrush`；
   - Pressed：`PressedBackground`、`PressedBorderBrush`、`PressedForeground`。
4. 可视 `ui:Button` 样式中，不得用 `IsMouseOver` / `IsPressed` 触发器直接设置 `Background` 或 `BorderBrush` 来覆盖 Wpf.Ui 状态属性。若框架没有 Hover 前景属性，允许用窄范围的 `IsMouseOver` 触发器只改变 `Foreground`；Pressed 前景优先使用 `PressedForeground`。
5. Focus 使用 Provider 已支持的 FocusVisualStyle 或明确的 Focus chrome，不以改变背景作为唯一焦点提示。
6. Disabled 必须以实际 Wpf.Ui 模板为准验证；不能假定控件上的 `Background` / `Foreground` Setter 一定胜过模板内的 Disabled trigger。避免为此复制整套模板。
7. 保留现有尺寸、触控命中区域、图标、Tooltip、Automation Name、命令与绑定语义。
8. `App.Button.InteractionHost` 是明确例外：它只负责命令、键盘焦点和命中区域，透明模板不绘制第二层状态 Surface，父级 Selection/Surface 是唯一视觉状态 owner。不得为了“统一”而给它添加可见 Hover/Pressed 填充。
9. 不为旧 Style Key 长期保留 alias；完成迁移后删除不再使用的旧实现。

## 重点核查

- `Buttons.xaml` 中所有共享样式，不只 `Danger`。
- `ProviderStyleBridge.xaml` 的 alias 职责是否仍清晰；不要把应用语义塞进 Provider Bridge。
- Style Gallery 的按钮样式 TargetType 和动态构造调用点是否因类型迁移失效。
- 所有被迁移样式的 XAML 调用点；不能只修改 Style 定义。
- 所有状态触发器中是否仍有争夺同一可视元素的 Background / BorderBrush owner。

## 不在本任务中

- 不修改删除、保存、选择、导航或播放命令的业务逻辑。
- 不全面重写 MenuItem；菜单在 T004 处理。
- 不复制 Wpf.Ui 完整 `ControlTemplate`。
- 不引入新的万能 Button 控件、事件系统或全局运行时模板改写。

## 验收

- 共享视觉按钮样式已使用 Wpf.Ui 状态属性；`InteractionHost` 的例外原因与行为保持明确。
- 受影响调用点全部迁移，XAML TargetType 一致。
- 不存在为了修复背景状态而复制 Provider 模板的新增代码。
- 运行相关 Presentation/WPF focused tests、XAML 资源加载检查及 Release build。
- 更新临时审查报告中的“已实施/剩余问题”部分，但不要提交该报告。
