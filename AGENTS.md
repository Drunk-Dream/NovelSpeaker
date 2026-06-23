# AGENTS.md

## 项目目标

实现一个 Windows 10/11 桌面小说听书应用。

第一版核心范围：

- 本地 TXT 小说导入。
- 自动章节识别和文本分段。
- 兼容 Legado 风格的 HTTP 在线 TTS 规则。
- 在线语音完整下载后播放。
- 后续段落预取。
- 本地音频缓存。
- 播放进度持久化。
- 简洁的 WPF 用户界面。

## 强制技术约束

- 使用 C#、.NET 10 和 WPF。
- 使用 CommunityToolkit.Mvvm。
- 使用 Microsoft.Data.Sqlite。
- 使用 Jint 执行受限 JavaScript。
- 使用 NAudio 播放本地音频文件或流。
- 使用依赖注入，但不要引入完整企业级框架。
- 异步 I/O 必须支持 `CancellationToken`。
- 业务逻辑不得写在 code-behind 中。
- ViewModel 不得直接发起 HTTP 请求或操作 SQLite。
- TTS 规则引擎不得直接控制播放器。
- 播放器不得负责文本切分或规则解析。

## 第一版明确不做

- EPUB、PDF、MOBI。
- 在线书源和网络小说抓取。
- 用户账户、云同步和后端服务器。
- 多角色自动配音。
- 语音克隆。
- 有声书导出。
- WebSocket TTS。
- 真正的边生成边播放。
- 完整复刻 Legado 所有 JavaScript API。
- 插件市场。
- 自动更新。

## 实现策略

每次改动应尽量形成可运行的纵向切片。

推荐顺序：

1. 硬编码文本通过假 TTS 生成本地音频并播放。
2. TXT 导入和章节解析。
3. HTTP GET TTS 规则。
4. HTTP POST、Header、Body 和模板变量。
5. 缓存与预取。
6. JavaScript 表达式。
7. 规则导入、编辑和测试。
8. 进度恢复。
9. 错误恢复和缓存管理。
10. 界面整理和打包。

## 代码修改要求

- 修改前先阅读相关文档和现有测试。
- 涉及 UI/UX 设计、页面结构、导航层级、视觉方向、状态反馈或交互模式的改动时，必须先参考 `docs/06_UI_AND_USER_FLOWS.md`，并与其保持一致。
- 若新的 UI/UX 需求与 `docs/06_UI_AND_USER_FLOWS.md` 冲突，应先更新该方案或明确说明偏离原因，再进行实现；不要绕过该方案直接各自设计。
- 不要一次性重构无关模块。
- 新增公共接口时必须说明其职责边界。
- 对解析器、缓存键、限流器和状态机添加单元测试。
- 修复缺陷时先添加能够复现问题的测试。
- 不要将 API 密钥、登录信息、小说正文写入日志。
- 不要把导入规则中的 JavaScript 作为可信代码。
- 不开放 Jint 对任意 CLR 类型、文件系统、进程或反射的访问。
- 不直接复制 Legado 源代码；只参考行为和数据格式。

## 完成任务后的最低检查

当前环境的 `dotnet` 可执行文件路径为 `"/mnt/c/Program Files/dotnet//dotnet.exe"`。

若 shell 中未将 `dotnet` 加入 `PATH`，执行下列检查时应显式使用该路径。

```powershell
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

若仓库尚未配置其中某项，应在任务说明中明确指出，而不是伪造成功结果。

## 交付说明格式

完成一个开发任务后，应简要说明：

- 修改了什么。
- 为什么这样设计。
- 添加或更新了哪些测试。
- 如何手动验证。
- 是否存在尚未解决的风险。
