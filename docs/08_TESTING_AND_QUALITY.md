# 测试与质量

## 1. 目标

测试用于保护稳定行为和架构边界，不用于冻结旧内部实现。架构重构期间允许删除、合并、重写测试，测试总数量允许减少。

衡量标准：

- 风险是否被覆盖；
- 测试层级是否正确；
- 是否依赖实现细节；
- 是否支持继续重构；
- WPF 自动测试是否安全隔离。

## 2. 测试项目职责

### Domain/Application

验证：

- 业务规则；
- use case；
- Playback command/session/checkpoint；
- Cache/Export coordinator；
- query/projection；
- cancellation/version；
- 错误分类。

不实例化真实 WPF。

### Infrastructure Integration

验证：

- SQLite schema/migration/query；
- 文件/路径；
- HTTP/TTS；
- Jint；
- NAudio 技术适配；
- cache index/file；
- 真实序列化。

### PresentationTests

验证：

- ViewModel state；
- command；
- activation/deactivation；
- staged loading 顺序；
- snapshot/read-model projection；
- 页面取消和迟到结果。

不得因为方便而加载真实 Window。

### WpfTests

只验证真正依赖 WPF 的：

- navigation composition；
- binding；
- virtualization；
- focus；
- popup/dialog；
- layout/hit testing；
- scrolling/locator；
- style/resource/theme。

## 3. 测试精简

允许：

- 删除旧 internal class 结构测试；
- 删除 Presentation/WPF 对同一非 WPF 行为的重复覆盖；
- 合并重复 fake/stub；
- 拆大 fixture；
- 重构 TestKit；
- 删除只为兼容旧 API 存在的测试。

禁止为了保留旧测试重新创建旧 abstraction/compat wrapper。

## 4. 必须长期保护的行为

- PlaybackSnapshot 与 ReadingProgress checkpoint 语义；
- 显式切章/切段成功、失败、取消；
- 强类型 navigation/ReturnRoute；
- ActiveCache/Export owner 生命周期；
- SQLite migration；
- cache identity/plan/status；
- 外部 TXT 路径安全；
- TTS 脚本安全和限流；
- WPF hidden Desktop fail-closed；
- 主题/关键交互；
- 大列表结构合同。

## 5. Architecture Fitness Tests

长期验证：

1. 四层依赖方向。
2. App 非 Bootstrap 不引用 Infrastructure。
3. Shared 不依赖 Feature。
4. Feature 无双向依赖。
5. ordinary Page/ViewModel 默认不注册 Singleton。
6. ViewModel/Feature controller 不使用 `IServiceProvider`。
7. 不新增通用 EventBus/Messenger。
8. Application 不引用 WPF。
9. 页面不直接写 ReadingProgress。
10. Playback mutable session state 只有指定 owner。
11. large-list helper 不使用首屏 `Clear + N × Add`。
12. 迁移 compatibility wrapper/Obsolete bridge 不跨 Phase 留存。

Architecture tests 不使用绝对毫秒阈值。

## 6. 大列表/性能测试

性能回归分两类：

### 结构性测试

- staged loading 首帧不等待 enrichment；
- current/cache change 只更新必要项；
- 大列表不产生 N 次同步 collection add；
- locator 完成后解除临时 layout/readiness 订阅；
- 页面离开后旧 enrichment 失效。

### 真实规模诊断

在架构迁移后用 180/1000/3000+/10000 章节运行隔离 WPF 场景，记录首帧、Dispatcher、locator、cache decoration、memory 和 SQLite。

真实耗时用于诊断，不作为脆弱 CI 固定毫秒门槛。

## 7. WPF 隔离 Desktop

默认自动测试不得在用户当前 Desktop 显示顶层窗口。

- 普通 Page/UserControl 优先使用无顶层 Window 的 host。
- 真实 Window/Popup/Focus/HWND 生命周期使用 `tests/TestKit/Wpf` 隔离 Desktop。
- 隔离初始化失败必须 fail closed，不回退当前 Desktop。
- 未经用户当前任务明确授权，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
- 视觉产物生成权限不等于可见窗口权限。

## 8. 异步测试

- 等待事件、状态版本、barrier/gate 或可控 `TimeProvider`。
- 不用固定 `Thread.Sleep`/任意 `Task.Delay` 猜测完成。
- cancellation 是正常控制流。
- 测试必须观察 fire-and-forget owner 的异常。

## 9. 视觉测试

- Style Gallery 只展示资源族。
- 正式截图来自真实 View + 脱敏 fixture。
- `artifacts/visual-review/` 是显式验收产物，不是默认测试基线。
- 任务完成后删除一次性 screenshot/trace/script。

## 10. 质量门禁

完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

每个架构迁移任务合入 `dev` 前至少保证：

- Release build 通过；
- 本任务相关测试通过；
- ArchitectureTests 通过；
- 无临时诊断产物；
- 当前任务声明应删除的兼容代码已经删除。

完整 testhost 因环境阻塞时必须记录真实原因，不通过删测试或绕过隔离来“获得绿色”。
