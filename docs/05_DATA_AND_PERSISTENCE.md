# 数据与持久化

## 1. 数据所有权

| 数据 | 位置 | 说明 |
|---|---|---|
| 书籍、章节、规则、进度、缓存索引、操作 journal | SQLite | 当前 schema version 6 |
| 规范化正文 | `Books/<book-id>/content.txt` | 应用内部副本 |
| TTS 音频 | `Cache/Tts/...` | 数据库保存索引/统计 |
| 非敏感设置 | `settings.json` | 原子保存 |
| 日志 | `Logs/` | 脱敏 |
| 操作暂存 | `Operations/<operation-id>/` | 导入/删除/恢复使用 |

外部用户 TXT 永远不是应用可写数据。

## 2. 数据目录

```text
%LocalAppData%/NovelSpeaker/
├─ novelspeaker.db
├─ settings.json
├─ Books/
├─ Cache/Tts/
├─ Operations/
└─ Logs/
```

所有数据库记录的内部路径都必须通过集中 resolver 规范化和验证。删除、移动、覆盖前必须确认目标仍位于应用数据根目录，并拒绝可逃逸根目录的路径/reparse-point 情况。

## 3. SQLite 兼容

当前数据库版本为 6。核心表包括：

- `SchemaVersion` / `AppMetadata`
- `Books`
- `Chapters`
- `ChapterRules`
- `RegexReplacementRules`
- `HttpTtsRules`
- `ReadingProgress`
- `AudioCacheEntries`
- `BookOperations`

规则：

- 已发布 migration 只追加，不修改、合并或重新编号。
- 新库和受支持旧版本升级都必须走同一 migration runner。
- 高于当前版本的数据库安全拒绝。
- SQLite row/connection/transaction 类型不得泄露到 Application API。

## 4. 书籍导入

导入使用可恢复流程：

```text
analyze source
  → normalize text
  → stage internal file
  → write DB metadata/chapters
  → finalize internal file
  → complete operation journal
```

失败或中断后只清理应用自己创建的暂存/内部文件，不能修改外部源文件。重复检测基于规范化输入所定义的稳定哈希策略。

## 5. 正文与动态分段

- SQLite 保存章节范围/元数据，规范化正文保存在内部文件。
- 播放时读取章节文本，再按当前文本设置动态分段。
- 正则替换只改变 DisplayText/SpeechText，不重写原始章节范围。
- “未加载”“已加载但无可播放段落”必须是不同状态。

## 6. AudioCacheKey

缓存键继续使用现有版本化结构，并包含能唯一确定播放结果的关键输入，例如：

- 书籍/章节/段落位置。
- TTS 规则身份。
- 语速。
- 最终 SpeechText。
- `AudioCacheKey.CurrentVersion` 命名空间。

架构清理不得顺手改为另一种内容寻址策略。确需改变时必须新增版本和兼容/迁移测试。

## 7. 缓存写入与保护

缓存写入：

```text
generate validated audio
  → write staging file
  → flush/close
  → atomic move/replace
  → persist index
```

- 临时文件 owner 必须明确，失败和取消均清理。
- 正在播放、生成、预取、主动缓存写入或导出的缓存条目受保护。
- LRU 和用户清理都必须经过同一保护 registry。
- 缓存文件丢失/损坏时索引可修复，不能导致播放状态机永久失效。

## 8. 主动缓存状态

主动缓存批次是运行时任务，不要求持久化历史任务中心。

批次的规则、语速和文本配置在创建时冻结；生成出来的音频按正常 `AudioCacheKey` 落盘，因此即使批次完成后用户更改设置，旧缓存仍是合法的独立缓存版本，由 LRU 决定是否回收。

## 9. 缓存管理查询

Application 提供：

- 全局总占用与上限。
- 按书籍的大小、已缓存章节数。
- 按章节的大小、条目数和当前配置下的完整度。
- 受保护条目和不可清理原因。

UI 不直接查询 SQLite 或扫描文件系统。

## 10. MP3 导出

导出只读取当前规则 + 当前语速 + 当前文本处理结果对应的完整章节缓存。

每章输出一个 MP3：

```text
<选择的根目录>/
└─ <安全书名>/
   ├─ 001_<安全章节名>.mp3
   ├─ 002_<安全章节名>.mp3
   └─ ...
```

规则：

- 同章节多个音频段按播放顺序解码/合并并统一输出 MP3。
- 不同章节不合并成整书文件。
- 不完整章节不能导出；导出动作不得自动生成缺失缓存。
- 文件名清理 `<>:"/\\|?*`、控制字符、Windows 保留设备名、尾部空格/点，并保证最终路径长度可用。
- 已存在同名文件时生成 `name (2).mp3`、`name (3).mp3`，绝不静默覆盖。
- 导出临时文件在取消/失败时清理；用户已存在文件不纳入回滚。

## 11. 设置存储

`settings.json` 保存非敏感桌面设置，例如：

- 默认语速、预取数量、文本分段参数。
- 主题、日志等级、书名解析模板。
- 缓存上限、当前 TTS 规则选择。
- 关闭窗口行为、启动最小化到托盘。
- 迷你播放器位置和置顶状态。

设置保存使用临时文件 + 原子替换。损坏 JSON 备份为带 UTC 时间戳的 `.corrupt` 文件并回退到规范化默认值。

“当前处于迷你模式”、定时停止剩余时间、主动缓存任务进度不持久化。

## 12. 删除与恢复

删除书籍和跨数据库/文件操作使用 operation journal 或等价可恢复协议。

- 删除书籍不影响应用数据根目录外文件。
- 缓存清理不删除书籍、章节、阅读进度或规则。
- 进程启动只恢复未完成记录；已完成记录不重复执行。
- 恢复失败输出脱敏诊断并保持可重试状态。

## 13. 隐私

- 设置、日志和操作 journal 不保存小说正文副本之外的敏感请求内容。
- 日志/异常/诊断不得写出完整 URL、Header、Body、Token 或其它凭据。
- TTS 规则中的敏感字段按规则模型正常持久化时，必须在所有展示和诊断出口脱敏。