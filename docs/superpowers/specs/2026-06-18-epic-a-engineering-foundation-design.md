# Epic A 工程基础设计

## 背景

`docs/11_TASK_BACKLOG.md` 中的 Epic A 目标是为后续导入、TTS、播放、缓存和进度恢复建立稳定工程底座。

当前仓库已有：

- .NET 10 WPF 应用骨架。
- 基础项目文档。
- 单一 `NovelSpeaker.App` 项目。

当前仓库尚缺：

- 分层项目结构。
- 测试项目。
- 依赖注入与日志组合根。
- 应用数据目录服务。
- SQLite 迁移基础设施。
- CI 构建与测试流程。

本设计只覆盖 Epic A 缺口，不提前实现小说导入、章节解析、HTTP TTS、播放状态机或缓存业务。

## 目标

本次实现完成后，仓库应满足以下状态：

- 解决方案包含 `App`、`Domain`、`Application`、`Infrastructure`、`UnitTests`。
- WPF 应用通过 `ServiceCollection` 启动核心服务与窗口，而不是在 code-behind 中承载业务逻辑。
- `MainWindow` 绑定最小 ViewModel，证明 `CommunityToolkit.Mvvm` 已接通。
- 应用具备清晰的应用数据目录抽象，负责 `%LocalAppData%\\NovelSpeaker` 及其子目录边界。
- 应用启动早期可以创建 SQLite 数据库并运行显式 schema 迁移。
- 仓库具备统一的 nullable、分析器和包版本配置。
- 仓库具备最小 GitHub Actions Windows CI，可执行 restore、build、test。
- 自动化测试覆盖应用数据目录与迁移基础设施的基础行为。

## 非目标

本次实现明确不做：

- `Books`、`Chapters`、`ReadingProgress`、`HttpTtsRules` 等业务表。
- 小说导入、章节解析、文本分段。
- HTTP 请求执行、Jint、NAudio 集成。
- 规则管理、播放控制或 UI 产品化。
- ORM、复杂迁移框架或完整宿主框架。

## 方案选型

评估了三种路线：

### 方案 A：最小骨架补齐

新增分层项目、最小 ViewModel、DI、日志、应用数据目录、SQLite 迁移执行器、测试项目和 Windows CI。

优点：

- 与 Epic A 边界严格一致。
- 后续纵向切片可直接复用。
- 风险和改动面都可控。

缺点：

- 迁移链路初期只有基础表，后续仍要继续补业务迁移。

### 方案 B：一步到位基础设施

在工程骨架之外，同时加入首批业务表、设置存储和更多启动服务。

优点：

- 后续阶段表面上少补一些基础设施。

缺点：

- 会提前固化导入、进度和规则存储决策。
- 超出 Epic A 范围，增加返工风险。

### 方案 C：超轻骨架

只拆项目、加测试和 CI，把 DI、日志和迁移留为占位。

优点：

- 初始改动最少。

缺点：

- 后续每个 Epic 都会再次触碰启动与基础设施。
- 不能形成真正可复用的工程底座。

### 结论

采用方案 A。它能以最小代价补齐当前仓库缺口，同时不把后续业务设计提前写死。

## 解决方案结构

实现后解决方案结构调整为：

```text
NovelSpeaker.slnx
Directory.Build.props
Directory.Packages.props
src/
  NovelSpeaker.App/
  NovelSpeaker.Domain/
  NovelSpeaker.Application/
  NovelSpeaker.Infrastructure/
tests/
  NovelSpeaker.UnitTests/
```

## 职责边界

### NovelSpeaker.Domain

职责：

- 放置纯领域基础类型或共享值对象。
- 不依赖 WPF、SQLite、HTTP 或日志实现。

Epic A 中保持极轻量，只保留后续可复用的基础代码占位，避免空壳项目没有实际价值。

### NovelSpeaker.Application

职责：

- 定义应用层抽象接口。
- 约束基础设施与 UI 的交互边界。

Epic A 中重点承载：

