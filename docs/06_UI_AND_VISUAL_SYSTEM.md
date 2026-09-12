# UI 与视觉系统

## 1. 总体原则

NovelSpeaker 是桌面听书工具，UI 优先信息效率、清晰操作和稳定响应。

- 页面结构服务于内容，不用多层装饰容器替代信息层级。
- Light / Dark 保持相同布局和交互语义。
- 标准 WPF/Wpf.Ui 控件优先使用 Provider 正式能力。
- 页面状态与业务命令来自 ViewModel/Application owner；code-behind 只处理 WPF 特有生命周期与交互桥接。
- 大页面先可交互，再做次级 enrichment。

## 2. 页面信息架构

主窗口保留清晰一级入口：

- Library；
- Playback；
- Settings；
- 必要的长期后台状态入口。

设置首页为入口列表，子页保持扁平，不重复无意义分组标题和多层 Card。

Rules 页面采用统一工作台语义：列表 + 选择 + draft/editor + Save/Cancel；具体协议见专项规范。

## 3. 响应式 Library

Library 使用响应式多列卡片：

- 根据可用宽度决定列数和 card width。
- 常用窗口宽度下避免明显无意义空白。
- Row 内少量 Card，不建立第二套 virtualization。
- 外层列表使用 WPF 标准 recycling virtualization。
- resize 后保持稳定书籍顺序，并尽量保持逻辑 anchor 可见。

跨页面滚动恢复保存逻辑 anchor（例如 BookId），不保存自定义虚拟画布状态。

## 4. 超长列表

BookDetails、Player、CacheManagement 等连续 catalog 至少按 10,000 条设计：

- 用户侧不显式分页；
- 连续 scrollbar；
- current item 可直接定位；
- catalog 与 dynamic decoration 分离；
- selection 与 WPF container 分离；
- 首屏不使用 `Clear + N × Add`；
- WPF virtualization 负责 container/layout，应用负责轻量数据 projection 与有界更新。

不自行实现 ItemContainerGenerator、recycling protocol、scroll extent/viewport/offset 状态机。

## 5. Staged Loading 与性能

复杂页面：

```text
Critical
→ First Interactive Frame
→ Secondary Enrichment
→ Background Enhancement
```

首个交互帧只等待必要内容，例如 Header、轻量 catalog、当前阅读位置和主要动作。

禁止在首屏 Dispatcher 同步执行：

- 大规模 DTO→复杂 VM；
- 全量 cache decoration；
- 全量 sort/group/hash；
- 无界 locator/layout 循环；
- 同步等待后台任务。

真实性能耗时用于诊断和跨版本观察，不以脆弱的绝对毫秒阈值作为主要 CI 合同。

## 6. 视觉资源

资源分层：

```text
Wpf.Ui provider
→ NovelSpeaker palette
→ design tokens
→ provider style bridge
→ explicit App.* styles
→ shared controls
→ feature components
→ page-local layout
```

- Palette 保存语义颜色。
- Token 只保存跨多个组件稳定的 spacing/radius/icon/min-size/typography/animation。
- 页面专用几何不进入全局 Token。
- 应用级不为标准控件建立 NovelSpeaker 隐式样式接管。
- 标准控件完整 ControlTemplate 替换只允许局部、明确且有 WPF 合同测试的场景。

## 7. Shared Controls

全局 Shared 只包含真实跨 Feature 复用的应用自有 UI primitive，例如：

- AppPageHeader；
- Settings row/navigation row；
- form field；
- status view；
- section surface。

Book card、rule card、player content、cache chapter item 仍由 Feature 拥有。

## 8. Surface 与浮层

Dialog、Flyout、Popup、独立状态窗口遵守 **Single Surface**：

- 宿主已经提供完整 Surface 时，内容默认透明或弱分组。
- 不默认在 Popup/Flyout/Dialog 内再嵌完整 Raised Card。
- 四角不得因内外两层背景出现直角残影或双层边界。

诊断工具悬浮控制条属于明确的独立轻量工具 Surface，不嵌套多层 Card。

## 9. 主题与图标

- 支持 Light / Dark / System。
- System 跟随操作系统实际主题。
- 图标使用主题语义资源，不硬编码黑色/白色。
- Hover/Pressed/Focus 不得让 Dark Mode 图标失去可读性。
- 主题切换不通过运行时代码重写标准控件完整模板。
- Light/Dark 快捷切换保持高频可达。

## 10. 控件交互

- Icon Button 使用紧凑、可访问的 Tooltip/Automation Name。
- Slider thumb/track/filled track 保持几何连续。
- ToggleSwitch 的 focus/hit-test 范围与可见布局一致，不保留宽大隐藏点击区域。
- 列表 item 的 Hover/Selected/Current 状态尽量由单一视觉层表达。
- Esc 优先由局部临时交互消费，否则进入统一页面返回语义。
- Reduced Motion / 系统动画设置保持可用。

## 11. Diagnostics UI

### 性能遥测

Settings 中使用一行轻量入口：

- 性能遥测 Toggle；
- 清除遥测数据的 Icon Button；
- 导出诊断信息的 Icon Button；
- 少量说明文字。

不展示数据大小、最近采集时间、窗口数量等监控面板信息。

### 诊断工具

从 Settings 打开诊断工具后显示悬浮控制条。

准备状态：

```text
[开始诊断] [容量上限/次级设置] [关闭]
```

采集中：

```text
诊断中 + 时长
[标记问题] [截取当前窗口] [结束]
```

结束后：

```text
诊断已保存
[重新开始] [导出] [完成]
```

要求：

- 在用户点击“开始诊断”前不创建/采集诊断会话。
- “标记问题”表示问题发生在该时间点附近，可在问题即将发生或刚发生后点击。
- Marker、截图、立即导出都是增强功能，不是完成诊断的必选步骤。
- 达到硬容量上限后明确显示采集已停止，用户可以调整上限后重新复现。
- 不加入长时间录制提示、复杂问题分类或问题描述表单。
- 截图只截 NovelSpeaker 自身窗口，且必须由用户主动触发。

## 12. 自动视觉验收

WPF tests 重点保护：

- 资源作用域与主题；
- Provider style bridge；
- Focus/HitTest；
- 关键 Shared Control；
- Popup/Dialog Single Surface；
- Light/Dark 图标可读性；
- Reduced Motion；
- 需要时对真实 View 使用确定性脱敏 fixture。

一次性截图、视觉脚本和 trace 在任务完成后删除。人工视觉验收始终是可选补充，不阻塞 Agent 完成任务。
