# T001：审计永久测试并建立保留/删除判定

## 目标

基于 `docs/08_QUALITY_AND_TESTING.md` 的新准入标准，对当前全部自动测试做一次系统审计，为后续瘦身提供明确、可执行的分类依据。

本任务不追求测试数量目标，也不以覆盖率作为判断依据。

## 范围

至少审计：

- `tests/NovelSpeaker.Domain.UnitTests`
- `tests/NovelSpeaker.Application.UnitTests`
- `tests/NovelSpeaker.App.PresentationTests`
- `tests/NovelSpeaker.Infrastructure.IntegrationTests`
- `tests/NovelSpeaker.App.WpfTests`
- `tests/TestKit` 中仅服务于测试的辅助设施
- 与测试执行直接相关的 CI 配置

重点检查：

- WPF 样式、资源、Visual Tree、精确尺寸等细节断言；
- ViewModel 内部调用次数、具体加载顺序、辅助文案和投影字段断言；
- Architecture rule contract tests 是否在过度测试“规则检测器本身”；
- Application/Infrastructure 中同一核心行为是否被大量重复微测试覆盖；
- 是否存在已没有真实核心风险的历史回归测试；
- 是否存在只被低价值测试引用的 TestKit/helper。

## 分类

每组测试归入：

1. **KEEP**：明确保护核心用户流程、数据/兼容性/安全边界或关键架构契约；
2. **MERGE/REWRITE**：风险值得保护，但当前断言过细、重复或绑定实现；
3. **DELETE**：主要锁定实现细节、非核心 UI/文案/资源结构、无风险内部调用或与更高层测试重复；
4. **TEMPORARY-ONLY PATTERN**：未来调试可能有用，但不应作为永久测试资产的模式。

允许为审计建立临时脚本或临时测试验证判断，但任务结束前必须删除。

## 实施要求

- 不按文件大小或测试数量机械删除；
- 对 KEEP 必须能说明保护的核心契约；
- 对 DELETE 不要求逐个测试写长说明，可以按文件/测试簇说明共同原因；
- 发现真实核心行为缺口时，只记录应补的最小核心测试，不在本任务扩张测试；
- 发现测试失败时先判断产品行为是否真实回归，不假设生产代码必须迎合测试；
- 不修改生产代码，除非只是为了完成审计所必需且行为保持的极小修正；若需要实质生产代码修改则记录到 T002。

## 交付

在 `TASK_BACKLOG.md` 的 T001 完成成果中简要记录：

- 各测试项目的主要 KEEP / MERGE / DELETE 方向；
- 是否发现必须补齐的核心测试缺口；
- 是否发现可以删除的 TestKit/fixture；
- T002 应执行的关键动作。

详细历史由 Git 保留，不新增长期“测试清单文档”。

## 验收

- 审计覆盖五个测试项目和 TestKit；
- 结论与 `docs/08_QUALITY_AND_TESTING.md` 一致；
- 没有遗留临时测试、脚本、fixture 或报告文件；
- 对审计过程中修改的任何文件执行必要的 targeted validation；
- 本任务不要求为了审计本身执行完整 `dotnet test`，除非实际修改影响测试执行。
