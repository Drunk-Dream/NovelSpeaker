# T024：精简 Architecture Fitness 源码扫描器

## 目标

降低自制 C#/XAML source scanner、synthetic parser fixtures 与实现形状断言的维护面，同时保留少量长期架构合同。

审计依据：F06 / S04。相关合同：`docs/01_SYSTEM_ARCHITECTURE.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 按检查逐项分类：依赖方向/模块循环/owner/trust boundary 等长期约束；具体类型清单、私有 setter、virtualization markup/语法细节等实现形状约束。
- 保留真实长期合同的稳定检查；删除过窄/重复规则、只服务于被删规则的 parser/helper 和 synthetic fixtures。
- 不整体删除 Architecture Fitness Tests，不用新增白名单绕过产品源码检查，也不为留下的扫描器增加穷举 parser 测试。
- 只有在框架不能表达真实长期约束时才保留自制扫描逻辑，并将其范围缩到必要语法。

## 验收

- 每个保留的架构检查都能对应到长期合同及受保护的回归边界。
- 删除规则后，引用、helper 与 fixtures 一并清理；保护上述合同的代表性检查仍可运行。
- 执行 Architecture focused tests、format 和 Release build。

## 交付

记录移除的实现形状规则与保留的核心架构测试依据，更新 Backlog 并删除本规格。
