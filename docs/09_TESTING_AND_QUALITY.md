# 测试与质量策略

## 1. 目标

测试体系用于保护用户行为、数据兼容、安全边界和架构依赖。架构重组期间，测试必须先固定行为，再允许移动类型或拆分实现；不得把“项目能编译”当作重构验收。

## 2. 测试项目分层

终态测试项目：

```text
tests/
├─ NovelSpeaker.Domain.UnitTests
├─ NovelSpeaker.Application.UnitTests
├─ NovelSpeaker.Infrastructure.IntegrationTests
├─ NovelSpeaker.App.PresentationTests
└─ NovelSpeaker.App.WpfTests
```

| 项目 | 目标框架 | 允许引用 | 主要内容 |
|---|---|---|---|
| Domain.UnitTests | `net10.0` | Domain | 领域不变量、纯算法、值对象 |
| Application.UnitTests | `net10.0` | Application、Domain | 用例、状态机、策略、投影、端口 fake |
| Infrastructure.IntegrationTests | `net10.0`，仅确需 Windows 时单独标记 | Infrastructure、Application、Domain | SQLite、文件、HTTP、本地服务器、Jint、缓存和音频解码 |
| App.PresentationTests | `net10.0-windows` | App、Application | ViewModel、路由、反馈、页面控制器，不创建视觉树 |
| App.WpfTests | `net10.0-windows` | App | STA 视觉树、主题、焦点、虚拟化、滚动和可访问性 |

约束：

- 纯单元测试不引用 App，不启动 WPF Dispatcher。
- WPF 项目可以串行；其它项目默认允许并行。
- SQLite、真实文件、本地 HTTP 和 NAudio 解码测试不命名为 UnitTests。
- 测试项目依赖方向与产品项目一致，不通过引用完整 App 获得所有实现。

拆分测试项目属于架构迁移任务；完成前现有单项目仍需保持全量通过。

## 3. 风险优先级

最高优先级：

- 播放状态机、会话取消、旧结果隔离和暂停跳转语义。
- SQLite 迁移、跨数据库/文件恢复和路径根目录约束。
- 缓存键、原子写入、保护、损坏恢复和 LRU。
- Jint 白名单、执行限制和敏感信息脱敏。
- TTS 请求编译、错误分类、限流和响应验证。
- 章节解析、动态分段、正则超时、空结果和原始偏移映射。
- 页面激活、未保存保护、自动保存竞态和滚动竞态。

较低风险的简单 DTO、无分支映射和框架样板不追求逐行覆盖。

## 4. 特征测试与重构测试

移动或拆分以下对象前必须先建立特征测试：

- `PlaybackCoordinator` 和本地音频协调器。
- 书籍导入、删除和缓存清理。
- TTS 规则导入/编辑/选择。
- 章节规则与正则规则工作区。
- 设置规范化与自动保存。
- Shell 导航、页面激活和未保存保护。

特征测试保护外部可观察行为，不照抄私有实现。每个迁移批次遵循：

```text
补充目标行为测试
  → 新增目标实现
  → 迁移调用者和对应测试
  → 删除旧实现/临时适配
  → 运行切片测试和全门禁
```

禁止长期保留 `New`、`V2`、`Refactored`、`Compat` 并行实现。

## 5. 领域与应用测试

### 5.1 Books/Text

覆盖：

- BOM、严格 UTF-8、GB18030/UTF-16 回退与低置信度选择。
- 规范化、SHA-256 去重、文件名元数据。
- 常见章节标题、正文伪标题、无章节和超大文本。
- 中英文标点、引号、省略号、空段和超长段落。
- Display/Speech/Both 正则顺序、`CultureInvariant`、`100 ms` 超时与空结果。
- 分段/规则变化后的原始字符偏移恢复。

### 5.2 Speech

覆盖：

- NovelSpeaker/Legado 来源模型转换和不支持字段。
- GET、POST JSON、POST Form、Header、Body 和模板表达式。
- 当前明确不支持的 Cookie/LoginInfo、`jsLib`、复杂 `source.get/put` 被拒绝或不执行。
- 401/403、429/Retry-After、5xx、超时、空响应、文本/JSON 错误和损坏音频。
- 用户错误消息和日志对 URL、Header、Token、正文及异常消息脱敏。

自动测试使用脱敏规则样本，不访问真实第三方 TTS 服务。

### 5.3 Playback

使用可控端口 fake 覆盖：

1. 缓存命中和未命中。
2. 自动跨段、跨章和全书结束。
3. 暂停、继续、停止和段内恢复。
4. 快速切章/切段、旧请求晚到和重复完成事件。
5. 规则、语速和正则语音变化创建新会话。
6. 仅展示文本变化时当前音频不中断。
7. 预取窗口、优先级、去重和取消。
8. 缓存损坏恢复与连续失败暂停。
9. 已加载空章节与未加载章节明确区分。
10. 事件处理协作者抛错时不会形成未观察异常。

## 6. Infrastructure 集成测试

### 6.1 SQLite

- 新库迁移到当前版本。
- 已支持旧版本升级。
- 低于最低版本和高于当前版本均安全拒绝。
- migration 失败回滚且版本号不前移。
- 每连接启用 foreign keys，约束与级联生效。
- busy timeout、并发写和取消行为。
- 仓储字段级更新、稳定排序和非法历史数据隔离。

