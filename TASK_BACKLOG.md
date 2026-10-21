# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：U3-B — 书库布局与跨页位置恢复修复

- **状态：** 已完成（T001–T003）。本轮任务规格已删除，完整实施与审查历史由 Git 保存；旧 U3 及本轮未提交证据在全部本地提交后按用户要求清理。
- **执行基线：** Codex 开工时本地 `dev` 的实际 HEAD 和工作区。先执行 `git status` 并阅读现有未提交差异；不得把远端仓库状态、U3-A 报告里的旧 HEAD 当作最新实现，也不得覆盖用户未提交改动和临时报告。
- **授权目标：** 只处理 U3-A/T005 在隔离真实 Shell 下复现的两项问题：① Library 因没有有限 viewport 而由导航外层滚动、失去有效行虚拟化；② Library → Details/Player → 返回后丢失可视书籍逻辑 anchor。后者**先在①修复后重新测试**，不能未经确认再造一套位置恢复机制。
- **依据证据：** `U3_AUDIT_REPORT.md` 的 T005-LIB-01/02（如用户本地有该报告），以及 `U3_AUDIT_WORKLOG.md`。报告前半部分“没有确认缺陷”是 T001–T004 的历史结论，不能覆盖 T005 新证据。T005 记录：900×640 时 Frame 高约 589.6 DIP、Page 高约 2606.62 DIP、13/13 行实现；1280×820、1450×1000 同样由外层滚动；`NavigationViewContentPresenter.IsDynamicScrollViewerEnabled` 实测为 True。有限 Page host 的 10,000 本书仍可实现有界行容器。跨页返回曾从可视首行 15–17 回到 00–02。**以上是实验事实，不是根因判定。**
- **范围边界：** 保留响应式书卡、Current/Selected、搜索排序、Management Mode、书籍导入/删除/导出、BookDetails、Player、Cache/Playback 的既有语义；不做 U3 整体 UI 改版、极窄详情页摘要重排、在线源或完整书库备份；不新增数据库迁移、持久化布局状态或自定义虚拟化框架。
- **长期依据：** `AGENTS.md`；按各任务阅读 `docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/08_QUALITY_AND_TESTING.md`，需要复核批量选择时再查 `docs/specs/BATCH_MANAGEMENT.md`。任务规格优先于本 Backlog 的实施摘要。

## 2. 执行约束

- 已按 **T001 → T002 → T003** 串行完成实现、自动验证、独立审查与本地原子提交；各项已完成临时规格已删除，成果保留在下方。
- 根因优先：从 Shell/NavigationViewContentPresenter、Page measure/arrange、ScrollViewer ownership、Library ListBox 内部虚拟化及锚点生命周期逐层测量，不能只隐藏滚动条、裁剪内容、提高窗口最小尺寸或复制一个硬编码高度补丁。优先复用 WPF/Wpf.Ui 原生能力和项目稳定组件。
- **禁止全局破坏其它页面的滚动需求。** Library、Player、BookDetails、Settings 等的滚动行为不同；若改共享导航宿主，必须针对性验证需要外层滚动的页面未回归。不得为了 Library 而全局无条件关闭 ScrollViewer 或将所有页面改为固定高度。
- 安全验证：仅用隔离 Windows Desktop、随机应用数据根及虚构书籍/章节；不读取真实 `Data/`、用户小说、设置或 Provider 凭据，不在交互桌面展示测试窗，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。隔离失败须 fail closed 并如实记录。
- 只为核心可观察回归新增最少量正式测试，集中覆盖有限 viewport/虚拟化及跨页 anchor；视觉细节、Visual Tree 私有构成和精确像素留给临时验证，不形成长期锁定。临时 harness/截图/诊断在对应任务完成前清理；若保留精选证据仅放项目根目录的用户自管临时报告/目录，明确不提交。
- LF 换行；不未经授权 push、PR、tag、release。除本轮必要的生产修改、少量核心测试及最后必要的长期文档修订，不触碰无关文件。默认每项结束保持可构建、可执行相关测试。

## 3. 已完成任务

