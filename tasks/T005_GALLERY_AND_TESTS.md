# T005 — 修正 Style Gallery 状态验证并补充最少量自动检查

**优先级：** P1  
**类型：** Visual verification / Architecture tests  
**前置：** T002、T003、T004

## 目标

让 Style Gallery 和自动验证能发现真实的按钮状态问题，而不是仅证明控件上的 `Background` 属性被设置。保留轻量测试策略，不引入全面的像素截图基线。

## Style Gallery

检查 `GallerySceneBuilders.cs`、`GalleryMenusScene.cs`、`GalleryMediaControlBar.cs`、主题选择器与截图生成流程。

当前 Gallery 的 `ApplyButtonPreviewState` 使用 Gallery 附加属性和临时 Style Trigger 模拟状态，可能直接设置 `Background` / `Foreground`，这与 Wpf.Ui 模板真实读取 `MouseOverBackground` / `PressedBackground` 的路径不等价。

实施要求：

1. 覆盖所有共享按钮样式及领域按钮样式，包括 Primary、Secondary、Subtle、Icon、Danger、DangerIcon、ContextAction、Media Button、ToolbarControl、Navigation Entry，以及菜单危险项。
2. 明确区分“语义资源/静态状态预览”和“真实 WPF 输入状态验证”；不能将只设置控件 Background 的静态样本标记为真实 Hover/Pressed 验收。
3. 优先复用真实 Wpf.Ui 控件模板。需要展示真实 Hover / Pressed / Keyboard Focus 时，通过 Gallery 的真实交互或隔离 Desktop 自动化驱动输入，不复制 Provider 完整模板来制造假状态。
4. 可以保留方便对比的静态状态矩阵，但必须通过 Wpf.Ui 对应状态属性表达其语义，并明确它的覆盖范围；如无法让静态矩阵真实进入模板状态，不应声称它验证了真实输入路径。
5. High Contrast：只有在 Gallery 可以应用真实 Windows High Contrast 资源而不伪造系统颜色时才增加完整场景；否则以资源映射检查和适用的运行时验证覆盖，不要建立一个看似真实但实际是模拟色值的 HC 主题。
6. 生成的截图与 manifest 属于临时视觉复核产物，任务完成前清理；不加入 Git。

## 自动测试

遵守 `docs/08_QUALITY_AND_TESTING.md`。只增加能保护稳定架构或可访问性合同的少量测试。

建议覆盖：

- 一项静态架构检查：共享可视按钮的状态 owner 遵守 Wpf.Ui 状态属性合同；不得只测试某个 Style Key 的顺序、Setter 顺序或具体 BasedOn 路径。
- 一项语义颜色检查：Light / Dark 中关键前景/背景配对满足对比度门槛；High Contrast 资源使用正确系统角色配对。不要冻结所有颜色 Hex 值。
- 如需确认模板优先级，可增加隔离 Desktop 的临时真实输入测试，实际触发 Hover / Pressed / Keyboard Focus 并观察用户可见结果。优先检查稳定的最终效果，不把私有 Visual Tree 结构作为断言。
- 若真实输入测试不能稳定隔离当前 Desktop，必须 fail closed；不得设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1` 作为绕过。

测试范围要覆盖共享样式架构与一个代表性真实 Wpf.Ui 模板路径；不要为每个页面、每种颜色、每个像素新增永久测试。临时复现测试完成后删除；只有符合长期测试准入标准的架构/对比度检查才保留。

## 验收

- Gallery 不再把“控件属性被设值”误当作“Wpf.Ui 模板状态正确”。
- 所有样式族均在矩阵或明确的交互场景中出现；遗漏项有原因。
- 真实模板状态得到代表性验证；测试不显示在用户当前 Desktop。
- 永久测试数量克制、断言稳定；临时截图、脚本、fixture 和测试已清理。
