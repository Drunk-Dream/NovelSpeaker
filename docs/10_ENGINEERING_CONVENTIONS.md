# 工程约定

## 1. 代码组织

- 产品项目按 `Domain → Application ← Infrastructure` 与 `App` 分层，具体边界见 `02_TECH_STACK_AND_ARCHITECTURE.md`。
- 每层内部按功能切片组织，不再新增全局 `Interfaces`、`Models`、`Services`、`Helpers` 大目录。
- 类型默认留在最具体的功能目录；只有被两个以上稳定功能消费时才进入 `Common`/`Shared`。
- 目录、命名空间、主公共类型和文件名一致。
- 不保留 `New`、`V2`、`Final`、`Refactored`、`Old`、`Compat` 等迁移期并行实现；迁移任务结束时删除旧入口。
- 不为一个调用点创建纯转发 wrapper，除非它代表设备、存储、外部系统、平台或测试所需的稳定边界。

## 2. 代码风格

- 启用 Nullable、Implicit Usings、最新分析器和 Warnings as Errors。
- 公共接口和关键公共类型使用 XML 注释说明职责、所有权和边界，不复述类型名。
- 一个方法承担一个可命名职责；优先守卫子句，避免多层 `if/try/loop` 嵌套。
- 不使用压缩成一行的 `if`、`try/catch` 或多个语句；保持与相邻代码一致的可读格式。
- `async` 方法以 `Async` 结尾；`async void` 仅限事件入口。
- 不吞掉异常，不用异常控制正常业务分支。
- 捕获 `Exception` 做降级时必须先传播 `OperationCanceledException`，并明确允许降级的异常范围。
- 不添加只叙述代码表面行为的注释。

## 3. 文件与类型规模

行数不是拆分目标，但用于触发职责审查：

- 生产 C# 文件超过约 `300` 行时，评审是否混合多个职责。
- 超过约 `500` 行的非生成文件必须在任务说明中解释状态所有权和为何不能安全拆分。
- ViewModel/Coordinator 出现超过约 `12` 个构造依赖或大量互相影响的状态组时，必须先绘制职责和状态所有权，再继续增加功能。
- 测试文件超过约 `800` 行时按行为场景拆分，不按任意行数切割。

迁移、XAML 资源字典、生成代码和数据 fixture 可例外，但仍需保持清晰定位。

## 4. 命名

- `Rule`：用户保存的规则实体或值。
- `Draft`/`EditorModel`：尚未提交的编辑副本。
- `ParsedRequest`：规则计算后的传输中立请求描述。
- `SpeechSegment`：章节中的朗读单元。
- `AudioCacheEntry`：已持久化音频索引。
- `PlaybackSession`：拥有独立取消和版本的一次播放过程。
- `PlaybackSnapshot`：给 UI 的不可变状态。
- `Repository`：领域实体/聚合持久化集合。
- `Store`：设置、进度或文件状态存储。
- `Gateway`/`Client`：外部系统边界。
- `Coordinator`：真正拥有长期状态机和串行化入口的对象。

避免含糊名称 `Manager`、`Helper`、`Utils`、`Processor`。测试 fake 以端口职责命名，例如 `ControllableAudioGateway`，不使用 `FakeEverything`。

## 5. Domain 与 Application

Domain 可以包含：

- 稳定业务实体和值对象。
- 纯不变量、规范化和计算。
- 不依赖外部技术的状态枚举。

Domain 不包含：

- SQLite 行、HTTP response、文件路径操作和 JSON source DTO。
- 页面列表项、编辑器状态、请求预览和 Snackbar 文案。
- 需要 Jint、NAudio、WPF 或网络的行为。

Application 可以包含：

- 用例实现和业务编排。
- 命令、查询、结果和 UI 无关 read model。
- Infrastructure 需要实现的语义端口。
- 播放状态机、规则工作区和设置校验。

Application 不包含或暴露：

- `SqliteConnection`、`SqliteCommand`、事务或 SQL。
- `HttpClient`、Jint Engine、NAudio、WPF/Wpf.Ui 类型。
- 任意应用数据绝对路径拼接。

## 6. Infrastructure

- 只实现 SQLite、文件、HTTP、Jint、NAudio、JSON 设置和日志适配。
- SQL、row mapper、迁移和连接配置就近放在 Persistence/Sqlite。
- 语义化原子端口可以跨数据库与文件实现暂存/恢复，但不能向 Application 暴露具体事务。
- 适配器把底层异常映射为 Application 可理解的安全类别；不直接生成页面文案。
- 不因为一个服务访问数据库就把整个用例放进 Infrastructure。

## 7. ViewModel 与 WPF

ViewModel 可以：

- 暴露可绑定的语义状态和命令。
- 调用 Application 用例。
- 维护页面编辑副本和轻量输入校验。
- 将 Application 结果投影为页面状态。

ViewModel 不可以：

- 执行 SQL、直接使用 HttpClient/Jint/NAudio 或读写文件。
- 自己维护核心播放状态机。
- 引用具体 Page、Window、Dispatcher、`FontWeight`、Brush 或 Wpf.Ui 图标类型。
- 通过 `Application.Current` 取得服务或 Dispatcher。
- 长期持有整本书正文。

View 使用触发器、转换器或资源把语义状态映射为图标、字体和颜色。

Code-behind 仅用于：

- WPF 生命周期、焦点、拖放和视觉树定位。
- 虚拟化、滚动、动画和无法合理绑定的控件适配。
- 将事件转交 ViewModel/页面控制器。

