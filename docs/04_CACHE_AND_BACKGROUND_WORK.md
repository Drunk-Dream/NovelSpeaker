# Cache 与后台工作

## 1. 定位

本文件中的 `Cache` 默认指 **Speech/Audio Cache** 及其派生的 Speech Plan、Coverage 和相关后台工作，不是 Book Source Content 的所有者。

- Local Source 正文是持久业务数据，不属于 Cache。
- Future Online Source 正文使用独立文本缓存，不进入 Audio Cache。
- Book / Source / Content 的权威合同见 `specs/BOOK_DATA_MODEL.md`。

## 2. 职责与数据所有权

- **CacheStore**：物理音频文件、索引及其有效性事实的唯一 owner。
- **CacheCatalog / read model**：组合物理事实和按需 Coverage，返回只读、场景化视图；读取本身不得生成正文、Plan 或写入缓存数据库。
- **Cache invalidation**：描述提交完成后受影响的作用域；不维护第二份持久化完整度真值。
- **Coverage**：用当前 Text Profile、Synthesis Profile、有效 Speech Plan 与缓存索引精确匹配。
- **Speech Plan 计算**：输入章节正文及冻结的文本处理配置，得到可播放分段及其身份；计算不隐含数据库持久化。
- **Speech Plan 持久化**：在存在音频缓存或将建立音频缓存时保存必要 Plan；不为仅被浏览、仅被读取的章节建表记录。
- **Stale repair**：仅为有实际音频缓存的过期 Plan 提供低优先级、按需后台修复。
- **Active Cache / Export**：各自拥有已提交批次的运行生命周期。

不建立通用 `CacheManager`、任务调度平台或额外的跨页面 Plan 内存缓存。

## 3. Physical Cache 与 Coverage

物理统计与当前配置下的逻辑完整度分离。`AudioCacheEntries` 的物理存在不代表在当前配置下有效。

- **普通目录**（Player、BookDetails 等）：先加载和展示目录；在可见窗口范围内异步补充稀疏 Coverage。仅当 Plan 有效、当前配置下存在有效缓存且能够精确计算时显示百分比；其它情况（包括 0%、过期、失败、无缓存）均留空，不显示“计算中”、过期百分比或替代文案。
- **CacheManagement**：基于物理缓存事实保留所有实际有缓存的章节，不因切换 Provider 后当前配置的有效比例为 0% 而隐藏这些条目。完整度标注遵循同样的真实百分比／空白规则。
- **查询路径**：保持轻量、批量、只读、可取消，不加载章节正文，不进行分段/Regex 处理，不创建 Speech Plan，不因滚动导致 `ChapterSpeechPlanSegments` 增长。大目录使用虚拟化及 viewport 级查询，不为不可见的未缓存章节主动计算。
- **缓存匹配**：有效 Plan 的当前分段身份、最终 SpeechText 哈希、当前 Synthesis Profile 和 Ready 音频索引必须一致；过期 Plan 不能提供旧百分比。缓存文件的损坏/缺失由既有维护/失效机制处理，不为了每次滚动做文件扫描或音频解码。
- UI 查询无需等待异步修复。修复成功只发布相应章节的 Coverage 变化，按当前页面激活状态重新读取并显示真实百分比。

## 4. Cache invalidation

- 物理/index mutation 只有提交成功后才发布 invalidation。
- 已知具体章节时，不无条件退化为整本书或全目录刷新。
- 高频 mutation 可在 Cache 域内有界合并；read model 保持单一真实数据来源。
- Settings/Speech/Regex 等源模块只发布语义变化，Cache 解释对 Coverage 的影响。
- Provider/语速等合成配置变化沿用现有 `SynthesisProfileFingerprint` 匹配，**不因此重建 Speech Plan**；这是已有行为，保持不变。
- Text Profile 变化可使已有 Plan 过期；重新匹配旧音频前必须先获得当前文本配置下的 Plan。
- CurrentCatalog replacement / Source switch 是 Books/Playback 上下文变化，Cache 不建立跨 Source Chapter Identity。

## 5. Speech Plan 计算、保存和修复

### 5.1 三项通用原则

1. **读取不创建**：目录、Coverage 及物理缓存查询不得计算或持久化 Plan。
2. **使用时计算**：Playback、Prefetch、Active Cache 等实际需要正文和朗读分段的用例按需计算 Plan，计算本身不写数据库。
3. **按需持久化**：只有章节存在音频缓存或即将成功建立音频缓存，才需要持久化可用于后续精确 Coverage / Export 的 Plan；没有音频缓存时不长期保存 Plan。

