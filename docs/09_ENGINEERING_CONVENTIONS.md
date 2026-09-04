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

完整门禁见 `08_TESTING_AND_QUALITY.md`。
