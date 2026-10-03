# T027：收敛 UI 重复资源并核实 Gallery-only 样式

## 目标

减少重复 WPF converter/template 声明，并基于实际设计系统用途决定 Gallery-only 输入样式是否应保留。

审计依据：S08 / I06 / I07。相关合同：`docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 将重复 `BooleanToVisibilityConverter` 资源移至合适的 Application owner；验证 Window、Page、ContextMenu 中的 `StaticResource` 解析与 Light/Dark 首次创建行为。
- 验证 ComboBox Standard/Compact 的重复 string DataTemplate 查找结果；仅在运行行为等价后删除 Compact 副本。
- 查明 PasswordBox Standard/Compact、Compact ComboBox/CheckBox 是否代表受支持的设计系统合同。若无产品或已确认未来用途，连同专用 Gallery fixture 和 `ProviderStyleBridge` alias 一并删除；否则保留并记录用途。
- 不将资源 key 顺序、Visual Tree 形状或像素布局作为长期测试合同。

## 验收

- 隔离 WPF 验证覆盖受影响资源的解析、主题前景/状态与 Gallery 样例显示；不使用可见窗口绕过隔离。
- 删除的资源、fixture 和 bridge 无剩余 caller；保留的资源有明确 owner/用途。
- 执行相关 Presentation/WPF focused tests、format 和 Release build。

## 交付

移除被确认重复/无用途的资源与 fixture，更新 Backlog 并删除本规格。
