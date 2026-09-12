# 质量与测试

## 1. 目标

测试用于保护稳定行为、数据安全和架构边界，不用于冻结旧内部实现。允许随着架构重构删除、合并或重写实现细节测试。

衡量标准：

- 关键风险是否覆盖；
- 测试是否处在正确层级；
- 是否依赖私有实现；
- 是否支持继续重构；
- WPF 自动测试是否安全隔离；
- 自动验收是否足以让 Agent 无需人工等待即可继续。

## 2. 测试层级

### Domain / Application

验证业务规则、use case、Playback session/checkpoint、Cache/background coordinator、query/projection、cancellation/version、Observability contract 等。不实例化真实 WPF。

### Infrastructure Integration

验证 SQLite migration/query、文件/路径、HTTP/TTS、Jint、NAudio、cache index/file、JSONL、诊断 SQLite store、真实序列化和导出。

### Presentation

验证 ViewModel state、command、activation/deactivation、staged loading、snapshot/read-model projection、诊断工具状态和迟到结果。不得因为方便加载真实 Window。

### WPF

只验证依赖 WPF 的 navigation composition、binding、virtualization、focus、popup/dialog、layout/hit testing、scrolling/locator、style/resource/theme、悬浮诊断工具窗口和主动当前窗口截图边界。

## 3. 长期必须保护

- PlaybackSnapshot 与 ReadingProgress checkpoint 语义。
- 显式切章/切段成功、失败和取消。
- 强类型 navigation/ReturnRoute。
- Active Cache / Export / Repair owner 生命周期。
- SQLite migration。
- Cache identity / plan / coverage。
- 外部 TXT 路径安全。
- TTS 脚本安全、请求编译与限流。
- 大列表结构合同。
- WPF hidden Desktop fail-closed。
- Light/Dark/System 和关键视觉交互。
- Logging / Telemetry / Diagnostics 失败不影响业务。
- 诊断隐私边界与主动截图例外。
- 诊断 Session 跨进程继续、用户结束、容量上限和导出。

## 4. Architecture Fitness Tests

长期守护：

1. 四层依赖方向。
2. Application 模块无 cycle。
3. App 非 Bootstrap 不直接依赖 Infrastructure。
4. Shared 不依赖 Feature，Feature 无双向依赖。
5. ordinary Page/ViewModel 默认不注册 Singleton。
6. ViewModel/Feature controller 不使用 Service Locator。
7. 不新增通用 EventBus/Messenger。
8. Application 不引用 WPF。
9. 页面不直接写 ReadingProgress。
10. Playback mutable session state 只有指定 owner。
11. Cache 不反向依赖 Playback session truth。
12. Observability API 不泄露 Infrastructure store 类型。
13. Logging、Telemetry、Diagnostic Session writer/store 不互相依赖。
14. 内部 migration compatibility wrapper/Obsolete bridge 不长期留存。

Architecture tests 不使用绝对毫秒阈值。

## 5. 大列表与性能回归

结构性自动测试优先验证：

- staged loading 首帧不等待 enrichment；
- current/cache change 只更新必要范围；
- 大列表不产生 N 次同步 collection add；
- locator 完成后解除临时订阅；
- 页面离开后旧 enrichment 失效。

真实规模回归保留 180 / 1000 / 3000+ / 10000 章节场景，并观察首帧、Dispatcher、locator、cache decoration、memory、SQLite 和必要的普通性能遥测。

真实耗时用于诊断与比较，不作为脆弱固定毫秒门槛。

## 6. Observability 测试原则

生产日志：

- schema 与 EventId 稳定；
- queue overflow 不阻塞业务；
- rotation/retention；
- Exception 序列化与隐私清理；
- 写入失败降级。

性能遥测：

- 默认关闭；
- 开关/清除；
- 稀疏窗口与可合并统计；
- retention/capacity；
- 导出不错误合并版本/不同指标合同。

诊断会话：

- Start 前不采集；
- Active Session 跨正常重启和 Unexpected process end；
- End 后不可续写；
- Marker 前后数据和 snapshot；
- Session 内匿名对象不可反向映射真实 ID；
- 主动截图只捕获 NovelSpeaker 窗口且仅由用户操作触发；
- 达到硬容量上限停止采集；
- `.nsdiag` 与关联日志可导出；
- Diagnostics store 失败不影响业务。

## 7. WPF 隔离 Desktop

默认自动测试不得在用户当前 Desktop 显示顶层窗口。

- 普通 Page/UserControl 优先无顶层 Window host。
- 真实 Window/Popup/Focus/HWND 生命周期使用 `tests/TestKit/Wpf` 隔离 Desktop。
- 隔离初始化失败 fail closed，不回退当前 Desktop。
- 未经当前任务明确授权，不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
- 生成视觉验收产物不等于允许显示窗口。

## 8. 异步与时间

- 使用事件、状态版本、barrier/gate 或可控 TimeProvider。
- 不用固定 `Thread.Sleep`/任意 `Task.Delay` 猜测完成。
- cancellation 是正常控制流。
- 测试观察所有 background owner/fire-and-forget 的异常。

## 9. 人工验收

人工视觉/交互验收始终属于**可选补充**：

- 不作为 Task 完成条件。
- 不阻塞 Agent 执行下一任务。
- 不要求用户批准后才可提交自动验证通过的任务。
- 后续人工发现问题时，新建修复 Task 并补充自动回归测试。

Agent 可以自行生成截图或视觉产物进行自动/静态辅助验收，但任务通过后删除一次性产物。

## 10. 完整质量门禁

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

任务可以使用 focused tests 加快迭代；是否要求完整门禁由 task spec 指定。一个 Phase 的最终收口任务应执行完整门禁。

环境导致 testhost/网络/沙箱阻塞时如实记录；禁止删测试、弱化架构规则、开启可见 Desktop 或绕过隔离来制造绿色。
