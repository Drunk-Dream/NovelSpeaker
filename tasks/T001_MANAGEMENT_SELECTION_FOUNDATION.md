# T001：建立统一 Management Mode 选择基础设施

## 目标

在当前 `DesktopSelectionController<TKey>` 的 stable-key 选择能力之上，建立可被多个页面复用的“Normal Mode / Management Mode”选择 primitive。

本任务只做共享交互基础设施，不把 Book/Provider/Rule/Chapter 的业务动作放进 Shared。

## 必读

- `AGENTS.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`

## 当前代码关注点

至少审计：

- `src/NovelSpeaker.App/Shared/Presentation/Selection/DesktopSelectionController.cs`
- `src/NovelSpeaker.App/Shared/Presentation/Selection/*`
- 当前 Rules / SpeechServices / Player / CacheManagement 对该 controller 的调用方式
- `tests/NovelSpeaker.App.PresentationTests/Selection/DesktopSelectionControllerTests.cs`

不要先改业务页面，只确定共享 primitive 能支撑后续迁移。

## 必须实现

共享层必须能表达：

- `IsManagementMode`
- Enter / Exit
- normal mode Ctrl/Shift 进入模式并建立 selection
- management mode 普通 click toggle
- Ctrl/Shift toggle/range
- Select All
- visible/manageable item set reconciliation
- selected count = 0 时仍保持 management mode
- page leave/reset 时退出并清空
- stable-key refresh 保留仍可见同一对象
- right-click selected item → 保留 selection
- right-click unselected item → selection 变为仅该 item

`DesktopSelectionController` 可以继续承担集合/anchor/range 算法；可以新增更高一层 controller 组合它。不要为了模式管理重写已经稳定的 selection engine。

## 不做

- 不加入 Book/Provider/Rule 类型依赖。
- 不建立全局 SelectionService。
- 不做跨页面 selection。
- 不修改 CacheManagement 的文件管理器式行为。
- 不在 shared controller 中实现 delete/export/cache 等业务动作。
- 不新增 EventBus/Messenger。

## 测试

永久测试只保护长期行为：

- mode 进入/退出；
- 0 selected 不退出；
- hidden item reconciliation；
- normal vs management click 行为；
- right-click selection 语义；
- Select All 只选当前 item set。

可以重写/合并现有只锁定 controller 私有实现的测试，不追求测试数量。

## 自动验收

本任务不属于 breaking migration，完成时应至少：

```powershell
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

若 restore 状态不足，可先执行标准 locked restore。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
