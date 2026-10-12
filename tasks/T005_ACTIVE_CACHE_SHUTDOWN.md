# T005 — Active Cache 退出取消与有界排空

- **类型/优先级：** 实施 / P2 Lifecycle。依赖 T004；T006 独立但默认待本任务通过后执行。
- **已验证风险：** `StartupCoordinator.ShutdownCoreAsync` 取消 process token 后，`WpfStartupRuntime.WaitForBackgroundTasksAsync` 排空 Export、SpeechPlanRepair、CacheInvalidation，但没有 Active Cache。Active Cache 的批次 CTS 独立于 caller process token，仅 `ActiveCacheCoordinator.DisposeAsync` 才取消并无限 await active task。WPF `App.OnExit` 通过同步桥接最多等待约 5 秒；对不配合取消的假 Provider 已复现 dispose 不会自行结束。尚无真实数据丢失证据。

## 批准的目标行为

- **退出应用**（不是隐藏、页面离开或普通章节切换）应要求 Active Cache 停止开始新工作、向正在执行的批次发送取消，并作为现有 process background owner 参与**有界**排空；不能把 cancel 延迟到容器 Dispose 阶段。
- 已成功生成且原子落盘的缓存仍保留；未完成缓存不能以 Ready 状态留下。取消、失败及用户主动终止不得无限延长窗口退出。
- 现有 WPF 有界退出桥接继续有效。对不遵守 cancellation 的外部 Provider，不能让任意 `DisposeAsync` 无界等待导致进程退出路径僵住；超时须按有限资源能做出的 best-effort 收尾如实记录。不能声称未结束 worker 已安全完成、不能在后台继续写正式数据后又错误宣布 drain 完成。

## 允许变更

1. 追踪 `WpfStartupRuntime.WaitForBackgroundTasksAsync`、`StartupCoordinator.ShutdownCoreAsync`、`ActiveCacheCoordinator` 的取消/等待/释放，明确实例 ownership、process exit record 与 cleanup 排序；避免通过修改 WPF 5 秒 bridge 常量掩盖逻辑。
2. **优先复用**现有 Cache/Export/Repair background drain 的角色和等待模型，加入 Active Cache 所需的狭窄取消/排空入口；如必须修改该 coordinator `DisposeAsync`，采用不会留下新的无界等待的最简单可靠实现。不新增抽象 `BackgroundTaskManager`、通用 Task Registry 或额外进程常驻 watcher。
3. 原有等待上限不能因为新加 Active Cache 被串行反复叠加而造成更长的实际应用退出。优先沿用或共享既有退出预算；解释超时与释放的关系，并确保故障/取消可被记录为**有限信息**而非敏感路径或正文。
4. 活跃 Active Cache 仍与页面生命周期分离；隐藏到托盘、打开其它页面、切章均不取消它；正常的 Cancel/replace/progress 和其它 Export/Repair 顺序保持合理，不重写整个 StartupCoordinator。
5. 仅在真实合同变化处同步 `docs/02_RUNTIME_AND_NAVIGATION.md` 和/或 `docs/04_CACHE_AND_BACKGROUND_WORK.md` 的短条款，避免把具体超时数值永久写成产品承诺。

## 验证/验收

- 使用独立可取消合成 Provider 和无响应 Provider 测试：请求退出后**立即可观察到批次 token 被取消**，合作 Provider 有界收尾且正确释放，非合作 Provider 不会让应用/协调器进入无界等待；超时条件下不伪造 Complete/Ready 或“已安全停止”。
- 以真实 startup/DI 接线或隔离最窄 host 级测试验证确实覆盖 App → StartupCoordinator → WpfStartupRuntime → Active Cache 的生产链路，而非只测试新 helper；环境不足则如实记录未覆盖，不得宣称完整退出已验证。
- 回归：Cache 在页面离开/托盘隐藏时继续运行、已完成音频保留、取消批次不会新增 Ready 的半写条目、正常退出的 Export/repair/diagnostics 清理仍执行且没有异常吞没。
- 保留少量跨模块高价值核心回归；临时 noncooperative Provider/harness 退出后清理。使用非破坏性合成内容，不接触真实用户文件。执行 focused tests、Release build、format/`git diff --check`，如实记录门禁。

## 超出授权的停止点

若要改变数据文件完成协议、缓存持久化 owner、从进程退出自动恢复未完成 job、持久化后台 job 记录、额外进程结束机制等，停止相关扩张并记录 `Needs Decision`，不要默认实施。

## 完成

T005 标记成果/验证/限制，删除任务规格；T007 将进行最终交叉检查。不要为了按时关窗而跳过必要安全清理或把无界等待藏到其它服务里。
