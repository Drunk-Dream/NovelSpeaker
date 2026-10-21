# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：U3-B — 书库布局与跨页位置恢复修复

- **状态：** 待执行。上一阶段 U3-A（T001–T005）已调查收口，其完成历史由 Git、用户自行管理的 `U3_AUDIT_REPORT.md`、`U3_AUDIT_WORKLOG.md` 与证据包保存。本文件开始新的任务轮次，**清空上一轮任务调度，从 T001 重新编号**。
- **执行基线：** Codex 开工时本地 `dev` 的实际 HEAD 和工作区。先执行 `git status` 并阅读现有未提交差异；不得把远端仓库状态、U3-A 报告里的旧 HEAD 当作最新实现，也不得覆盖用户未提交改动和临时报告。
- **授权目标：** 只处理 U3-A/T005 在隔离真实 Shell 下复现的两项问题：① Library 因没有有限 viewport 而由导航外层滚动、失去有效行虚拟化；② Library → Details/Player → 返回后丢失可视书籍逻辑 anchor。后者**先在①修复后重新测试**，不能未经确认再造一套位置恢复机制。
- **依据证据：** `U3_AUDIT_REPORT.md` 的 T005-LIB-01/02（如用户本地有该报告），以及 `U3_AUDIT_WORKLOG.md`。报告前半部分“没有确认缺陷”是 T001–T004 的历史结论，不能覆盖 T005 新证据。T005 记录：900×640 时 Frame 高约 589.6 DIP、Page 高约 2606.62 DIP、13/13 行实现；1280×820、1450×1000 同样由外层滚动；`NavigationViewContentPresenter.IsDynamicScrollViewerEnabled` 实测为 True。有限 Page host 的 10,000 本书仍可实现有界行容器。跨页返回曾从可视首行 15–17 回到 00–02。**以上是实验事实，不是根因判定。**
- **范围边界：** 保留响应式书卡、Current/Selected、搜索排序、Management Mode、书籍导入/删除/导出、BookDetails、Player、Cache/Playback 的既有语义；不做 U3 整体 UI 改版、极窄详情页摘要重排、在线源或完整书库备份；不新增数据库迁移、持久化布局状态或自定义虚拟化框架。
- **长期依据：** `AGENTS.md`；按各任务阅读 `docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/08_QUALITY_AND_TESTING.md`，需要复核批量选择时再查 `docs/specs/BATCH_MANAGEMENT.md`。任务规格优先于本 Backlog 的实施摘要。

## 2. 执行约束

- 默认按 **T001 → T002 → T003** 顺序执行，使用 `tasks/Txxx_*.md` 作为任务合同。完成一项后在此保留 `[x]` 与简短成果、自动验证，删除其临时任务规格；按调用约定继续下一项，人工验收不是阻断条件。
- 根因优先：从 Shell/NavigationViewContentPresenter、Page measure/arrange、ScrollViewer ownership、Library ListBox 内部虚拟化及锚点生命周期逐层测量，不能只隐藏滚动条、裁剪内容、提高窗口最小尺寸或复制一个硬编码高度补丁。优先复用 WPF/Wpf.Ui 原生能力和项目稳定组件。
- **禁止全局破坏其它页面的滚动需求。** Library、Player、BookDetails、Settings 等的滚动行为不同；若改共享导航宿主，必须针对性验证需要外层滚动的页面未回归。不得为了 Library 而全局无条件关闭 ScrollViewer 或将所有页面改为固定高度。
- 安全验证：仅用隔离 Windows Desktop、随机应用数据根及虚构书籍/章节；不读取真实 `Data/`、用户小说、设置或 Provider 凭据，不在交互桌面展示测试窗，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。隔离失败须 fail closed 并如实记录。
- 只为核心可观察回归新增最少量正式测试，集中覆盖有限 viewport/虚拟化及跨页 anchor；视觉细节、Visual Tree 私有构成和精确像素留给临时验证，不形成长期锁定。临时 harness/截图/诊断在对应任务完成前清理；若保留精选证据仅放项目根目录的用户自管临时报告/目录，明确不提交。
- LF 换行；不未经授权 push、PR、tag、release。除本轮必要的生产修改、少量核心测试及最后必要的长期文档修订，不触碰无关文件。默认每项结束保持可构建、可执行相关测试。

## 3. 当前任务

- [ ] **T001（P1 / Layout）— 恢复真实 Shell 中 Library 的有限高度与列表虚拟化**  
  任务合同：`tasks/T001_U3B_LIBRARY_VIEWPORT.md`。先在真实隔离 Shell 复现并确定根因，重点查 NavigationView 动态滚动开关为何与预期不一致；提供修复前后宿主、Page、ListBox、ScrollViewer、已实现行数证据，确保设置页等仍可正常滚动。

- [ ] **T002（P1 / Navigation）— 复核并必要时修复跨页面返回的书库锚点恢复**  
  任务合同：`tasks/T002_U3B_LIBRARY_ANCHOR.md`。在 T001 之后重新执行 Library → BookDetails → Player → Library；若旧缺陷随布局修复消失且已有核心回归足够，允许无生产代码结束。否则沿现有 `LibraryScrollState`/`LibraryScrollViewerStateBehavior` 做最小修复。

- [ ] **T003（P1 / Closure）— U3-B 回归验证、减法清理与长期合同收口**  
  任务合同：`tasks/T003_U3B_REGRESSION_CLOSURE.md`。交叉验证窗口/主题/导航/长书库/搜索排序/选择/返回及其它页面滚动，执行完整自动门禁，删除临时产物与冗余代码，必要时微调 `docs/06_UI_AND_VISUAL_SYSTEM.md`，最终清空 `tasks/` 已完成规格。

## 4. 本轮不实施

- 书库卡片视觉重做、BookDetails 双栏重排、长章节摘要密度优化、批量 TXT 导入或管理功能扩张。
- 自定义虚拟化引擎、全局导航滚动重写、独立书库滚动数据库/设置保存、通用布局管理器、额外计时器和同职责重复状态 owner。
- 仅因 T005 导入进度/取消一次不确定超时就变更导入生命周期：该项仍是 `Unknown`，需要单独可靠复现才另立任务。
- 未经授权的数据库、Book/Source/CurrentCatalog/ReadingProgress、Playback/Cache/Speech 语义变化。

## 5. 完成标准

1. 在真实隔离 Shell 的最小/默认/宽窗口，Library 页面高度不随数据全集无限膨胀；主体列表拥有有限 viewport、独立滚动，行容器实现数量随 viewport 有界；书卡仍维持正确列数与响应式布局。
2. Library → Details/Player → 返回，在排序/可见集合未变的情况下恢复先前可视书籍附近的逻辑 anchor，而不是重置到首行；滚动、筛选与排序变化遵循原有选择/恢复规则。
3. 其它需要全局滚动的页面无回归，Player 内部滚动、BookDetails 目录定位、不同窗口与 Light/Dark 均正常。
4. 完成 focused 验证与收口全量门禁；临时测试/诊断/截图已清理或用户自管且明确不提交，长期测试仅留核心回归；Backlog 记录成果，已完成的任务规格删除。
