# T003 — 收口领域按钮样式与全项目调用点

**优先级：** P1  
**类型：** 领域样式与调用点审查  
**前置：** T002

## 目标

将共享按钮状态机制扩展到所有领域按钮样式和生产调用点，确保没有遗漏的普通视觉按钮继续依赖冲突的 WPF Button 状态路径或隐式默认样式。

## 调查与实施范围

至少检查并按实际代码扩展：

- `App.Media.Button`
- `App.Media.ToolbarControl`
- `App.Navigation.SettingsEntry`
- 其它 `App.*` 中 TargetType 为 `Button` / `ui:Button` 的样式；
- 所有页面、Window、Dialog、ControlTemplate、DataTemplate 中的 `<Button>` 与 `<ui:Button>`；
- C# 中动态创建的按钮；
- Metadata、Chapter、Regex、Speech Provider 等编辑器的保存/取消/试听/新增/删除操作；
- `NovelSpeaker.StyleGallery` 的全部按钮 fixture；
- 未显式设置语义 Style 的用户可见按钮。

## 实施规则

1. 除明确的 InteractionHost 或经证明拥有独立视觉模型的专用控件外，用户可见的普通操作按钮应使用 `ui:Button` 和显式语义样式（`App.Button.*` 或必要的 `App.Media.*` / `App.Navigation.*`）。
2. 将仍使用标准 `Button` 作为普通视觉控件的领域样式迁移到 Wpf.Ui 状态属性机制；对应调用点在同一任务中迁移。
3. `App.Media.Button`、`App.Media.ToolbarControl` 等已使用 `ui:Button` 的样式，也要检查其 Hover / Pressed / Focus / Disabled 处理是否依赖会被模板覆盖的 `Background` / `BorderBrush` 触发器。
4. `App.Navigation.SettingsEntry` 应按实际职责决定是否迁移为 `ui:Button`；若保留标准 WPF Button，必须有明确的专用模板/状态 owner 证据，不能只因历史上使用它而保留。
5. 每个可视按钮必须有明确语义：
   - Primary：主要确认/保存；
   - Secondary / Subtle：次要、取消或低强调动作；
   - Icon：图标动作；
   - Danger / DangerIcon：确实具有破坏性或高风险的操作。
   不得仅因“关闭窗口”或“退出页面”就自动归为 Danger。
6. Metadata 编辑器的保存、取消按钮必须显式使用语义样式，并与 Chapter / Regex 编辑器的交互层级一致。
7. 对剩余未显式指定 Style 的按钮逐个归类：补充语义样式，或记录为何应保留 Provider 默认/专用样式。不得通过全局隐式样式接管所有 WPF Button。
8. 保留现有 Command、CommandParameter、Tooltip、AutomationProperties、键盘导航、控件尺寸和命中区域。
9. 不为每个页面新增同义的局部 Style；优先复用已有共享/领域样式。

## 特殊控件与例外

- 如果某个控件的可见 Hover / Pressed 由外层 Selection/Surface 管理，则 Button 本身不再绘制第二层 Surface。
- Settings Navigation Row、SelectionSurface 内部命中按钮等应区分“真正的按钮外观”与“仅命令/命中宿主”。
- 所有例外必须在 `BUTTON_STYLE_AUDIT_REPORT.md` 中列出其职责、视觉 owner 和保留理由；不新增无边界的例外类别。

## 验收

- 全项目扫描的普通可视按钮均有明确的语义样式或有证据的专用控件样式。
- 不存在由于共享样式 TargetType 迁移造成的 XAML 类型错误、命令丢失或 Automation Name 遗失。
- 媒体工具栏、导航按钮、Metadata/Rules 编辑器、Library/Details、Cache、Speech Services 等调用点通过相关 focused tests 和 Release build。
- 报告中列出的例外与实际代码一致。
