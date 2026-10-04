# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：基于代码库审计的维护性清理

本阶段根据 `CODEBASE_AUDIT_REPORT.md` 安排，目标是收紧输入资源边界、修复异步状态竞争、删除已完成迁移留下的冗余路径，并降低工程维护面。任务按依赖顺序执行；每项详细范围与验收条件见对应 `tasks/Txxx_*.md`。

本阶段不重新设计产品功能，不引入新的通用框架，不擅自改变持久化数据合同。审计中关于 `LegacyRuleId` / `SynthesisProfiles.RuleId` 的持久数据删改、Source 模型撤销、HTTP 重定向凭据行为和路径 TOCTOU 均未获充分证据或数据授权，不纳入本轮实施任务。

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短成果摘要
- `[!]` 阻塞，仅用于必须由用户决定的新产品、架构、隐私或持久化边界

默认按编号串行执行。明确要求连续执行整个 Backlog 时，可按顺序继续，不等待人工验收。实施任务保持仓库可构建、可测试；本阶段不包含 staged breaking migration。

任务完成后，满足 task spec 的自动验收、清理临时产物、在此处记录简短成果并删除对应 task spec。已完成的历史实施细节由 Git 保存，不在 Backlog 复制。

## 3. 清理任务

### 输入与运行时边界

## [x] T016（P2）：为章节规则正则设置执行时限

目标：让章节规则校验与导入切章共用有界正则执行策略，超时以稳定且可理解的规则/导入失败呈现，不保留无 timeout 的生产执行路径。

依赖：无。审计依据：F01。

完成成果：章节规则编辑与切章统一使用 100 ms Regex timeout；超时返回明确导入失败。Focused tests、格式检查与 Release build 通过。

## [x] T017（P2）：限制 HTTP TTS 成功响应的落盘字节数

目标：在成功响应写入临时音频的单一流复制边界执行字节预算；超过预算时取消复制、删除部分文件并返回稳定失败，不增加不一致的预检/二次复制路径。

依赖：无。先核实现有 Provider 格式与产品音频长度所需范围；审计依据：F02。

完成成果：在唯一响应落盘复制点按实际读取字节限制为 64 MiB，超限返回稳定失败并清理部分文件；focused tests 13 项、格式检查与 Release build 通过。

## [x] T018（P2）：修复 Active Cache 取消与 CTS 替换竞态

目标：确保取消活动批次时，CancellationTokenSource 不会在检查/取消期间被并发替换并释放；复用现有任务 owner 和同步边界，不增加新的同步层。

依赖：无。审计依据：F03。

完成成果：在现有 active-slot 锁内完成 CTS 取消与状态转换；受控替换竞态用例、focused tests、格式检查与 Release build 通过。

## [x] T019（P2）：确认并收敛 Playback 替换提交边界

目标：验证目标 checkpoint 成功后、目标音频准备失败或取消时的 snapshot、持久进度与 Resume 行为；若当前行为违反长期合同则修复 rollback/提交边界，否则记录证据并关闭，不为测试假设改变产品语义。

依赖：无。审计依据：I01。

完成成果：播放型替换在目标音频启动后才保存目标 checkpoint；准备失败或取消时恢复旧 session、snapshot 与进度。Playback focused tests 62 项、格式检查与 Release build 通过。

### 迁移残留与数据读取

## [x] T020（P2）：移除无生产用途的 BackgroundTaskRegistry

目标：确认不存在反射或外部动态入口后，删除无生产调用的 registry、startup/shutdown wiring 及只保护该类型实现形状的测试；保留真实启动维护行为。

依赖：无。审计依据：S01。

完成成果：确认无生产注册或动态入口后删除 registry、空 shutdown wiring 与专属实现测试；保留 startup 直接等待维护及真实后台 owner 的有界关闭。Startup/组合根/架构 focused tests 31 项、格式检查与 Release build 通过。

## [x] T021（P2）：合并 Provider 限流实现路径

目标：在保留并发许可、排队、pace、retry-after、取消与 lease 行为的前提下，将 Provider-keyed 调用收敛到一条 typed limiter path，删除 synthetic RuleId/string adapter 与不再独立使用的旧接口。

依赖：T018。审计依据：F04 / S02。

完成成果：ProviderRequestLimiter 直接以 typed ProviderId/RateLimit 持有唯一调度状态，删除旧接口、synthetic RuleId/string adapter 和重复注册；既有队列、pace、retry-after、取消与 lease 核心测试迁移至 typed API。Speech/Infrastructure/组合根 focused tests 110 项、格式检查与 Release build 通过。

