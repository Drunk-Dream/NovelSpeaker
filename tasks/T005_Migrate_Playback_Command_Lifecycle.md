# T005：迁移 Playback 命令与 session replacement 生命周期

## 依赖与阶段性质

依赖 T004。处于 T004–T007 staged breaking migration window。

本任务允许 low-level audio callback 与旧 snapshot 同步逻辑留到 T006；不得为了它们恢复已删除的高层平行状态。

## 目标

把用户命令、Books/Regex/Settings 已提交变化和 session replacement 迁入 T004 的 authoritative runtime/transition 边界，统一“准备目标 → commit → effects/checkpoint”的顺序。

## 必读

- `AGENTS.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 7、11 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 5、7、10、11、13 节

## 必须迁移的命令

- Start / OpenPaused；
- Play/Resume / Pause / Stop / Clear；
- chapter/segment jump、next/previous；
- retry 与连续失败自动 skip；
- Provider 与 speak speed 变化；
- Books metadata/catalog/source change；
- Regex speech-profile change。

## Session replacement 与 commit

1. 目标 Book/content/provider/position 的解析属于 preparation，不能提前覆盖 current runtime。
2. 新 session 只有在能形成合法目标 runtime 后一次性 commit。
3. preparation 失败或取消保留旧 session、旧 logical position 与必要 audio protection；不要通过复杂字段级 rollback 重新拼旧状态。
4. commit 后再取消/释放旧 session effects，并按稳定边界 checkpoint。
5. Source context 与 session identity 同时参与迟到 result 校验。

## Progress、Prefetch 与失败恢复

- jump/start 成功后才 checkpoint 新逻辑位置；pause/stop/replacement/shutdown 保持原稳定保存语义。
- prefetch window 从 committed runtime 派生，旧 session prefetch 取消或结果失效。
- 连续失败计数和“跳过 3 段后暂停”由 runtime transition 维护，不在多个 helper 保存副本。
- retry 只存在一个 owner，避免 Provider 与 Playback 竞争的双重恢复循环。

## 删除与简化

- 删除 `_currentBook`、`_currentProvider` 等仅为旧 Coordinator alias/同步存在的成员（若仍有真实用途，迁入 runtime）。
- 删除字段级 `RestorePreviousSessionAfterReplacementFailure` 类 rollback 路径，改由未提交新 session 保留旧 runtime。
- 合并重复位置解析后提交逻辑，但不建设万能 command handler。

## 强制验证

- start/open/jump preparation 失败或取消不提交目标位置。
- session replacement 成功后旧 content/prefetch/result 无法写入新 runtime。
- pause/stop/clear/shutdown checkpoint 与原产品语义一致。
- Provider/Regex change 保持“当前句/下一句”语义；metadata-only change 不打断音频。
- failure retry/skip threshold、显式恢复清零窗口保持核心测试覆盖。
- 执行相关 Playback focused tests 与 Application build；完整门禁留给 T007。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
