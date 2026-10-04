# T003：收敛 Regex 变更传播并完成 Phase A 验收

## 依赖与阶段性质

依赖 T002。本任务结束 T001–T003 staged breaking migration window，必须恢复完整可构建、可测试状态。

## 目标

让 Playback 自己消费 `IRegexReplacementRuleWorkspaceService.Changed` 的稳定语义，删除 Regex 页面在每种保存路径后手工刷新 Playback 的重复编排，并对 Phase A 做完整收口。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 6、7、11 节
- `docs/specs/REGEX_REPLACEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`

## 实施要求

1. Playback process/session owner 订阅 Regex workspace 的 `Changed`，只对 `AffectsSpeechProfile=true` 的已提交变化排入 serialized playback boundary。
2. 保持现有行为：影响 SpeechText 时根据当前来源位置与新 Speech Plan 解析最近可播放段；只影响 DisplayText 时不得无理由中断音频。
3. 保存、启用/停用、删除、排序、导入、批量删除、配置恢复都依赖 workspace 发布，不由 ViewModel 额外调用 Playback。
4. 删除 `IPlaybackRegexReplacementRefresher`、`RefreshRegexReplacementAsync` 的页面依赖、DI 注册和仅服务旧传播方式的测试/fake。
5. observer 生命周期归 process owner；dispose 时解除订阅，事件回调不执行同步长工作。

## Phase A 清理审计

全仓搜索并处理：

- mutation 后由 ViewModel 调用 `Refresh*` / `Handle*Deleted` 传播跨模块后果；
- `BookCatalogInvalidationState` 与旧 Books change 类型；
- 同一已提交变化同时由调用者和 observer 处理两次；
- 为迁移临时保留的 old/new/compat wrapper。

只修复与本阶段相同根因的实例；不要借机扩展成通用消息框架或处理无关 UI refresh。

## 测试收敛

- 保留核心测试：Regex 改变当前 speech profile、Display-only change、非当前 Book change、删除/metadata/catalog committed change、observer isolation、迟到结果保护。
- 删除只断言 ViewModel 调用了 refresher fake 的实现细节测试，改由 workspace mutation → consumer 行为验证覆盖。
- 不为每个按钮命令复制同一 propagation 测试。

## 完整门禁

执行 `AGENTS.md` 标准完整门禁。若环境限制无法执行，必须记录真实限制；代码或测试自身失败时不得结束窗口。

完成后更新 `TASK_BACKLOG.md`、记录删除的旧传播 API，并删除本文件。
