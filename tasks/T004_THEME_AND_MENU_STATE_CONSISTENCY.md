# T004 — 统一主题状态资源与菜单动作反馈

**优先级：** P1  
**类型：** Palette / MenuItem / 对比度  
**前置：** T002、T003

## 目标

统一所有按钮与菜单动作的状态语义和颜色配对，覆盖 Light、Dark、High Contrast。按钮使用 Wpf.Ui 的状态属性；MenuItem 继续使用适合其模板的高亮机制，但不得产生冲突或不可读的前景/背景组合。

## 主题与语义资源

检查 `Palette.Light.xaml`、`Palette.Dark.xaml`、`Palette.HighContrast.xaml`、`Buttons.xaml`、`Menus.xaml`、`Media.xaml`、`Navigation.xaml` 及相关控件模板。

按需要补充最少量的语义资源，尤其要区分：

- 实色危险背景上的前景；
- Subtle 危险背景上的前景；
- 危险操作 Hover 背景；
- 危险操作 Pressed 背景及前景；
- 普通按钮、强调按钮的 Hover / Pressed 配对；
- High Contrast 下由系统颜色定义的对应角色。

不要复用同一个 `Danger.Text` 去表示“实色危险背景上的文字”和“浅色危险背景上的文字”，除非在所有主题下实际验证其配对均正确。

## 状态规范

- Filled Danger：Default、Hover、Pressed 都保持明确的危险语义；Hover 不得从实色危险按钮跳变成普通正文色的浅色按钮。
- Danger Icon：可以在静止态保持中性外观，Hover/Pressed 进入危险强调状态；前景与背景必须配对。
- Danger Menu Item：默认危险文本；Hover 使用清楚可见的危险反馈；Pressed/Highlighted 的前景与背景不能使用不同语义色而导致对比度不足。
- Primary、Secondary、Subtle、Icon、ContextAction、Media Button、ToolbarControl、Navigation Entry 等所有变体都要检查，不能只验证 Danger。
- Focus 必须有键盘可见的焦点提示；Disabled 与正常状态明确区分，不用透明度混合制造未计算的颜色组合。

## Wpf.Ui 菜单模板

- 核实锁定 Wpf.Ui 版本的 `MenuItem.xaml` 及其 `IsHighlighted`、Pressed 和 Disabled 触发器。
- 不假定对 `MenuItem.Background` 的 Style Setter 一定控制最终渲染的内部 Border。
- 优先使用已有模板支持的资源入口或作用域资源。只有在确认没有可用 Provider 扩展点时，才考虑最小范围的自定义模板；不得复制整套 Wpf.Ui MenuItem 模板作为首选方案。
- 普通菜单项、危险菜单项、分组标题、ContextMenu 与 Rules 控件模板应采用一致的菜单状态 owner。

## High Contrast

按系统颜色的语义成对映射，例如：

- 普通 Control surface：`ControlColor` + `ControlTextColor`；
- Highlight / selected / pressed surface：`HighlightColor` + `HighlightTextColor`；
- 普通 Window surface：`WindowColor` + `WindowTextColor`。

不得把 `HighlightTextColor` 直接用作 `ControlColor` 背景上的前景，亦不得假定用户的 High Contrast 配色固定为某组 RGB 值。High Contrast 下不强制保留品牌红色；通过系统配色、图标和操作文本保留动作可识别性。

## 对比度验收

- Light / Dark 的普通文字前景与其真实背景对比度不低于 **4.5:1**。
- 对状态识别必要的图标、边框或控件图形满足适用的 **3:1** 非文本对比度要求。
- 逐项检查实际状态最终采用的前景/背景组合，不只检查 Palette 默认色。
- Disabled 控件有 WCAG 适用豁免，但仍须有可辨识的禁用状态。
- High Contrast 主要验证系统资源角色的配对关系；如使用运行时对比度测量，不能将单一系统配色的 RGB 值固化为长期合同。

## 验收

- 所有共享与领域按钮变体及危险菜单项的状态矩阵完整。
- 没有将实色按钮在 Hover 时变成普通文字浅底的语义跳变。
- Light / Dark 颜色组合通过对比度计算；High Contrast 使用系统语义成对映射。
- 未新增完整复制的 Provider Button/MenuItem 模板。
- 运行相关主题资源检查、菜单相关 focused tests 与 Release build。
