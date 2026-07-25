# NovelSpeaker 项目上下文

本目录是 NovelSpeaker 项目信息的唯一文档入口，用于向开发者和编程 Agent 提供稳定、可重复读取的产品、架构、数据、UI、运行时、质量和任务上下文。仓库根目录的 `AGENTS.md` 只记录开发约束，不重复维护项目事实。

项目目标是开发一个轻量、简洁、可快速迭代的 Windows 小说听书应用。第一版以本地 TXT 小说和兼容 Legado 风格的 HTTP 在线 TTS 规则为核心，不实现完整阅读器、在线书源或云端书库。

## 建议阅读顺序

1. `AGENTS.md`
2. `00_PROJECT_BRIEF.md`
3. `01_PRODUCT_SCOPE.md`
4. `02_TECH_STACK_AND_ARCHITECTURE.md`
5. `03_HTTP_TTS_COMPATIBILITY.md`
6. `04_PLAYBACK_PIPELINE.md`
7. `05_DATA_AND_PERSISTENCE.md`
8. `06_UI_AND_USER_FLOWS.md`
9. `07_SETTINGS_PAGES.md`
10. `08_RUNTIME_AND_LIFECYCLE.md`
11. `09_TESTING_AND_QUALITY.md`
12. `10_ENGINEERING_CONVENTIONS.md`
13. `11_DECISIONS_RISKS_OPEN_QUESTIONS.md`
14. `12_REGEX_REPLACEMENT_PIPELINE.md`
15. `TASK_BACKLOG.md`

数字编号文档描述产品、架构、数据、UI、运行时、安全和质量的稳定终态，不记录某次重构的执行波次或临时过渡方案。所有仍需执行的任务、依赖、风险门和验收状态统一维护在 `TASK_BACKLOG.md`；已完成历史移入 `archives/`。

归档内容可按需查看 `archives/`，但归档不作为最新实现依据。

## UI/UX 设计基线

以下文档是当前项目 UI/UX 设计的基准方案：

- `06_UI_AND_USER_FLOWS.md`
- `07_SETTINGS_PAGES.md`

后续涉及以下内容时，应先参考该方案并与其保持一致：

- 主窗口信息架构、两项一级导航和二级页面层级。
- 书库、播放、设置首页、七个设置二级页、TTS 规则、章节规则、缓存与数据二级页和缓存管理三级页的职责边界。
- 导航栏“正在播放”入口、状态提示和错误反馈模式。
- 歌词式正文、段落进度和手动滚动后的自动居中规则。
- Wpf.Ui Fluent 视觉、主题、响应式布局和可访问性基线。

当前主线的正则替换设计见 `12_REGEX_REPLACEMENT_PIPELINE.md`。快捷键、响应式断点和可访问性验收以 `06_UI_AND_USER_FLOWS.md` 为准。历史设计与已完成任务的追溯材料见 `archives/`，最新约束以本入口和数字编号文档为准。

## 项目核心原则

- 首先完成稳定的“导入小说 → 在线合成 → 连续播放 → 恢复进度”闭环。
- 在线 TTS 使用可导入、可编辑的 HTTP 规则，不绑定单一云服务商。
- 播放调度、TTS 请求、缓存、文本解析和 UI 必须解耦。
- 第一版只支持 TXT，不为了未来功能提前建立复杂框架。
- 优先实现完整段落音频下载后播放，不在第一版实现真正流式音频播放。
- 从第一天开始支持取消、超时、限流、缓存、错误恢复和进度持久化。
- 参考 Legado 的设计思想，但独立实现，不直接复制其 GPL 代码。

## 预期技术栈

- C#
- .NET 10
- WPF
- Wpf.Ui 4.x
- CommunityToolkit.Mvvm
- Microsoft.Data.Sqlite.Core、SQLitePCLRaw.bundle_winsqlite3
- Jint
- NAudio
- xUnit

## MVP 交付标准

用户能够：

1. 导入一本常见编码的 TXT 小说。
2. 查看自动识别的章节。
3. 导入或新建一个 HTTP TTS 规则。
4. 测试规则并试听。
5. 建立全局正则替换规则，并分别作用于展示或语音文本。
6. 选择章节开始在线朗读。
7. 暂停、继续、切换段落和章节。
8. 在下次启动时恢复进度。
9. 使用已经缓存的音频再次播放。
10. 在在线 TTS 或正则规则失败时获得明确且脱敏的错误提示。