- 应用数据目录抽象。
- 数据库初始化抽象。
- SQLite 连接工厂抽象。

该项目不直接依赖 WPF 或 `Microsoft.Data.Sqlite`。

### NovelSpeaker.Infrastructure

职责：

- 实现 `Application` 中声明的接口。
- 承担文件系统、SQLite 和启动期基础设施代码。
- 提供依赖注入注册扩展。

Epic A 中计划包含：

- `LocalAppDataDirectoryProvider`
- `SqliteConnectionFactory`
- `SqliteMigrationRunner`
- `StartupDatabaseInitializer`
- 服务注册扩展

### NovelSpeaker.App

职责：

- 作为组合根。
- 负责 WPF 应用生命周期、View、ViewModel 和服务组装。

启动流程：

1. 创建 `ServiceCollection`。
2. 配置日志。
3. 注册应用层与基础设施服务。
4. 执行数据库初始化。
5. 解析 `MainWindow` 与 `MainWindowViewModel`。
6. 将 ViewModel 绑定到主窗口。

`MainWindow.xaml.cs` 只保留视图初始化，不放业务逻辑。

### NovelSpeaker.UnitTests

职责：

- 覆盖 Epic A 中最易回归的基础设施行为。
- 为后续分层演进建立测试入口。

## 核心接口

建议先引入以下接口：

```csharp
public interface IAppDataDirectoryProvider
{
    string RootDirectoryPath { get; }
    string DatabasePath { get; }
    string LogsDirectoryPath { get; }
    string BooksDirectoryPath { get; }
    string CacheDirectoryPath { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken);
}
```

职责边界：

- 只负责应用数据目录路径规则与目录创建。
- 不负责数据库打开、文件复制或配置读写。

```csharp
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
```

职责边界：

- 负责在应用启动时确保数据库及 schema 处于可用状态。
- 不暴露具体迁移细节给 UI 或 ViewModel。

```csharp
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
```

职责边界：

- 只负责打开可用连接。
- 不负责 schema 迁移、SQL 执行或事务管理。

如果 `Application` 项目不希望直接暴露 `SqliteConnection` 类型，可以在实现阶段评估是否将该接口留在 `Infrastructure` 内部；但 Epic A 优先以简单、清晰为主，不额外引入抽象层级。

## UI 与 MVVM 最小切片

为了证明 `CommunityToolkit.Mvvm` 已接通，主窗口将绑定一个最小 `MainWindowViewModel`。

该 ViewModel 只承担两个目标：

- 暴露最小启动状态文本。
- 展示应用数据目录或数据库初始化结果摘要。

不在 Epic A 中引入导航、页面切换或复杂命令。

这样可以验证：

- ViewModel 构造由 DI 驱动。
- WPF 绑定链路有效。
- 业务逻辑没有下沉到 code-behind。

## 数据目录设计

根目录采用文档既定边界：

```text
%LocalAppData%\NovelSpeaker\
├─ app.db
├─ Books\
├─ Cache\
└─ Logs\
```

Epic A 中只要求：

- 能解析出这些路径。
- 首次启动可自动创建必需目录。
- 数据库文件路径稳定可预测。

不在本阶段创建 `settings.json`、`Secrets` 或业务缓存子目录树。

## 数据库迁移设计

### 迁移策略

采用简单显式 SQL 迁移，不引入 ORM 或外部迁移框架。

迁移执行器负责：

- 创建数据库文件所在目录。
- 打开 SQLite 连接。
- 检查 `SchemaVersion` 表是否存在。
- 按版本顺序应用未执行迁移。
- 记录当前 schema 版本。

### Version 1 范围

`Version 1` 只创建工程底座必需内容：

- `SchemaVersion`
- `AppMetadata`

`AppMetadata` 只用于证明迁移链路可用，不承载业务数据设计。

### 幂等性要求

- 空数据库首次初始化成功。
- 已初始化数据库再次执行不报错。
- 已有较新版本时不重复执行旧迁移。

## 依赖注入与日志