- [x] **T001（P1 / Layout）— 恢复真实 Shell 中 Library 的有限高度与列表虚拟化**
  完成成果：确认同一 NavigationViewContentPresenter 在 Shell 配置后，被 Wpf.Ui OnNavigated 的 SetCurrentValue 重新启用动态滚动。Library Page 复用已有 Frame viewport 约束，主体 ListBox 独立滚动，未改共享 Shell/Settings 滚动策略。125 本最小窗口从 Page 12106.62 DIP、63/63 行变为 589.6 DIP、约 4–6 个实现行；25/125 本三尺寸往返、Light/Dark、导航开合与 Settings 外层滚动验证通过。保留一个真实隔离 Shell 核心 WPF theory。Release build 与 focused WPF 3/3 通过，独立审查 PASS。

- [x] **T002（P1 / Navigation）— 复核跨页面返回的书库锚点恢复**
  完成成果：T001 后原 BookId + 相对偏移机制自然恢复，无需修改锚点生产实现。新增一个真实 Shell 高层 theory，覆盖 25/125 本、中部/尾部、Details/Player 两条返回路径、新 Page、完整顺序不变与实际逻辑行可见。三尺寸 × Light/Dark 共 24 次返回通过；审查发现额外 LoadAsync 可能遮蔽导航加载，已改为事件驱动等待真实 Library/Details/Player 就绪并重跑矩阵。最终 Release build、focused WPF 5/5、Presentation 26/26 通过，复审 PASS。

- [x] **T003（P1 / Closure）— U3-B 回归验证、减法清理与长期合同收口**
  完成成果：冷启动 Light/Dark 下，125 本加长书名/作者及 120 章虚构书籍验证通过；覆盖快速往返、搜索无结果/清除、排序、管理选择、删除锚点、Details 信息/目录双滚动和 Player 无 Provider 空态 viewport。10,000 本独立 Page host 的既有虚拟化测试保留并通过。仅保留上述两个 Shell 核心 theory，不新增兼容层、第二 scroll owner 或 anchor owner；临时 harness、source backup、测量与本轮失败诊断已删除。长期 UI 合同补充有限 viewport/内部列表与 Settings 滚动职责。
  自动验收：locked restore、format verify、Release build（0 警告/错误）、全量 test **1031/1031** 全部通过（Domain 34、Application 262、Infrastructure 358、Presentation 319、WPF 58；零跳过）。沙箱内 restore 曾报 NU1900（NuGet 漏洞索引不可达），全量测试曾有 26 个 HttpListener 初始化“句柄无效”；相同标准命令在沙箱外全部通过，未弱化检查或修改依赖/隔离。无未执行的强制门禁、无新增长期合同冲突；可选人工交互验收未执行。

## 4. 本轮不实施

- 书库卡片视觉重做、BookDetails 双栏重排、长章节摘要密度优化、批量 TXT 导入或管理功能扩张。
- 自定义虚拟化引擎、全局导航滚动重写、独立书库滚动数据库/设置保存、通用布局管理器、额外计时器和同职责重复状态 owner。
- 仅因 T005 导入进度/取消一次不确定超时就变更导入生命周期：该项仍是 `Unknown`，需要单独可靠复现才另立任务。
- 未经授权的数据库、Book/Source/CurrentCatalog/ReadingProgress、Playback/Cache/Speech 语义变化。

## 5. 完成标准（均已满足）

1. 在真实隔离 Shell 的最小/默认/宽窗口，Library 页面高度不随数据全集无限膨胀；主体列表拥有有限 viewport、独立滚动，行容器实现数量随 viewport 有界；书卡仍维持正确列数与响应式布局。
2. Library → Details/Player → 返回，在排序/可见集合未变的情况下恢复先前可视书籍附近的逻辑 anchor，而不是重置到首行；滚动、筛选与排序变化遵循原有选择/恢复规则。
3. 其它需要全局滚动的页面无回归，Player 内部滚动、BookDetails 目录定位、不同窗口与 Light/Dark 均正常。
4. 完成 focused 验证与收口全量门禁；临时测试/诊断/截图已清理或用户自管且明确不提交，长期测试仅留核心回归；Backlog 记录成果，已完成的任务规格删除。
