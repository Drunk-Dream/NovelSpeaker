# AGENTS.md

## 文件定位

本文件是 NovelSpeaker 仓库的开发约束和当前实现基线，供 Codex、Claude Code 等编程 Agent 使用。它描述的是仓库现在实际拥有的能力，不是早期的功能愿望清单。

如果实现、设计文档和本文件不一致，应先确认实际代码和测试，再同步更新相关文档；不要通过扩大本文件描述来掩盖实现差异。涉及产品行为时，以设计文档为准；涉及当前任务状态时，以 `docs/TASK_BACKLOG.md` 和代码/测试为准。

## 项目目标

NovelSpeaker 是面向 Windows 10/11 的桌面小说听书应用，当前产品主线是：

`本地 TXT 导入 → 章节识别 → 文本分段 → 正文/朗读文本处理 → HTTP TTS 完整下载 → 本地播放 → 缓存、预取和进度恢复`

当前项目版本属性为 `0.1.2`，发布形式为自包含 `win-x64` 便携 ZIP。项目使用 GPL-3.0-or-later；发布包同时包含项目许可证和 `THIRD-PARTY-NOTICES.txt`。

## 当前实现基线

### 工程和技术栈

- C#、.NET SDK `10.0.301`，由 `global.json` 固定；桌面项目目标为 `net10.0-windows`。
- WPF + Wpf.Ui 4.x，使用 FluentWindow、NavigationView、主题、Snackbar 等能力。
- `CommunityToolkit.Mvvm` 用于 ViewModel 和命令。
- `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_winsqlite3` 用于 SQLite。
- `Jint` 用于受限的 Legado 风格模板表达式。
- `NAudio` 用于本地音频播放。
- `Microsoft.Extensions.DependencyInjection` 和基础 Logging 用于轻量依赖注入；不引入完整企业级框架。
- xUnit + `Microsoft.NET.Test.Sdk` 用于自动测试。
- 包版本集中在 `Directory.Packages.props`；各项目保留 `packages.lock.json`。
- `Directory.Build.props` 开启 Nullable、Implicit Usings、最新分析器、Warnings as Errors、预览语言版本和锁定还原。

### 项目结构

```text
src/NovelSpeaker.Domain          领域模型和纯数据类型
src/NovelSpeaker.Application     用例边界、公共接口和应用模型
src/NovelSpeaker.Infrastructure  SQLite、文件、HTTP、Jint、缓存、播放等实现
src/NovelSpeaker.App             WPF 壳层、页面、ViewModel、导航、主题和输入
tests/NovelSpeaker.UnitTests     单元、集成、ViewModel 和 WPF 页面测试
docs/                            产品、架构、UI、测试、决策和任务文档
```

依赖方向应保持为 `App → Infrastructure/Application → Domain`。Infrastructure 实现 Application 的接口；Domain 不依赖 WPF、HTTP、SQLite 或具体基础设施。

### 已实现的产品链路

- TXT 直接导入：BOM/UTF-8 检测，常见中文编码回退，低置信度时显示最小编码选择对话框；导入过程可取消。
- 导入时规范化文本、计算 SHA-256、检测重复书籍、复制源文件到应用数据目录，并写入 SQLite 书籍和章节数据。
- 支持内置章节规则、章节规则管理、无章节回退，以及自然段、中文/英文标点和超长文本分段；运行时段保留原始字符偏移。
- 全局正则替换已接入动态分段之后的展示和朗读链路，支持 Display、Speech、Both 三种作用范围；使用 `CultureInvariant` 和每条规则每段 `100 ms` 超时。规则启用、排序和编辑保存均已与编辑副本解耦。
- TTS 规则支持 JSON 对象/数组导入、新建、编辑、删除、启用、禁用、请求预览、测试和试听；包含 Legado 风格规则转换和脱敏展示。
- HTTP TTS 支持 GET、POST JSON、POST Form、自定义 Header、Body 模板、`speakText`、`speakSpeed`、LoginInfo、会话内 Cookie 和受限模板表达式；检查状态码、Content-Type、音频文件头和 NAudio 可解码性。
- 规则请求支持超时、取消、有限重试、规则级限流以及 401、429、5xx、网络、空响应、文本/JSON 错误和损坏音频的错误分类。
- 播放链路由协调器负责会话、自动跨段/跨章、暂停/继续/停止、跳转、旧会话隔离、播放进度和错误状态；播放器只负责本地音频设备。
- 在线音频先完整下载到临时文件，验证成功后进入本地缓存，再由 NAudio 播放；支持缓存命中、原子写入、损坏缓存重建、后台维护、默认后续两个段落预取和取消旧预取。
- 音频缓存使用位置相关键：`bookId + chapterIndex + segmentIndex + ruleId + speakSpeed + finalSpeechText`。不要擅自改成跨位置内容复用或规则配置哈希键。
- SQLite 迁移和仓储覆盖书籍、章节规则、正则规则、TTS 规则、阅读进度和音频缓存索引；JSON 保存非敏感应用设置。
- UI 已有书库、书籍详情、播放、设置首页、播放设置、TTS 规则、导入与文本、章节规则、正则替换、缓存与数据、缓存管理、外观、诊断与关于页面。
- 已实现主题切换、Windows 11 Mica 降级、全局快捷键、关键页面可访问性基础、虚拟列表、滚动动画、Snackbar、滚动文件日志和复制脱敏诊断摘要。
- `.github/workflows/ci.yml` 执行锁定还原、Release 构建、测试和格式检查；`.github/workflows/release.yml` 执行版本校验、自包含发布、ZIP/SHA256 生成和 GitHub Release 创建。

