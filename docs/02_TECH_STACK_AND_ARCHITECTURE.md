# 技术栈与总体架构

## 技术栈

| 领域 | 选择 |
|---|---|
| 语言 | C# |
| 运行时 | .NET 10 |
| UI | WPF |
| MVVM | CommunityToolkit.Mvvm |
| 数据库 | SQLite |
| SQLite 驱动 | Microsoft.Data.Sqlite |
| JavaScript | Jint |
| 音频播放 | NAudio |
| HTTP | HttpClient |
| 日志 | Microsoft.Extensions.Logging |
| 测试 | xUnit |
| Mock | NSubstitute 或 Moq，二选一 |
| JSON | System.Text.Json |

## 为什么选择 WPF

- 项目仅面向 Windows。
- 界面规模较小。
- 开发速度和稳定性优先于最新视觉体系。
- 便于与 .NET、SQLite、Jint、NAudio 集成。
- 不需要 WebView 和前后端双技术栈。

## 建议解决方案结构

```text
NovelSpeaker.sln
├─ src/
│  ├─ NovelSpeaker.App/
│  ├─ NovelSpeaker.Application/
│  ├─ NovelSpeaker.Domain/
│  └─ NovelSpeaker.Infrastructure/
└─ tests/
   ├─ NovelSpeaker.UnitTests/
   └─ NovelSpeaker.IntegrationTests/
```

如果早期拆分多个程序集妨碍开发，可以先保留一个应用程序集和一个测试程序集，但目录和依赖方向仍应遵循本文档。

## 依赖方向

```text
App
  ↓
Application
  ↓
Domain

Infrastructure
  ↓
Application + Domain
```

原则：

- `Domain` 不依赖 WPF、SQLite、Jint、NAudio 或网络库。
- `Application` 只依赖领域模型和抽象接口。
- `Infrastructure` 实现数据库、HTTP、脚本、音频和文件系统。
- `App` 负责组合依赖、View、ViewModel 和应用生命周期。

## 核心模块

```text
Novel Import
├─ EncodingDetector
├─ TxtNormalizer
├─ ChapterSplitter
└─ TextSegmenter

TTS Rules
├─ RuleImporter
├─ RuleCompatibilityAnalyzer
├─ RuleNormalizer
├─ TemplateEvaluator
├─ JavaScriptEvaluator
├─ RequestTemplateParser
├─ RequestBuilder
├─ RateLimiter
├─ CookieStore
├─ HttpExecutor
└─ ResponseValidator

Playback
├─ PlaybackCoordinator
├─ PlaybackSession
├─ PlaybackAudioProvider
├─ PrefetchScheduler
├─ ProgressService
├─ AudioCache
└─ AudioPlayer

Persistence
├─ BookRepository
├─ ProgressRepository
├─ TtsRuleRepository
├─ SettingsRepository
└─ CacheRepository
```

## 推荐目录结构

```text
src/NovelSpeaker.App/
├─ App.xaml
├─ Bootstrap/
├─ Views/
├─ ViewModels/
├─ Converters/
└─ Resources/

src/NovelSpeaker.Domain/
├─ Books/
├─ Speech/
├─ Playback/
└─ Common/

src/NovelSpeaker.Application/
├─ Books/
├─ Speech/
├─ Playback/
├─ Settings/
└─ Abstractions/

src/NovelSpeaker.Infrastructure/
├─ Books/
│  ├─ Parsing/
│  └─ FileStorage/
├─ Speech/
│  ├─ Rules/
│  │  ├─ Import/
│  │  ├─ Normalization/
│  │  └─ Compatibility/
│  ├─ Scripting/
│  ├─ Http/
│  └─ Cookies/
├─ Audio/
├─ Persistence/
├─ Security/
└─ Logging/
```

## 核心接口建议

```csharp
public interface IBookImporter
{
    Task<BookImportResult> ImportAsync(
        string filePath,
        BookImportOptions options,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IChapterSplitter
{
    IReadOnlyList<ParsedChapter> Split(string normalizedText);
}
```

```csharp
public interface ITextSegmenter
{
    IReadOnlyList<SpeechSegment> Segment(Chapter chapter);
}
```

```csharp
public interface ITtsRequestCompiler
{
    Task<ParsedTtsRequest> CompileAsync(
        NormalizedHttpTtsRule rule,
        TtsRuleContext context,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IHttpTtsExecutor
{
    Task<TtsAudioResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IAudioCache
{
    Task<AudioCacheEntry?> TryGetAsync(
        AudioCacheKey key,
        CancellationToken cancellationToken);

    Task<AudioCacheEntry> StoreAsync(
        AudioCacheKey key,
        Stream audio,
        AudioMetadata metadata,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IAudioPlayer : IAsyncDisposable
{
    PlaybackState State { get; }

    event EventHandler? PlaybackCompleted;
    event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

    Task LoadAsync(
        string filePath,
        CancellationToken cancellationToken);

    void Play();
    void Pause();
    void Stop();
}
```

说明：

- `HttpTtsRule` 或等价持久化模型负责保存导入 JSON 和用户配置。
- `NormalizedHttpTtsRule` 是运行时使用的内部模型，裁剪到当前版本真正支持的能力。
- 播放链路只依赖规范化模型和 `ParsedTtsRequest`，不直接理解导入 JSON 的边缘语法。

## 播放层核心对象

`PlaybackCoordinator` 是应用层服务，不属于 ViewModel。

它负责：

- 创建和销毁播放会话。
- 接收播放、暂停、停止、跳章和跳段命令。
- 串行化状态变更。
- 协调当前段解析、音频提供、预取调度和进度保存。
- 汇总用户可见状态。

它不负责：

- 直接读写 WPF 控件。
- 解析 TTS 规则文本。
- 直接拼装 HTTP 请求。
- 执行 SQL。
- 直接计算章节正则。
- 保存 API 密钥明文。
- 独自承担所有缓存、预取和恢复细节。

建议协作者边界：

- `PlaybackAudioProvider`：负责“缓存命中或在线生成当前段音频”。
- `PrefetchScheduler`：负责预取窗口、去重和会话取消。
- `ProgressService`：负责保存和恢复阅读进度。
- `IAudioPlayer`：只负责本地音频加载、播放、暂停和停止。

## 依赖注入生命周期

建议：

- 单例：
  - 数据库连接工厂
  - HttpClientFactory
  - CacheService
  - CookieStore
  - PlaybackCoordinator
  - AudioPlayer
- 瞬态或轻量单例：
  - Parser
  - RuleNormalizer
  - RequestCompiler
  - Repository
- 每次播放会话创建：
  - `PlaybackSession`
  - CancellationTokenSource
  - `PrefetchScheduler`

## 不建议引入

- Prism。
- MediatR。
- 完整 DDD 框架。
- 事件溯源。
- CQRS。
- 微服务。
- 自建后端。
- 通用插件框架。

这些会显著增加第一版复杂度，却不改善主链路。
