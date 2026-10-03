# T023：将 CI 全局质量门禁移出测试矩阵

## 目标

每个 workflow revision 只运行一次 locked restore、format verify、solution Release build 与必要的 Gallery 检查，测试矩阵继续为各层提供独立可辨识结果。

审计依据：F05 / S03。相关合同：`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 检查 `.github/workflows/quality-matrix.yml` 的 PR/Release 触发、矩阵依赖、版本参数与测试发布结果。
- 将共享门禁放在单一 job/可复用步骤中，并明确矩阵测试只在门禁成功后运行。
- 删除 solution build 已覆盖的重复 Gallery build；若存在不能由 solution build 覆盖的发布检查，保留该检查并说明理由。
- 保留每个 test project 的单独日志、失败状态及适当并行度。

## 验收

- 静态核对 PR 和 Release 两条路径的依赖关系、锁定 restore、format/build 状态传播和测试矩阵覆盖。
- 使用 workflow 静态校验或仓库认可的本地验证；如无法模拟 Actions，明确记录验证边界。
- 不运行重复全仓测试作为本任务的必要条件。

## 交付

移除重复 job steps，更新 Backlog 并删除本规格。
