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

章节规则、正则规则等 Rules 页面采用统一工作台语义：列表 + 选择 + draft/editor + Save/Cancel。

“语音服务”页面同样使用桌面双栏工作台，但 Provider 不是普通 Rule：左侧统一管理 Provider Instance，右侧由 Provider Type 决定具体 Editor。

## 3. 语音服务管理页

桌面端保持：

```text
Provider 列表 | Provider Editor Host
```

规则：

- Provider 不按 HTTP / Edge / Local 分组。
- Provider 列表读取一份统一 SortOrder。
- 点击左侧项表示“正在编辑”，使用普通 Selection 视觉。
- CurrentProvider 是独立状态，只使用轻量状态图标/标识，不显示“当前”文字，也不允许通过该标识切换 Provider。
- Provider 没有 Enabled Toggle。
- HTTP Provider 支持复制、单条导出、删除等类型适用动作。
- Microsoft Edge 不显示创建、复制、导入、导出、删除等不适用动作。
- 页首提供“新建”和“导入”；当只有 HTTP 可由用户创建时，不显示无意义 Provider Type 选择器。
- HTTP 模板帮助只属于 HTTP 编辑器语境。
- Provider 编辑器之间共享 Draft/Dirty/Save/Cancel/Test 生命周期，不强行共享字段布局。
- 新建 HTTP Provider 先进入右侧 Draft，保存成功后才进入左侧列表。
- 切换左侧 Provider 或离开页面时，Dirty Draft 使用保存 / 放弃 / 取消保护。

## 4. 播放页 Provider 选择器

Provider 选择器是高频操作入口：

- 只显示已完成必要配置且当前可见的 Provider。
- 只显示名称，不显示 Provider Type 副标题。
- 不提供“None / 不使用语音服务”选项。
- Provider 顺序与管理页完全一致。
- 每个可选择项等宽并横向 Stretch，几乎占满浮窗内容区；浮窗保留合理内边距。
- 整行都是点击区域，不只让文字本身可点击。
- 当前 Provider 使用与目录 Current Item 相同的整项选中视觉，不再显示“当前”文字。
- CurrentProvider=None 时没有任何项呈选中状态。
- 底部“前往语音服务管理”是独立导航动作，与 Provider 项保持视觉间距。

## 5. 拖拽排序

所有支持手动排序的列表统一使用“插入槽位”语义，而不是目标卡片 Before/After 两套边界。

```text
────────  slot 0
Item A
────────  slot 1
Item B
────────  slot 2
```

要求：

- N 个当前可见 item 只有 N+1 个可见插入槽。
- 插入指示横线位于两张卡片的 gap 中，不压在卡片上/下边界。
- 相邻卡片之间只有一个候选位置，A.After 与 B.Before 不得重复表示同一点。
- 列表顶部和底部也各有一个唯一槽位。
- 保留已有边缘自动滚动等成熟拖拽体验。
- Provider、章节规则、正则替换规则等现有排序列表统一采用这一交互原则。

Provider 存在隐藏项时，可见列表只是完整排序的投影：

- 隐藏 Provider 不因为隐藏而主动改序。
- 拖到两个可见 Provider 之间时，按可见插入槽映射回完整排序。
- 用户显式拖动其它 Provider 可以跨过隐藏 Provider，因此隐藏 Provider 与其它项的相对关系可能随真实重排改变。
- 隐藏 item 不产生用户看不见的额外命中边界。

## 6. 响应式 Library

Library 使用响应式多列卡片：

- 根据可用宽度决定列数和 card width。
- 常用窗口宽度下避免明显无意义空白。
- Row 内少量 Card，不建立第二套 virtualization。
- 外层列表使用 WPF 标准 recycling virtualization。
- resize 后保持稳定书籍顺序，并尽量保持逻辑 anchor 可见。

跨页面滚动恢复保存逻辑 anchor（例如 BookId），不保存自定义虚拟画布状态。

## 7. 超长列表

BookDetails、Player、CacheManagement 等连续 catalog 至少按 10,000 条设计：

- 用户侧不显式分页；
- 连续 scrollbar；
- current item 可直接定位；
- catalog 与 dynamic decoration 分离；
- selection 与 WPF container 分离；
- 首屏不使用 `Clear + N × Add`；
- WPF virtualization 负责 container/layout，应用负责轻量数据 projection 与有界更新。

不自行实现 ItemContainerGenerator、recycling protocol、scroll extent/viewport/offset 状态机。

## 8. Staged Loading 与性能

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

## 9. 视觉资源

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

## 10. Shared Controls

全局 Shared 只包含真实跨 Feature 复用的应用自有 UI primitive，例如：

- AppPageHeader；
- Settings row/navigation row；
- form field；
- status view；
- section surface。

Book card、Provider card/editor、rule card、player content、cache chapter item 仍由 Feature 拥有。

共享拖拽排序只抽象稳定的“插入槽位/重排交互”，不把不同业务项的数据模型合并成万能列表模型。

## 11. Surface 与浮层

Dialog、Flyout、Popup、独立状态窗口遵守 **Single Surface**：

- 宿主已经提供完整 Surface 时，内容默认透明或弱分组。
- 不默认在 Popup/Flyout/Dialog 内再嵌完整 Raised Card。
- 四角不得因内外两层背景出现直角残影或双层边界。

诊断工具悬浮控制条属于明确的独立轻量工具 Surface，不嵌套多层 Card。

## 12. 主题与图标

- 支持 Light / Dark / System。
- System 跟随操作系统实际主题。
- 图标使用主题语义资源，不硬编码黑色/白色。
- Hover/Pressed/Focus 不得让 Dark Mode 图标失去可读性。
- 主题切换不通过运行时代码重写标准控件完整模板。
- Light/Dark 快捷切换保持高频可达。

## 13. 控件交互

- Icon Button 使用紧凑、可访问的 Tooltip/Automation Name。
- Slider thumb/track/filled track 保持几何连续。
- ToggleSwitch 的 focus/hit-test 范围与可见布局一致，不保留宽大隐藏点击区域。
- 列表 item 的 Hover/Selected/Current 状态尽量由单一视觉层表达。
- Esc 优先由局部临时交互消费，否则进入统一页面返回语义。
- Reduced Motion / 系统动画设置保持可用。

## 14. Diagnostics UI

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

## 15. 自动视觉验收

WPF tests 重点保护真正影响核心可用性的边界，不为样式微调建立永久测试。

对于 Provider 选择器、拖拽插入线、CurrentProvider 状态图标等视觉任务，可以使用任务内临时测试、Style Gallery 或截图进行验证；通过后必须删除临时截图、脚本和测试。

人工视觉验收始终是可选补充，不阻塞 Agent 完成任务。