Plan 是可重建的派生数据，而不是必须随着每次正文加载写入的业务记录。临时正文、分段及计算 Plan 随单章操作结束释放，不建立不受约束的进程级大对象缓存。

### 5.2 Plan 状态和修复边界

- `PlanMissing`：普通查询直接跳过，不登记补建；正常设计中不应存在有缓存音频却没有对应 Plan 的持久状态。已有异常数据通过定向维护修复或安全失效，不在滚动路径中补偿。
- `PlanStale`：仅在存在实际音频缓存、且当前需要展示其 Coverage 时申请后台修复；修复前不显示旧百分比。
- 有效 Plan：直接批量匹配当前配置下的音频，不重新读取正文、分段或持久化。
- 没有音频缓存的过期/孤立 Plan：不修复，随缓存生命周期清理。
- 配置或来源不可用、不可朗读等现有状态沿用错误/缺省处理，不新增 UI 状态机或复杂持久化状态枚举。

仅 Text Profile 或正文身份等确实影响 Plan 的变化才需要重新计算。Provider、语速等 Synthesis Profile 变化只使旧音频可能不匹配，不构成 Plan 重建理由；切回旧 Provider 时，其尚存有效缓存可再次匹配。

### 5.3 简单、按需的 Stale repair

保持 Cache 模块内部的轻量异步 owner，不建立通用调度器：

- **单 worker 顺序处理**，避免目录后台计算与播放、主动缓存争用明显资源。
- **只保存最新需要显示的窗口中的待处理项**，按章节与目标配置去重；队列仅保留轻量身份，不保留正文、Plan 或大型段落数组。
- 新滚动窗口取代旧窗口时，移除未开始且不再需要的请求；离开页面、当前来源失效或相关配置变化时撤销旧需求，正在执行的操作协作取消或安全丢弃过期结果。
- 任务执行前复核章节当前身份、物理缓存是否仍存在及目标 Text Profile；提交前再检查结果没有失效。一次修复尝试不得用无界循环追逐变化的配置。
- 一章修复成功后释放临时数据，只对该章节发布 Coverage invalidation；失败被观察/记录，但不得因查询或 invalidation 立即无限重试。
- 后台 owner 具有明确异常观察和进程退出有界停止；页面只拥有需求与订阅，不直接成为永久 worker 的 owner。

队列容量、细小取消优化和实现原语留给代码决定，不把通用优先级、持久任务队列、退避框架或多套 CTS/状态机变成长期要求。

### 5.4 缓存与 Plan 的一致性

- 首条音频缓存持久化时，应确保相应章节当前 Plan 的持久身份与音频索引一致；不允许长期产生“有音频却无 Plan”的部分提交状态。
- 尽可能在现有持久化 owner 内保证 Plan/音频索引元数据的原子提交或同等可靠的一致性；文件写入仍遵守既有临时文件、验证及取消规则。
- 无效/过期 Plan 不得通过旧完整度掩盖结果。仅 Plan Header 需要更新、分段输出未变时，应复用现有 `PlanOutputHash` 优化，避免删写全部分段行。
- 删除章节最后一份物理音频缓存后，对应 Plan 不再需要长期保存；在适当缓存清理/维护边界删除孤立 Plan，启动维护保留兜底。无音频时不把 Plan 长期堆积在 SQLite。
- 这些约束原则上使用当前表结构实现，不为内部职责拆分引入新 schema、持久状态或兼容层。

Catalog Entry 的 ChapterId 仅是当前目录的技术引用，不是跨 Source/跨 Catalog 的业务身份。

## 6. Audio Cache identity 与写入

Audio Cache identity 必须反映稳定段身份、最终 SpeechText、Provider Type 的版本化合成指纹、语速及真正影响音频结果的参数；不能把 Provider 名称、排序或最近使用时间当合成语义。

缓存命中无需合成；缓存未命中需遵守 Provider admission、音频校验、原子文件写入和索引提交。完成第一份持久音频时同步建立必要 Plan 元数据；后续复用同一当前 Plan，不为缓存命中而无条件重写 Plan。

失败/取消不得留下被视为 Ready 的不完整音频、孤立文件或无主 Plan。

## 7. Playback Prefetch

Playback Prefetch 属于 Playback session，不是 Cache process background job。

```text
Current Playback > Playback Prefetch > Active Cache
```