Code-behind 不执行 SQL、文件删除、HTTP、规则解析或播放会话协调。文件选择、剪贴板和系统目录通过统一 presentation port 封装，不在不同页面各自直接调用平台 API。

## 8. Page、组件与导航

- Page 是路由、激活、取消和未保存保护边界。
- UserControl 只用于两个以上页面复用，或拥有独立视觉行为/测试价值的复杂组件。
- 仅把整页包进同名 UserControl、手工设置 DataContext 的一对一 wrapper 不作为终态模式。
- 固定视口页面统一使用共享导航视口高度行为，禁止在各 Page code-behind 重复查找 `Frame`、订阅尺寸变化或绑定一套局部高度约束；分栏页面必须以真实 `Window + Frame` 和足量数据验证内部滚动区域具有有限视口。
- App 使用自有强类型 route/parameter；ViewModel 不引用具体 Page 类型。
- Wpf.Ui 路由映射、导航选择和兼容处理集中在 Shell adapter。
- 所有离开路径统一经过 navigation guard，不能只保护 BackCommand。

## 9. 依赖注入与状态所有权

- 功能切片提供各自注册模块，根注册方法只组合模块。
- 生命周期由状态所有权决定，不为“方便保留页面内容”随意注册 Singleton ViewModel。
- Singleton 必须线程安全，不捕获 Page/activation scope 服务。
- 页面状态由 activation scope 拥有；播放状态由应用级协调器拥有；操作状态由 operation scope 拥有。
- 测试/Debug 构建启用容器构建和 scope 校验。
- 业务代码不使用 service locator；只有组合根、框架 page provider 等明确边界可以解析 `IServiceProvider`。

## 10. 异步、取消与并发

- 所有异步 I/O、数据库、文件、HTTP 和可等待业务流程接收并传递 `CancellationToken`。
- 页面进入创建 activation Token，离开取消；操作可创建链接 Token。
- `CancellationToken.None` 只用于有明确理由的不可取消最终清理，并在代码附近说明。
- 取消是正常控制流，不显示为用户错误，也不被 `catch (Exception)` 转换为失败。
- 旧异步结果使用 SessionId、activation version 或 operation version 拒绝。
- 长期可变状态由单一串行入口保护；不要让多个事件回调直接并发推进状态。
- fire-and-forget Task 必须登记所有者、异常处理和取消；禁止裸 `_ = Task.Run(...)` 承担进程级工作。
- 防抖、重试、限流和超时使用可注入时间源，测试不依赖固定 sleep。

## 11. 结果、异常与用户反馈

- 重复书籍、需要选择编码、无规则等预期分支使用结果类型。
- 底层 I/O/网络异常由边界分类；用户可见文案由 Application/App 安全投影。
- 不把 `Exception.Message` 原样拼到 UI 结果，尤其是 HTTP、路径、模板和数据库异常。
- 技术细节只写入经过脱敏的结构化日志。
- 取消不记录 Error；可恢复故障不触发进程级异常处理器。

## 12. 配置和时间

集中管理：

- 最大段落长度。
- 默认预取数量和缓存上限。
- HTTP 超时、错误正文上限和重试次数。
- Jint 执行、语句、递归和输出限制。
- 正则执行超时。

Application 使用 `TimeProvider`/`DateTimeOffset`。Infrastructure 负责 ISO/SQLite 序列化；不得在多个用例中散落 `DateTime.UtcNow.ToString("O")`。

## 13. HTTP、SQLite 与文件

### HTTP

- 复用受控 HttpClient/handler；明确生命周期和规则会话策略。
- 使用流复制到受控临时文件，限制错误响应读取长度。
- 运输、重试、响应验证、临时音频存储和音频探测职责分离。
- 释放 Response、Request、Content 和 Stream。
- 当前禁用自动 Cookie；若以后支持，按规则隔离 CookieContainer 且不默认持久化。

### SQLite

- 连接工厂仅在 Infrastructure。
- 每连接启用 foreign keys 和合理 busy timeout。
- 所有 SQL 参数化，批量写入使用事务。
- 已发布 migration 只追加不改写。
- repository 不返回依赖活动 reader/connection 的对象。

### 文件

- 路径来自应用数据根或用户明确选择的只读源文件。
- 所有持久化路径通过集中 resolver 做 canonicalization 和 root containment。
- 缓存文件名使用哈希；设置、缓存和书籍写入使用临时文件与原子切换。
- 跨数据库/文件操作具有可恢复 journal，不能只依赖进程内补偿。
- 不跟随规则输入或被篡改数据库指向应用根目录外路径。

## 14. JavaScript、安全与隐私

- 规则脚本默认不可信。
- 禁止 CLR、文件、进程、反射、任意网络、环境变量和任意宿主对象。
- 每次计算隔离状态，限制时间、语句、递归和输出长度。
- 只暴露白名单函数。
- 日志、异常、预览、Snackbar 和诊断摘要统一脱敏。
- 不记录小说正文、完整 URL、Authorization、API Key、Cookie、LoginInfo、Header、Body 或响应正文。

## 15. 测试与提交

- 缺陷修复先提交失败回归测试。
- 实现和直接测试属于同一原子变更。
- 一个迁移任务完成时同时删除旧实现和临时适配；不能把清理无限推后。
- 不把无关格式化、重构、行为变化和依赖升级混在同一任务/提交。
- 每个任务按 `09_TESTING_AND_QUALITY.md` 运行切片验证；阶段收口运行全门禁。
