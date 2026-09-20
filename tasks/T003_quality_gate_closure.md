# T003：收口质量门禁与后续测试工作流

## 目标

在 T002 完成测试瘦身后，清理测试基础设施残留，确认 CI 与新的长期测试策略一致，并执行一次完整质量收口。

## 检查内容

- 五个现有测试项目是否仍各自有长期价值；
- `.github/workflows/quality-matrix.yml` 是否仍只是运行有效测试项目，没有重复或失效入口；
- `tests/TestKit` 是否还包含只服务于已删除测试的 helper/harness；
- 是否存在被删除测试留下的 fixture、TestAssets、脚本、截图或环境变量；
- AGENTS/docs 中的新规则是否与实际测试运行方式一致；
- 是否有测试为了新策略被简单 skip/disable，而不是正确删除或重写。

## CI 原则

如果现有五层项目结构仍然合理，保留结构和 Quality Matrix，不为了“测试变少”机械合并项目。

只有在以下情况调整 CI/项目结构：

- 某测试项目已经没有任何长期测试价值；
- workflow 存在重复执行或指向失效项目；
- 某入口仅为已删除的历史测试设施服务。

不得通过 filter、skip、降低断言或减少默认执行范围来制造绿色。

## 临时验证

允许建立临时验证脚本/测试确认 CI 或 TestKit 清理，但完成后必须删除。

## 完整验收

执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

并检查：

- 所有测试均无可见桌面窗口泄漏；
- 无新增未解释 warning；
- 无遗留临时测试/fixture/script/output；
- 工作区没有本轮生成的一次性产物；
- `TASK_BACKLOG.md` 记录最终剩余测试体系的简要成果，但不维护测试数量 KPI。

## 完成标准

本阶段完成后，未来任务应能够遵循：

- 核心功能/核心 Bug：优先考虑 test-first，增加最小稳定核心测试；
- 非核心修改：默认不新增永久测试；
- 调试/研究：可以写临时测试，但必须在完成用途后删除；
- 已有测试失败：先判断核心契约是否真实失败，再决定修生产代码还是删改测试；
- 测试不是产品需求来源。
