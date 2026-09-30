# T002：收拢播放连续失败恢复

## 目标

避免单个 TTS/缓存异常永久阻塞播放，同时把自动内容损失限制在明确的连续 3 段以内。

## 权威参考

- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/specs/HTTP_TTS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `AGENTS.md`

## 实施原则

先审计现有 Provider Runtime、HTTP retry、Edge transport、`PlaybackRecoveryPolicy` 与空音频/缓存损坏路径，确定每类重试的唯一 owner。不要在 Playback 再套一层与 Runtime 重复的网络重试循环。

### 最终失败后的行为

- 可恢复瞬时错误：由现有稳定边界执行有限重试。
- 明显配置/认证/权限/协议无效等非瞬时错误：不机械重复请求，可以直接视为当前段最终失败。
- 当前段最终失败后自动跳到下一段。
- 成功进入任一可播放段后，连续自动跳过计数归零。
- 连续自动跳过第 3 段后暂停；下一段不得继续自动请求/播放。
- 用户显式 Play/Retry/调整 Provider 后恢复时开启新的连续失败观察窗口。

缓存损坏继续优先按现有语义作废并重建；只有最终无法获得可播放音频时才计入自动跳段。

### 状态与反馈

- 保留可操作的错误/状态提示，让用户知道播放因连续失败暂停。
- 不为每次瞬时 retry 弹 Dialog。
- 不记录小说正文、Provider Secret 或完整请求内容。

## 不要做

- 不引入 Provider 自动 fallback。
- 不建立无界 retry/backoff 框架。
- 不让一次错误跳过整章或大量内容。
- 不把 cancellation 计为播放失败。

## 核心测试

永久测试只保护稳定合同：

- 最终失败会跳过一个段；
- 成功段重置连续计数；
- 连续 3 段最终失败后暂停且不自动处理第 4 段；
- cancellation 不增加失败计数；
- 显式恢复后可以重新尝试后续播放。

可用临时测试验证具体 Provider/缓存错误分类，完成后删除。

阶段完成前执行完整质量门禁。