DI 采用 `Microsoft.Extensions.DependencyInjection`，不引入完整企业级宿主框架。

日志采用 `Microsoft.Extensions.Logging`，Epic A 中满足以下目标即可：

- 支持启动期日志。
- 支持基础设施初始化日志。
- 不把日志耦合进 View 或 code-behind。

后续如需文件日志，可以在 `Infrastructure.Logging` 方向扩展；Epic A 先不强制引入外部 provider。

## 测试策略

遵循 TDD，先写失败测试，再补实现。

Epic A 最小测试集包括：

### AppDataDirectoryProvider

- 能生成预期根目录与子目录路径。
- 调用 `EnsureCreatedAsync` 后目录被创建。

### SqliteMigrationRunner

- 空数据库能初始化到 Version 1。
- 重复执行保持幂等。

### Service Registration

- 核心服务可从容器解析。
- `MainWindowViewModel` 可通过 DI 创建。

测试优先使用临时目录和临时数据库，不依赖用户真实 `%LocalAppData%`。

## CI 设计

采用 GitHub Actions Windows-only 流水线，原因是：

- WPF 构建天然依赖 Windows。
- 与本项目真实运行环境一致。
- 能尽早暴露桌面项目特有构建问题。

流水线最小步骤：

1. 安装 .NET 10 SDK。
2. `dotnet restore`
3. `dotnet build -c Release`
4. `dotnet test -c Release`
5. 若仓库格式化配置稳定，再执行 `dotnet format --verify-no-changes`

## Backlog 更新策略

`docs/11_TASK_BACKLOG.md` 中的 Epic A 只勾选本次真正完成且可验证的项目。

预期可完成项包括：

- 创建 .NET 10 WPF 解决方案。
- 建立 `Domain`、`Application`、`Infrastructure`、`App` 和 `Tests`。
- 配置 `CommunityToolkit.Mvvm`。
- 配置依赖注入。
- 配置 `Microsoft.Extensions.Logging`。
- 建立应用数据目录服务。
- 配置 Nullable 和分析器。
- 建立数据库迁移基础设施。
- 添加 CI 构建和测试。

若实现中发现某项仍只是占位而非可运行能力，则保持未勾选。

## 风险与控制

### 风险 1：过度抽象

若在 Epic A 过早设计过多通用接口，会拖慢后续纵向切片。

控制：

- 只保留当前真正需要的接口。
- 优先让组合根与基础设施可运行。

### 风险 2：迁移接口泄露底层细节

若应用层直接暴露过多 SQLite 细节，后续会增加耦合。

控制：

- 初始化能力对 UI 仅暴露 `IDatabaseInitializer`。
- 数据访问细节留在基础设施中。

### 风险 3：测试覆盖过浅

若只验证“应用能启动”，迁移和目录规则容易在后续演进中回归。

控制：

- 先覆盖路径规则、迁移初始化、迁移幂等性和服务注册。

## 手动验收

Epic A 完成后的最低手动验收：

1. 首次启动应用，主窗口可以打开。
2. 主窗口能显示最小 ViewModel 状态。
3. 应用数据目录被自动创建。
4. `app.db` 被创建。
5. 再次启动应用时数据库初始化不报错。

## 实施顺序

建议按以下顺序实现：

1. 添加解决方案级构建配置和分层项目。
2. 添加测试项目并写首批失败测试。
3. 实现应用数据目录服务。
4. 实现 SQLite 连接工厂和迁移执行器。
5. 接入 DI、日志和最小 ViewModel。
6. 添加 GitHub Actions Windows CI。
7. 更新 backlog 勾选状态。

## 结论

Epic A 应落地为“可运行、可测试、可扩展的工程骨架”，而不是抢跑业务功能。

只要工程底座具备：

- 清晰分层，
- 最小 MVVM 闭环，
- 可重复的数据库初始化，
- 基础自动化测试，
- Windows CI，

后续各 Epic 就可以围绕稳定底座按纵向切片继续推进。
