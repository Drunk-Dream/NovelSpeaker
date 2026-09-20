# T002：按核心契约瘦身现有测试体系

## 目标

执行 T001 的审计结论，把永久测试资产收敛到真正保护核心契约的范围。

## 优先顺序

1. `NovelSpeaker.App.WpfTests`：优先删除样式细节、资源顺序、精确像素、Visual Tree 结构等实现锁定测试；
2. `NovelSpeaker.App.PresentationTests`：合并/删除过细的 ViewModel 投影、调用次数和内部时序测试；
3. Architecture tests：保留少量关键架构门禁，减少对规则解析器/检测器本身的穷举式 contract tests；
4. Application / Infrastructure：删除重复覆盖，将同一核心行为收敛为少量代表性行为测试；
5. `tests/TestKit`：删除仅被已移除测试使用的 helper、fixture 和 harness 代码。

## 永久测试处理规则

- KEEP：保持或仅做必要整理；
- MERGE/REWRITE：优先变成更高层、面向稳定行为的少量测试；
- DELETE：直接删除，不创建 compatibility wrapper；
- 发现核心测试缺口：只增加覆盖该风险的最小永久测试。

对于核心 Bug/行为调整，推荐先写最小失败测试再实现，但不是强制流程。

## 临时测试

允许创建临时测试来：

- 确认某个旧测试是否只是在锁实现；
- 复现当前真实行为；
- 验证测试合并后的覆盖；
- 辅助安全删除 TestKit/helper。

临时测试完成用途后必须删除，不得进入最终提交。

## 生产代码边界

本任务不是生产代码重构任务。

只有以下情况允许最小修改生产代码：

- 删除过度测试后发现一个真实的核心回归；
- 为了让一个核心测试通过稳定公共行为验证，必须解除明显的测试专用耦合；
- 清理纯测试专用、已经没有生产用途的兼容入口。

任何更大的架构/产品问题记录为后续任务，不在本轮顺带扩张。

## 验收

至少：

- 所有剩余测试都能对应到 `docs/08_QUALITY_AND_TESTING.md` 的永久准入标准；
- 不再存在明显锁定 XAML resource 顺序、Setter 顺序、精确像素、Visual Tree 形状的永久测试，除非能证明它们本身是核心功能契约；
- 不再以内部调用次数或可自由重构的私有结构作为主要永久断言；
- Architecture tests 保留关键边界，不对检测器实现做无必要穷举；
- 删除所有本任务临时测试、临时 fixture、脚本和输出；
- 运行受影响测试项目的 targeted/full project tests；
- `dotnet build -c Release --no-restore` 通过。
