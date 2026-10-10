# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：永久测试做减法（约 600 例）

- **规划基线：** `main` @ `833333d1e694e8a9c4163e73e0b8face0cacc780`（v0.9.0 合并提交）。执行时先确认本地实际 HEAD；若已有后续合法修改，以当前真实代码为准复核候选项，不回退用户修改。
- **最近完整门禁基线：** 1233/1233（Domain 15、Application 293、Infrastructure 472、Presentation 351、WPF 102）；这是 T019 的历史记录，**不是本轮重新 discovery 的结果**。T001 必须使用当前 HEAD 重新列举可运行的测试用例，特别处理 `[Theory]` 展开的数据行。
- **目标：** 保留约 **600 个**高价值、低维护成本的永久自动测试；优先以 **575–625** 为收敛区间。以下分层配额是规划用的弹性预算，不得牺牲高风险核心覆盖硬凑数字：Domain ≈15、Application ≈180、Infrastructure Integration ≈240、Presentation ≈125、WPF ≈40。
- **本轮只重构/删除/合并测试代码及其专用 fixture、辅助类和必要测试项目配置；不改生产业务行为、SQLite schema、产品文档或 CI 的真实验证要求。** 若发现真实产品缺陷，记录证据，非本轮为减测而顺手改业务。
- **长期依据：** `AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`；必要时再阅读当前任务规格列出的领域文档。历史审计曾发现架构测试的自制源码解析器和文件布局断言过重；应复核当前实现，不将历史审计结论直接当作待删除清单。
- 上一轮 T001–T019 均已完成，实施与验证历史可通过 Git 和此前 Backlog 版本追溯。本文件切换为新阶段调度，编号从 T001 重新开始；`tasks/` 仅保留未完成任务合同。

## 2. 执行规则与不可删保护面

- `[ ]` 未开始；`[-]` 进行中；`[x]` 已完成，附简短成果；`[!]` 仅用于必须由用户决定的新边界。
- 按 T001 → T006 串行执行，默认每个任务结束时仓库可构建、可测试；**本轮不启用 staged breaking migration window**。人工验收可选，绝不阻塞。
- **删测试必须有风险依据：** 对每一组删除/合并，先定位所保护的风险、找到保留下来的替代测试或说明它不符合永久准入标准；优先删除结构、文案、私有调用细节、穷举参数组合、重复跨层验证，不因当前绿色或高覆盖率就保留。
- 核心底线：导入/删除/回滚、已发布数据库迁移、存储路径和 reparse-point/Junction 防逃逸、缓存原子性与身份、Provider 配置/安全/合成、播放逻辑位置即时投影与迟到音频拒绝、进度恢复、正式导出、页面关键取消/生命周期、诊断隐私和失败隔离、WPF Desktop 隔离 fail-closed、关键架构依赖方向必须仍有**可执行且有效**的回归保护。
- 不通过 Skip、Trait、过滤、禁用文件、删除测试项目、改 CI 或折叠理论数据却不覆盖真实风险来伪造数量。测试发现数按真实运行的总用例计，必须同时记录各项目数和合计；不要把源码文件数或 `[Fact]` 标签数冒充测试数。
- 测试支持代码做减法：删除最后使用方消失后的 test doubles、fixture、专用 parser、无用测试数据和不再引用的 TestKit 帮助类；不得删除仍由其它高价值测试使用的共享基础设施，也不得新建庞大测试框架。
- 完成任务时按 `AGENTS.md` 记录简短结果并删除对应 `tasks/Txxx_*.md`；无需保存额外长审计报告到仓库。一次性统计脚本、临时测试、覆盖表、报告与输出留在临时目录并在收口时清理。
- 每个任务的 reviewer 读取 `.codex/review-checklist.md`，核查实际 diff。T006 运行标准完整门禁；清理期间以 focused tests + 可构建状态为主，不为阶段优化降低关键检查。

## 3. 任务安排

### Phase A — Presentation 测试去细节化

