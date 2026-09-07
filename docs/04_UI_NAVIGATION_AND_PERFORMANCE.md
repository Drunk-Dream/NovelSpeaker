# UI、导航与性能

## 1. 总体原则

- 桌面应用优先信息效率和可操作性。
- 页面结构与视觉样式分离：本文件定义交互/布局/性能合同，视觉 Token 和 Surface 见 `07_VISUAL_DESIGN_SYSTEM.md`。
- WPF code-behind 只处理控件生命周期、focus、drag/drop、scroll、virtualization、animation 和事件桥接。
- 业务状态与命令由 ViewModel/Application owner 提供。
- WPF 已稳定提供的控件生命周期、container generation、scroll/virtualization 机制优先直接使用或轻量组合，不在 Feature 内重建并行框架状态机。

## 2. 主窗口信息架构

主窗口保留清晰一级导航：

- 书库；
- 播放；
- 设置；
- Shell Footer 中的长期后台状态入口。

不建立浏览器式多层历史导航。

## 3. 强类型导航

应用级导航只维护完整 `CurrentRoute`。

普通页面固定父级：

- BookDetails → Library；
- Settings 子页 → Settings；
- RegexReplacementRules → ImportTextSettings；
- CacheManagement → CacheAndData。

Player 唯一使用一次性动态 `ReturnRoute`：

- Library → Player → Library；
- BookDetails(book) → Player → 同一 BookDetails(book)；
- Shell 正在播放入口捕获进入前完整 route。

`ReturnRoute` 不指向另一个 Player，不形成递归历史链。

PageHeader、Alt+Left、可用 Esc 使用统一 `NavigateBackAsync`。

## 4. Page/ViewModel 生命周期

普通页面 transient。Navigation cache/Page singleton 不用于恢复页面参数、编辑状态或规避加载成本。

需要跨页面存在的状态必须属于明确 process/session owner。

## 5. 书库

- 响应式书籍卡片布局根据可用宽度决定列数和卡片宽度。
- 大书库的排序/过滤先基于 read model 计算，再批量提交 UI projection。
- 当前活动书籍只根据 matching PlaybackSnapshot 更新对应卡片，不重新查询整个 library。
- 卡片不承担 Playback session owner 职责。
- 冷启动首次进入、空书库后异步导入、页面返回都必须使用同一正常 ItemsSource/布局路径，不依赖二次导航、固定延时或强制刷新才能显示。

### 5.1 响应式 Row Projection

Library 的响应式布局只负责轻量 presentation 计算：

```text
visible books
    ↓
available width
    ↓
column count + card width
    ↓
row grouping
```

保持现有视觉约束：

- `MinItemWidth` 约 300；
- `MaxItemWidth` 约 360；
- 水平/垂直间距约 16；
- 不完整的最后一行保持与前面行相同左对齐基线。

实现使用类似 `LibraryBookRowProjection` 的轻量 row model。Row 内只包含少量 Card，不为 Row 内再次建立独立虚拟化系统。

### 5.2 标准 WPF 行级虚拟化

目标 UI 结构：

```text
standard ItemsControl/ListBox
└─ VirtualizingStackPanel (Recycling)
   ├─ Row
   │  ├─ BookCard
   │  ├─ BookCard
   │  └─ BookCard
   ├─ Row
   └─ ...
```

要求：

- outer ItemsControl/ListBox 自己拥有标准 ScrollViewer/scroll contract；
- 使用 WPF `VirtualizingStackPanel` 对 Row 做 recycling virtualization；
- 不由 Library Feature 直接调用 `ItemContainerGenerator.GenerateNext/Recycle`；
- 不自行维护 realized start/count、extent、viewport、offset 等 WPF 内部状态；
- 不实现与标准 panel 并行的 `IScrollInfo` forwarding/state machine；
- 10,000 本书时 UI container 数量应主要随 viewport row 数增长，而不是随书籍总数增长。

### 5.3 Scroll State

跨页面保留滚动位置时保存逻辑 anchor，而不是保存自定义虚拟画布状态：

```text
AnchorBookId
    ↓
row projection lookup
    ↓
RowIndex
    ↓
standard ScrollIntoView / BringIntoView
```

如需更精确恢复相对顶部偏移，可在目标 Row 已由 WPF realization 后做一次有界相对调整。

禁止：

- 固定 Dispatcher Delay；
- 多轮无界/高次数 retry；
- 根据自维护 item height/extent 模拟整张虚拟画布；
- 为滚动恢复重新接管 WPF generator 生命周期。

resize 导致列数变化时：

- 重新计算 row projection；
- 保持书籍顺序；
- 尽量保持当前 `AnchorBookId` 可见；
- 不保留已经失效的旧 row/container identity。

## 6. 书籍详情

### Critical

- Header；
- 轻量章节 catalog；
- 当前阅读位置；
- 立即可用的编辑/播放入口。

### Secondary

- 当前章节自动定位；
- viewport/current cache decoration；
- 次级统计。

### Background

- 非可视区域 enrichment；
- 可延后维护信息。

BookDetails 不通过全量 mutable item 状态阻塞首帧。

## 7. 播放页

- XAML 绑定单一 PlayerViewModel。
- 章节目录、正文和播放控制的 presentation controller 职责分离。
- current chapter/segment 与 PlaybackSnapshot 保持一致。
- 手动切换章节/段落后目录与正文定位使用显式 interaction controller。
- 用户主动定位优先于后台 decoration。

## 8. 超长章节目录

目标支持至少 10,000 章连续目录：

