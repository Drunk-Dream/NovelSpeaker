# Cache 与后台工作

## 1. 定位

本文件中的 `Cache` 默认指 **Speech/Audio Cache** 以及由其派生的 Speech Plan/Coverage/后台工作。它不是 Book Source Content 的所有者。

Source Content 边界：

- Local Source 正文是持久业务数据，不属于 Cache。
- Future Online Source 正文使用独立文本缓存体系，不进入 Audio Cache 模块。
- Book / Source / Content 精确合同见 `specs/BOOK_DATA_MODEL.md`。

## 2. Audio Cache 职责

长期至少区分：

- **CacheStore**：物理 audio cache/index/file 的唯一事实；
- **CacheCatalog/read model**：提供场景化 immutable 缓存视图；
- **Cache invalidation**：表达受影响 scope，不把派生统计当第二真值；
- **Coverage**：组合当前配置、Speech Plan 与物理缓存；
- **Speech Plan repair**：缺失/过期 plan 的后台补建 owner；
- **Active Cache**：用户主动缓存 batch owner；
- **Export**：章节 MP3 导出 batch owner。

不建立大一统 `CacheManager`。

## 3. Physical Cache 与 Coverage

物理统计和逻辑完整度分离。普通目录只显示有有效计划且能形成正常百分比的缓存完整度；0%/异常状态不显示。CacheManagement 展示所有有缓存章节，即使当前配置下为 0%。

普通 read query 不同步等待 repair；repair 由 Cache-owned orchestration 登记并异步处理。

## 4. Cache invalidation

- mutation 只有在物理/index 提交完成后才发布 invalidation。
- 已知具体章节时不无条件退化为整本书失效。
- 高频 mutation 可以在 Cache 域内有界合并。
- Settings/Speech/Regex 等源模块只发布自身 semantic change，由 Cache 内部解释影响。
- CurrentCatalog replacement / Source switch 是 Books/Playback 上下文变化；Cache 不反向建立跨 Source Chapter Identity。

## 5. Speech Plan

- 播放、prefetch、Active Cache、Export 在消费前确保当前 plan。
- Coverage 发现缺失/过期 plan 时返回当前状态，并触发后台 repair，不同步等待。
- 同章 in-flight 合并，并发受限。
- 删除某章最后一条音频缓存时可以同步删除不再有意义的对应 plan/segment。
- Catalog Entry 的 ChapterId 只是当前目录内的技术引用，不定义跨 Source/跨 Catalog 的业务章节身份。

## 6. Audio Cache identity 与写入

Audio Cache identity 必须反映：

- 稳定段身份；
- 最终 SpeechText；
- Provider Type 的版本化 synthesis fingerprint；
- 全局语速与其它真正影响音频结果的参数。

不得使用 Provider 名称、排序或最近使用时间代替合成语义。

写入保持：

```text
resolve current plan
→ cache lookup
→ provider admission
→ synthesize
→ validate audio
→ atomic file write
→ commit index
```

失败/取消不得留下被视为 Ready 的不完整条目。

## 7. Playback Prefetch

Playback Prefetch 属于 Playback session，复用 Cache/Speech 稳定能力但不成为 Cache process background job。

```text
Current Playback > Playback Prefetch > Active Cache
```

Active Source context 或 target revision 改变后，旧上下文未完成 Prefetch 必须取消或失效。

## 8. Active Cache

- 全应用最多一个 Active Cache batch。
- batch 创建时冻结章节集合、Provider、typed config、语速和其它影响合成的配置快照。
- 页面切换、播放切章和主窗口隐藏不取消已提交 batch。
- 取消停止未开始工作，已经生成的有效缓存保留。
- 页面只消费 immutable progress snapshot，不拥有 batch lifecycle。

## 9. MP3 Export

- 全应用最多一个导出批次。
- 提交时冻结书籍/章节集合与目标目录。
- 每章一个 MP3，不覆盖现有同名文件。
- 失败/取消清理未完成临时文件。
- 页面离开不取消已提交导出。

## 10. Cache UI

CacheManagement 页面只拥有 filter/selection/live projection；页面离开取消页面查询/订阅，但不取消 Active Cache/Export/repair。Player、BookDetails、CacheAndData 与 CacheManagement 复用 Cache-owned read model/change source，不各自重新实现一致性算法。

## 11. Future Online Source Text Cache

该缓存与 Audio Cache 完全独立。

长期边界：

```text
BookSourceBinding + ContentLocator
        ↓
Online text cache metadata
        ↓
plain text file
```

要求：

- 正文实体以文本文件保存；SQLite 只保存必要元数据，例如 Binding、locator/URL 和本地路径；
- 缓存唯一性不得只使用章节序号；优先以 Binding + stable ContentLocator 识别；
- Catalog Entry 能通过自身 locator 找到对应正文；
- Source switch 不清理缓存；
- refresh catalog 不清理缓存；
- refresh bound-source list 不因某个 Binding 暂时未发现就即时清理正文文件；
- 不使用 LRU，不设置普通运行期自动淘汰；
- Book 删除时清理该 Book 的所有在线正文缓存；
- 用户主动“清理正文缓存”时按明确范围清理；
- locator 明确变化时不允许把旧正文错误复用于新章节；
- Local Source 正文永远不进入这一缓存体系。

当前阶段只允许为上述未来能力预留不会阻碍实现的数据/接口边界，**不得实现 Online Source text fetching、缓存管理 UI 或 LRU**。

## 12. 后台 owner 原则

Active Cache、Export、Speech Plan repair 各自拥有明确生命周期，不建设通用 BackgroundTaskManager。

所有 background owner：

- 可取消；
- 可观察异常；
- 有 bounded shutdown；
- 不依赖页面实例存在；
- 不把 progress snapshot 当持久业务真值。

未来 SearchSession / RefreshSession 不属于 Audio Cache background owner；其生命周期由对应搜索/换源功能拥有。

## 13. 非目标

- 不把 Cache 变成所有模块变化的事件中心。
- 不为普通 cache mutation 全量重算 UI。
- 不为内部重构长期保留 compatibility reader/wrapper。
- 本轮不实现 Online Source 正文抓取、在线正文缓存运行时或高级管理。
