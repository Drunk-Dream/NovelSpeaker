# T002 — 跨页面返回后 Library 逻辑锚点的复核与必要修复

## 1. 目标与授权

处理 U3-A/T005 的 **T005-LIB-02**：25 本虚构书目，最近阅读排序不变，在 1450×1000 的 Library 滚动至可视首行约为 15–17 后，经 `BookDetails → Player → 返回 Library`，可视首行变成 00–02。实验表明当时由 Shell 外层 ScrollViewer 滚动，而现有锚点组件监听内部列表；它们可能同根，**禁止未经 T001 修复后复测就单独重写状态恢复**。

先读 `AGENTS.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`（第 9 节 Library 逻辑 anchor）、`docs/02_RUNTIME_AND_NAVIGATION.md`（页面导航/激活与独立寿命）、`docs/08_QUALITY_AND_TESTING.md`。看 T001 实际改动与 U3-A/T005 原始证据。

## 2. 调查与实施步骤

1. **复现后判断是否还存在。** 基于 T001 最新实现、真实隔离 Shell 与虚构书目，设置可预测书籍顺序，滚动到中间/尾部，记录离开前 `AnchorBookId`、相对偏移、可见首行和内部 scroll offset。执行 Library → Details → Player → 返回、Library → Player → 返回等实际路由，确保关闭后的 page activation 与新页面生命周期完成，再量测锚点。
2. **若 T001 后已恢复：** 本任务无需生产代码改动。确认至少一项现有核心测试或补上最小 WPF 高层回归，防止未来 Shell 约束再破坏 anchor；不要因为任务编号存在而修改 `LibraryScrollViewerStateBehavior`。
3. **若仍丢失：** 先追踪现有 `LibraryScrollState`、`LibraryScrollViewerStateBehavior`、`LibraryPage.xaml.cs` 的 unloaded/capture、new-page reattach、`Rows` projection/row index 映射、restoration readiness 和迟到任务。根据真实断点缩小修复；保留既有逻辑 BookId + 相对偏移方案，不引入按像素全局存储或第二套 scroll owner。
4. **覆盖有意变化。** 搜索/过滤使 anchor 书籍不可见、书籍被删除或集合实际排序变化时，沿用现有语义选择合理回退，不假设必须保持原物理 Y 坐标。排序/过滤后不应恢复到已不可见/已删除对象。管理选择集与滚动状态相互独立。
5. **验证交错。** 慢加载/快速进出页面/Resize/主题切换/导航栏开合情况下，旧 Page 的迟到恢复任务不得滚动新的 Page；不用固定 `Task.Delay`、额外定时器或新通用状态机。若重排刚好发生，结果仍应保持合理 anchor，避免非用户主动导航清空滚动状态。

## 3. 验证矩阵

- 在真实隔离 Shell 下，使用 25 / 100+ 本虚构书籍，窗口最小/默认/宽，导航栏折叠/展开（以真实应用允许的方式），中部和尾部 anchor；覆盖已加载与快速进出、Details/Player 两种返回路径。
- 关键断言关注**可视的同一本或同一逻辑行（允许布局导致同一行相邻书籍）**，不是固定 `VerticalOffset` 数值；保持原排序与可见集合时不能总回到第一行。
- 搜索无结果/清除、排序/数据变更、切换 Management Mode，确保不丢失或错误继承 selection 和阅读进度；既有 `LibraryViewModel` 与页面导航高层测试正常。
- 若真实 Shell 由于隔离环境不可创建，记录限制，不能以有限 Page host 的模拟自动判定缺陷解决。测试仍遵守 fail-closed、随机根、无真实用户数据和禁止交互桌面输出。

## 4. 交付与完成

- 向根目录临时 `U3B_VALIDATION_REPORT.md` 增量追加 T002 修复前/后或“无代码修改”的客观证据，说明是否由 T001 自然解决。
- 只有确认为核心回归且既有测试不足时才新增/扩展**一个**高层 WPF/Presentation 回归；不测试私有状态机每个回调或固定像素。
- focused tests + Release build 通过，临时 UI harness/fixture/trace 清理。生产代码变更若有，须说明为何是最小修复。
- Backlog 标记 T002 `[x]` 并记录成果，删除本规格，进入 T003，无需等待人工验收。
