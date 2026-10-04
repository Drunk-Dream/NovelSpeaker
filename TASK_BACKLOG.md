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

## [x] T002（P2）：避免 Active Cache 进度更新重建整份章节列表

目标：保留 Active Cache coordinator 的快照所有权与 UI 现有排序/状态，只更新变化的章节投影，避免每个片段完成后清空并重新创建全部行。

完成成果：Shell 按批次与 ChapterIndex 保留章节行实例，进度快照只更新当前变化行，批次替换时才重建；排序、摘要、取消与终态通知保持原语义。

## [x] T003（P2）：收窄 Release workflow 的写权限

目标：让校验与质量门禁 job 只持有所需的读取权限，仅 publish job 获得创建/上传 Release 所需的 `contents: write`。

完成成果：Release workflow 默认与 validate/quality job 限为 `contents: read`，仅 publish job 显式拥有 `contents: write`；保留原有 tag、产物和发布校验流程。

### 死代码与文档漂移

## [x] T004（P3）：删除旧 HTTP 迁移 codec 未使用的序列化方法

目标：删除 `SerializeHeaders`、`SerializeRequestOptions` 及仅供其使用的 writer 代码；保留旧数据库迁移仍需要的读取/解析行为。

依赖：无。

完成成果：删除 `SerializeHeaders`、`SerializeRequestOptions` 及专属 writer/编码器依赖；旧 HTTP Provider 迁移读取逻辑和既有兼容测试保留。

## [x] T005（P3）：修正文档中的 Regex 规则导航归属

目标：将运行时导航文档中的 RegexReplacementRules 父级路由描述更新为当前 Settings 路由，并核对相关术语和链接。

依赖：无。

完成成果：运行时导航文档将 RegexReplacementRules 的稳定父级更新为 Settings，与路由定义及导航测试一致。

### 先测量再决定

## [x] T006（P2 调查）：量化启动路径迁移的重复扫描成本

目标：测量启动时对 `LocalBookSources` 和 `AudioCacheEntries` 路径列重复扫描的成本，并确认当前/最旧受支持数据库是否还可能含待迁移绝对路径；本任务只形成有证据的处置决定。

完成成果：审查确认 v12 将旧 Books.StoredFilePath 原样复制进 LocalBookSources.StoredContentPath；旧 v4–v11 数据库可保留绝对路径，而当前导入/音频索引 writer 使用 storage key，因此没有可证明安全的无状态跳过条件。合成 SQLite 两表各 1k/10k/100k 行（5% 合成绝对路径），逐表流式扫描并按当前批量更新模式执行，7 次回滚测量的中位耗时为 5.06/49.96/1345.24 ms，100k 行峰值 Python 跟踪分配中位数 0.80 MiB；结果不含 SQLite native 分配，Python 与 .NET 包装层差异使其不能视为应用启动基准。保留启动迁移，不新增持久 marker/schema；完整路径数据、合成库和 harness 均为临时且已删除。

## [x] T007（P2 调查）：评估 SourceContentReader 的整本正文缓存收益

目标：量化整本正文字符串的保留内存与重复章节读取收益，据此决定保留、缩短生命周期或有界化；不得未经测量直接删除缓存。

完成成果：追踪确认 singleton `ISourceContentReader` 服务于播放章节读取与 TXT 导出；导入由独立 `TextFileAnalyzer` 读取整本文本，不经过该缓存。临时 .NET 10 合成 UTF-8 ASCII 文件测量 1/10/50 MiB，暖机后比较 7 次 `File.ReadAllTextAsync` + 64 KiB `Substring` 与保留整本字符串后的切片：未缓存中位数 4.02/54.53/218.37 ms、分配 4.18/40.63/202.55 MiB/次；缓存切片为 0.0071/0.0082/0.0134 ms、约 0.126 MiB/次。这仅量化文件读取/字符串切片，不含缓存路径每次仍执行的 SQLite 元数据查询或后续分段/规则处理，不能视为端到端播放收益。缓存字符串保留约 2/20/100 MiB UTF-16 正文载荷；实际磁盘冷读与真实书籍大小分布未测，分配量含 .NET 文件读取临时开销。文件读取及切片成本随源大小显著下降，但缺乏真实分布以确定安全上限，故保留单条最近正文缓存；未来任何生命周期/上限调整须保持取消传播和并发读取正确性。本调查未改变注册或行为，合成文件与临时 harness 已删除。

### 测试维护

## [x] T008（P3）：去除可共享的诊断测试时钟重复实现

目标：对照 TestKit 的 Manual/FixedTimeProvider 与诊断测试中的本地实现；语义完全等价时复用共享实现并删除重复类，保留必要的特例。

依赖：无。

完成成果：诊断 store/export 测试复用 TestKit 的 Manual/FixedTimeProvider 并删除重复类；ViewModel 测试保留固定 UTC+8 本地时区特例并注明共享实现不匹配的原因。

## [ ] T009（P3）：清理不承载行为合同的 WPF 实现细节断言

目标：结合断言上下文、历史回归和质量合同，删除仅锁定资源 key、brush identity、icon enum 或内部 visual-tree 形状的低价值断言；保留用户行为、导航、键盘与辅助功能合同。

依赖：无。范围与验收见 [T009 task spec](tasks/T009_Trim_WPF_Implementation_Detail_Assertions.md)。

## 4. 暂不排期

- `SourceContentReader`、启动路径扫描任务完成前，不引入缓存淘汰器、扫描完成标记或持久化状态；任何 schema/持久化变更需另行授权。
- Chapter/Regex 规则 workspace 的交换格式重复：导入差异和内建规则策略尚未证明可统一；不先建立通用规则框架。
- `ActiveSourceContext.CatalogVersion` 的 first-chapter-ID 约定：当前全量替换会生成新 ChapterId，未发现行为缺陷；后续可在相关查询改动中评估 SQL 去重，不新增持久版本列。
- `AppSettingsService` 通知异常语义与锁内发布、`PlayerAutoScrollCoordinator` 生命周期、`PlaybackCoordinator` 拆分及规则导入反馈差异：当前证据不足以支持行为或 owner 变更；不按文件大小单独拆分协调器。
- AppSettings / Source 模型 / HTTP 重定向凭据 / storage-root TOCTOU 等产品、兼容与安全边界，继续遵守长期文档和现有授权约束；本轮审计没有批准改变这些边界。
