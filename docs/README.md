# NovelSpeaker 文档索引

本目录描述 NovelSpeaker 的稳定产品形态、架构约束、专项设计、质量标准和当前开发计划。

## 阅读顺序

1. `00_PROJECT_BRIEF.md`：项目目标、用户价值和总体边界。
2. `01_PRODUCT_SCOPE.md`：页面、功能和用户可见行为。
3. `02_TECH_STACK_AND_ARCHITECTURE.md`：技术栈、分层、依赖方向、平台适配与 UI 样式所有权。
4. 按任务阅读对应专项设计：
   - `03_HTTP_TTS_COMPATIBILITY.md`
   - `04_PLAYBACK_PIPELINE.md`
   - `05_DATA_AND_PERSISTENCE.md`
   - `06_UI_AND_USER_FLOWS.md`
   - `07_SETTINGS_PAGES.md`
   - `08_RUNTIME_AND_LIFECYCLE.md`
   - `12_REGEX_REPLACEMENT_PIPELINE.md`
   - `13_VISUAL_DESIGN_SYSTEM.md`
5. `09_TESTING_AND_QUALITY.md`：测试分层、视觉契约、回归资产和质量门禁。
6. `10_ENGINEERING_CONVENTIONS.md`：编码、资源、样式作用域、异步和提交约定。
7. `11_DECISIONS_RISKS_OPEN_QUESTIONS.md`：已确认决策、风险和仍需明确的问题。
8. `TASK_BACKLOG.md`：当前阶段唯一有效的开发顺序、任务状态和自动验收。

## 文档职责

### 数字编号文档

`00`–`13` 只描述产品和系统的最终形态：

- 不记录迁移波次、临时兼容方案或“当前做到哪一步”。
- 不使用待办状态表达尚未实现的内容。
- 实现变化后，文档直接更新为最终应有行为。
- 同一事实只在最合适的文档中定义，其余文档通过引用建立关系。

### 当前 Backlog

`TASK_BACKLOG.md` 是唯一有效的开发计划：

- 使用 Todo List 表达未开始、进行中、完成和阻塞状态。
- 任务按实际开发依赖顺序编号。
- 每项任务只包含 Codex 能自动执行并以自动检查关闭的内容。
- 默认一次只执行一个编号任务；任务完成后停止，由下一次指令决定是否继续。
- Codex 完成任务后保留任务条目，标记为 `[x]`，并简要记录主要成果与验收结果。
- 删除、重写或插入任务属于新的任务规划操作，不由 Codex 在完成任务时自行触发。需要删除或重写旧计划时不创建归档副本，历史通过 Git 追溯。

## 当前视觉设计入口

全局视觉目标和长期样式边界统一以 `13_VISUAL_DESIGN_SYSTEM.md` 为依据。该文档同时定义：

- 浅色与深色主题的颜色、表面层级和整体质感。
- “克制型 Fluent”的 Hover、Pressed、Keyboard Focus、Selected/Checked 状态语义，以及 Light/Dark/High Contrast 的主题适配原则。
- Wpf.Ui 与 NovelSpeaker 的模板、资源和主题所有权。
- Palette、Token、按控件族集中维护的具名样式、自有控件、Feature 组件和页面布局的作用域。
- `Shared/Theming/Resources` 与 `Shared/Presentation/Controls` 的最终目录、加载顺序和依赖方向。
- 正式自有控件与 Style Gallery fixture 的边界。
- Style Gallery 作为按资源族组织的长期样式目录，以及按稳定 page/window 身份组织的按需截图、WPF 契约和视觉验收工具；生成的 `artifacts/visual-review/` 不是默认测试基线或长期仓库资产。
- 主窗口、各功能页、对话框和迷你播放器的最终形态。

当前公共视觉资源与正式 UI 迁移阶段已经完成。后续开发顺序见 `TASK_BACKLOG.md`。