## [ ] T022（P2）：移除 CacheCatalog 的逐本 fallback

目标：将生产批量目录依赖的 `IBookLibraryQuery` 明确为必需依赖，删除逐本 metadata 查询 fallback 和虚构默认进度/时间值；保留单本详情查询能力。

依赖：无。审计依据：S07。

### 工程门禁与导入并发

## [ ] T023（P2）：将 CI 全局质量门禁移出测试矩阵

目标：restore、format、solution build 与已由 solution build 覆盖的 Gallery build 不按每个测试项目重复执行；保留各测试层独立、可辨识的结果及失败状态。

依赖：无。审计依据：F05 / S03。

## [ ] T024（P2）：精简 Architecture Fitness 源码扫描器

目标：保留长期架构合同所需的依赖方向、模块边界、owner 与 trust-boundary 检查；删除冻结私有实现形状、重复穷举 parser 行为及仅支撑这些断言的 fixture/helper。

依赖：无。不得整体移除 Architecture Fitness Tests。审计依据：F06 / S04。

## [ ] T025（P2）：缩小 BookMutationGate 独占区

目标：在证明导入解析/staging、目标重新验证、文件 journal 和 SQLite commit 的并发及恢复语义后，将 singleton gate 缩到真正需要串行化的区间；若证明不足，保留现状并记录具体阻碍，不冒险释放锁。

依赖：无。审计依据：D01 / S05。

## [ ] T026（P2）：评估并降低大型 TXT 导入的峰值内存

目标：先明确当前支持的本地 TXT 范围并量化全文读取、规范化、hash、切行的重叠表示；只在有明确收益时调整数据流，避免无目标的通用 streaming 框架或改变章节识别语义。

依赖：无。审计依据：大型 TXT 导入内存风险（见审计报告技术债与调查候选）。

### 小型重复项与维护文档

## [ ] T027（P3）：收敛 UI 资源重复并核实 Gallery-only 样式

目标：在验证 WPF Application resource 查找后，将重复的 `BooleanToVisibilityConverter` 声明收敛到合适的共享 owner；核对 ComboBox 重复模板和 Gallery-only 控件样式的实际合同，删除已确认无用途的副本/bridge/fixture。

依赖：无。只删除已证明可省略的资源，不以 Gallery 当前无产品 caller 单独判死。审计依据：S08 / I06 / I07。

## [ ] T028（P3）：移除 Visual Review manifest 的过期 fallback

目标：让 `Generate-VisualReviewManifest.ps1` 只接受当前 Gallery manifest 结构，删除旧 `Scenarios` 和缺失 `scene` 的默认兼容路径；保留路径/hash 校验和索引生成。

依赖：无。审计依据：S09。

## [ ] T029（P3）：收敛 AGENTS 与长期 owner 文档的重复规则

目标：让 `AGENTS.md` 保留执行时必须直接看到的硬约束与 owner 文档入口，将稳定 UI/test 合同的详细定义归还唯一 owner 文档；不得削弱隐私、安全、持久化授权或验收约束。

依赖：无。当前 Backlog 历史日志已在本次计划整理中移除。审计依据：AGENTS 与 owner 文档规则重复（S06）。

## [ ] T030（P3）：统一 Release 输出与 ZIP 内容校验规则来源

目标：逐项比较 publish 目录与最终 ZIP 的 required/excluded predicates；仅消除规则漂移和重复维护，保留对最终封包内容及 tag/checksum 的完整验证。

依赖：无。不得以删掉其中一层校验作为简化。审计依据：D05。

## 4. 暂不安排

- `LegacyRuleId` / `SynthesisProfiles.RuleId`：涉及已有 SQLite 数据与恢复路径；先完成读取者和版本兼容调查，并取得具体持久化变更授权后再排期。
- Source 模型：属于已批准的长期 Book/Source 方向，不因当前仅有 Local Source 实现而撤销（I03）。
- HTTP 跨主机重定向凭据语义、数据根 TOCTOU：当前证据不足以形成修复任务（I04 / I05）。
- App 对 Domain 的传递项目引用：目前属于低置信度工程表达问题，没有明确收益前不单独建立重构任务（D06）。
- resolver convenience constructors、release 目录/ZIP 双层校验：前者收益不足，后者各层核验职责不同；不作为删除目标（审计报告 9.5–9.6）。
