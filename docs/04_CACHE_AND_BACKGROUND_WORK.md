# Cache 与后台工作

## 1. 定位

Cache 是一级 Application 模块，负责物理缓存事实、当前配置下的缓存完整度、Speech Plan 派生数据以及与缓存相关的后台作业。Cache 不是 Playback persistence 的附属层，也不拥有 Playback session mutable truth。

## 2. 职责拆分

长期职责至少区分：

- **CacheStore**：物理 cache/index/file 的唯一事实；负责写入、删除、验证、lease/protection 与廉价物理聚合。
- **CacheCatalog**：提供 global/book/chapter 的 immutable 物理缓存 read model。
- **Cache invalidation**：表达哪些 Cache read model/coverage 已失效，不携带第二套统计真值。
- **Cache Coverage**：组合当前配置、Speech Plan 与物理缓存，回答指定章节的当前配置完整度。
- **Speech Plan repair**：唯一拥有缺失/过期 plan 的后台补建生命周期。
- **Active Cache**：用户主动提交的章节缓存 batch owner。
- **Export**：章节 MP3 导出 batch owner。

不建立大一统 `CacheManager`。

## 3. 物理缓存与 Coverage

物理缓存统计与逻辑完整度分离。

物理统计：

```text
EntryCount
TotalSizeBytes
CachedChapterCount
```

Coverage：

```text
CachedSegmentCount
ExpectedSegmentCount
Status
Percentage
```

普通目录只显示有有效计划且能形成正常百分比的缓存完整度，0%/异常状态不显示。缓存管理页展示所有有缓存章节，即使当前配置下为 0%。

Coverage 查询只读，不在 query 内启动后台副作用。

## 4. Cache invalidation

- mutation 只有在物理/索引提交完成后才发布 invalidation。
- 变更源报告最窄已知范围，例如 Global / Book / Chapters。
- 已知具体章节时不无条件退化成整本书失效。
- invalidation 表达“哪里/哪类数据需要重读”，不携带总大小、百分比等派生统计作为第二真值。
- 高频 mutation 可以在 Cache 域内短窗口合并；具体时间参数属于实现细节。
- active 页面只重读当前可见/需要的最小 read model。
- 同类刷新保持 single-flight，刷新中再次失效只标记 dirty，完成后补一轮。

Settings/TTS/Regex 等源模块只发布自身 typed semantic change，由 Cache-owned integration 判断是否映射为 Coverage/Plan 失效。源模块不直接调用 Cache invalidation API。

## 5. Speech Plan

章节消费链路中的 Speech Plan 属于当前配置派生数据：

- 播放、prefetch、Active Cache、Export 在消费前确保当前 plan。
- Coverage 查询发现 `PlanMissing` / `PlanStale` 时只返回状态。
- 需要补建时由明确 orchestration 向 process repair owner 登记。
- 同章 in-flight 请求合并，并发受限。
- 计划完整构建后短事务提交。
- 补建完成后发布最窄 Coverage invalidation。
- 删除某章最后一条缓存时同步删除不再有意义的对应 plan/segment。
- 健康维护可清理长期没有任何 cache index 的残留 plan。

## 6. 缓存身份与写入

缓存身份必须反映稳定段身份、最终 SpeechText 语义和版本化 synthesis profile，不使用仅运行时稳定的 SegmentIndex 作为唯一缓存身份。

写入：

```text
resolve current plan
→ cache lookup
→ TTS admission
→ synthesize
→ validate audio
→ atomic file write
→ commit index
```

失败/取消不得留下被视为 Ready 的不完整条目。

## 7. Playback Prefetch

Playback Prefetch 属于 Playback session，不属于 Cache process background job。

它使用 Cache/Speech 的稳定能力并遵守共同 TTS admission 优先级：

```text
Current Playback > Playback Prefetch > Active Cache
```

Cache 不通过 Prefetch 反向依赖 Playback mutable session。

## 8. Active Cache

- 全应用最多一个 Active Cache batch。
- batch 创建时冻结章节集合和必要配置快照。
- coordinator 拥有 CTS、Task、进度和终态。
- 页面切换、播放切章和主窗口隐藏不取消已提交 batch。
- 取消停止未开始工作，已经生成的有效 cache 保留。
- 完成/取消/失败后释放 active slot。
- 页面通过 immutable progress snapshot 展示状态。

## 9. MP3 Export

Export 是 Cache 相关的独立 process background job：

- 全应用最多一个导出批次。
- 提交时冻结书籍/章节集合与目标目录。
- 只导出当前配置下可验证完整的章节。
- 每章一个 MP3。
- 安全处理目录/文件名，同名使用编号后缀，不覆盖。
- 导出期间持有必要 cache protection/lease。
- 失败/取消清理未完成临时文件。
- 页面离开不取消已提交导出。

## 10. Cache UI

Cache 页面只拥有页面 filter/selection/live projection：

- global overview 在页面 active 时自动追上 CacheStore 真值。
- 当前书/当前章节只刷新受影响 read model。
- Coverage 只刷新 current/viewport/明确受影响范围。
- catalog reconciliation 不使用普通 mutation 下的全量 `Clear + Add`。
- 缓存变化不无条件清空用户有效选择。
- 页面离开取消页面查询/订阅，但不取消 Active Cache/Export/repair。

## 11. 后台 owner 原则

Active Cache、Export、Speech Plan repair 各自拥有明确生命周期，不建设通用 BackgroundTaskManager。

所有 background owner：

- 可取消；
- 可观察异常；
- 有 bounded shutdown；
- 不依赖页面实例存在；
- 不把 progress snapshot 当作持久化业务真值。

## 12. 非目标

- 不把 Cache 变成所有模块变化的事件中心。
- 不为了实时 UI 对每个 cache entry mutation 触发全局重算。
- 不为内部 Cache 重构长期保留 compatibility reader/wrapper。
- 不让普通 Coverage 查询逐文件解码或启动 repair。
