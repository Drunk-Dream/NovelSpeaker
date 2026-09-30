# 数据与兼容

## 1. 数据权威来源

| 数据 | 权威来源 |
|---|---|
| 外部小说正文 | 用户 TXT |
| 书籍/章节元数据 | SQLite |
| 章节/元数据/正则规则配置 | 对应正式持久化 store |
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
- 开发阶段从旧 HTTP TTS Rule 模型迁移到 Speech Provider 时，v8 migration 只转换能按新合同安全表达的配置；不可转换项静默丢弃，不新增迁移报告或跳过项表。成功迁移后删除旧规则表；旧运行路径在本阶段后续任务中清理，不双读、不双写，也不保留旧格式恢复接口。
- 旧 TTS Rule 的 `IsEnabled`、Legado 兼容字段等没有新 Provider 对等语义时，不为它们建立长期兼容状态；原本禁用的可转换项迁移为普通 Provider，但不自动成为 CurrentProvider。旧名称发生大小写不敏感冲突时，确定性生成唯一名称。
- v9 增加 `EdgeSpeechProviderConfigs`，以 ProviderId 为主键/外键，VoiceId、FriendlyName、Locale、Gender 可空以表达未配置；`SpeechProviders` 对 Edge 类型使用唯一索引保证单实例。升级保留现有 Provider、排序和缓存，失败时事务回滚。
- 新增 Book Description、元数据规则持久化或其它本轮 schema/settings 持久化字段时，继续遵守 `AGENTS.md` 的逐项授权要求；产品文档确认目标行为不等价于数据库迁移授权。

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
- 用于分享的 HTTP Provider 交换格式不包含 ProviderId、SortOrder、CurrentProvider 或设备运行元数据。
- Microsoft Edge 是固定单实例，但仍然是普通 Provider Instance；首次启用实验功能时才创建，之后隐藏不删除。
- Edge 的 VoiceId 是合成身份，其余 Voice 字段是离线显示快照；完整 Voice Catalog 仅保存在进程内，约一小时 TTL，不进入持久化。
- `settings.json` 的 `EnabledExperimentalFeatureIds` 默认空集合；保留未知 ID，仅展示当前注册功能。启停 Edge 的设置变更与必要的 CurrentProvider 清空在同一次设置保存中完成，停用失败保留原选择；重新启用不自动恢复选择。

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
→ detect explicit chapter titles
→ extract filename/header metadata
→ apply chapter rules + optional blank-line chaptering
→ persist book/chapter metadata
```

Book 元数据长期支持 Title、Author、Description。Description 属于书籍持久元数据，不从章节正文派生显示时临时计算。

文件名元数据规则、正文头部元数据规则、章节规则与“空行分章”的精确执行语义见 `specs/BOOK_IMPORT.md`。

旧 `BookFileNameTemplate` 设置与模板解释逻辑直接废弃；不把旧值迁移成新元数据规则，不维持双读或兼容层。新版本首次使用元数据规则时以新默认规则集为准。

重新分章、删除和恢复必须保持路径安全与事务/补偿语义。源 TXT 不被覆盖或重写。规则/导入设置变化不静默重写已经导入的书籍；只有明确重新导入/重建时才使用最新配置。

## 7. Query 与 read model

持久化层不向 UI 暴露“一切都有”的大型 DTO。Application 使用场景化 query：

- Library summaries；
- Book header（包含可选 Description）；
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

## 9. Provider 与规则导入/导出

HTTP Provider 使用 NovelSpeaker 自有版本化交换格式；章节规则、正则替换规则、文件名元数据规则和正文头部元数据规则分别使用各自的版本化交换格式。

共同原则：

- 一个文件/剪贴板文档可以包含一个或多个同类型项。
- 批量导出把当前选择集写入一个文档，而不是为每项弹出独立保存对话框。
- 批量导入逐项解析与校验；单项失败不回滚其它有效项。
- 完全重复项跳过；同名不同内容按对应工作台的唯一命名/追加规则处理，不覆盖既有项。
- 新导入项保持文档中的稳定顺序并追加到对应排序末尾。
- 导入不自动改变 CurrentProvider，也不自动切换正在编辑的规则/Provider。

Provider 特有规则：

- 配置相同但名称不同的 HTTP Provider 仍新增；同名但配置不同则自动生成唯一名称后新增。
- Microsoft Edge 等不可分享的内置 Provider 不参与导入/导出或批量导出选择。
- Provider 分享导出包含完整 HTTP 配置，可能包含 API Key、Token、Cookie 等敏感值。
- 不建立自动 Secret 分离或自动脱敏导出；导出前必须明确提醒用户检查敏感凭据。

私人配置备份与用于分享的 Provider/规则导出是两个不同语义，不得共用“合并导入”逻辑来替代完整恢复。

## 10. 配置备份与恢复

第一版配置备份是本地、私人、版本化的完整配置快照。

### 包含

- App Settings，包括主题、播放/文本相关全局设置、实验功能状态、CurrentProvider 等正式设置；
- 全部 Speech Provider 持久配置及排序；
- HTTP Provider 中的 API Key、Token、Cookie 等敏感凭据；
- 章节规则；
- 正则替换规则；
- 文件名元数据规则；
- 正文头部元数据规则。

为了恢复 CurrentProvider、排序和其它内部引用关系，私人备份可以保存分享格式刻意省略的内部稳定身份；它不是可公开分享的交换文件。

### 不包含

- 用户 TXT、内部书籍副本或 Library catalog；
- Book/Chapter 内容与 ReadingProgress；
- Speech Plan、音频缓存和 Active Cache 运行态；
- 生产日志、普通性能遥测、诊断会话或问题诊断导出；
- 临时文件和其它可重建派生数据。

### 恢复语义

- 恢复前先完整解析并校验备份文档；明显损坏或不支持的 schema 不进行部分写入。
- 恢复采用“替换当前配置快照”语义，而不是与现有配置静默合并。
- 在开始替换前明确提示当前配置会被覆盖；书籍、阅读进度和缓存不受影响。
- 跨多个持久化 store 的恢复必须有明确协调/补偿边界，不能在失败后留下部分旧、部分新的不可解释状态。
- 恢复结束后由各配置 owner 通过现有 typed change/invalidation 语义刷新运行时；不得直接从 UI 手工同步各模块内部状态。
- 第一版备份文件不要求加密，但创建时必须明确提示其中可能包含敏感凭据，应按私密文件保存。
- 第一版不包含 WebDAV、账号同步或远程上传。

## 11. 删除与恢复

SQLite 外键级联只能处理记录，不能代替真实文件协调。

删除书籍或缓存时必须明确协调：

- SQLite records；
- internal file；
- cache index/file；
- ReadingProgress；
- 必要的恢复/补偿状态。

删除当前 Provider 时必须同时把 CurrentProvider 清空为 None，不自动切到其它 Provider。

任何恢复路径都不能越过数据根安全边界。

## 12. 诊断数据生命周期

生产日志、性能遥测和诊断会话属于辅助诊断数据，不是业务真值。

- 性能遥测默认关闭，历史可由用户清除。
- 普通遥测内部保留时间/容量属于实现策略，不形成用户可配置合同。
- 已结束 `.nsdiag` 不自动删除；用户通过系统文件管理器管理。
- 每个诊断会话具有用户可设的硬容量上限。
- 诊断导出包是派生交换格式，不成为第二份运行时真值。
- 诊断基础设施损坏、满盘或写入失败不得损坏业务数据。

## 13. 隐私边界

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

配置备份/Provider 导出是用户主动创建的业务文件，不属于诊断脱敏输出。它们可以包含敏感 Provider 凭据，但必须在创建前清楚提示风险。

## 14. 兼容边界

必须长期兼容：

- 已发布 SQLite migration 与正式用户数据；
- 用户外部 TXT；
- 正式发布后的 Provider 配置/设置与规则；
- 明确发布的 Provider/规则交换格式；
- 正式发布的配置备份格式；
- 已发布的稳定交互语义。

当前开发阶段不继续承诺：

- 旧 NovelSpeaker TTS Rule 导入格式；
- Legado HTTP TTS Rule 导入兼容；
- 旧 `BookFileNameTemplate` 及其模板格式；
- `source`、`java.*` 等仅为 Legado 兼容存在的模板 API；
- 内部 namespace/class/interface；
- 未发布诊断内部实现；
- 临时 task/spec；
- internal JSON payload 的未发布实现细节；
- 为架构迁移存在的临时 adapter/wrapper。
