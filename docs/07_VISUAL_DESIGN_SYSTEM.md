# 视觉设计系统

## 1. 定位

Wpf.Ui 提供标准 WPF/Wpf.Ui 控件的基础视觉，NovelSpeaker 通过 palette、token、具名 style、自有控件和 Feature 组件形成稳定产品视觉。

本文件只保存长期视觉合同，不保存逐任务截图、临时参数 A/B、Gallery 调试过程或单页一次性调整记录。

## 2. 核心原则

1. **内容优先**：页面视觉服务信息层级，不用装饰性容器堆叠替代布局。
2. **单一强调色**：强调色用于关键状态/主要动作，不让多个高饱和色竞争。
3. **Surface 层级优先于边框堆叠**：通过背景、间距、圆角和层级建立结构。
4. **Light/Dark 结构一致**：主题变化不改变布局和控件语义。
5. **单一资源 owner**：同一颜色、间距、圆角和控件样式只有一个正式来源。
6. **Wpf.Ui template owner**：标准控件完整模板默认由 Provider 所有。

## 3. 资源分层

```text
Wpf.Ui provider dictionaries
→ NovelSpeaker Palette
→ Design Tokens
→ Provider Style Bridge
→ App.* explicit styles
→ NovelSpeaker shared controls
→ Feature components
→ Page-local layout
```

### Palette

只保存语义颜色：Surface、Text、Border、Accent、State 等。

### Token

只保存跨多个组件稳定的：

- spacing；
- radius；
- icon size；
- minimum control size；
- typography scale；
- animation duration。

页面专用宽度、边距和分栏几何不进入全局 Token。

## 4. Style 规则

- 应用级不为标准 WPF/Wpf.Ui 控件定义 NovelSpeaker 隐式样式。
- 需要扩展 Provider 时使用显式具名 `App.*` style。
- 标准控件完整 `ControlTemplate` 替换只允许局部、明确、有专项 WPF 契约测试的场景。
- NovelSpeaker 自有 CustomControl 可以拥有默认样式键。
- Feature 内局部隐式样式不能逃逸到应用全局。

## 5. Shared Controls

全局 Shared 控件只包含真实跨 Feature 复用的应用自有 UI，例如：

- `AppPageHeader`；
- Settings list/row/navigation row；
- form field；
- status view；
- section surface；
- 其它已证明跨业务域复用的 presentation primitive。

Book card、rule card、player content、cache chapter item 等仍由 Feature 拥有。

## 6. Surface 与浮层

Dialog、Flyout、Popup、独立状态窗口遵循 Single Surface。

- ContentDialog 本身已是主 Surface 时，内容默认透明/弱分组。
- Popup/Flyout 不再默认嵌套完整 Raised Card。
- 只有存在明确二级信息分组时才使用弱背景/Divider。
- 四角不得因内外两层背景形成直角残影或双层边界。

## 7. 图标与主题

- 图标颜色使用主题语义资源，不硬编码黑色/白色。
- Hover/Pressed/Focus 状态不能把 Dark Mode 图标变为不可读的黑色。
- Theme 切换只更新 Wpf.Ui 主题和 palette，不运行时重写标准 Style/ControlTemplate 类型资源。
- System 模式跟随操作系统 Light/Dark。

## 8. 按钮和输入

- 普通 IconButton Hover 使用圆角状态层，避免突兀方形边框。
- Click/Pressed 不因 thumb/icon 放大造成裁切或遮挡。
- Slider thumb、track、filled track 保持几何连续，不出现因填充收缩产生的视觉断裂。
- ComboBox/下拉行保持整行可点，文字左、指示图标右。
- ToggleSwitch 可点击/focus 范围应与可见布局一致，不保留宽大隐藏 hit area。

## 9. 列表与选择

- 列表 item 的 Hover/Selected/Current 状态由单一视觉层表达，避免内外两层悬浮背景。
- Current 与 Selected 语义可同时存在但视觉层级要清楚。
- 大列表使用 virtualization 时，视觉状态不得依赖 container 长期存在。

## 10. Rules 工作台

- 左侧规则卡片采用信息区 + ToggleSwitch 的左右结构。
- 不显示冗余 `⋮` 按钮；单项动作使用 ContextMenu。
- 拖动排序使用插入线和轻量拖动态，不做复杂邻项动画。
- 编辑区只在用户选择规则后显示编辑内容。

## 11. Settings

- Settings 首页和子页使用统一的 AppSettings* 控件族。
- 子页不重复无意义分组标题和多层卡片。
- 导航入口使用 icon + title + chevron。
- 页面级几何由页面 owner 管理。

## 12. Book / Player / Cache

- 书库卡片使用响应式宽度，不在常用窗口尺寸留下明显无意义空白。
- BookDetails/Player 的长目录优先内容密度和清晰 current 状态。
- CacheManagement 条目统一使用页面可用宽度，不在右侧再嵌套重复面板。
- MiniPlayer 只保留媒体控制相关信息，避免复制主播放页复杂 UI。

## 13. Style Gallery

Style Gallery 是开发工具：

- 展示正式资源/控件族；
- 不进入生产导航/发布包；
- scene 按稳定资源族命名，不使用 backlog 任务编号；
- Gallery 不复制正式页面。

正式页面视觉验收必须实例化真实 View 和确定性脱敏 fixture。

## 14. 自动验收

WPF tests 重点验证：

- 资源作用域；
- 主题切换；
- Provider style bridge；
- Focus/HitTest 范围；
- 关键 shared control template；
- Popup/Dialog single-surface 合同；
- Light/Dark 图标可读性；
- Reduced Motion 路径。

不把像素截图哈希作为默认完整测试门禁。

## 15. 禁止项

- 应用级标准控件隐式样式接管。
- 为统一视觉复制 Wpf.Ui 默认模板。
- 页面局部尺寸塞进全局 Token。
- Dialog-in-Card / Flyout-in-Card 默认双层 Surface。
- Dark Mode 硬编码黑色图标。
- 为临时截图创建生产控件副本。
- 在稳定设计文档保存逐任务视觉调试日志。
