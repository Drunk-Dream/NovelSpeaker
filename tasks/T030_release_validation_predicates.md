# T030：统一 Release 内容校验规则来源

## 目标

减少 publish output 目录与最终 ZIP 的 required/excluded predicates 重复维护，同时保留两层各自有效的完整性检查。

审计依据：D05。范围：`.github/workflows/`、release packaging scripts 与相关文档。

## 范围与约束

- 对照 publish directory 与 ZIP 最终内容检查，逐条比较输入、排除项、必需资产、tag ancestry 与 checksum 验证。
- 只有确实相同的策略列表才抽成单一来源或共用实现；目录检查和 ZIP 检查若检测不同故障，必须继续保留两层验证。
- 不因减少重复而放宽 release artifact 验收，不改版本发布流程或需要用户授权的远端操作。

## 验收

- 用当前有效 artifact 及缺少必需文件/包含禁止文件的 fixture 验证两层检查的职责与失败信号。
- 静态核对 PR/Release workflow 引用、失败传播及 tag/checksum 约束；无需触发真实发布。
- 运行仓库认可的脚本/workflow 验证；不运行无关完整测试。

## 交付

记录收敛的规则来源及保留两层验证的原因，更新 Backlog 并删除本规格。