### 当前测试基线

测试项目使用本地可控 HTTP 测试服务器和脱敏规则样本，不依赖真实第三方 TTS 服务。测试覆盖范围包括：

- TXT 编码分析、规范化、章节识别、文本分段、文件名元数据、哈希去重和导入事务。
- 章节规则、正则替换仓储/工作区/管线、超时/异常隔离、空结果和原始偏移映射。
- TTS 规则转换、导入、仓储、模板编译、受限 Jint、GET/POST 请求、Cookie、错误分类和限流。
- SQLite 迁移、书籍/进度/缓存仓储，缓存键、原子写入、损坏音频恢复、LRU 清理和预取取消。
- 播放器、播放协调器、会话隔离、自动滚动和播放 ViewModel。
- 设置自动保存、主题、导航、页面生命周期、关键 WPF 视觉树和可访问性行为。
- 日志轮转、异常处理、全链路敏感信息脱敏、诊断摘要和依赖注入注册。

## UI/UX 约束

凡是涉及页面结构、导航、视觉方向、状态反馈、滚动、快捷键、可访问性或交互模式，必须先阅读并遵循：

- `docs/06_UI_AND_USER_FLOWS.md`
- `docs/07_SETTINGS_PAGES.md`
- 正则替换相关改动还必须阅读 `docs/12_REGEX_REPLACEMENT_PIPELINE.md`

当前 UI 信息架构为：一级导航只有“书库”和“设置”；播放页、书籍详情和设置页是上下文/二级页；设置下有播放设置、TTS 规则、导入与文本、章节规则、缓存与数据、外观、诊断与关于；正则替换和缓存管理是三级页。

遵守以下已确定交互：

- 不恢复底部全局播放器条；播放继续时通过导航栏“正在播放”入口返回播放页。
- 书库使用响应式横向书籍卡片网格；书籍详情不使用全局纵向滚动，章节目录保持虚拟化。
- 书库、TTS 规则、章节规则和正则替换不以 DataGrid 作为主要交互；复杂管理页使用左侧列表 + 右侧编辑/管理区。
- 设置首页只提供分组入口，不直接编辑具体设置，也没有统一保存按钮；设置项按页面自动保存或字段级保存。
- 播放中跳转、规则/语速变化和章节变化必须取消并隔离旧会话；暂停状态下的跳转不能意外开始播放。
- 播放正文自动居中不能抢夺用户手动滚动；新动画必须取消旧动画，系统减少动画时直接定位。
- 成功操作优先使用 Snackbar/页内状态，危险操作和未保存修改才使用确认对话框；状态不能只靠颜色表达。

若新的 UI 需求与设计文档冲突，先更新设计文档或明确记录偏离原因，再改代码；不要绕过设计文档建立并行交互。

## 架构边界和安全规则

- 业务逻辑不得写入 code-behind；code-behind 只处理 WPF 生命周期、视图连接和不可移出的 UI 适配。
- ViewModel 不得直接发起 HTTP 请求、创建 `HttpClient`、操作 SQLite 或访问音频设备；通过 Application 服务接口完成用例。
- TTS 规则解析/编译/执行不得直接控制播放器；播放器不得负责文本切分、正则替换或规则解析。
- 播放调度、音频提供、缓存、文本处理和规则引擎保持独立职责；新增公共接口时在接口注释或任务说明中写清职责边界。
- 所有异步 I/O、文件操作、SQLite 操作、HTTP 请求和可等待的业务流程都必须接受并传递 `CancellationToken`；不要用同步阻塞等待替代异步流程。
- 规则脚本是不可信输入。Jint 必须保持白名单边界，禁止 CLR、文件系统、进程、反射、任意网络和任意宿主对象访问。当前模板求值有超时、语句数、递归深度和输出长度限制；修改限制时必须增加回归测试。
- 不直接复制 Legado 源代码，只参考公开行为和数据格式，独立实现兼容层。
- 不把 API 密钥、Cookie、LoginInfo、Token、Header、Body、规则原文、错误正文或小说正文写入普通日志、异常摘要、Snackbar、请求预览或诊断摘要；使用统一脱敏器。
- 当前没有 SecretStore。TTS 规则中的敏感结构化字段保存在本地 SQLite，尚未进行静态加密；不能把项目描述成已经提供凭据加密。
- 不在测试、日志或提交中使用真实凭据和真实私人小说正文；测试使用 `tests/NovelSpeaker.UnitTests/TestAssets` 中的脱敏样本。
- 修复缺陷时先增加能复现问题的测试；解析器、缓存键、限流器、播放状态机、迁移和安全边界的行为改变必须同步更新测试。
- 避免一次性重构无关模块；保留现有用户改动，优先形成可运行的纵向切片。

