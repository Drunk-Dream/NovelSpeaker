# NovelSpeaker 长期文档索引

`docs/` 只描述 NovelSpeaker 的长期最终形态：产品行为、稳定架构、数据兼容、视觉系统、诊断能力和质量合同。任务过程、迁移步骤、一次性调试信息和当前实现计划不进入编号文档。

## 文档分层

### 编号文档

| 文件 | 唯一职责 |
|---|---|
| `00_PRODUCT_AND_SCOPE.md` | 产品定位、核心能力、用户可观察行为、非目标 |
| `01_SYSTEM_ARCHITECTURE.md` | 四层架构、Application 模块、App Feature、依赖方向、状态 owner 与长期架构原则 |
| `02_RUNTIME_AND_NAVIGATION.md` | Process/Page/Playback/Background 生命周期、启动关闭、导航和桌面生命周期 |
| `03_BOOKS_PLAYBACK_AND_PROGRESS.md` | Books → Text → Speech → Playback → ReadingProgress 的核心听书链路 |
| `04_CACHE_AND_BACKGROUND_WORK.md` | 物理缓存、Coverage、Speech Plan、Prefetch、Active Cache、Export 与后台 owner |
| `05_DATA_AND_COMPATIBILITY.md` | 持久化数据、数据根、SQLite migration、用户数据保护与兼容边界 |
| `06_UI_AND_VISUAL_SYSTEM.md` | 页面/列表/UI 性能、导航呈现、主题、资源、Surface、视觉与交互合同 |
| `07_OBSERVABILITY_AND_DIAGNOSTICS.md` | 生产日志、普通性能遥测、诊断会话、隐私与诊断导出 |
| `08_QUALITY_AND_TESTING.md` | 测试分层、Architecture Fitness Tests、WPF 隔离、性能回归与质量门禁 |

### 长期专项规范

`docs/specs/` 只保存必须精确定义、不能用简要架构文档替代的协议/流水线合同：

- `specs/HTTP_TTS.md`
- `specs/REGEX_REPLACEMENT.md`

不要因为某个模块实现复杂就新增专项规范；只有存在长期协议兼容、执行语义或数据合同需要精确定义时才使用 `specs/`。

## Agent 开发入口

Agent 不应把 `docs/` 当成每次任务都要全文阅读的需求集合。

实际开发入口为：

1. 根目录 `AGENTS.md`
2. 根目录 `TASK_BACKLOG.md` 中的当前任务
3. 当前任务对应的 `tasks/Txxx_*.md`
4. 任务规格明确引用的少量长期文档

`tasks/` 中的实施规格是临时文件，任务完成并通过自动验收后删除；历史由 Git 保存。

## 长期文档维护规则

- 一条稳定规则只在一个 owner 文档中定义，其它文档只做链接或简短引用。
- 编号文档只写当前目标终态，不记录 `Txxx`、Phase、本轮迁移、临时 workaround 或 Codex 执行步骤。
- 具体毫秒数、队列容量、SQL 索引、内部类名等可调实现参数通常不属于长期产品合同。
- 已确认决策直接成为对应 owner 文档中的当前事实，不另建长期 `DECISIONS.md` 复制一份。
- 工程执行规则放在 `AGENTS.md`，不在 `docs/` 再维护一份 Engineering Conventions。
- 当前任务和完成历史放在根目录 `TASK_BACKLOG.md`；不建立 `docs/archive/`。
- 一次性截图、trace、性能采样、诊断报告和临时迁移文档在任务完成后删除。
- 若实现证明长期目标存在根本冲突，Agent 应记录证据并停止相关扩张，由新的规划阶段修改 owner 文档。
