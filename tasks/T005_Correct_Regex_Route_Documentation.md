# T005：修正文档中的 Regex 规则导航归属

## 目标

将 `docs/02_RUNTIME_AND_NAVIGATION.md` 中 RegexReplacementRules 的稳定父级描述改为当前产品实际使用的 Settings 路由。

## 背景与证据

本轮扫描在 App 导航生命周期结果中确认：当前 `ShellNavigationAdapter`、Settings 页面入口和 route tests 均将 RegexReplacementRules 归属 Settings；长期导航文档仍写作 `RegexReplacementRules → ImportTextSettings`。Git 历史显示代码于 2026-10-01 更新。

## 范围

- 核对当前 route 定义、route tests 和页面入口。
- 只修正文档中这一处已证实的父级关系，并检查相邻 route 示例是否仍准确。
- 不改变导航代码或产品返回行为。

## 验收

- 文档父级与当前 route、入口和测试一致。
- 文档链接有效，文本保持 LF。
- 无需新增测试；执行 Markdown 链接/格式检查（若项目已有）或手工核对链接目标。

## 依赖与风险

无前置任务。若实现与测试之间出现新的冲突，停止改写并记录证据，不推断产品意图。