Active Source context 或 target revision 改变后，旧上下文未完成 Prefetch 必须取消或失效。正常播放/Prefetch 只保存当前实际需要的临时正文和 Plan；不得因仅准备内容就无条件写入 Plan。

自动音频预取仅在 Playback intent 为 Play 时运行。暂停（包括定时暂停）立即撤销待处理窗口并尽力取消在途预取；暂停状态打开书籍、跳转及修改语音/文本配置均不重启预取，迟到的旧窗口不得重新生效。已提交的有效音频缓存保留，已加载的暂停音频仍可恢复；Resume 按原 PrefetchCount 恢复预取，0 继续禁用。暂停仅撤销 Playback Prefetch 所拥有的工作，不取消独立的 Active Cache。

## 8. Active Cache

- 全应用最多一个 Active Cache batch。
- batch 创建时冻结章节集合、Provider、typed config、语速及影响朗读的规则/文本配置快照。
- 仅持久化必要的轻量批次身份和运行快照；**章节正文、朗读分段和计算 Plan 按章获取、处理、释放**，不把大量章节的完整正文/分段长时间留在一个 FrozenBatch 中。
- 来源上下文在准备或执行期间失效，应沿用现有明确拒绝/取消语义，不能混用新旧正文。
- 页面切换、播放切章和隐藏主窗口不取消已提交 batch。真实应用退出主动请求取消并有界排空。
- 取消停止未开始的工作，已成功提交的有效缓存保留；未完成结果不能标为 Ready。
- 页面只消费 immutable progress snapshot，不拥有 batch 生命周期。

## 9. MP3 Export

- 全应用最多一个导出批次。提交时冻结书籍/章节集合与目标目录。
- 导出以当前 Text Profile 的有效 Plan 与当前合成配置对应的完整音频为依据；不得把过期音频计作已缓存，也不得为了导出自动补全缺失音频。
- 每章一个 MP3，不覆盖现有同名文件；失败/取消清理未完成临时文件。
- 页面离开不取消已提交导出。当前有效 Plan 的持久元数据可用于复原导出键。

## 10. Cache UI

CacheManagement 页面只拥有 filter/selection/live projection；页面离开取消页面查询/订阅，且撤回由该页面提出、不再需要的 Stale repair 需求，但不取消已提交的 Active Cache/Export。Player、BookDetails、CacheAndData 与 CacheManagement 复用 Cache-owned read model/change source，不各自重新实现完整度算法。

## 11. Future Online Source Text Cache

该缓存与 Audio Cache 完全独立：

```text
BookSourceBinding + ContentLocator
        ↓
Online text cache metadata
        ↓
plain text file
```

- 正文实体保存为文本文件；SQLite 只保存必要元数据（Binding、locator/URL、本地路径等）。
- 缓存身份不得只取章节序号，优先 Binding + stable ContentLocator；Catalog Entry 通过 locator 找到正文。
- Source switch、catalog refresh 不自动清理正文；Binding 暂时未发现时不立即删除缓存。
- 不使用 LRU，不设置普通运行期自动淘汰。
- Book 删除时清理相关在线正文缓存；用户显式清理时按选定范围处理。
- locator 变化不复用旧正文。Local Source 永远不进入这一缓存体系。

当前仅保留不会阻碍未来实现的边界，不实现 Online Source 抓取、运行时缓存或管理 UI。

## 12. 后台 owner 原则

Active Cache、Export 的已提交批次属于跨页面 process background job。Stale repair 的**执行 worker** 由 Cache 进程 owner 管理，但其**待修复需求**来自当前有效页面/可视窗口；页面关闭可撤回未处理需求，不意味着停止其它已提交批次。

所有后台 owner 必须可取消、观察异常并有界退出；observer 异常不破坏 worker 完成责任或 slot 释放。不把 progress snapshot 当成持久业务真值，不建立通用 `BackgroundTaskManager`。

未来 SearchSession / RefreshSession 不属于 Audio Cache owner。

## 13. 非目标

- 不将 Cache 作为其它模块的通用事件中心。
- 不为普通 Cache mutation 或目录滚动全量重算 UI。
- 不为内部重构保留 Old/New/V2/Compat reader、wrapper、状态机。
- 不引入进程级巨大 Plan 内存缓存、持久化修复队列或通用重试调度框架。
- 不变更已有 Provider 合成身份计算和重匹配行为。
- 不实现 Online Source 抓取、缓存运行时或高级管理。
