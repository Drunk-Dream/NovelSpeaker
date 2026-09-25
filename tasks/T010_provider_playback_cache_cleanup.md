# T010：完成 Provider 播放/缓存接入并清理旧 TTS Rule 体系

## 目标

让 Playback、Prefetch、Active Cache、Coverage、Export 和播放页 UI 全部以 Provider 为唯一语音服务模型，并删除旧 TTS Rule 顶层模型、旧术语和兼容路径。

## 播放页 Provider 选择器

把现有“切换规则”浮窗改为“切换语音服务”。

要求：

- 只显示已配置且当前可见 Provider。
- 按统一 Provider SortOrder。
- 只显示名称，不显示 Type。
- 不提供 None 项。
- 每个选项横向 Stretch、等宽并几乎占满浮窗内容区；浮窗保留合理 Padding。
- 整行可点击。
- CurrentProvider 使用与目录 Current Item 一致的整项选中视觉，不显示“当前”文字。
- CurrentProvider=None 时没有任何项选中。
- 区分：
  - 有可用 Provider 但 CurrentProvider=None → “尚未选择语音服务”；
  - 没有任何可用 Provider → “尚无可用的语音服务”。
- 底部入口改为“前往语音服务管理”。

不要为具体像素/颜色增加永久 WPF 测试。

## 播放生命周期

统一实现：

- 切换 CurrentProvider 不打断当前已经开始播放的语句。
- 保存当前 Provider 新配置不打断当前语句。
- CurrentProvider 变为 None 不打断当前语句。
- 下一条尚未开始的语句读取最新 CurrentProvider + 最新已保存 config。
- 不自动 fallback。
- 若下一条需要合成时 CurrentProvider=None，停止继续生成并进入稳定可理解状态，不错误推进阅读进度。

## Prefetch / Active Cache

Prefetch：

- 后续尚未开始请求跟随最新播放 Provider/config。
- 旧 Provider/config 已完成的音频可以保留，但 fingerprint 不匹配时不得误用。
- 旧 pending prefetch 是否主动取消由现有生命周期和最简实现决定，不建立第二套 background owner。

Active Cache：

- 批次开始时冻结 Provider、typed config、全局 Rate、影响 SpeechText 的必要配置。
- 中途切换 Provider、编辑 Provider、隐藏 Edge 均不混入新配置。
- 新批次才使用新状态。

## Cache Identity

把顶层语义从 `TtsRuleFingerprint` 收敛为 `ProviderSynthesisFingerprint`。

HTTP 可以复用成熟的“有效请求合同”算法，但类型/命名不得继续把 Playback/Cache 暴露给旧 Rule 模型。

要求：

- Provider Name / SortOrder / ProviderId 不进入音频合成 identity。
- HTTP RateLimit 不进入。
- HTTP URL/Method/Header/Body/模板执行合同进入。
- Edge Voice/协议映射合同进入。
- 全局 SpeakSpeed 继续参与 SynthesisProfile。
- 旧物理缓存不因重构主动删除。
- 新 profile 不匹配时旧缓存只是当前不可用。
- 配置相同的两个 HTTP Provider 可以共享缓存。

## Coverage / Export / Decoration

- CurrentProvider/config 变化通过 Speech 自身 typed semantic change 表达。
- Cache-owned integration 决定 Coverage invalidation，不让 Speech 直接调用 Cache API。
- Export 继续只导出当前 Provider + 当前 Rate + 当前文本配置下完整且可验证的缓存。
- 所有旧 `SelectedRuleUnavailable` 等用户可见语义改成 Provider/语音服务术语。

## 旧体系清理

完成全仓扫描并删除/重命名：

- `HttpTtsRule` 作为顶层业务对象；
- `SelectedTtsRuleId`；
- `TtsRuleSelection*`；
- Legado TTS import/parser/converter；
- 旧规则 enable/disable；
- 用户可见“TTS 规则”“切换规则”“当前规则”等已经失效的顶层术语；
- 仅为旧模型保留的 compatibility wrapper、old/new/v2 forwarding API；
- 与旧细粒度行为绑定且不再保护核心契约的测试。

HTTP Provider 内部仍可正常使用“请求规则”“模板规则”等普通语言，不必机械删除“规则”一词。

更新当前用户文档/README 中与已实现功能直接相关的旧 TTS Rule 描述；长期 docs 已由本阶段规划提前更新，不再复制新的决策文档。

## 自动验收

永久测试聚焦：

- CurrentProvider 切换/删除/隐藏/失去配置后的当前句与下一句语义；
- Active Cache snapshot 冻结；
- Provider fingerprint 缓存复用/失效；
- Playback / Prefetch / Export 通过 Provider Runtime 工作；
- 播放页在 CurrentProvider=None / 无可用 Provider 时保持核心可用状态。

完成后执行完整质量门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

如果完整测试暴露旧 TTS Rule 细节测试失败，按 `docs/08_QUALITY_AND_TESTING.md` 判断其是否仍保护核心契约；不要为了旧测试恢复旧 API。

## 完成要求

- 清理所有临时测试、截图、fixture、脚本；
- 更新 Backlog 完成成果；
- 删除本任务文件；
- 完整自动门禁通过后即完成，不等待人工验收。
