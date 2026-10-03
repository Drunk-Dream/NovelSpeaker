# T021：合并 Provider 限流实现路径

## 目标

将当前 Provider-keyed typed port 经 synthetic `RuleId` / string adapter 到旧 rule-keyed limiter 的路径，收敛成一条 Provider-keyed API，保留既有调度语义。

依赖：T018。审计依据：F04 / S02。相关合同：`docs/01_SYSTEM_ARCHITECTURE.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 确认 limiter 当前所有调用方，并定位队列、并发许可、pace、retry-after、取消及 lease 的唯一 owner。
- 迁移实现至 typed Provider identity/limit 边界，删除 synthetic ID/string 格式转换、无独立使用者的旧 `ITtsRateLimiter` 接口和重复 DI 注册。
- 不改变请求公平性、并发限制、重试节奏和取消语义；不新增通用调度抽象。

## 验收

- 既有 limiter 核心行为测试覆盖队列、retry-after、并发 permit 与取消；如现有测试锁定私有实现，按长期合同收敛而非照搬。
- 全仓引用搜索确认 RuleId/string adapter 已移除。
- 执行 Speech/Infrastructure focused tests、format 和 Release build。

## 交付

更新 Backlog 成果，删除旧接口/adapter/test fixture 中无效部分及本规格。
