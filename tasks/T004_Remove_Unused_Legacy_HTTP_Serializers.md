# T004：删除旧 HTTP 迁移 codec 未使用的序列化方法

## 目标

从 `LegacyHttpRuleFieldsCodec` 删除没有调用方的 `SerializeHeaders` 和 `SerializeRequestOptions`，以及只为这些 writer 服务的代码；继续支持现有数据库中的旧 HTTP 配置迁移读取。

## 背景与证据

审计报告 F004：`LegacyHttpProviderMigration` 使用 codec 的解析方法；全仓源码和测试检索未发现两个序列化方法的调用方，也未发现反射/动态入口。

## 范围

- 删除两个未调用的序列化方法及其专属 writer/import。
- 保留 legacy parsing、迁移字段解释、错误处理和已有兼容行为。
- 检查 migration 注册、生产调用方、测试及动态使用后再删除。

## 验收

- 仓库搜索确认上述序列化方法及仅供其使用的 writer helper 不再存在。
- 旧 HTTP Provider migration 的有效、缺失字段及不可转换项测试通过。
- 不新增永久测试覆盖已删除的私有实现细节；按需执行相关 migration focused tests、format 和 Release build。

## 长期合同

- `docs/05_DATA_AND_COMPATIBILITY.md` 第 4、9 节：已发布数据迁移兼容与 Provider 导入/导出边界。
- `docs/08_QUALITY_AND_TESTING.md` 第 6.3 节：关键持久化兼容测试。

## 依赖与风险

无前置任务。严禁删除旧数据库解析逻辑或修改已发布 SQLite migration。
