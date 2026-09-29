# T005：收口问题诊断证据聚合与退化可见性

## 背景

本轮实际运行中，生产 JSONL 已包含带正确 `diagnosticSessionId` 的 fatal exception，但导出的问题诊断包 `logs.jsonl` 为 0 bytes。当前关联日志读取对文件级/解析异常采取静默 catch，容易把“证据读取失败”表现成“没有证据”；单条损坏 JSONL 也可能使同一文件后续有效记录整体被放弃。

## 权威参考

- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 必须完成

1. 复核 `SqliteDiagnosticSessionExportService` 的职责，把关联生产日志读取收敛为明确的小型职责；是否提取内部 reader 类型由代码清晰度决定，不为了分层而机械拆类。
2. 关联日志继续使用现有稳定 correlation（优先 `diagnosticSessionId`，必要时结合 process/time coverage），不复制完整异常到 `.nsdiag` 只为方便导出。
3. 修正单条 malformed JSONL、单个文件不可读、正在轮转等情况的 best-effort 行为：单条坏记录不得放弃整个文件后续有效记录。
4. 日志读取结果必须能区分：
   - complete：读取完成，允许匹配记录为 0；
   - partial：部分文件/记录不可读但仍获得部分证据；
   - unavailable：相关日志证据无法可靠读取。
   具体类型/命名可自行决定，但语义必须稳定。
5. `summary.md` 客观写出问题诊断证据完整性/退化状态；不得自动推断问题根因。
6. `logs.jsonl` 仍只包含关联的生产日志记录；导出保持本地、脱敏和原子提交。
7. 诊断自身的读取/导出失败继续进入 Production Logging，但不得递归制造第二套诊断故障链。

## 复杂度边界

- 不建立全局 `DiagnosticHealth` 服务、通用 evidence pipeline、插件系统或统一数据湖。
- 状态只服务于当前导出结果和必要的 Session/Export 摘要。
- 不引入 Crash Dump/Minidump、自动截图或新的用户配置。
- 不修改 `.nsdiag` schema；如确有必要，停止并请求数据库变更授权。

## 永久测试

至少覆盖：

- 一个带正确 Session correlation 的 fatal/error 日志能够进入问题诊断 `logs.jsonl`；
- 没有匹配记录时导出为 complete + empty，而不是 degraded；
- 单条损坏 JSONL 不阻断同文件后续有效关联记录，结果为 partial；
- 不可读日志源不会使整个 ZIP 导出失败，并在 summary 中体现 partial/unavailable；
- 既有隐私过滤和 atomic bundle 行为继续成立。

## 自动验收

运行问题诊断导出/日志轮转/Session store focused integration tests，并在阶段收口时执行完整 Release 质量门禁。清理所有用于复现本轮日志问题的临时文件和脚本。