### 6.2 文件与跨资源恢复

- 导入/删除在每个故障点中断后的幂等恢复。
- 设置临时写、flush、原子替换、损坏文件恢复。
- 缓存临时文件、原子切换、索引/文件不一致和孤儿清理。
- 恶意绝对路径、`..`、符号链接/reparse point 不得触碰应用根目录外文件。
- 永不修改用户外部源 TXT。

### 6.3 HTTP 测试服务器

本地服务器至少提供：

```text
GET  /audio
POST /audio-json
POST /audio-form
GET  /error-json
GET  /error-text
GET  /unauthorized
GET  /rate-limited
GET  /server-error
GET  /slow
GET  /empty
GET  /corrupt-audio
```

只有当 Cookie 能力正式进入产品范围并实现后，Cookie endpoint 才成为成功路径测试；当前可保留为不支持兼容样本，不能在文档中据此宣称功能已实现。

## 7. Presentation 与 WPF 测试

### 7.1 Presentation

- ViewModel 只输出语义状态，不暴露 WPF/Wpf.Ui 类型。
- App 路由参数和导航策略不依赖具体 Page 类型。
- 所有规则工作台的选择、编辑副本、保存/放弃/取消行为。
- 自动保存串行化和旧请求不得覆盖新值。
- 播放 Snapshot 到页面状态的投影。
- 错误分类到用户安全文案的投影。

### 7.2 WPF

- 一级导航、上下文页和返回路径。
- 页面 activation 创建/取消和事件注销。
- 未保存保护覆盖返回按钮、一级导航、快捷键、正在播放入口和窗口关闭。
- 虚拟列表、焦点顺序、Automation Name 和状态不只依赖颜色。
- 手动滚动 4 秒恢复、旧动画取消、减少动画和虚拟化目标延迟生成。
- 浅色、深色、系统主题和 Windows 10 外观降级。

WPF 测试共享一个明确的 STA Host 和视觉树 helper。测试文件不得各自复制递归视觉树遍历。

## 8. 测试组织和可维护性

- 测试目录与生产 feature 对齐。
- 一个巨型测试类按行为拆分，例如 Playback 的 Commands、Navigation、Recovery、RegexRefresh、Projection。
- 共享 fake 按端口命名并提供最少可配置行为；禁止建立包揽所有依赖的万能 Fake。
- fixture、迁移、损坏音频和脱敏规则样本是受保护资产，不因“无生产引用”删除。
- 测试音频只位于 tests/TestAssets，不复制到正式应用输出和发布包。
- 每个缺陷先增加能失败的回归测试，再修改实现。

## 9. 时间、并发与稳定性

- 防抖、限流、重试、自动居中恢复使用 `TimeProvider` 或手动调度器。
- 测试等待明确事件、TaskCompletionSource 或状态版本，不使用任意 `Task.Delay`/`Thread.Sleep` 猜测完成。
- 测试必须有超时保护，失败时输出当前安全状态。
- 纯测试允许并行；只在共享 SQLite 文件、音频设备或 WPF Dispatcher 的最小集合内串行。
- 临时目录和服务器端口由 fixture 隔离并在失败后清理。

## 10. 架构测试

自动检查：

- Domain 无项目依赖和外部技术包。
- Application 不引用 SQLite、Jint、NAudio、WPF、Wpf.Ui 或 Infrastructure。
- App 的 Features/Shared 不引用 Infrastructure，只有 Bootstrap 可引用。
- Infrastructure 不引用 App/WPF，也不新增页面工作区或业务协调器。
- 非 Infrastructure 文件不出现 SQL/`SqliteConnection`。
- ViewModel 公共 API 不出现 Page、Window、Dispatcher、`FontWeight` 或 Wpf.Ui 图标类型。
- 测试项目引用关系符合第 2 节。

## 11. 性能和长稳验证

- 大 TXT 导入、数千章节和超长章节动态分段。
- 大量正则规则以及灾难性回溯隔离。
- 连续播放至少三十分钟，多次切章/规则/语速。
- 大缓存目录的启动维护、统计和 LRU。
- 快速导航、重复命令和网络频繁断开/恢复。
- 滚动日志轮转与并发写。

性能测试记录数据规模、环境和阈值，不只记录“感觉流畅”。

## 12. 质量门禁

必须严格按以下顺序执行，避免无 RID 隐式还原破坏锁文件目标：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

CI 和本地日常验证使用同一顺序。只有依赖或版本确实变化时才允许 `--force-evaluate`，并必须审查全部 `packages.lock.json`。

发布还需验证：

- 自包含 `win-x64` 便携 ZIP 可在干净 Windows 10/11 环境运行。
- ZIP 包含 `LICENSE` 和 `THIRD-PARTY-NOTICES.txt`，不包含测试音频、测试配置或私人数据。
- ZIP 与 `.sha256` 匹配。
- 日志、错误、预览和诊断摘要不包含正文或凭据。

## 13. 架构迁移完成标准

- 五类测试项目职责清晰，纯测试不再因 WPF 全局禁用并行。
- 超大测试文件按行为拆分，重复 fake/视觉树 helper 已收敛。
- 新架构依赖由自动测试保护。
- 播放、迁移、安全、缓存和页面生命周期的关键特征测试完整。
- 全量质量门禁与发布包检查通过。