- 不显式分页；
- scrollbar 连续；
- current item 可直接定位；
- WPF Recycling virtualization；
- selection 与 container 分离；
- catalog 和 dynamic decoration 分离。

禁止以 `Clear + N × Add` 作为大型列表首屏提交方式。

## 9. Data virtualization 与 UI virtualization

WPF virtualization 只减少可视 container/layout 成本，不能消除：

- DTO materialization；
- item object 构造；
- collection notification；
- full-list status projection；
- sort/group/hash。

因此大型页面必须同时控制数据和 presentation 工作量。

UI virtualization 的职责优先交给 WPF 标准虚拟化控件。应用负责：

- 提供轻量、稳定的数据 projection；
- 提供 O(1)/有界 lookup；
- 控制批量 notification；
- 控制 dynamic decoration 范围。

应用不负责重新实现框架本身的：

- item container generator；
- container recycling protocol；
- scrolling extent/viewport；
- layout invalidation state machine。

## 10. Staged Loading

复杂页面统一：

```text
Critical
→ First Interactive Frame
→ Secondary Enrichment
→ Background Enhancement
```

要求：

- 首个交互帧之前只做必要工作；
- enrichment 与 locator 不挤在同一 Dispatcher 长工作窗；
- `Task.Yield()` 不等于 render boundary；
- 不用固定延时猜测首帧；
- 快速导航时旧阶段必须取消/失效。

## 11. Dispatcher 性能合同

Dispatcher 上应保持短、小、可中断到下一工作项的 UI 提交。

禁止在页面首屏同步执行：

- 大规模 DTO→复杂 VM；
- 全量 cache status property 更新；
- 全量排序/grouping；
- 无界 layout/locator 循环；
- 同步等待后台任务。

性能 CI 优先结构性/顺序测试，不建立脆弱的绝对毫秒门槛。

## 12. Cache 页面与感知实时刷新

Cache UI 的产品合同是“页面 active 时自动追上真实缓存状态”，不要求每个 cache entry 提交后立即刷新一遍 UI。

实现原则：

- 连续 Cache mutation 通过短窗口合并形成用户感知实时刷新；具体 debounce/coalescing 毫秒数不是固定产品合同；
- `CacheAndData` 的总大小、条目数、使用率等 global overview 在 active 时自动更新；
- `CacheManagement` 左侧书籍 summary 只刷新受影响 Book；
- 当前书章节的物理统计只刷新受影响 Chapter；
- Coverage 只刷新 current/viewport/明确受影响章节，不随单个 mutation 全量扫描整书；
- 同一刷新在 in-flight 时再次失效，使用 dirty/retry 语义，不并发堆积相同查询；
- cache status/read model 不通过逐项目 N+1 查询形成高频刷新路径。

CacheManagement 额外遵守：

- 页面 VM transient；
- filter/selection 属于页面 state；
- active cache/export 属于 process owner；
- 列表支持大数据集批量 projection；
- selection 使用 chapter id/index，与 WPF container 和 cache decoration 解耦；
- 新章节首次出现缓存时保留现有有效选择，新章节默认未选中；
- 章节最后一条缓存消失时，只移除该章节及其无效选择，不清空其它选择；
- catalog reconciliation 不以 `Clear + Add` 或无条件 `resetSelection` 处理普通 cache mutation；
- PageHeader 提供清理/导出动作和已选数量。

页面离开只取消页面查询/选择/live projection，不取消已提交后台批次。重新进入以当前 read model 为基线，不依赖离开期间逐事件补发。

## 13. Rules 工作台

三类规则共享：

- 左侧规则列表；
- selected item；
- draft/dirty；
- Save/Cancel；
- reorder；
- import/delete 交互。

业务模型、validation、试听/预览仍各自独立。

规则页面进入时不自动打开编辑对象。右键菜单处理单项导出/删除/排序相关动作；页面级导入放 PageHeader。

## 14. Settings

- 首页为设置入口列表。
- 子页 VM transient。
- 即时设置直接更新 process settings；草稿型设置在保存前保留页面 draft。
- Light/Dark 高频切换除完整外观设置入口外提供快捷入口；System 模式的实际 Light/Dark 显示按系统主题解析。

## 15. Mini Player 与托盘

- MiniPlayer 只投影共享 Playback state，不创建第二套状态机。
- 恢复主窗口与退出应用是不同动作。
- 托盘只负责桌面生命周期入口，不承载业务状态。

## 16. Dialog / Flyout / Popup

遵守 Single Surface：宿主已经提供完整浮层 Surface 时，内部内容默认不再嵌套第二个完整 Card/Raised Surface。

详细视觉规则见 `07_VISUAL_DESIGN_SYSTEM.md`。

## 17. 键盘与可访问性

- Tab/方向键/focus 范围与可见控件一致。
- 不允许 ToggleSwitch 等控件拥有远大于可视内容的隐藏点击区域。
- Esc 先由局部临时交互消费，否则进入统一页面返回语义。
- Reduced Motion / 系统动画设置必须保持可用。

## 18. 性能验收规模

架构重构后使用至少：

```text
180
1000
3000+
10000 chapters
```

并覆盖 current index：开始/中部/尾部。

验证：

- Library cold start / async initial projection；
- Library resize / row regroup / scroll restore；
- Library → BookDetails；
- Player → BookDetails；
- first interactive frame；
- Dispatcher responsiveness；
- locator；
- cache decoration；
- continuous scrolling；
- memory/allocation；
- SQLite query/materialization。

此前 Player → Back → BookDetails 的 3000+ 章节返回卡顿已确认解决，后续仅作为真实规模性能回归场景保留；只有稳定复现退化时才重新进入 profiling，不预设专项 workaround。
