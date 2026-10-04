# T006：量化启动路径迁移的重复扫描成本

## 目标

为启动时扫描 `LocalBookSources.StoredContentPath` 与 `AudioCacheEntries.FilePath` 的必要性和成本建立证据，并给出保留、简化或另立后续任务的结论。

## 背景与证据

审计报告 D001：`StartupDatabaseInitializer` 每次启动都会调用 `AppStoragePathMigrationService`；服务在事务中读取两列全部行，再筛选绝对路径。当前 writer 保存 storage keys，但尚未以最旧受支持数据库/数据验证完成状态。

## 范围

- 梳理所有当前 writer、migration 和启动调用方，确认绝对路径能否再次产生及最旧受支持 DB 状态。
- 使用脱敏/合成数据测量代表性行数下的查询、扫描和事务成本；不得输出完整文件路径。
- 形成是否有足够收益移除/门控扫描的决策。若需持久 marker/schema 字段，另行提出授权需求和数据影响，不在本任务实施。

## 验收

- 记录样本规模、数据构造方式、查询耗时/分配（可获得时）、测量方法及不确定性。
- 以代码和数据库兼容合同证明保留扫描或提出安全的无 schema 简化方向；没有证据时选择保留现状。
- 本任务不改 SQLite schema、已发布 migration、用户数据或启动行为。
- 临时样本、脚本和数据库在任务结束前删除；无需因调查本身新增永久测试。

## 长期合同

- `docs/05_DATA_AND_COMPATIBILITY.md` 第 2、4、7 节：storage key、append-only migration 与持久化 query 边界。
- `docs/08_QUALITY_AND_TESTING.md` 第 6.3 节：migration/关键 SQLite 行为的测试准入。

## 依赖与风险

无前置任务。发现必须变更持久状态才能安全优化时，停止实施并另行申请具体授权。
