# T006 — 按钮样式专项最终复核与收口

**优先级：** P1  
**类型：** Closure / 全项目验收  
**前置：** T001–T005

## 目标

对完整差异做一次独立收口，确认共享架构、全部调用点、主题状态、菜单、Gallery 与自动检查形成一致的最终结果；不继续叠加无必要的抽象。

## 必须执行

1. 阅读 `.codex/review-checklist.md`，按其中要求对最新完整 diff 逐项报告通过、失败或不适用及依据。
2. 重新全项目扫描生产 XAML/C# 中的 Button、ui:Button、按钮样式、菜单项与动态创建控件，确认 T001 报告中的每个项目均有最终处置。
3. 复查可视 `ui:Button` 样式中是否仍存在通过 `IsMouseOver` / `IsPressed` 直接争夺 `Background` / `BorderBrush` 的触发器；区分 Slider、Selection Surface、MenuItem 和无视觉 InteractionHost 等非目标控件，避免误报。
4. 检查 `App.Button.InteractionHost` 与所有保留例外仍只有一个视觉状态 owner，且有证据和文档依据。
5. 检查危险填充按钮、危险图标按钮、危险菜单项的 Default / Hover / Pressed / Focus / Disabled，以及 Primary、Secondary、Subtle、Icon、ContextAction、Media、Navigation 等其余变体。
6. 复核 Light / Dark 实际对比度配对、High Contrast 系统色映射，以及 `docs/06_UI_AND_VISUAL_SYSTEM.md` 与 `docs/08_QUALITY_AND_TESTING.md` 是否与最终实现一致。
7. 运行 `git diff --check`、XAML/资源加载相关检查与任务需要的 focused tests。
8. 执行完整门禁：

   ```powershell
   dotnet restore --locked-mode -r win-x64
   dotnet format --verify-no-changes --no-restore
   dotnet build -c Release --no-restore
   dotnet test -c Release --no-build
   ```

9. 清理临时截图、manifest、脚本、测试、fixture 和诊断文件。根目录的 `BUTTON_STYLE_AUDIT_REPORT.md` 是用户自管的临时报告，不提交、不擅自删除。
10. 在 Backlog 中记录最终结果；按 `AGENTS.md` 删除已完成任务的 `tasks/Txxx_*.md`。

## 禁止

- 不为通过门禁而删除核心测试、弱化 Desktop 隔离或跳过关键验证。
- 不复制 Wpf.Ui 完整按钮/菜单模板。
- 不保留无调用者的兼容样式、重复状态 owner、临时测试入口或仅用于 Gallery 的生产代码。
- 不将截图作为永久像素基线。
- 不 push、创建 PR、tag 或 release。

## 验收

- 完整门禁通过；如环境限制导致某项无法执行，必须如实记录，不得声称全部通过。
- 全项目按钮样式和调用点清单全部关闭或明确说明保留原因。
- 主题与菜单状态的前景/背景配对可读，High Contrast 配对正确。
- Review Checklist 已逐项报告。
- 临时副产物清理完成，Backlog 有简短成果总结。
