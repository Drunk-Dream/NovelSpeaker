# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：代码库审计后的维护与清理

本轮根据 2026-10-04 全仓库扫描结果安排，从 T001 重新编号。每项当前任务的详细范围、证据与验收条件以其 task spec 为准；上一轮已完成的 T016–T030 保留在 Git 历史中，不在当前调度表重复维护。

任务顺序优先处理明确的资源生命周期、重复 UI 投影和权限范围问题，再处理低风险死代码和需要测量/语义核实的维护候选。审计中的低置信度事项不自动转为重构任务。

本轮不改变 SQLite schema、持久化数据或用户外部 TXT；任何后续方案若需要新增持久状态或迁移，须先单独取得授权。遵守 `AGENTS.md`、各 task spec 与被引用的长期 owner 文档。

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，并追加简短成果
- `[!]` 阻塞，仅用于必须由用户决定的新产品、架构、隐私或持久化边界

默认按编号串行执行。明确要求连续执行整个 Backlog 时，可按顺序继续。每项任务必须满足 task spec 的自动验收；完成后清理临时产物、在此处记录成果并删除对应 task spec。不得因可选人工验收未执行而阻塞。

## 3. 审计后任务

### 临时文件、运行时投影与权限

## [x] T001（P2）：清理异常退出遗留的临时语音文件

目标：为 RuleTests、ProviderPreviews 和 TTS 临时音频补齐安全的启动残留清理，避免生成的语音内容在异常退出后无限期留存。

完成成果：RuleTests、ProviderPreviews 与 TTS staging 改用进程租约目录；启动维护和 Clear All 仅回收已失主目录，保留活动文件、持久缓存和 Local Source。旧版无租约残留因无法安全区分活跃实例而跳过。

## [ ] T002（P2）：避免 Active Cache 进度更新重建整份章节列表

目标：保留 Active Cache coordinator 的快照所有权与 UI 现有排序/状态，只更新变化的章节投影，避免每个片段完成后清空并重新创建全部行。

依赖：无。范围与验收见 [T002 task spec](tasks/T002_Incremental_Active_Cache_Projection.md)。

## [ ] T003（P2）：收窄 Release workflow 的写权限

目标：让校验与质量门禁 job 只持有所需的读取权限，仅 publish job 获得创建/上传 Release 所需的 `contents: write`。

依赖：无。范围与验收见 [T003 task spec](tasks/T003_Narrow_Release_Workflow_Permissions.md)。

### 死代码与文档漂移

## [ ] T004（P3）：删除旧 HTTP 迁移 codec 未使用的序列化方法

目标：删除 `SerializeHeaders`、`SerializeRequestOptions` 及仅供其使用的 writer 代码；保留旧数据库迁移仍需要的读取/解析行为。

依赖：无。范围与验收见 [T004 task spec](tasks/T004_Remove_Unused_Legacy_HTTP_Serializers.md)。

## [ ] T005（P3）：修正文档中的 Regex 规则导航归属

目标：将运行时导航文档中的 RegexReplacementRules 父级路由描述更新为当前 Settings 路由，并核对相关术语和链接。

依赖：无。范围与验收见 [T005 task spec](tasks/T005_Correct_Regex_Route_Documentation.md)。

### 先测量再决定

## [ ] T006（P2 调查）：量化启动路径迁移的重复扫描成本

目标：测量启动时对 `LocalBookSources` 和 `AudioCacheEntries` 路径列重复扫描的成本，并确认当前/最旧受支持数据库是否还可能含待迁移绝对路径；本任务只形成有证据的处置决定。

依赖：无。不得未经授权增加 schema 标记、迁移或其它持久状态。范围与验收见 [T006 task spec](tasks/T006_Measure_Startup_Path_Migration_Scan.md)。

## [ ] T007（P2 调查）：评估 SourceContentReader 的整本正文缓存收益

目标：量化整本正文字符串的保留内存与重复章节读取收益，据此决定保留、缩短生命周期或有界化；不得未经测量直接删除缓存。

依赖：无。范围与验收见 [T007 task spec](tasks/T007_Evaluate_Source_Content_Cache.md)。

### 测试维护

## [ ] T008（P3）：去除可共享的诊断测试时钟重复实现

目标：对照 TestKit 的 Manual/FixedTimeProvider 与诊断测试中的本地实现；语义完全等价时复用共享实现并删除重复类，保留必要的特例。

依赖：无。范围与验收见 [T008 task spec](tasks/T008_Consolidate_Diagnostic_Test_Clocks.md)。

## [ ] T009（P3）：清理不承载行为合同的 WPF 实现细节断言

目标：结合断言上下文、历史回归和质量合同，删除仅锁定资源 key、brush identity、icon enum 或内部 visual-tree 形状的低价值断言；保留用户行为、导航、键盘与辅助功能合同。

依赖：无。范围与验收见 [T009 task spec](tasks/T009_Trim_WPF_Implementation_Detail_Assertions.md)。

## 4. 暂不排期

- `SourceContentReader`、启动路径扫描任务完成前，不引入缓存淘汰器、扫描完成标记或持久化状态；任何 schema/持久化变更需另行授权。
- Chapter/Regex 规则 workspace 的交换格式重复：导入差异和内建规则策略尚未证明可统一；不先建立通用规则框架。
- `ActiveSourceContext.CatalogVersion` 的 first-chapter-ID 约定：当前全量替换会生成新 ChapterId，未发现行为缺陷；后续可在相关查询改动中评估 SQL 去重，不新增持久版本列。
- `AppSettingsService` 通知异常语义与锁内发布、`PlayerAutoScrollCoordinator` 生命周期、`PlaybackCoordinator` 拆分及规则导入反馈差异：当前证据不足以支持行为或 owner 变更；不按文件大小单独拆分协调器。
- AppSettings / Source 模型 / HTTP 重定向凭据 / storage-root TOCTOU 等产品、兼容与安全边界，继续遵守长期文档和现有授权约束；本轮审计没有批准改变这些边界。