## 当前明确不做和已知限制

第一版及当前主线不实现：

- EPUB、PDF、MOBI 和其他电子书格式。
- 在线书源、网络小说抓取、用户账户、云同步、后端服务器和跨平台。
- WebSocket TTS、真正的边生成边播放、有声书批量导出和多角色自动配音/语音克隆。
- Windows 本地 TTS 回退、WebView 登录、Cookie 持久化、`jsLib` 和复杂 `source.get/put` 可变状态语义。
- SecretStore、规则敏感值静态加密、插件市场和自动更新。
- 单本书专属 TTS 规则/语速、托盘图标、定时停止、媒体键、输出设备选择等第二阶段功能。

当前发布风险必须保持可见：不保证兼容所有社区 Legado 规则；规则敏感值未静态加密；发布包未代码签名，Windows SmartScreen 可能提示。修改这些限制的描述时要同步 `README.md`、`docs/01_PRODUCT_SCOPE.md` 和 `docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`。

## 文档阅读和修改规则

开始编码前，根据任务至少阅读：

1. `docs/README.md` 和本文件。
2. 对应领域设计文档：书籍/解析读 `00`、`01`、`05`；播放读 `04`；TTS 读 `03`；UI 读 `06`、`07`；正则读 `12`。
3. `docs/09_TESTING_AND_QUALITY.md` 和 `docs/10_ENGINEERING_CONVENTIONS.md`。
4. `docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`、`docs/TASK_BACKLOG.md`，确认没有违反既有决策。
5. 相关现有测试，尤其是要修改的服务、ViewModel、页面和仓储测试。

完成行为变化后，必要时同步 README、设计文档、测试策略、决策记录和 backlog。已完成的大量历史任务保存在 `docs/archives/`，归档不是新的实现依据。

如果环境支持探索型子代理，代码库探索优先使用小模型和只读探索；否则使用 `rg`、`rg --files` 和小范围文件读取，不要无目的加载整个 `obj` 或生成物目录。

## 日常验证

当前环境的 .NET 可执行文件路径为：

```text
/mnt/c/Program Files/dotnet//dotnet.exe
```

若 `dotnet` 不在 `PATH`，使用上述完整路径。Codex 沙箱中调用该 Windows .NET SDK 可能需要提权；遇到权限阻止时申请提权，不要用普通还原绕过锁文件约束。

仓库有意为 `win-x64` 保留锁定目标，部分锁定目标可以为空。普通 `dotnet restore`，以及会隐式进行无 RID 还原的 `dotnet build`/`dotnet test`，可能删除这些目标并制造无关的 `packages.lock.json` 差异。日常质量门禁必须严格按以下顺序执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

源码或文档开发期间，可在 Windows 环境手动启动：

```powershell
dotnet run --project src/NovelSpeaker.App --no-restore
```

只有在项目版本或依赖确实改变、锁定模式要求更新时，才执行：

```powershell
dotnet restore -r win-x64 --force-evaluate
```

执行后必须审查所有 `packages.lock.json` 差异，确认依赖变化符合预期且 `win-x64` 目标仍存在，然后重新执行 `dotnet restore --locked-mode -r win-x64` 验证。若某项工具未配置或当前环境无法运行，必须在交付说明中明确指出，不得伪造通过。

## 版本发布

当用户明确要求“更新版本号并发布”时，必须依次完成：

1. 更新 `Directory.Build.props` 中的包、程序集、文件和信息版本，并检查发布工作流使用的版本参数。
2. 在发布前完成锁定还原、格式检查、Release 构建和测试。
3. 提交代码和版本变更。
4. 在发布提交上创建符合 `vX.Y.Z` 的 Git 标签。
5. 推送目标分支和标签；标签必须指向 `main` 可达的提交，以触发 `.github/workflows/release.yml`。
6. 等待工作流成功，确认 GitHub Release、`NovelSpeaker-vX.Y.Z-win-x64.zip` 和对应 `.sha256` 资产可用。
7. 发布成功后更新 GitHub Release 说明，面向用户结构化概括主要变更、缺陷修复、验证和下载信息，再次核对说明与资产；在此之前不得宣告发布完成。

未得到用户明确授权时，不要自行提交、打标签、推送、创建 Release 或修改远端内容。

## 交付说明

完成开发任务后，简要说明：

- 修改了什么，以及为什么这样设计。
- 添加或更新了哪些测试；如果只是文档修改，明确写“未修改测试代码”。
- 如何手动验证和自动验证结果。
- 尚未解决的风险、环境限制或未执行的检查。
