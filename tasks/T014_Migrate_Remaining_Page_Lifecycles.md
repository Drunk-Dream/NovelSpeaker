# T014：迁移其余高收益页面并完成生命周期验收

## 依赖与阶段性质

依赖 T013。任务结束时执行标准完整门禁。

## 目标

迁移 Cache/Player/Settings/SpeechServices 中与 T012 模式真正等价的手写生命周期，删除残留重复设施；不以“所有 version 都消失”为目标，不统一语义不同的 state owner。

## 必读

- `AGENTS.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3、5、6、8 节
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 3–5 节
- `docs/08_QUALITY_AND_TESTING.md`

## 优先调用点

按收益和 T008–T011 后的实际代码重新核对：

- CacheManagement/CacheAndData 的 load、selection replacement、decoration window；
- PlayerViewModel/PlayerContentController 的 book/chapter load 与 page activation；
- PlaybackSettings/GeneralSettings/ImportTextSettings 的 debounced save；
- SpeechServices 的 page CTS、voice catalog/search latest-wins、preview/test lifetime；
- 仍同时持有 activation token、generation、CTS、OwnedTaskRegistry 的 feature-local controller。

## 保留边界

- Playback session、Active Cache、Export、Speech Plan repair、Provider preview audio 等非 page owner 保持自己的 lifetime。
- Catalog revision、selected Book identity、audio session identity、layout/viewport readiness 不是 cancellation 的别名时继续保留。
- WPF code-behind 只桥接 Loaded/Unloaded/viewport/focus，不把业务 operation 搬回 code-behind。
- `OwnedTaskRegistry` 若仍被非-activation owner 合理使用可以保留；若所有用途已被更明确 owner 替代则删除。

## 清理审计

全仓检查手写 `CancellationTokenSource + int version/generation + IsCurrent`。每处分类为：已迁移、真实 data revision、非 page lifetime 或待后续领域任务；不要机械替换。删除无使用者的 helper、重复 task registry、临时 adapter 和过度测试。

## 强制验证

- 快速导航/切换/search/debounce/test/preview 不产生 stale UI commit 或未观察异常。
- page leave 不取消 process/session/background work。
- Settings 最终保存 latest value，不因取消产生错误反馈。
- Provider voice search/catalog 与试听生命周期保持，旧 editor 结果不进入新 editor。
- architecture tests 保持 Shared 不依赖 Feature，未引入通用异步框架。
- 执行 `AGENTS.md` 标准完整门禁。

完成后在 `TASK_BACKLOG.md` 记录迁移页面、保留的特殊 revision 和净删除设施，并删除本文件。
