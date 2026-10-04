# T009：清理不承载行为合同的 WPF 实现细节断言

## 目标

根据质量合同和测试历史，精简只锁定资源 key、brush identity、精确 icon enum 或内部 visual-tree 结构的 WPF 断言；保留真实产品行为和回归保护。

## 背景与证据

审计报告 D005：`SelectionSurfaceVisualTests` 检查部分资源 key 和 brush identity；`PlayerViewLayoutTests` 检查具体 icon enum、null content 和命名 visual-tree/layout 细节。质量文档默认不将资源 key、模板组成和内部 visual structure 作为永久合同。

## 范围

- 对报告列出的断言逐项追踪历史原因、对应用户行为、辅助功能/键盘/导航/关键入口合同。
- 删除纯实现细节断言；若原测试混合了行为与结构断言，只删除低价值部分。
- 保留因已知应用崩溃或关键入口可用性而成立的代表性测试；WPF 测试继续使用隔离 Desktop/fail-closed harness。
- 不改产品视觉、不新增截图/像素基线，也不以本任务重构测试框架。

## 验收

- 每个被删除断言都有明确理由：没有独立产品/辅助功能合同，且不对应仍需保护的已知回归。
- 关键页面创建、导航、键盘/automation name、focus/Popup 生命周期等适用行为覆盖保留。
- 受影响的 WPF tests 在隔离 Desktop 中通过；失败必须 fail closed，不启用可见窗口环境变量。
- 不添加永久实现形状测试；执行受影响 WPF focused tests、format 和 Release build。

## 长期合同

- `docs/08_QUALITY_AND_TESTING.md` 第 3、7、8 节：永久测试准入、WPF 行为合同和隔离 Desktop。
- `docs/06_UI_AND_VISUAL_SYSTEM.md` 第 12、15、16、18 节：视觉资源与交互的长期产品合同。

## 依赖与风险

无前置任务。若断言关联历史回归或当前 accessibility contract，保留并记录依据，不为了减少行数而删除。
