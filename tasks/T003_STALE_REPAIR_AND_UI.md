# T003 — 简化 Stale repair 与完整度展示

- **类型/优先级：** Implementation / P0 Background & UX。依赖 T001、T002。
- **必读：** `AGENTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/specs/REGEX_REPLACEMENT.md`；`SpeechPlanRepairCoordinator`、`CacheReadModel`、`CacheCoverageQuery`、`CacheInvalidationCoordinator`、`CacheManagementCompletenessFormatter`，及 Player / BookDetails / CacheManagement viewport decoration 的现有 latest-operation slot。

## 授权终态

`PlanMissing` 永远不由目录查询补建。`PlanStale` 仅在**确有物理音频缓存且位于当前需要展示的窗口**时低优先级异步修复。目录先出现，真实百分比修复成功后再出现；不阻塞滚动。其它情况下右侧保持空白。

## 调度策略：刻意保持简单

1. 以现有 Cache repair owner 为基础收敛为**单 worker**，按章顺序处理。只保留轻量任务标识（章节、来源/配置身份等）与当前最新的可视修复需求，不缓存正文/分段。可以使用标准 `Channel` / 简单受保护集合或其它现成 .NET 原语，但不要实现通用 scheduler。
2. 同章、同目标配置去重；当窗口滚动改变时，尚未开始且不再需要的项移除或失效。页面离开/来源切换/配置改变时撤销旧需求；Worker 的进程生命周期与页面需求分开，不取消已接受的 Active Cache/Export。
3. 正在执行的任务在安全边界复核章节身份、当前 Text Profile 和音频缓存存在性；若已不需要，协作取消或安全丢弃结果。一次修复不使用无界 while 重试去追逐规则变化。异常必须观察，失败不得每次 Coverage refresh 就立即再入修复。
4. 修复过程使用 T002 的纯计算及按需保存角色；已无音频缓存的章节不持久化新 Plan。修改前后配置验证确保不会把过期计算结果标记为当前。
5. 成功后只发对应章节 Coverage invalidation；由既有 read model + 页面 activation/latest-operation slot 更新当前可见项，不触发全目录 property 风暴。不新建全局发布器或第二套 UI 数据流。
6. 不把调度参数、精确容量、临时失败标记、CTS 组合写进长期 docs；以简单、有界、可测试为准。移除已无用的旧 `Task.Run` 无界登记、重复 retry、防御性层叠注册代码。

## UI 合同

- Player、BookDetails、CacheManagement 中，**只显示当前配置下完整度可信且高于 0% 的百分比**。有效缓存为零、`PlanMissing`、`PlanStale`、失败、`PlanUnavailable`、配置不可用等均不显示任何标注或占位文案。
- 不显示 `0%`、`完整度：计划计算中`、`完整度：计划更新中`、spinner；旧百分比不能在 Stale 修复期间继续出现。
- CacheManagement 的物理缓存书/章仍可见，即使在当前 Provider 下没有有效音频；不要因百分比空白而从物理列表移除。
- Provider/语速改变仍按**现有** synthesis fingerprint 进行重匹配，不新增 Plan rebuild 行为；切回旧 Provider 时旧的仍有效缓存重新被识别。

## 自动验收

- 有缓存的 Stale 在窗口内触发一次后台修复；恢复后只有对应章节拿到准确百分比，目录初始展示及滚动不等待修复。
- 100 次快速窗口变化及离开页面模拟：后台一次只处理一个章节；未开始的旧需求不累积、不读取正文、不大量写入；运行中的过期结果不会复写新配置/来源结果。
- 失败/取消及频繁 invalidation 不产生无限自激重试或无界任务表；单 worker 仍能在 shutdown 的现有总预算内停止。
- Regex/Text Profile 变化后旧百分比隐藏；当前分段与 Ready 音频键共同决定新百分比。只切 Provider 重新匹配缓存，无 Plan 修复。
- 无有效缓存的全部目录 UI 标注为空；有真实部分/全部缓存时显示正确百分比；CacheManagement 物理条目不丢失。
- 执行 Cache repair/Application/Presentation 及必要 WPF focused tests、Release build、format/diff check；任何真实 WPF/隔离 Desktop 测试遵守 `AGENTS.md`。

## 完成

更新 Backlog `[x]`，简短记录被清除的旧调度/显示路径及测试；删除本规格，继续 T004。
