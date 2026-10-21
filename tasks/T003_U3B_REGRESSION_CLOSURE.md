# T003 — U3-B 整体回归与收口

## 1. 目标

核对 T001/T002 的修复确实解决 U3-A/T005 所证实的两个 Library 问题，并排除共享 Shell、Library 页面与定位器边界的新回归。完成必要的长期文档收口、减法清理和标准自动质量门禁；**不继续无证据扩张 UI/UX 优化范围**。

阅读 `AGENTS.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/08_QUALITY_AND_TESTING.md`；需要交叉复核选择行为时读取 `docs/specs/BATCH_MANAGEMENT.md`。以本地最新工作区及 T001/T002 已完成记录为准。

## 2. 重点复核

1. **Shell 与 Library 布局：** 实际 `NavigationViewContentPresenter`、Page/RootGrid、`BooksItemsControl` 测量层次正确。900×640、1280×820、1450×1000、宽度/高度双向往返、导航开合、Light/Dark、长书名/作者/章节、空书库/搜索无结果、25/100+/可控高容量数据下不存在由书库撑开的外层全局滚动。内部虚拟化容器有界，滚动/回收无明显卡顿或偏离。
2. **返回与锚点：** Library → BookDetails → Player → Library 和 Library → Player → Library；不变排序/可见集合时保持逻辑 anchor。搜索、排序、重新排列和数据删除的已有交互语义正确；Manual scroll、Page 离开/返回、当前播放/已选择不同状态不互相串扰。
3. **跨页回归：** BookDetails 信息区与目录仍独立滚动、当前章可定位；Player 仍保留 U2 有限 viewport/底部控制区/内部滚动；Settings 等需要内容滚动的页面不丢失全局滚动能力；空态、错误/加载与导航入口可达。
4. **复杂度复核：** 不保留冗余 scroll owner、第二套 anchor、无意义事件订阅/延迟布局刷新、固定时间等待和多余 wrapper；事件订阅必须成对释放。若 T001 动到 Shell 宿主，确认没有把它与 Feature 直接双向耦合。
5. **测试取舍：** 只保留保护核心产品行为的最少量 WPF/Navigation 高层回归（T001/T002 合计通常 1–2 个或在既有测试中扩展）；删除仅用于诊断的 harness、私有 VisualTree/像素锁定测试。不得为了控制测试数量删除仍保护高风险功能的既有测试。

## 3. 收口条件

- 如果新布局规则/页内滚动责任发生有意义的变化，在 `docs/06_UI_AND_VISUAL_SYSTEM.md` 简要补充**长期结果合同**，必要时更新 `docs/02_RUNTIME_AND_NAVIGATION.md`；不要写历史过程、T001/T002 任务号或临时测量数值。无长期合同变化则不改文档。
- 运行项目 `AGENTS.md` 当前规定的完整门禁，以仓库实际 target framework/commands 为准：locked restore、format verify、Release build、全量 test；检查 diff、LF 与测试残留。不可执行时准确记录环境阻断与未验证范围，不能虚报通过。
- `U3B_VALIDATION_REPORT.md` 仅作为项目根目录用户自管的未提交临时报告，包含最小前后证据、根因总结、测试与限制；不得提交，勿自行覆盖用户已有的临时报告。任何截图也只有必要且脱敏的精选证据可留在根目录未跟踪目录，其余临时产物清理。
- 默认不发布、不 push、不建 PR/tag/release，不触动真实用户书库/配置/音频缓存。若发现超出授权范围的高风险问题，记录证据和后续候选，不顺手重构。
- 最终 `TASK_BACKLOG.md` 的 T001–T003 全部 `[x]`、带简要成果及自动验证；删除这三份已完成 `tasks/` 规格，确保本阶段无待办残留。完成后停止并向用户汇报，U3-B 可收口。
