# T004 — 孤立 Plan 清理与 Active Cache 按章处理

- **类型/优先级：** Implementation / P1 Resource。依赖 T002、T003。
- **必读：** `AGENTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/05_DATA_AND_COMPATIBILITY.md`；`AudioCacheFacade`、`AudioCacheMaintenance`、`SqliteChapterSpeechPlanStore`、`ActiveCacheCoordinator`、章节正文服务以及现有 Active Cache/退出/Export 测试。

## A. Plan 随缓存生命周期收口

1. 确认 Plan 是可重建派生数据；章节最后一份物理缓存实际删除后，不长期留存无音频关联的 Plan/Segments。复用现有 `DeletePlansWithoutCacheEntriesAsync` 或采用同等简单的定向清理；不要在每次滚动/读取时触发全表清理。
2. 保留启动时孤立 Plan 清理兜底；缓存删除、容量淘汰、Book 删除、修复时缓存已不存在等操作应有明确清理边界。避免正常操作频繁扫描全数据库，优先沿用已知受影响章节作用域。
3. 原 Provider 的缓存即使对当前 Provider 无效也仍属于**物理存在的音频缓存**。不能错误删除仍有物理缓存的 Plan；用户切回旧 Provider 应能重新识别可用缓存。
4. 不把 `VACUUM` 放进 UI 滚动、热缓存写入/删除路径。SQLite 回收页、WAL checkpoint 等遵循现有可控维护策略；不以进程启动清理作为防止数据库异常膨胀的主要修复。
5. 如仅需检查 Plan 状态/指纹，避免读取整个分段集合；优先复用轻量 Header 查询并移除冗余数据加载。不要为了这点创建新的缓存镜像表。

## B. Active Cache 内存生命期

1. 当前 `ActiveCacheCoordinator.StartAsync` 会批量加载选中章节正文并把所有 `FrozenSegment` 留在批次中。保持**冻结 Provider/语速/规则/选中章节/来源身份**的行为，但大型正文、分段和 Plan 改为**逐章加载、处理、释放**。
2. 保持原已提交后台 batch 的取消、页面离开后运行、进程退出有界排空、重复提交拒绝、失败继续或停止策略及 immutable progress snapshot；不得为降低内存而改变用户可见业务规则。
3. 冻结规则和文本处理配置，避免批次中途设置变化使不同章节按不同规则计算；正文/当前 Catalog 来源失效要遵循已有一致性检查，不能把新来源正文混入旧批次。
4. 不把全部章节内容移入进程静态字典、长期 service 或另一个大集合；完成一章即可解除大型对象引用，不改变音频缓存的保护/删除边界。
5. 如 T002 已顺手完成部分改造，检查实际代码只做缺失部分，避免重复抽象。

## 自动验收

- 删除章节最后缓存条目后 Plan/Segments 能按安全生命周期移除；保留旧 Provider 缓存的章节不会误删；重启清理兜底可用。
- 有多章主动缓存任务时，批次不在开始阶段载入并长期持有全部正文/分段；可用受控章节读取服务和弱引用/生命周期断言或规模探针验证，不因 GC 数字波动使测试脆弱。
- 规则在批次期间变化，批次仍使用启动时冻结规则；来源更改/取消/退出时无晚到缓存错写，已成功缓存留存。
- 播放/Export/CacheManagement 和目前正常的进程关闭行为不退化。运行相关 focused tests、Release build、format/diff check。
- 如实记录未执行项；删除一次性规模探针、测试正文和数据文件。

## 禁止事项

不新增 schema migration，不添加通用缓存淘汰平台、内存常驻 Plan 池、长生命周期正文缓存或在线书源逻辑。

## 完成

更新 Backlog `[x]`，删除本任务文件；进入 T005。
