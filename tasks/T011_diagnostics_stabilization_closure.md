# T011 — 诊断系统稳定化收口

## 目标

对 T007–T010 做跨模块收口，不增加新功能。

## 必须审计

1. Storage trust boundary 只有一个权威实现。
2. Scoop Data-root Junction 真实布局可工作。
3. Active Diagnostic Session 跨重启后始终有可见录制提示。
4. WPF 控制条不持有第二份 Session truth。
5. queue pressure 与 storage failure 语义分离。
6. 普通诊断/问题诊断共享 atomic bundle output。
7. 诊断系统自身失败可由生产日志定位且不泄露隐私。
8. 脱敏摘要不包含完整本地路径。
9. 无实验 adapter、重复 guard、临时脚本/截图、孤立 DI 注册。

## 自动验收

运行所有相关 focused tests，随后完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

额外确认：

- 测试不得通过开启可见 Desktop 获得绿色。
- 不使用固定 Sleep/Delay 猜完成。
- 人工验收永远可选，不阻塞完成。
- 如果“性能监测导出失败”仍可复现且新日志已给出根因，记录新的后续任务，不在本收口任务中无依据扩展范围。

## 完成

- `TASK_BACKLOG.md` 将 T007–T011 按实际结果标记完成并写简短成果。
- 删除已完成 task spec。
- 不自动规划下一阶段功能。
