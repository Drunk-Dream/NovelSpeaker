# T001 — 恢复真实 Shell 中 Library 的有限高度与列表虚拟化

## 1. 目标与授权

修复 U3-A/T005 的 **T005-LIB-01**：隔离生产组件 Shell 中 Library 页面内容高度超过导航 Frame 的可用高度，内部 `BooksItemsControl` 随全部数据增高，外层 `NavigationView` 接管滚动，导致 recycling virtualization 无法发挥作用。

本任务可以为确认的原因修改最少量 Shell/Library UI 宿主代码及必要核心测试，但**不允许**改书卡视觉、重新设计页面、变更业务/数据库/缓存逻辑，也不批准全局强制关闭所有页面的外层滚动。

先读 `AGENTS.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`（响应式 Library、超长列表、Staged Loading）、`docs/02_RUNTIME_AND_NAVIGATION.md`（页面生命周期）、`docs/08_QUALITY_AND_TESTING.md`。阅读 U3-A 报告 T005 章节及实际实现，路径仅作为发现入口，不能假定已定位根因。

## 2. 已知证据与需检查调用链

隔离真实 Shell 的记录：900×640 时 Frame/页面高度约 589.6/2606.62 DIP，13/13 行实现；1280×820 与 1450×1000 也由外层滚动且实现全部行；三个尺寸 `NavigationViewContentPresenter.IsDynamicScrollViewerEnabled=True`。同页有限高度 Page host 上 10,000 本虚构书籍实现有界行容器。这**证明 Shell 场景的约束缺失，不证明某一属性被谁改写。**

重点调查：

- `src/NovelSpeaker.App/Shell/Activation/WpfShellPlatformAdapter.cs` 中 `ConfigureNavigationPresenter` 的调用时机、模板替换、动态滚动属性的赋值来源/值优先级，及路由切换或重新应用模板后是否重新启用；
- `MainWindow.xaml(.cs)`、Shell activation/NavigationView 宿主、实际 `ScrollContentPresenter`/Frame/ContentPresenter 的 measure 与 arrange；
- `src/NovelSpeaker.App/Features/Books/Library/LibraryPage.xaml(.cs)` 的 RootGrid `Auto/Auto/*`、`MinHeight`、内部 ListBox/ScrollViewer 测量边界；
- `LibraryResponsiveLayout`、`LibraryScrollViewerStateBehavior` 与现有隔离 WPF Shell/Library 测试，以确认并非卡片分组本身取消虚拟化；
- U2 `PlayerPage` 已采用的 viewport 处理与 Settings 页面全局滚动行为，仅作对照，不盲目复制。

## 3. 实施要求

1. **先复现并量测。** 在测试专用随机数据根，用正式 Shell 组件及虚构 25 本与更多书籍（至少 100+，有条件测 1,000/10,000）记录 navigation viewport、Page/RootGrid、ListBox 与内外 ScrollViewer 高度、滚动 owner、VirtualizingStackPanel 的 realized rows。显示尺寸选 900×640、1280×820、1450×1000，覆盖导航栏开合。必要时追踪动态滚动值在哪个生命周期阶段恢复为 True。
2. **修复实际约束来源。** 让 Library 的可用高度自外部宿主正确传递到 `*` 行；最终由内部 ListBox 滚动。若必须改变共享 Shell 宿主，只实施最小兼容性修正并验证普通 Settings 等仍需外层滚动的页面；避免将 Player 的特殊行为推广为所有页面默认规则。
3. **不要遮蔽症状。** 不能靠 `ClipToBounds`、压缩/隐藏书卡、`ScrollViewer.VerticalScrollBarVisibility=Hidden`、强改窗口 MinHeight 或数据量硬阈值伪造有效 viewport。杜绝环形 `ActualHeight` 反馈和多重 `SizeChanged` 布局修复逻辑。优先现有 WPF/Wpf.Ui 能力。
4. **保留用户行为。** 空态/无搜索结果、书卡不同列数、搜索排序、Management Mode/Current/Selected、鼠标/键盘入口、无加载卡顿、滚动锚点组件均不退化。除修复直接必需的 Shell/Library 修改外，不扩展范围。
5. **测试与证据。** 以隔离 Desktop 中的真实 Shell 回归为核心，不仅验证有限 Page host。验证 25 与 100+ 本、滚动到中/末端后 realized rows 仍有界且不随总数线性增大，内部具有可滚动 extent，外围没有由书库撑起的长页面。避免冻结精确 DIP 值或固定“恰好 N 个容器”。新增/扩展一个高价值 WPF 核心行为测试即可，优先复用既有 Shell/Library 测试入口。

## 4. 环境与边界

只用 `tests/TestKit/Wpf` 隔离 Desktop，禁止 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1` 与测试窗口进入用户交互桌面；隔离失败则 fail-closed。数据为随机根/虚构小说，禁止打开用户正式 `Data/`、真实书库、凭据；不触发真实音频/网络。任何临时 harness/诊断应在任务结束前删除；可在根目录用户自管 `U3B_VALIDATION_REPORT.md` 中记录无敏感信息的复现和前后数值，不能提交该报告。

## 5. 交付与验收

- 最终说明：根因调用链（含证据）、最小修复位置、为何不破坏其它页面、修复前后有限 viewport 与 realized 行数量对照、所有执行的测试与剩余限制。
- 在隔离真实 Shell 下验证最小/默认/宽窗口来回缩放、导航展开/收起、Light/Dark、100+ 虚构书目、设置页全局滚动与 Player/Details 可达性。
- `dotnet build -c Release` 与 focused tests 通过（确有环境阻断须明确报告）；必要时运行格式检查。不得新增数据库 migration、测试专用生产开关或通用布局管理器。
- 将 Backlog T001 标记 `[x]`，简述成果和证据，删除本规格并进入 T002；不需要人工验收等待。
