# 数据与持久化

## 1. 数据所有权

| 数据 | 权威来源 |
|---|---|
| 外部小说正文 | 用户 TXT 文件 |
| 书籍/章节元数据 | SQLite |
| 当前活动播放位置 | PlaybackSnapshot |
| 可恢复阅读进度 | SQLite ReadingProgress |
| 规则/设置 | SQLite/settings store |
| speech plan | SQLite 派生数据 |
| 音频缓存 | 应用内部可重建文件 + SQLite index |
| active cache/export 运行态 | Process coordinator snapshot |

外部 TXT 永不由应用写回。

## 2. 数据目录

正式应用数据根：程序目录下 `Data/`。

开发默认使用独立开发数据根，自动测试使用测试拥有的临时根。`NOVELSPEAKER_DATA_ROOT` 只作为开发/诊断显式覆盖。

旧 `%LocalAppData%/NovelSpeaker` 不建立隐式探测、迁移、回退或双读路径。

## 3. SQLite migration

- 已发布 migration 只能追加。
- 不为代码整理修改、合并、删除、重编号已发布 migration。
- schema 变化必须有升级测试。
- 内部架构重构不得把“兼容旧内部 API”与“兼容已发布数据”混为一谈。

## 4. ReadingProgress

运行时与持久化语义：

```text
matching PlaybackSnapshot
    > persisted ReadingProgress baseline
```

- 当前活动书籍 UI 以 Snapshot 覆盖持久化基线。
- 非活动书籍/重启使用 SQLite。
- 显式章节/段落跳转成功后 checkpoint 新位置。
- 页面不得直接写 ReadingProgress。
- 不进行逐毫秒高频 SQLite 写入。

## 5. 书籍导入

导入流程：

```text
choose TXT
→ resolve/validate path
→ detect encoding
→ normalize text
→ apply chapter rules
→ persist book/chapter metadata
```

导入、重新分章和删除必须遵守路径安全与事务/补偿语义。

## 6. Query / Read Model

数据读取采用场景化 query：

```text
Library summaries
Book header
Book catalog
Current reading position
Chapter content
Cache statuses for indices/range
Cached book/chapter summaries
```

原则：

- query 返回 immutable read model；
- 不为一个页面无条件返回所有统计/状态；
- 不在 App 组合多个低层 store 完成页面级 read model；
- 避免 N+1；
- 大目录查询支持一次 catalog materialization，但动态 status/enrichment 不与 catalog 强绑定。

## 7. 章节 Catalog

大型章节目录的基础 catalog 只包含稳定轻量字段，如：

- chapter id/index；
- sort order；
- title；
- 必要定位元数据。

Current/Selected/CachePercentage/Loading 等动态状态不作为完整 catalog 的必需 mutable 字段。

## 8. 文本处理与 Speech Plan

章节消费链路：

```text
chapter source text
→ text profile / regex processing
→ current ChapterSpeechPlan
→ stable segment identity
→ SpeechText hash
→ synthesis profile fingerprint
→ AudioCacheKey
```

每章只保存当前有效 speech plan，不保存无意义历史版本和完整 `SpeechText` 副本。

## 9. 缓存身份

音频缓存身份不得使用仅在运行时稳定的 SegmentIndex。

缓存键由稳定段身份、最终合成文本语义和版本化 synthesis profile 共同决定。

TextProfileFingerprint 用于判断 speech plan 是否需要重建，不直接等价于音频缓存键。

## 10. CacheStore 与 CacheCatalog

目标区分：

- **CacheStore**：index/file 原子操作、写入、删除、lease/protection、验证。
- **CacheCatalog**：页面/业务查询所需 read model 和 status query。

页面不直接组合 index、文件、speech plan 多个低层接口。

## 11. 缓存写入

```text
resolve current plan
→ cache lookup
→ TTS admission
→ synthesize
→ validate audio
→ atomic file write
→ commit index
```

失败/取消不能留下被视为 Ready 的不完整条目。

## 12. 缓存完整度

普通完整度查询优先聚合 SQLite 已有 plan/index，不逐文件解码。

严格验证发生在播放、导出和低优先级健康维护等需要真实使用音频的边界。

普通目录：

- 只显示有有效计划且能形成正常百分比的缓存完整度；
- 0%/异常状态不显示。

缓存管理：

- 展示所有有缓存章节；
- 当前配置下 0% 仍显示为 0%。

## 13. 计划补建与清理

- 播放、预取、主动缓存、导出在消费前确保当前 plan。
- 完整度读取发现过期 plan 可登记后台补建。
- 缓存管理发现有缓存但缺失 plan 时可补建。
- 同章 in-flight 请求合并。
- 删除某章最后一条 cache index 时同步删除对应 plan/segment。
- 启动健康维护集合式清理长期无任何 cache index 的残留 plan。

## 14. 主动缓存

Active cache batch 的运行态不持久化为长期历史任务中心。已生成 cache 正常保留。

页面只提交不可变批次参数并订阅 coordinator snapshot。

## 15. MP3 导出

- 只导出可验证完整的章节。
- 每章一个 MP3。
- 自动建立书名目录并安全处理文件名。
- 同名使用编号后缀，不覆盖。
- 导出期间持有必要 cache protection/lease。
- 输出临时文件在失败/取消时清理。

## 16. 设置与规则

- 设置持久化使用原子写入。
- 规则顺序、启用状态和请求语义必须具有稳定持久化表示。
- 配置指纹使用版本化规范序列化，避免字段顺序、默认值或大小写导致错误 cache identity。

## 17. 删除与恢复

SQLite 外键只能级联记录，不能自动删除音频文件。书籍删除必须明确协调：

- 内部文件路径；
- cache index/file；
- book/chapter/reading progress；
- operation journal/恢复。

任何中断恢复都不能删除应用数据根之外的文件。

## 18. 隐私与日志

日志、异常、Snackbar、请求预览和诊断摘要不得包含：

- 小说正文；
- 完整 URL/Header/Body；
- 响应正文；
- Token/API Key；
- 用户私人路径中不必要的信息。
