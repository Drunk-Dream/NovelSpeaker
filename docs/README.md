# NovelSpeaker 文档索引

本目录描述 NovelSpeaker 的稳定产品形态、架构约束、专项设计、质量标准和当前开发计划。

## 阅读顺序

1. `00_PROJECT_BRIEF.md`：项目目标、用户价值和总体边界。
2. `01_PRODUCT_SCOPE.md`：页面、功能和用户可见行为。
3. `02_TECH_STACK_AND_ARCHITECTURE.md`：技术栈、分层、依赖方向和代码组织。
4. 按任务阅读对应专项设计：
   - `03_HTTP_TTS_COMPATIBILITY.md`
   - `04_PLAYBACK_PIPELINE.md`
   - `05_DATA_AND_PERSISTENCE.md`
   - `06_SECURITY_AND_DATA_SAFETY.md`
   - `07_OBSERVABILITY_AND_OPERATIONS.md`
   - `08_RUNTIME_AND_LIFECYCLE.md`
   - `12_REGEX_REPLACEMENT_PIPELINE.md`
   - `13_VISUAL_DESIGN_SYSTEM.md`
5. `09_TESTING_AND_QUALITY.md`：测试分层、回归资产和质量门禁。
6. `10_ENGINEERING_CONVENTIONS.md`：编码、资源、异步和提交约定。
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
- 每项任务包含范围、前置条件和可重复的自动验收。
- 已完成阶段从当前 Backlog 移出并归档。

### 归档

`archives/` 只用于历史追溯：

- 归档任务不再作为当前实现依据。
- 最新行为始终以数字编号文档和 `TASK_BACKLOG.md` 为准。
- 归档文件不重新开启任务，也不覆盖后续设计决策。

## 当前视觉设计入口

全局界面重设计统一以 `13_VISUAL_DESIGN_SYSTEM.md` 为最终形态依据。该文档覆盖：

- 浅色与深色主题的语义颜色和表面层级。
- 字体、间距、圆角、描边、阴影与动效。
- 主窗口、播放页、书库、规则工作台、设置、缓存管理和迷你播放器。
- 按钮、列表、输入控件、进度、对话框、Flyout、Snackbar 和状态视图。
- WPF 资源字典组织、主题切换、可访问性和视觉验收标准。

任务 1 的当前实现审计和行为基线见：

- `VISUAL_ASSET_AUDIT.md`：顶级窗口、页面、运行时 Dialog/Flyout/Snackbar/菜单、局部组件和现有行为契约。
- `VISUAL_ASSET_AUDIT.json`：供测试机器检查的完整 XAML 清单、主题链路、迁移归属和测试文件矩阵。