- [x] **T001（P0）**：完成成果：真实用例由 1233 降至 1214（Domain 15、Application 293、Infrastructure 472、Presentation 332、WPF 102）；移除项目/测试目录布局、工作流与 fixture 清单断言、重复 Cache 投影排列及非核心播放计时器细节，保留分层依赖、模块环、关键 state owner、书库操作、页面迟到结果和大目录/cache scope 风险。Presentation 阶段建议值未达（332）；上述不可替代风险及跨层页面生命周期合同仍由各自最合适的 Presentation 行为测试保护。Presentation 332/332、Release build、format verify 均通过。
- [x] **T002（P0）**：完成成果：Presentation 由 332 降至 312 例；合计真实用例由 1214 降至 1194（Domain 15、Application 293、Infrastructure 472、Presentation 312、WPF 102）。合并重复的 Rules/Metadata 编辑决策矩阵与管理手势/槽位排列，保留 dirty 保存与取消、Provider 保存/切换和安全、批量部分失败、异步旧结果拒绝、诊断隐私/fatal 归因、主题文字与关键架构边界。Presentation 建议区间未达（312，建议 110–145）；保留项分别保护不同数据覆盖风险、页面/进程生命周期及安全/隐私行为，未为达数量预算删除这些独立契约。Presentation 312/312、全项目 Release build、format verify 通过。

### Phase B — WPF 与业务单元测试收敛

- [x] **T003（P1）**：完成成果：WPF 由 102 降至 51 例，总计 1143 例（Domain 15、Application 293、Infrastructure 472、Presentation 312、WPF 51）。删除非核心动画、像素布局、重复交互和纯视觉细节覆盖，保留隔离 Desktop/fail-closed、关键窗口/导航/托盘生命周期、Provider Popup 渲染、焦点上下文、真实滚轮路由及 10,000 项目录尾部有界定位。51 例比建议上限多 1：焦点上下文与长目录尾部映射是独有 WPF 风险。清理失效的 PlayerView 布局 fixture 和视觉辅助；保留 Popup 所需最小 fake 服务。WPF 51/51、Release build（0 warnings/errors）、format verify 通过；独立复审 PASS，NS-01/02 不适用、NS-03 通过。
- [x] **T004（P1）**：完成成果：Application 由 293 降至 244 例，Domain 保留 15 例，总计由 1143 降至 1094 例（Infrastructure 472、Presentation 312、WPF 51）。合并等价导入/删除/元数据失败/规则导入排列、Provider 配置边界与播放投影输入，移除重复投影、仅验证注入时钟/ID 序列的用例及无调用方 fake 成员；保留文件名/编码识别、导入提交与回滚/取消、Playback 旧结果拒绝及保存恢复、Active Cache 配置冻结/取消所有权、导出完整性、Provider 凭据与模板安全、Observability 隐私和失败隔离。244 例高于建议上限 49 例；复核剩余用例后确认其覆盖不同状态所有权、跨异步边界或安全/数据完整性风险，没有可由现有高层行为测试替代的重复组，故未为数量目标删除这些契约。Application 244/244、Domain 15/15、Infrastructure 导入仓储 focused tests 18/18；Release build 0 warnings/errors、format verify、diff check 通过。独立复审 PASS，NS-01/02/03 不适用。

### Phase C — 集成测试去重复与整体门禁

- [x] **T005（P0）**：完成成果：Infrastructure Integration Tests 从 472 减至 327 例，删除/合并重复状态排列、内部实现细节和等价跨层路径，主要涉及 PlaybackCoordinator、Diagnostics、Provider/HTTP、设置存储及 Cache 测试。保留正式迁移与 rollback、SQLite 外键/导入候选、缓存文件与索引一致及清理、目录身份/路径边界、Provider/HTTP/Jint 安全与取消及 Retry-After 冷却、诊断隐私/恢复/不可用归因、真实音频解码及导出有效性等不可替代风险；327 高于建议值约 240，未为数量目标削减这些保护。总量由 T004 的 1094 降至 949（Domain 15、Application 244、Infrastructure 327、Presentation 312、WPF 51）。Infrastructure 327/327、Application 244/244、Presentation 312/312；Release build 0 warnings/errors、format verify、diff check 通过。独立复审 PASS，NS-01/02/03 不适用。
- [ ] **T006（P0）**：统一清点已保留的风险覆盖、消除残余无用 fixture，复核约 600 例与完整质量门禁。依赖 T005，详见 `tasks/T006_CLOSURE.md`。目标总数 575–625；不可替代的高风险用例允许使实际数量略超预算，必须记录原因，不得为凑数删除。

## 4. 非目标与结果记录

本轮不改产品功能、数据结构、发布流程，不把已有 bug fix、测试基础设施重写、架构重构或新功能开发混入“测试减法”。无需新增覆盖率门槛；不因数量减少就扩大测试内部的断言细节。

每项完成成果至少简记：原/现各层可运行测试数、删/合并的主要类别与代表文件、保留的不可删风险、focused/build/format 结果与限制；T006 补充完整命令及汇总。测试数量应来自测试发现或运行的真实输出。
