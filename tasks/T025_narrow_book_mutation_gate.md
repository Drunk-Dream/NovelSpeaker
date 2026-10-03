# T025：缩小 BookMutationGate 独占区

## 目标

减少导入准备期间无关 Book mutation 的全局阻塞，同时维持导入、删除、文件 staging/journal 与 SQLite commit 的一致性。

审计依据：D01 / S05。相关合同：`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`、`docs/05_DATA_AND_COMPATIBILITY.md`、`docs/specs/BOOK_IMPORT.md`。

## 范围与约束

- 先绘制 DirectBookImportService 中读取、解析、规范化、hash、切章、staging、target revalidation、journal 与 commit 的数据依赖/失败恢复边界。
- 验证删除/Source 更新及 Active Cache 冻结时哪些操作必须互斥；只将 gate 缩至经证明安全的边界。
- 保持已批准 schema、外部 TXT 不修改、恢复 journal 归属与目标重新验证合同，不建立第二把全局 gate 或新的状态 owner。
- 若不能证明准备阶段与目标状态/恢复逻辑解耦，应保留现状并在成果中给出证据；不得仅为响应速度推测性释放锁。

## 验收

- 使用受控并发验证不同 Book 可并发准备、同一目标变更冲突安全，以及失败/取消后文件和 journal 一致性。
- 永久测试只保留对核心数据安全有长期价值的行为场景，删除临时验证资产。
- 执行 Book import/Source lifecycle focused tests、format、Release build；如风险判断要求，运行相关完整测试层。

## 交付

说明 gate 实际缩小的范围或保留锁的证明理由，更新 Backlog 并删除本规格。
