# T005 — Infrastructure Integration 以数据风险为中心清理

依赖：T004。规划基线 `main` @ `833333d`；实际以当前 HEAD 为准。

## 目标

将 `tests/NovelSpeaker.Infrastructure.IntegrationTests/` 历史 472 例收敛至约 **240 例**。重点合并数据库/Cache/Playback/Provider/Diagnostics 各套跨层重复的参数排列、内部适配器细节和重复端到端路径；保留真实 I/O 下能够发现数据破坏、安全漏洞、协议问题的测试。**这是高风险清理任务，数量预算不得压倒不可替代的数据保护。**

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`、`docs/05_DATA_AND_COMPATIBILITY.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`、`docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`、`docs/specs/HTTP_TTS.md`（对应 Provider 子组）。

## 优先审查的测试簇

1. Playback：`PlaybackCoordinatorLifecycleTests.cs`、`PlaybackCoordinatorFlowTests.cs`、`PlaybackCoordinatorRecoveryTests.cs`、`PlaybackCoordinatorRefreshTests.cs`、`PlaybackCoordinatorRegexChangesTests.cs`、`PlaybackCoordinatorActiveSourceTests.cs`、`ProviderProductionPipelineTests.cs`。把相同“旧 session/target 不提交”“同一切换语义在多状态执行”合成少数最有区别度的集成场景；避免沿用旧 Coordinator 的具体内部调度步骤或音频 request 计数。
2. Cache/SQL：`SqliteAudioCacheTests.cs`、`Persistence/SqliteMigrationRunnerTests.cs`、`Persistence/BookSourceMigrationTests.cs`、`Books/BookImportRepositoryTests.cs`、`Books/BookManagementServiceTests.cs`、`Books/BookOperationRecoveryServiceTests.cs`、`Persistence/SqliteReadingProgressStoreTests.cs`、`JsonAppSettingsStoreTests.cs`。删除重复的 happy path、同一错误用四层复测、具体 schema 排布/访问函数次数（非持久契约部分）；**保留正式数据迁移、rollback、原子写/删、用户数据不损坏、缓存索引与文件一致、并发关键风险和 reparse-point 防越界**。
3. Provider/HTTP：`Speech/HttpTtsClientTests.cs`、`Speech/HttpProviderRuntimeTests.cs`、`Speech/JintTemplateEvaluatorTests.cs`、`Speech/TtsResponseValidatorTests.cs`、`Speech/EdgeProtocolTests.cs`、`ProviderRequestLimiterTests.cs`、`Persistence/SpeechProviderPersistenceTests.cs`。合并可等价验证的 HTTP 请求体形状/异常分类；保留安全 Header、timeout/cancel、响应损坏、模板权限、规则迁移、Provider round-trip、重复名称与缺配置安全回落。
4. Diagnostics、Export、路径：`Diagnostics/LocalPerformanceTelemetryStoreTests.cs`、`Diagnostics/RollingFileLoggerProviderTests.cs`、`Diagnostics/SqliteDiagnosticSessionStoreTests.cs`、`Diagnostics/SqliteDiagnosticSessionExportServiceTests.cs`、`ConfigurationBackupTests.cs`、`ChapterMp3ExportWriterTests.cs`、`FileSystem/*Tests.cs`。裁减指标字段顺序、文件命名排列、普通消息格式及重复注入点；保留隐私脱敏、导出可信、session 恢复、fatal 退出归因、内容安全、安装/Junction/内部 reparse-point 防逃逸、正式文件导出有效性。

## 明确安全底线

- 不删除所有版本迁移的关键 up/down 数据验证、事务 rollback/失败恢复、外部路径防逃逸；不能因测试运行较慢就移除这些合同。若某些旧版本迁移已经由一个覆盖全部已发布版本的真实路径保护，可合并但须说明实测的旧版本范围。
- 不把真实 SQLite/file I/O 测试改成纯 mock 然后声称覆盖相同持久化风险；不以“只要不异常”为唯一断言。
- 不删除原子缓存行为、进度迁移、目录身份、Provider 选择安全、生成音频识别、HTTP/Jint sandbox 的代表性回归。
- 诊断/日志/遥测失败不影响业务，隐私数据不能落地；WPF/Desktop 隔离与完整 CI 仍由其它层验证。

## 验收

- 每组保留测试能指出明确的真实基础设施失败模式。建议 Infrastructure 225–255 例；若保护真实数据安全必须超过此区间，明确列明不可替代的用例和代价，再交给 T006 整体平衡。
- Infrastructure 全部 focused tests 通过，并运行涉及的 Application/Presentation 关键交叉验证；Release build、format/diff 检查通过。
- 移除无用 test fake、audio asset、临时库文件/目录和 TestKit helper 时先检查仓库实际引用；不删仍需要的真实编解码演示音频或隔离宿主资源。
