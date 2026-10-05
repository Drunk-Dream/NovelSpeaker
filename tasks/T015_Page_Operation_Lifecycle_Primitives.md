# T015：补齐 activation 与 latest-wins 小型生命周期原语

## 依赖与阶段性质

依赖 T014。本任务不在 staged breaking window 内，结束时仓库必须保持可构建、focused tests 通过。

## 目标

在现有 `PageActivationController` / `PageActivationScope` 基础上补齐少量高频缺口，使页面不再反复手写：

```text
CTS + version + IsCurrent + finally Dispose + task observation
```

必须清楚区分三类概念：

1. **Page activation**：页面存在期、订阅、page-owned work；
2. **Latest operation**：搜索、筛选、debounced save、projection 等后发覆盖先发；
3. **Data revision**：catalog/playback/layout 等不同事实版本。

## 必读

- `AGENTS.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3、5、6 节
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 3–5 节
- `docs/08_QUALITY_AND_TESTING.md`

## 先做模式核对

以 `LibraryViewModel`、`BookDetailsViewModel`、`PlaybackSettingsViewModel`、`PlayerContentController`、`SpeechServicesViewModel` 为样本，把现有 CTS/version 分成上面三类。只有至少三个调用点语义真正相同的 latest-wins 模式才进入共享 primitive；其它保持 feature-local。

## 原语要求

- 优先扩展现有 activation scope，而不是建立第二套 `PageLifetime`。
- latest-operation slot 最多负责：替换/链接 CTS、单调 identity、异常观察、`IsCurrent`/`TryCommit`、cancel/dispose。
- 支持与当前 activation token 链接，但 latest operation 替换不能取消整个页面。
- cancellation 是正常控制流；迟到异常只在当前 activation/operation 仍有效时投影。
- 不持有业务 DTO，不知道 Book/Cache/Playback/Settings，不调度通用后台任务。
- 不建立 reflection、基类层级、Rx 引入或通用 scheduler。

允许存在一个基础 operation slot 和一个很薄的 debounce 使用方式；不要为 API 对称添加未被真实调用的能力。

## 代表性迁移

选择一个低风险、高重复的实际调用点在本任务中迁移，证明 API 能净删样板且不把业务 revision 错删。不要先批量迁移所有页面。

## 强制验证

- 新 operation 会取消旧 operation，旧结果不能 commit。
- page deactivation 取消 current operation，解除订阅并观察 completion。
- replacement cancellation 不取消 activation；activation cancellation 会取消 operation。
- current operation exception 可被 owner 投影，stale/cancel 不重复报告。
- dispose 可重复且无 CTS 泄漏。
- 代表性调用点净减少手写 lifetime state；运行 Shared/activation focused tests、format、Release build。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
