# Cache 与后台工作

## 1. 定位

本文件中的 `Cache` 默认指 **Speech/Audio Cache** 以及由其派生的 Speech Plan/Coverage/后台工作。它不是 Book Source Content 的所有者。

Source Content 边界：

- Local Source 正文是持久业务数据，不属于 Cache。
- 未来 Online Source 的章节正文可以使用独立 Source-scoped 文件缓存，但该能力不进入当前 Audio Cache 模块，也不在当前版本实现高级管理。
- Book / Source / Content 的长期合同见 `specs/BOOK_DATA_MODEL.md`。

Audio Cache 是一级 Application 模块，负责物理缓存事实、当前配置下的缓存完整度、Speech Plan 派生数据以及与音频缓存相关的后台作业。Cache 不是 Playback persistence 的附属层，也不拥有 Playback session mutable truth。

## 2. 职责拆分

长期职责至少区分：

- **CacheStore**：物理 audio cache/index/file 的唯一事实；负责写入、删除、验证、lease/protection 与廉价物理聚合。
- **CacheCatalog**：提供 global/book/chapter 的 immutable 音频缓存 read model。
- **Cache invalidation**：Cache 内部表达哪些 physical/catalog/coverage 投影已失效，不把原因枚举泄漏给 App。
- **Cache read model owner**：组合物理缓存、目录元数据、Coverage 与 repair 状态，提供场景化 immutable book/chapter view 和窄变化通知。
- **Cache Coverage**：组合当前配置、Speech Plan 与物理缓存，回答指定章节的当前配置完整度。
- **Speech Plan repair**：唯一拥有缺失/过期 plan 的后台补建生命周期。
- **Active Cache**：用户主动提交的章节音频缓存 batch owner。
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

对外 read query 返回当前可用结果，不同步等待 repair 完成。若读取发现 PlanMissing/PlanStale，Cache-owned orchestration 负责向 repair owner 登记；页面不再持有 repair requestor，也不负责“query → repair → 等待 invalidation → query”协议。

## 4. Cache invalidation

- mutation 只有在物理/索引提交完成后才发布 invalidation。
- 变更源报告最窄已知范围，例如 Global / Book / Chapters。
- 已知具体章节时不无条件退化成整本书失效。
- Cache 内部 invalidation 表达“哪里/哪类数据需要重算”，不携带总大小、百分比等派生统计作为第二真值。
- 高频 mutation 可以在 Cache 域内短窗口合并；具体时间参数属于实现细节。
- Cache read model owner 只重算当前被观察/请求的必要范围，并向 App 发布 book/chapter scope + revision 等窄变化事实。
- 同类刷新保持 single-flight，刷新中再次失效只标记 dirty，完成后补一轮。

Settings/Speech Provider/Regex 等源模块只发布自身 typed semantic change，由 Cache-owned integration 判断是否映射为 Coverage/Plan 失效。源模块不直接调用 Cache invalidation API。

Source switching / Catalog replacement 属于 Book/Playback 上下文变化。Cache 可以根据技术 ChapterId、BookId 和配置身份判断旧音频是否仍可命中，但不得反向建立跨 Source Chapter Identity。

## 5. Speech Plan

章节消费链路中的 Speech Plan 属于当前配置派生数据：

- 播放、prefetch、Active Cache、Export 在消费前确保当前 plan。
- Coverage 计算发现 `PlanMissing` / `PlanStale` 时返回当前状态，并由 Cache-owned orchestration 向 process repair owner 登记；读取本身不等待补建完成。
- 同章 in-flight 请求合并，并发受限。
- 计划完整构建后短事务提交。
- 补建完成后发布最窄 Coverage invalidation。
- 删除某章最后一条音频缓存时同步删除不再有意义的对应 plan/segment。
- 健康维护可清理长期没有任何 cache index 的残留 plan。

Catalog entry 可以保留技术性 ChapterId 作为 Speech Plan 外键，但该 ID 只服务内部稳定引用，不定义 Source 间或 Catalog 更新间的业务章节对应关系。

## 6. 缓存身份与写入

音频缓存身份必须反映：

- 稳定段身份；
- 最终 SpeechText；
- Provider Type 自己定义的版本化 `ProviderSynthesisFingerprint`；
- 全局语速与其它真正影响音频结果的统一合成参数。

不得使用 ProviderId、Provider 名称、SortOrder、最近使用时间或 HTTP 请求频率限制代替合成语义。

结果：

- Provider 重命名或排序不使缓存失效。
- 两个有效合成配置完全相同的 HTTP Provider 可以复用同一音频缓存。
- 修改 Voice、URL/Header/Body 等真正影响合成的配置后，旧物理文件可以保留，但新配置不得错误命中。
- 切换 Provider 后旧缓存可以保留；切回未改变的原配置时允许重新命中。

写入：

