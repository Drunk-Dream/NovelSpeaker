# 数据与兼容

## 1. 数据权威来源

| 数据 | 权威来源 |
|---|---|
| 外部小说正文 | 用户 TXT |
| 书籍/章节元数据 | SQLite |
| 当前活动播放位置 | Playback session / PlaybackSnapshot |
| 可恢复阅读进度 | SQLite ReadingProgress |
| Speech Provider 元数据、排序与类型配置 | 正式持久化 store |
| CurrentProvider 与其它用户设置 | Settings store |
| Speech Plan | Cache 管理的可重建派生数据 |
| 音频缓存 | Cache 管理的可重建文件 + index |
| Active Cache / Export 运行态 | Process coordinator snapshot |
| 普通性能遥测 | 本地可删除诊断数据 |
| 诊断会话 | `.nsdiag` 会话文件 |
| 生产日志 | 本地 JSONL |

外部 TXT 永不由应用写回。

## 2. 数据根与存储信任边界

正式应用数据位于程序最终解析得到的数据根。开发与自动测试使用独立数据根，避免污染真实用户数据。

显式开发/诊断覆盖必须是可识别的开发能力，不建立旧数据根的隐式探测、双读、回退或静默迁移。

应用管理的持久化路径必须经过统一的数据根解析、归属验证和信任边界检查：

- 最终选定的数据根本身是可信锚点。数据根及其祖先可以由安装器、包管理器或用户环境通过 Junction、Symlink 或其他 reparse point 映射，不得仅因为数据根本身或其祖先是 reparse point 就拒绝启动或访问。
- 数据根内部不得继续穿越未经应用管理的 reparse point。Books、Cache、数据库、日志、Telemetry、Diagnostics 等内部路径必须保持在同一逻辑数据根内，并拒绝内部链接逃逸。
- reparse-point 规则必须由统一的存储信任边界组件负责，Diagnostics、Cache、Books 等模块不得维护不同的私有规则。
- Scoop 的典型 `current\Data -> persist\novelspeaker\Data` 布局必须作为受支持场景纳入自动测试。

用户显式选择的导出目标不属于应用数据根。Windows 保存文件对话框只决定最终导出文件的位置和文件名；导出目标可以位于数据根之外，应用内部源数据位置不得要求用户手动选择。导出实现只操作用户选择的目标文件及其同目录临时文件，不借此扩大应用内部存储访问范围。

## 3. SQLite migration

- 已发布 migration append-only。
- 不修改、合并、删除或重编号已发布 migration。
- schema 变化必须有升级测试。
- 内部 namespace/API/目录重构不得产生无意义 migration。
- 已发布用户数据兼容与内部代码兼容是两个不同问题；项目不为内部 compatibility 长期保留 wrapper。
- 开发阶段从旧 HTTP TTS Rule 模型迁移到 Speech Provider 时，使用一次性 migration 转换能按新合同安全表达的配置；不可转换项跳过，并向用户展示一次成功/跳过数量与逐项原因。迁移完成后删除旧运行路径，不双读、不双写，也不保留旧格式恢复接口。
- 旧 TTS Rule 的 `IsEnabled`、Legado 兼容字段等没有新 Provider 对等语义时，不为它们建立长期兼容状态；原本禁用的可转换项迁移为普通 Provider，但不自动成为 CurrentProvider。旧名称发生大小写不敏感冲突时，确定性生成唯一名称。

## 4. Speech Provider 数据

Provider Type 与 Provider Instance 分离。

公共持久化至少表达：

- ProviderId；
- ProviderType；
- 全局唯一显示名称；
- SortOrder；
- 创建/更新时间等必要内部元数据。

类型专属配置使用 typed model，并由 Infrastructure 负责具体存储表示。不要让万能 JSON Config 字典进入 Domain/Application 公共合同。

规则：

- 所有 Provider 共用一份完整排序。
- 隐藏实验性 Provider 保留实例、配置和排序，不建立另一份“可见排序”。
- CurrentProvider 单独作为全局设置保存。
- HTTP Provider 导出格式不包含 ProviderId、SortOrder、CurrentProvider 或设备运行元数据。
- Microsoft Edge 是固定单实例，但仍然是普通 Provider Instance；首次启用实验功能时才创建，之后隐藏不删除。

## 5. ReadingProgress

- 当前活动书籍即时位置由 Playback session/snapshot 提供。
- SQLite ReadingProgress 是重启和非活动书籍的恢复基线。
- 页面不得直接写 ReadingProgress。
- 显式跳转成功后及时 checkpoint。
- 不进行逐毫秒高频 SQLite 写入。

## 6. Book 与 TXT

导入典型流程：

```text
choose TXT
→ validate path
→ detect encoding
→ normalize
→ apply chapter rules
→ persist book/chapter metadata
```

