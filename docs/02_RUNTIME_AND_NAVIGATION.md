# 运行时与导航

## 1. 生命周期层级

| 层级 | 典型状态 | Owner |
|---|---|---|
| Process | Shell、Settings、导航、托盘、长期基础设施 | Process service/coordinator |
| Playback session | 当前书/章/段、音频、prefetch、snapshot | Playback session owner |
| Page activation | 页面加载、filter、selection、draft、locator | Transient Page/ViewModel |
| Background job | 主动缓存、章节导出、Speech Plan repair | 对应 coordinator |
| Diagnostic session | 问题复现期间的诊断现场 | Diagnostics session owner |
| Operation | 导入、保存、试听、清理等一次性流程 | 发起 use case/controller |

模块归属与生命周期是两个维度。例如 Active Cache 属于 Cache 模块，但运行态是 process background job。

## 2. 启动

启动必须具有明确阶段，并且核心初始化失败时不进入半初始化正常运行态：

```text
bootstrap essential logging
→ load/normalize settings
→ build host/DI
→ initialize/migrate database
→ recover required state
→ initialize process/session/background owners
→ create Shell
→ apply desktop/theme behavior
→ show main window or tray
```

若存在仍 Active 的诊断会话，启动阶段恢复该会话并记录新的 process instance；诊断失败不得阻止 NovelSpeaker 正常启动。

## 3. Page activation

普通 Page/ViewModel transient。进入页面形成新的 activation/version、CTS、订阅和 staged-loading 生命周期。

离开页面：

- 取消 page-owned work；
- 解除页面订阅；
- 拒绝旧 activation 的迟到结果；
- 不取消 process/session/background owner 的工作。

不通过 Page singleton、Navigation cache 或固定延时保存/恢复业务真值。

## 4. 导航

应用使用强类型 route，业务层不维护浏览器式 Back/Forward 历史。

普通页面具有稳定父级，例如：

- BookDetails → Library；
- Settings 子页 → Settings；
- RegexReplacementRules → ImportTextSettings；
- CacheManagement → CacheAndData。

Player 是唯一使用动态一次性 ReturnRoute 的页面。ReturnRoute 记录进入 Player 前的完整业务 route，不递归指向另一个 Player。

PageHeader、Alt+Left 和未被局部交互消费的 Esc 复用统一返回语义。

## 5. Staged Loading

复杂页面统一：

```text
Critical
→ First Interactive Frame
→ Secondary Enrichment
→ Background Enhancement
```

- 首个交互帧之前只完成必要工作。
- enrichment/locator 不形成 Dispatcher 长工作窗。
- 所有阶段受当前 activation/version 保护。
- 快速离开/返回后旧阶段不得重新写入新页面。

## 6. Dispatcher 与异步

Dispatcher 只承担小型 UI 提交、WPF interaction 和 layout。

禁止在 Dispatcher 无界执行：

- 大规模 DTO→VM projection；
- 大规模 sort/group/hash；
- 全量 cache/status property 更新；
- 随完整数据集线性增长的连续工作；
- 同步等待后台任务。

`async void` 只用于 WPF 事件入口；fire-and-forget 必须有 owner。`Task.Yield()` 只改变 continuation 排队时机，不是后台线程切换。

## 7. Playback session 生命周期

Playback session 是当前活动播放状态唯一 owner。新书、显式章节/段落跳转和需要重建播放语义的配置变化通过受控 command/session generation 处理。

迟到的 HTTP、cache、audio callback、page projection 不得覆盖更新后的 session。

改变 session 的命令进入受控串行化边界；失败或取消不能提前提交目标位置。

## 8. Background job

主动缓存、章节导出和 Speech Plan repair 由对应 process background owner 持有。

页面只提交参数和观察 snapshot：

- 页面离开不取消已经提交的 background job；
- coordinator 拥有 CTS、Task、进度和终态；
- shutdown 统一取消并进行有界等待；
- 不建立通用 BackgroundTaskManager。

## 9. 桌面生命周期

主窗口关闭行为：

- Hide to tray：仅隐藏窗口，Process 继续。
- Exit：执行完整 shutdown。
- Ask：用户选择后执行对应路径。

迷你播放器、主窗口和系统媒体控制共享同一 Playback session，不创建第二套播放状态。

## 10. 诊断会话跨进程

诊断会话由用户显式开始和结束，不等同于 Process 生命周期。

- 关闭 NovelSpeaker 只结束当前 process，不自动结束 Active Session。
- Active Session 使用极小 marker 定位权威 `.nsdiag`。
- 下一次启动创建新 process instance 并继续同一 Session。
- 若上一个 process 未留下正常结束记录，只记录 `Unexpected` 事实，不主观判断一定是 Crash。
- 应用升级后仍可继续 Active Session；每个 process 记录自己的 appVersion。
- 已结束 Session 不重新打开继续写；再次复现创建新 Session。

## 11. Shutdown

关闭流程必须可等待、可重复且有界：

```text
block new UI operations
→ resolve navigation/edit guards
→ stop desktop callbacks
→ checkpoint playback/settings
→ stop/release audio
→ cancel background jobs
→ bounded await
→ flush logging/diagnostics best effort
→ dispose host/container
```

基础设施 flush 超时不得导致应用无限无法退出。
