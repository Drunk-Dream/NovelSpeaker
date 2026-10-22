# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：全项目按钮样式与交互状态架构收口

- **状态：** 执行中（T002–T006）。
- **执行基线：** Codex 开工时本地 `dev` 的实际 HEAD 与工作区。先执行 `git status`，保留所有用户已有修改；本任务不依赖远端分支状态覆盖本地工作。
- **目标：** 全面审查并统一 NovelSpeaker 的可视按钮状态机制，从共享样式架构解决 WPF Button 与 Wpf.Ui `ui:Button` 的状态优先级冲突；检查所有按钮变体、所有主题及实际调用位置，不只修复危险按钮。
- **核心决定：** 具有自身 Hover/Pressed 等可见状态的普通操作按钮，优先统一使用 `ui:Button` 和 Wpf.Ui 提供的状态属性。`App.Button.InteractionHost` 这类仅负责命令、键盘焦点和命中区域、由外层 Selection/Surface 唯一负责视觉状态的宿主保留为明确例外。菜单项属于独立控件族，按其实际模板与高亮机制处理，不强行套用 Button 属性。
- **长期依据：** `AGENTS.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/08_QUALITY_AND_TESTING.md`、`tools/NovelSpeaker.StyleGallery/README.md`。
- **技术依据：** 以仓库锁定的 Wpf.Ui 版本为准（当前调查指向 4.3.0）；参考该版本 `Button.xaml` / `Button.cs` / `MenuItem.xaml`、Microsoft WPF Dependency Property Value Precedence，以及 WCAG 2.2 对比度标准。不得用其他版本的模板行为替代当前实际实现。

## 2. 执行约束

- 严格按 **T001 → T002 → T003 → T004 → T005 → T006** 顺序执行。每个任务结束时仓库应保持可构建、可测试；不得依赖长期兼容 alias 或无文档的 staged breaking state。
- T001 只调查，不修改生产源码、测试或长期文档；输出临时报告 `BUTTON_STYLE_AUDIT_REPORT.md` 到仓库根目录。报告不提交 Git，由用户自行管理。后续任务优先复核实际源码，不因报告缺失而盲目照搬。
- 必须覆盖全项目 XAML 与 C# 动态创建的按钮，包括 `Button`、`ui:Button`、`App.Button.*`、`App.Media.*`、`App.Navigation.*`、菜单项、特殊交互宿主和 Style Gallery；不得只搜索 `Buttons.xaml`。
- 不复制 Wpf.Ui 完整 `ControlTemplate`，不新增通用按钮控件或额外主题框架。优先使用现有 `Provider.UiButton`、Wpf.Ui 状态属性、语义 Palette 与显式 `App.*` 样式。
- 同一个可点击区域只有一个视觉状态 owner。普通可视按钮不得由外层样式触发器和 Wpf.Ui 模板同时争夺背景/边框；InteractionHost 不绘制第二层 Hover/Pressed Surface。
- 语义颜色必须成对验证：Light / Dark 检查实际颜色对比度；High Contrast 使用系统颜色的正确配对，不把品牌红色强加到系统高对比模式。
- 不改变命令业务逻辑、删除/保存语义、Provider 生命周期、选择行为或导航行为。按钮控件类型迁移时保留命令、参数、Automation Name、Tooltip、键盘行为和点击区域。
- 自动测试是主要验收；人工视觉/交互验收永远可选，不阻塞后续任务。
- 临时脚本、临时测试、截图和诊断副产物在对应任务结束前清理；不得提交 `BUTTON_STYLE_AUDIT_REPORT.md`。
- 所有文件使用 LF 换行。不得 push、创建 PR、tag 或 release。

## 3. 任务顺序

### [x] T001（P1 / Audit）— 全项目按钮样式与状态 owner 盘点

完成标准：形成完整的样式/调用点/主题状态清单，确认 Wpf.Ui 版本和模板实际行为；只输出根目录临时报告，不修改源码。

### [ ] T002（P1 / Foundation）— 统一共享按钮样式的 Wpf.Ui 状态机制

完成标准：共享可视按钮样式统一到 `ui:Button` 状态属性；同一任务内迁移所有受影响调用点，保留 InteractionHost 明确例外，不复制 Provider 模板。

### [ ] T003（P1 / Call sites）— 收口领域按钮样式与全项目调用点

完成标准：媒体、导航、页面操作、Metadata 保存/取消、图标按钮及剩余未显式声明语义的可视按钮全部经过审查；保留的例外有明确职责与理由。

### [ ] T004（P1 / Themes）— 统一 Light/Dark/High Contrast 与菜单动作状态

完成标准：危险、强调、普通与低强调动作的 Hover/Pressed/Focus/Disabled 反馈符合语义；菜单高亮使用正确的模板/资源机制；颜色配对可读。

### [ ] T005（P1 / Verification）— 修正 Style Gallery 状态验证并补充最少量自动检查

完成标准：区分 Gallery 静态模拟状态与真实 WPF 输入状态；对关键模板路径做代表性运行时验证，对稳定的架构/对比度合同增加少量自动检查，不新增像素基线体系。

### [ ] T006（P1 / Closure）— 全项目复核、完整门禁与清理收口

完成标准：全量样式与调用点复扫通过，完成 Review Checklist 与完整质量门禁，清理临时副产物并记录成果。

## 4. 完成记录

本阶段已完成 T001。

- T001 完成：形成按钮样式、调用点、主题状态与 Wpf.Ui 4.3.0 模板行为审计报告；发现 Light DangerItem Hover 对比度 4.19:1；报告保留在根目录，不提交。
各任务完成后按 `AGENTS.md` 更新状态、记录简短成果并删除对应的临时 `tasks/Txxx_*.md`；最终保留阶段成果摘要，Git 历史保存已完成任务规格的演进记录。
