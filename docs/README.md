# NovelSpeaker 项目文档

`docs/` 是 NovelSpeaker 的产品、架构与开发计划入口。数字编号文档描述目标终态；`TASK_BACKLOG.md` 只描述尚未完成的开发任务；`archives/` 只用于历史追溯。

## 阅读顺序

1. `00_PROJECT_BRIEF.md`：产品定位和核心链路。
2. `01_PRODUCT_SCOPE.md`：目标产品范围。
3. `02_TECH_STACK_AND_ARCHITECTURE.md`：分层、功能切片和依赖方向。
4. `03_HTTP_TTS_COMPATIBILITY.md`：HTTP TTS 规则执行边界。
5. `04_PLAYBACK_PIPELINE.md`：播放、预取、主动缓存和媒体控制语义。
6. `05_DATA_AND_PERSISTENCE.md`：SQLite、文件、缓存和导出数据规则。
7. `06_UI_AND_USER_FLOWS.md`：全局 UI、选择模式、播放和规则工作台。
8. `07_SETTINGS_PAGES.md`：设置页层级和各子页职责。
9. `08_RUNTIME_AND_LIFECYCLE.md`：进程、页面、播放和后台任务生命周期。
10. `09_TESTING_AND_QUALITY.md`：自动测试和质量门禁。
11. `10_ENGINEERING_CONVENTIONS.md`：代码与工程约定。
12. `11_DECISIONS_RISKS_OPEN_QUESTIONS.md`：稳定决策和剩余风险。
13. `12_REGEX_REPLACEMENT_PIPELINE.md`：正则替换专项语义。
14. `TASK_BACKLOG.md`：下一阶段开发顺序、依赖和状态。

## 文档职责

- 根目录 `README.md`：只描述当前已经实现、用户今天可以使用的能力。
- 数字编号文档：描述产品和架构最终形态，可以包含尚待 Backlog 实现的目标设计。
- `TASK_BACKLOG.md`：唯一的计划和任务状态来源，不在数字文档重复 Wave/Epic。
- `AGENTS.md`：只记录开发约束和 Agent 工作规则，不重复维护产品事实。
- `archives/`：已完成或被替代的历史，不作为最新实现依据。

## 当前实现主线

NovelSpeaker 保持本地 TXT + HTTP TTS 的轻量范围。当前实现已经完成：

- DI、生命周期、资源所有权和测试体系收口。
- Fluent 视觉语言、规则工作台和设置子页统一。
- 章节主动缓存、后台进度、缓存管理和按章 MP3 导出。
- Windows 媒体控制、托盘、迷你播放器和定时停止。
- 桌面多选、当前章节定位、禁用状态、空状态和 UI 文案统一。

当前剩余质量门禁与任务状态以 `TASK_BACKLOG.md` 为准。
