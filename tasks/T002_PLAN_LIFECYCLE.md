# T002 — Plan 计算与持久化解耦

- **类型/优先级：** Implementation / P0 Persistence。依赖 T001。
- **必读：** `AGENTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/05_DATA_AND_COMPATIBILITY.md`、`docs/specs/REGEX_REPLACEMENT.md`；`ChapterSpeechPlanService`、`PlaybackContentResolver`、`CacheAudioGenerationProvider`、`ActiveCacheCoordinator`、`SqliteChapterSpeechPlanStore`、`SqliteAudioCacheIndex/Facade`、`ExportChaptersService` 及直接相关测试。

## 核心原则

把 Plan **计算**从数据库 **保存**中拆出来；避免“加载正文 / 分段 = 无条件产生大量 `ChapterSpeechPlanSegments`”。不增加一大堆场景枚举、策略接口或额外的持久化状态机。

## 实施内容

1. 调整现有 Plan 构建职责，使纯计算路径只返回当前正文、文本配置下的可播放分段与可复原 Plan 身份，不隐含 SQLite 写入。将持久化明确交给真实使用缓存的边界。对多余包装/重复计算做减法。
2. Playback/Prefetch 加载可不保存 Plan，且不影响原有 target-first、段落标识、朗读和缓存命中。不要用长期强引用字典缓存正文、段数组、Plan 或全部目录的中间结果。
3. **首次真正产生 Ready 音频缓存时**，确保对应章节的 Plan 具备可用、持久、准确的元数据；重复缓存命中不无条件 Save。同章在其它合成配置下的缓存仍可依靠同一 Text Profile Plan 识别。评估并采取尽量简单、可靠的事务/一致性措施，使进程取消、写文件失败、Plan 保存失败、索引提交失败和并发首写均不会长期产生“有 Ready 音频却无适用 Plan”的状态。播放本身不得因缓存持久化失败而错误回滚 UI target。
4. 保持当前 `PlanOutputHash` 未变则只更新 Header 的写入优化。若文档/源码验证有必要，允许合并 Plan 的轻量元数据查询；本任务不重造一套 Coverage 物化表。
5. Active Cache 真正生成音频时能持久化可被 Coverage/Export 复原的 Plan；MP3 Export 仍只复原**当前配置下完整**的缓存，不主动 TTS 补齐，不错误接受 stale plan。
6. 保持现有 SQLite schema 与公开数据完整性；如无法在现有边界下安全完成且确需持久化 schema 改动，按 `AGENTS.md` 暂停涉及该改动的部分请求决策，不自创 migration。

## 自动验收（少量关键路径）

- 对未缓存章节调用 Playback 内容加载/Plan Build，多次运行后数据库 Plan/Segments 仍不增长；朗读分段语义与此前一致。
- 首次音频缓存成功后 Plan/音频索引可跨重新打开数据库重建正确 Coverage 和导出所需音频键；重复命中不重新批量写入 PlanSegments。
- 失败、取消、并发相同章节第一次缓存与内容失效情形下无长期“Ready 音频无适用 Plan”或误计过期音频；旧有效缓存的保留规则不退化。
- 只影响 Display 的规则不重建音频身份；Speech/TextProfile 变更后的匹配保持准确。Provider 切换仅重新匹配，这是回归保护，不新增实现。
- 执行涉及 Application Cache/Playback、Infrastructure SQLite/Export 的 focused tests、Release build、format、diff check；按规定记录未执行项。

## 完成

只记录核心契约与清理掉的旧责任路径，不添加大量细节测试；更新 Backlog、删除本规格后继续 T003。