重新分章、删除和恢复必须保持路径安全与事务/补偿语义。源 TXT 不被覆盖或重写。

## 7. Query 与 read model

持久化层不向 UI 暴露“一切都有”的大型 DTO。Application 使用场景化 query：

- Library summaries；
- Book header；
- Chapter catalog；
- Current reading position；
- Chapter content；
- Provider summaries / Provider editor model；
- Cache status/coverage for explicit range；
- Cached book/chapter summaries。

避免 N+1；稳定 catalog 与动态 enrichment 分离。

## 8. Cache 数据

Cache 是可重建派生数据：

- 物理文件/index 可以被健康维护与清理。
- Speech Plan 只保存当前有意义的派生版本，不保存无意义历史副本。
- Provider synthesis fingerprint 使用版本化规范序列化，避免字段顺序/默认值导致错误身份。
- ProviderId、名称和排序不作为音频生成身份。
- Provider 配置变化后旧音频文件允许保留；fingerprint 不匹配时只视为当前配置不可用，不为此次变更强制清理旧物理文件。
- Cache 结构内部重构不要求长期 compatibility reader，除非该格式已经形成明确外部合同。

## 9. Provider 导入、导出与敏感值

HTTP Provider 使用 NovelSpeaker 自有版本化交换格式。

- 一个文件/剪贴板可以包含多个 Provider。
- 导入逐项解析，单项失败不回滚其它有效项。
- 只有名称大小写不敏感地相同且规范化 typed config 各字段相同才跳过；配置相同但名称不同仍新增，同名不同配置生成唯一名称后新增；不覆盖现有 Provider。
- 导入项追加到完整 Provider 排序末尾，不改变 CurrentProvider。
- 当前导出仍为单 Provider，但使用与多项导入相同的 envelope。
- Provider 导出包含完整 HTTP 配置，可能包含 API Key、Token、Cookie 等敏感值。
- 不建立自动 Secret 分离或自动脱敏导出；导出前必须明确提醒用户检查敏感凭据。
- 私人配置备份与用于分享的 Provider 导出是两个不同语义，后续同步/备份能力不得混淆两者。

## 10. 删除与恢复

SQLite 外键级联只能处理记录，不能代替真实文件协调。

删除书籍或缓存时必须明确协调：

- SQLite records；
- internal file；
- cache index/file；
- ReadingProgress；
- 必要的恢复/补偿状态。

删除当前 Provider 时必须同时把 CurrentProvider 清空为 None，不自动切到其它 Provider。

任何恢复路径都不能越过数据根安全边界。

## 11. 诊断数据生命周期

生产日志、性能遥测和诊断会话属于辅助诊断数据，不是业务真值。

- 性能遥测默认关闭，历史可由用户清除。
- 普通遥测内部保留时间/容量属于实现策略，不形成用户可配置合同。
- 已结束 `.nsdiag` 不自动删除；用户通过系统文件管理器管理。
- 每个诊断会话具有用户可设的硬容量上限。
- 诊断导出包是派生交换格式，不成为第二份运行时真值。
- 诊断基础设施损坏、满盘或写入失败不得损坏业务数据。

## 12. 隐私边界

结构化日志、普通遥测和 `.nsdiag` 默认禁止记录：

- 小说正文；
- 书名和章节标题；
- 完整本地路径；
- 完整 URL / Query String；
- HTTP Header / Body；
- Token / API Key；
- Provider 试听/合成文本；
- Regex 规则正文；
- SQL 参数中的用户数据；
- 缓存音频内容。

“脱敏诊断摘要”同样不得直接输出完整应用数据目录、日志目录、导出目标路径等绝对路径。需要表达存储状态时使用逻辑名称、布尔状态或脱敏后的有限信息。

诊断会话允许使用只在当前 Session 内有效、不可反向映射的匿名对象关联，例如 `Book #1`、`Chapter #3`。

用户主动触发“截取当前窗口”时，截图可以包含 NovelSpeaker 当前界面上的用户内容；该行为必须明确由用户主动触发，禁止自动截图或录屏。

## 13. 兼容边界

必须长期兼容：

- 已发布 SQLite migration 与正式用户数据；
- 用户外部 TXT；
- 正式发布后的 Provider 配置/设置；
- 明确发布的 Provider 交换格式和稳定交互语义。

当前开发阶段不继续承诺：

- 旧 NovelSpeaker TTS Rule 导入格式；
- Legado HTTP TTS Rule 导入兼容；
- `source`、`java.*` 等仅为 Legado 兼容存在的模板 API；
- 内部 namespace/class/interface；
- 未发布诊断内部实现；
- 临时 task/spec；
- internal JSON payload 的未发布实现细节；
- 为架构迁移存在的临时 adapter/wrapper。