```text
resolve current plan
→ cache lookup
→ Provider admission
→ synthesize
→ validate audio
→ atomic file write
→ commit index
```

失败/取消不得留下被视为 Ready 的不完整条目。

## 7. Playback Prefetch

Playback Prefetch 属于 Playback session，不属于 Cache process background job。

它使用 Cache/Speech 的稳定能力并遵守共同 admission 优先级：

```text
Current Playback > Playback Prefetch > Active Cache
```

Provider 或有效配置改变后，未开始的后续预取使用最新配置。旧配置已经完成的结果可以保留，但 fingerprint 不匹配时不得继续使用。

ActiveSource 切换时，旧 Source 对应的未完成 Prefetch 必须取消或失效，不能把迟到结果投影到新 Source 的当前播放位置。

Cache 不通过 Prefetch 反向依赖 Playback mutable session。

## 8. Active Cache

- 全应用最多一个 Active Cache batch。
- batch 创建时冻结章节集合、Provider Instance、Provider typed config、全局语速以及其它影响合成的必要配置快照。
- 运行中的 batch 不因用户切换 CurrentProvider、编辑 Provider 或调整后续播放配置而混入新的合成配置。
- coordinator 拥有 CTS、Task、进度和终态。
- 页面切换、播放切章和主窗口隐藏不取消已提交 batch。
- 取消停止未开始工作，已经生成的有效 audio cache 保留。
- 完成/取消/失败后释放 active slot。
- 页面通过 immutable progress snapshot 展示状态。

Playback 章节目录使用通用章节 Management Mode，`Cache` 是其中的批量动作，选择语义见 `specs/BATCH_MANAGEMENT.md`。Active Cache coordinator 只负责批次执行，不拥有页面 selection。

Cache 动作是幂等的：

- fully cached → skip；
- partially cached → fill missing；
- uncached → cache normally。

## 9. MP3 Export

Export 是 Audio Cache 相关的独立 process background job：

- 全应用最多一个导出批次。
- 提交时冻结书籍/章节集合与目标目录。
- 只导出当前 Provider、全局语速和文本处理配置下可验证完整的章节。
- 每章一个 MP3。
- 安全处理目录/文件名，同名使用编号后缀，不覆盖。
- 导出期间持有必要 cache protection/lease。
- 失败/取消清理未完成临时文件。
- 页面离开不取消已提交导出。

## 10. Cache UI

CacheManagement 页面只拥有页面 filter/selection/live projection：

- global overview 在页面 active 时自动追上 CacheStore 真值。
- 当前书/当前章节只请求受影响的组合 read model。
- Coverage 只请求 current/viewport/明确受影响范围。
- catalog reconciliation 不使用普通 mutation 下的全量 `Clear + Add`。
- 缓存变化不无条件清空用户有效选择。
- 页面离开取消页面查询/订阅，但不取消 Active Cache/Export/repair。
- CacheManagement 明确保留当前文件管理器式 Extended Selection，是页面级 Management Mode 的例外。
- 页面不解释 physical/catalog/coverage invalidation aspect，不维护 repair queue、book epoch 或 Cache 内部重算状态机。

Player、BookDetails、CacheAndData 与 CacheManagement 使用同一个 Cache-owned read model/change source。各页面可以拥有不同 projection window 和 selection，但不得各自重新实现 Cache consistency algorithm。

## 11. Future Online Source Content Cache

本阶段不实现 Online Source，但长期边界固定为：

- online chapter text 不进入 `app.db`；
- 使用 Source-scoped 文件缓存；
- cache lifetime 跟随绑定 Source；
- remove Source 时清理该 Source text cache；
- switch Source 不清理其它 Source cache；
- locator 变化/消失立即使对应正文失效；
- 不通过章节 title/position/body 猜测复用；
- 暂不设置容量上限；
- 暂不实现 LRU/自动淘汰；
- 暂不实现高级逐章缓存管理；
- 第一阶段只需要整本 Book 的 online text cache 全部清理能力。

这一缓存与 Audio Cache 是不同数据类别，不强行合并 store/index/管理 UI。

## 12. 后台 owner 原则

Active Cache、Export、Speech Plan repair 各自拥有明确生命周期，不建设通用 BackgroundTaskManager。

所有 background owner：

- 可取消；
- 可观察异常；
- 有 bounded shutdown；
- 不依赖页面实例存在；
- 不把 progress snapshot 当作持久化业务真值。

## 13. 非目标

- 不把 Cache 变成所有模块变化的事件中心。
- 不为了实时 UI 对每个 cache entry mutation 触发全局重算。
- 不为内部 Cache 重构长期保留 compatibility reader/wrapper。
- 不让普通 Coverage 查询逐文件解码、同步等待 repair，或把 repair 编排交给页面。
- 不在当前阶段实现 Online Source 正文抓取或高级正文缓存管理。
