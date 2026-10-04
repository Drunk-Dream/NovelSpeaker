# T003：收窄 Release workflow 的写权限

## 目标

将 `contents: write` 限制在实际创建/上传 Release 的 publish job；验证与复用质量门禁只获得运行所需的最小只读权限。

## 背景与证据

审计报告 F003：`.github/workflows/release.yml` 在 workflow 级授予 `contents: write`；validate 和 reusable quality jobs 只验证 tag、restore/build/test，只有 publish 调用 `gh release create/upload`。GitHub reusable workflow 权限由 caller 提供，callee 可以降低权限但不能提高。

## 范围

- 检查 release workflow 的所有 job、checkout、artifact transfer 和 reusable workflow 所需权限。
- 默认声明最小读取权限；仅 publish job 显式声明 `contents: write`。
- 确认被调用的 quality workflow 没有隐式依赖写权限，且权限可由 caller 安全降级。
- 保留 tag ancestry、发布产物校验、checksum 和 Release 创建行为。

## 验收

- 静态检查确认仅 publish job 拥有 `contents: write`，其它 job 的权限满足其动作且不继承写权限。
- 运行仓库已有 workflow/actionlint 或 workflow contract 验证；如无现有自动检查，执行针对 YAML 的可重复静态核对，不新增不必要工具依赖。
- Release 目录与最终 ZIP 的现有验证规则不变。
- 记录未能由本地验证覆盖的 GitHub 平台行为；不要求触发远程 Release。

## 依赖与风险

无前置任务。参考 GitHub reusable workflow permissions 文档；不得删掉发布所需写权限。
