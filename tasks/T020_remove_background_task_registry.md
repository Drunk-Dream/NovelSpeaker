# T020：移除无生产用途的 BackgroundTaskRegistry

## 目标

清除唯一生产调用已移除后残留的 `BackgroundTaskRegistry` 及相应 startup/shutdown wiring。

审计依据：`CODEBASE_AUDIT_REPORT.md` S01；历史线索 `12ac686a`。相关合同：`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/08_QUALITY_AND_TESTING.md`。

## 范围与约束

- 再检查生产项目、XAML/DI/反射及启动关闭路径中是否仍有注册或等待入口。
- 删除 registry、`WpfStartupRuntime` 对应字段/构造参数/停止接收/等待完成 wiring，以及仅验证该抽象本身的测试。
- 保留当前由 startup owner 直接 await 的维护操作、取消与异常观察行为。
- 不引入替代 registry、fire-and-forget helper 或测试专用 shim。

## 验收

- 全仓引用搜索确认没有动态/生产使用残留。
- 运行 startup/组合根相关 focused tests、format 和 Release build。

## 交付

记录删除的旧实现与保留的生命周期 owner，更新 Backlog 并删除本规格。
