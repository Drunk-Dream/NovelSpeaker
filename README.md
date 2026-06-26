# NovelSpeaker

一款面向 Windows 10/11 的轻量小说听书应用。

它的目标不是做一个完整电子书阅读器，而是把用户导入的本地 TXT 小说，稳定地转换成可连续播放、可暂停恢复、可缓存的在线语音体验。

## 项目亮点

- 本地 TXT 小说导入与自动章节识别。
- 章节内容按自然段和中文标点切分为朗读段落。
- 兼容 Legado 风格的 HTTP 在线 TTS 规则。
- 支持 GET、POST JSON、POST Form、Header、Body 和模板变量。
- 使用受限 JavaScript 执行规则表达式，避免开放任意 CLR 访问。
- 在线语音完整下载后再播放，并支持后续段落预取。
- 本地音频缓存与播放进度持久化。
- 简洁的 WPF 界面，专注于“听书”而不是“阅读器”。

## 当前状态

项目已经打通核心主流程，适合继续围绕“导入 TXT -> 选择规则 -> 在线合成 -> 连续播放 -> 恢复进度”迭代。

已覆盖的主要能力包括：

- TXT 导入、编码识别、重复检测和原文件复制。
- 章节识别、章节规则管理与文本分段。
- HTTP TTS 规则导入、导出、测试与兼容性分析。
- Jint 规则求值与安全限制。
- NAudio 本地播放、暂停、停止与完成事件。
- SQLite 持久化、阅读进度和用户设置保存。
- 音频缓存、LRU 清理和缓存统计。
- 基础 WPF 书库页、播放页、规则页和设置页。

## 技术栈

- C#
- .NET 10
- WPF
- CommunityToolkit.Mvvm
- Microsoft.Data.Sqlite
- Jint
- NAudio
- xUnit

## 设计原则

- 播放优先，而不是阅读器优先。
- 业务逻辑不写在 code-behind 中。
- ViewModel 不直接访问 HTTP 或 SQLite。
- TTS 规则引擎不直接控制播放器。
- 播放器不负责文本切分或规则解析。
- 所有异步 I/O 都支持 `CancellationToken`。

## 界面结构

应用采用单窗口页面式导航，主界面分为四个一级页面：

- 书库
- 播放
- 规则
- 设置

底部保留全局播放器条，确保用户在任何页面都能持续看到播放状态和核心控制。

详细的 UI 方案见：

- [docs/06_UI_AND_USER_FLOWS.md](docs/06_UI_AND_USER_FLOWS.md)

## 支持范围

第一版聚焦这些场景：

- 导入本地 TXT 小说。
- 自动识别章节和朗读段落。
- 导入并编辑 HTTP TTS 规则。
- 在线生成完整音频后播放。
- 缓存音频并恢复上次进度。

明确不做的内容包括：

- EPUB、PDF、MOBI。
- 在线书源和网络小说抓取。
- 用户账户、云同步和后端服务器。
- WebSocket TTS 和真正的边生成边播放。
- 插件市场和自动更新。

## 仓库结构

```text
src/
├─ NovelSpeaker.App/            WPF 应用入口、视图和 ViewModel
├─ NovelSpeaker.Application/    应用层接口与用例
├─ NovelSpeaker.Domain/         领域模型
└─ NovelSpeaker.Infrastructure/ 基础设施实现

tests/
└─ NovelSpeaker.UnitTests/      单元测试
```

## 本地运行

### 环境要求

- Windows 10 22H2 或更高版本，或 Windows 11
- x64
- .NET 10 SDK

### 启动

```bash
dotnet build
dotnet test
dotnet run --project src/NovelSpeaker.App
```

如果你的环境没有把 `dotnet` 加入 `PATH`，请直接使用安装路径：

```powershell
&"C:\Program Files\dotnet\dotnet.exe" build
&"C:\Program Files\dotnet\dotnet.exe" test
&"C:\Program Files\dotnet\dotnet.exe" run --project src/NovelSpeaker.App
```

## 测试

仓库内已有针对以下模块的测试：

- 小说导入与章节解析
- 规则导入、转换与测试
- Jint 模板求值
- HTTP TTS 请求编译与执行
- 本地音频播放
- 缓存与进度持久化
- 依赖注入装配

## 开发文档

项目的详细上下文和设计决策保存在 `docs/` 目录中，建议按下面顺序阅读：

1. [docs/00_PROJECT_BRIEF.md](docs/00_PROJECT_BRIEF.md)
2. [docs/01_PRODUCT_SCOPE.md](docs/01_PRODUCT_SCOPE.md)
3. [docs/02_TECH_STACK_AND_ARCHITECTURE.md](docs/02_TECH_STACK_AND_ARCHITECTURE.md)
4. [docs/03_HTTP_TTS_COMPATIBILITY.md](docs/03_HTTP_TTS_COMPATIBILITY.md)
5. [docs/04_PLAYBACK_PIPELINE.md](docs/04_PLAYBACK_PIPELINE.md)
6. [docs/05_DATA_AND_PERSISTENCE.md](docs/05_DATA_AND_PERSISTENCE.md)
7. [docs/06_UI_AND_USER_FLOWS.md](docs/06_UI_AND_USER_FLOWS.md)
8. [docs/07_DEVELOPMENT_MILESTONES.md](docs/07_DEVELOPMENT_MILESTONES.md)
9. [docs/08_TESTING_AND_QUALITY.md](docs/08_TESTING_AND_QUALITY.md)
10. [docs/09_ENGINEERING_CONVENTIONS.md](docs/09_ENGINEERING_CONVENTIONS.md)
11. [docs/10_DECISIONS_RISKS_OPEN_QUESTIONS.md](docs/10_DECISIONS_RISKS_OPEN_QUESTIONS.md)
12. [docs/11_TASK_BACKLOG.md](docs/11_TASK_BACKLOG.md)

## 计划中的后续工作

- 完善缓存优先的播放链路。
- 继续增强规则编辑和测试体验。
- 优化播放页和书库页的状态反馈。
- 补充更完整的截图、演示和发布说明。

## 说明

这是一个持续迭代中的桌面应用项目。README 会随着功能完善继续更新，以保持和实际代码状态一致。
