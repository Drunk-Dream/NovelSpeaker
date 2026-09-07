# 工程约定

## 1. 代码组织

- 文件名、主类型和 namespace 对应。
- Feature 优先按业务域组织，不按“Services/Helpers/Managers”横向堆放。
- 行为保持型 move/rename 与行为改变尽量分开。
- 新实现迁移完成后直接删除旧入口，不长期保留 Old/New/V2/Compat。

## 2. 命名

- Coordinator：拥有长期 session/background operation 生命周期。
- Controller：Feature 内编排一组明确交互/流程，不默认成为全局 service。
- Query：只读场景化 read model。
- Store/Repository：持久化技术边界。
- Snapshot：immutable 当前状态投影。
- Draft/EditorSession：页面编辑期可变状态。

避免万能 `Manager`、`Helper`、`Utils`。

## 3. Interface

新增 interface 前必须能说明：

- 边界在哪里；
- 谁调用；
- 谁实现；
- 生命周期；
- cancellation；
- 为什么 concrete internal class 不足够。

Feature-local projector/controller 默认 internal sealed。

## 4. 异步

- 异步 I/O/业务流程传递 `CancellationToken`。
- `OperationCanceledException` 不转成失败。
- 不用 `.Result`/`.Wait()` 无界阻塞 UI。
- `async void` 只限事件入口。
- fire-and-forget 必须有 owner。
- `Task.Yield()` 不视为后台线程切换。

## 5. Dispatcher

可能随数据规模增长的 CPU/projection 不应无界执行在 Dispatcher。

UI 提交应批量、短小，并与 staged loading/activation 生命周期一致。

## 6. 错误

- 技术异常不直接泄露到 UI。
- 用户可观察错误使用稳定安全投影。
- 日志不包含正文、凭据、完整请求/响应秘密。
- cancellation 不显示失败 Snackbar。

## 7. WPF

- code-behind 只处理 WPF 特有生命周期/交互。
- ViewModel 不引用 Page/Window/Dispatcher/Brush/Style/Thickness 等视觉类型。
- 普通 ViewModel transient。
- UI state 不依赖 container 长期存在。
- 标准控件视觉遵守 `07_VISUAL_DESIGN_SYSTEM.md`。
- container generation/recycling、scroll extent/viewport、标准 virtualization lifecycle 优先交给 WPF 自身；Feature 不建立与框架并行的第二套状态机。

## 8. 数据与文件

- 已发布 migration append-only。
- 所有持久化路径通过数据根 resolver。
- 永不修改外部 TXT。
- 删除前验证路径归属。
- cache 可重建，不用长期 compatibility reader 掩盖内部 cache 重构。

## 9. 安全

- TTS/脚本规则视为不可信输入。
- 不放宽文件、进程、反射、CLR、任意宿主对象能力。
- 测试 fixture 使用脱敏数据。

## 10. 测试

- 缺陷先建立失败行为测试。
- 重构前用特征测试保护用户可观察行为，不照抄私有实现。
- 测试可以随架构重构删除/重写，数量不是永久目标。
- WPF 自动测试默认隐藏 Desktop。

## 11. 文档

- 编号文档只保存稳定终态。
- 当前执行计划只在 `TASK_BACKLOG.md`。
- 新规划阶段允许直接重写 Backlog，历史交给 Git。
- 不建立 docs/archive 或任务历史索引。
- Codex 默认不重组编号文档；只有任务明确要求且目标架构确实变化时才修改相应 owner 文档。

## 12. Git

- 未经用户明确授权不提交、推送、创建 PR/Release/tag。
- 不丢弃用户已有改动。
- 用户要求提交时按逻辑目的拆原子 commit。
- commit 使用 English Conventional Commits。
- 纯目录/namespace move 尽量与行为变化分开。

## 13. 依赖与格式

只有依赖确实变化时更新 lock file。

本项目文本文件统一使用 LF。修改已有文本文件时不得无关地转换为 CRLF，也不得仅因换行符导致整文件重写。

完整门禁见 `08_TESTING_AND_QUALITY.md`。

## 14. 成熟能力优先 / Build-vs-Reuse

NovelSpeaker 的默认决策不是“自己实现”，而是先确认已有成熟能力是否已经解决问题。

判断顺序：

1. **平台/标准库**：优先 .NET、Windows、WPF 自带能力。
2. **当前技术栈**：优先 Wpf.Ui、CommunityToolkit.Mvvm、现有 Infrastructure/Application 能力。
3. **项目已有组件**：先复用职责清晰且语义匹配的已有实现，不复制第二套。
4. **成熟外部依赖**：只有现有技术栈存在真实能力缺口，且引入成本可控时再评估。
5. **自定义实现**：仅在前述方案不能满足已证实需求时采用。

自定义基础设施前必须记录：

- 现有能力具体缺少什么；
- 这是产品/性能/兼容性的真实要求，还是实现偏好；
- 为什么适配/组合现有能力不足；
- 自定义实现新增了哪些 mutable state、线程/Dispatcher、生命周期或资源 ownership；
- 如何测试和长期维护；
- 未来何时可以删除并回归标准能力。

特别警惕重复实现以下高复杂度基础设施：

- UI virtualization / item container lifecycle；
- scroll/viewport/extent 状态机；
- 通用消息总线；
- 通用后台任务调度器；
- DI/service locator；
- 自定义缓存数据库层替代已有 SQLite 能力；
- 自定义并发/重试框架；
- 已有库能够稳定完成的解析、序列化或网络基础设施。

“成熟能力优先”不等于盲目增加第三方依赖。对简单、稳定、领域特有的纯计算或少量 glue code，直接实现通常比引入新库更合适。重点是避免重新承担框架级基础设施的长期维护责任。
