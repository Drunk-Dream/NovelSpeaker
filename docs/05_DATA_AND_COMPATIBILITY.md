# 数据与兼容

## 1. 数据权威来源

| 数据 | 权威来源 |
|---|---|
| 外部小说正文 | 用户 TXT |
| 书籍/章节元数据 | SQLite |
| 当前活动播放位置 | Playback session / PlaybackSnapshot |
| 可恢复阅读进度 | SQLite ReadingProgress |
| 用户规则与设置 | 正式持久化 store |
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
- Scoop 的典型 `current\\Data -> persist\\novelspeaker\\Data` 布局必须作为受支持场景纳入自动测试。

用户显式选择的导出目标不属于应用数据根。Windows 保存文件对话框只决定最终导出文件的位置和文件名；导出目标可以位于数据根之外，应用内部源数据位置不得要求用户手动选择。导出实现只操作用户选择的目标文件及其同目录临时文件，不借此扩大应用内部存储访问范围。

## 3. SQLite migration

- 已发布 migration append-only。
- 不修改、合并、删除或重编号已发布 migration。
- schema 变化必须有升级测试。
- 内部 namespace/API/目录重构不得产生无意义 migration。
- 已发布用户数据兼容与内部代码兼容是两个不同问题；项目不为内部 compatibility 长期保留 wrapper。

## 4. ReadingProgress

- 当前活动书籍即时位置由 Playback session/snapshot 提供。
- SQLite ReadingProgress 是重启和非活动书籍的恢复基线。
- 页面不得直接写 ReadingProgress。
- 显式跳转成功后及时 checkpoint。
- 不进行逐毫秒高频 SQLite 写入。

## 5. Book 与 TXT

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

## 6. Query 与 read model

持久化层不向 UI 暴露“一切都有”的大型 DTO。Application 使用场景化 query：

- Library summaries；
- Book header；
- Chapter catalog；
- Current reading position；
- Chapter content；
- Cache status/coverage for explicit range；
- Cached book/chapter summaries。

避免 N+1；稳定 catalog 与动态 enrichment 分离。

## 7. Cache 数据

Cache 是可重建派生数据：

- 物理文件/index 可以被健康维护与清理。
- Speech Plan 只保存当前有意义的派生版本，不保存无意义历史副本。
- 配置指纹使用版本化规范序列化，避免字段顺序/默认值导致错误身份。
- Cache 结构内部重构不要求长期 compatibility reader，除非该格式已经形成明确外部合同。

## 8. 删除与恢复

SQLite 外键级联只能处理记录，不能代替真实文件协调。

删除书籍或缓存时必须明确协调：

- SQLite records；
- internal file；
- cache index/file；
- ReadingProgress；
- 必要的恢复/补偿状态。

任何恢复路径都不能越过数据根安全边界。

## 9. 诊断数据生命周期

生产日志、性能遥测和诊断会话属于辅助诊断数据，不是业务真值。

- 性能遥测默认关闭，历史可由用户清除。
- 普通遥测内部保留时间/容量属于实现策略，不形成用户可配置合同。
- 已结束 `.nsdiag` 不自动删除；用户通过系统文件管理器管理。
- 每个诊断会话具有用户可设的硬容量上限。
- 诊断导出包是派生交换格式，不成为第二份运行时真值。
- 诊断基础设施损坏、满盘或写入失败不得损坏业务数据。

## 10. 隐私边界

结构化日志、普通遥测和 `.nsdiag` 默认禁止记录：

- 小说正文；
- 书名和章节标题；
- 完整本地路径；
- 完整 URL / Query String；
- HTTP Header / Body；
- Token / API Key；
- TTS 文本；
- Regex 规则正文；
- SQL 参数中的用户数据；
- 缓存音频内容。

“脱敏诊断摘要”同样不得直接输出完整应用数据目录、日志目录、导出目标路径等绝对路径。需要表达存储状态时使用逻辑名称、布尔状态或脱敏后的有限信息。

诊断会话允许使用只在当前 Session 内有效、不可反向映射的匿名对象关联，例如 `Book #1`、`Chapter #3`。

用户主动触发“截取当前窗口”时，截图可以包含 NovelSpeaker 当前界面上的用户内容；该行为必须明确由用户主动触发，禁止自动截图或录屏。

## 11. 兼容边界

必须长期兼容：

- 已发布 SQLite migration 与正式用户数据；
- 用户外部 TXT；
- 用户规则/设置；
- 明确发布的规则格式和稳定交互语义。

不承诺长期兼容：

- 内部 namespace/class/interface；
- 未发布诊断内部实现；
- 临时 task/spec；
- internal JSON payload 的未发布实现细节；
- 为架构迁移存在的临时 adapter/wrapper。
