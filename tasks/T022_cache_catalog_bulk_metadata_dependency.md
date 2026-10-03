# T022：移除 CacheCatalog 的逐本 metadata fallback

## 目标

要求 CacheCatalog 批量列表使用 `IBookLibraryQuery`，删除缺少 bulk query 时逐本调用 `IBookPlaybackMetadataQuery` 并合成默认 metadata 的路径。

审计依据：`CODEBASE_AUDIT_REPORT.md` S07。相关合同：`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 核实所有生产构造路径和测试实例；维持单本详情已有 query API。
- 将 bulk query 作为构造所需依赖，移除逐本 fallback 及“未开始”/`DateTimeOffset.MinValue` 的伪默认数据。
- 保留批量列表排序、过滤、缺失书籍处理和单本查询语义；不新增 facade/adapter。

## 验收

- 生产 DI composition 能解析 CacheCatalog，所有直接构造 caller 显式提供真实依赖。
- 有核心行为证据表明批量列表 metadata 来自 bulk query，且不会因 fallback 缺失而伪造值。
- 执行 Cache/Application focused tests、format 和 Release build。

## 交付

删除旧 fallback 和仅服务于该 fallback 的测试替身/分支，更新 Backlog 并删除本规格。
