# T029：收敛 AGENTS 与长期 owner 文档的重复规则

## 目标

让 `AGENTS.md` 继续承载必须直接执行的仓库硬约束，同时让稳定 UI/test 合同的详细定义只维护在对应 `docs/` owner 文档。

审计依据：AGENTS 与 owner 文档规则重复（S06；报告不同章节的 D 编号不一致）。相关合同：`docs/README.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 对照 AGENTS 与 owner docs 中的重复段落，区分每次任务必须直接可见的执行硬约束和稳定产品/架构/质量合同。
- 将可由链接表达的详细 UI/test 合同改为简短约束加 owner 文档入口；保持用户数据保护、隐私、安全、WPF 隔离、测试强制门禁等必要硬要求可从 Agent 入口直接发现。
- 仅更新确实重复的规则，不重组 docs、不创建索引副本或新长期文档，不弱化任何合同。

## 验收

- 逐条核对迁移后的规则含义和引用有效性；全文搜索确认同一详细稳定合同不再多处维护。
- 本任务是文档修改，不运行 build/tests；检查 Markdown 链接与 LF。

## 交付

简述保留在 AGENTS 的执行硬约束、转为链接的 owner 合同，更新 Backlog 并删除本规格。
