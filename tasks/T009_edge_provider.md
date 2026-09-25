# T009：实现实验性 Microsoft Edge Provider

## 目标

增加第一个非 HTTP Provider，以验证 Provider Runtime 的真实可扩展性，同时保持实验功能隔离、单实例和最小产品面。

## 实验功能与生命周期

- Edge Provider 由实验功能控制，默认关闭。
- 当前只有这一项实验功能时，优先在现有合适设置页加入轻量“实验性功能”区域，不新增只有一个开关的独立页面。
- 首次启用时创建唯一 Microsoft Edge Provider。
- 固定名称 `Microsoft Edge`，不可重命名。
- 首次创建追加到完整 Provider SortOrder 末尾。
- 初次保持未配置，不自动选择 Voice。
- 未配置 Edge 不出现在播放页 Provider 选择器。
- 选择并保存有效 Voice 后才视为已配置。
- 关闭实验功能：隐藏 Provider，保留 ProviderId、Voice 配置和 SortOrder；若它是 CurrentProvider，则 CurrentProvider=None。
- 重新开启：恢复原实例、配置和排序，不自动恢复 CurrentProvider。
- Edge 允许手动排序，但不能创建、删除、复制、导入或导出。

## Edge Editor

右侧 Editor 与 HTTP 完全独立。

第一版只包含必要能力：

- 固定只读名称；
- Voice 搜索/选择；
- 手动刷新 Voice 列表；
- 试听 / 取消 / 保存。

Voice 列表：

- 展示友好名称；
- 展示语言/地区；
- Voice ID 作为次要信息；
- 搜索覆盖名称、语言/地区、Voice ID；
- 不增加独立性别/地区筛选；
- 不加入 Pitch、Style、Role；
- 允许短期本地缓存；
- 刷新失败或已保存 Voice 暂时不在新列表中时保留原值，不自动换 Voice。

保存只依赖本地配置合法，不要求当前服务在线。

试听使用统一固定文本和当前全局语速。

## Runtime / Infrastructure

- Edge 只通过 Provider Runtime 暴露给上层。
- transport 放在 Infrastructure。
- 实现前先验证当前 Edge Read Aloud 接口所需协议和依赖，不把未经验证的第三方示例直接固化为架构。
- 本任务完成前，必须用实际 Edge transport 对真实服务至少成功合成一次，并确认结果可解码；记录脱敏的验证环境、结果和日期作为任务完成证据，不保存文本、完整请求或音频为长期测试资产。仅有 fake/in-memory transport 测试不足以宣称 Edge Provider 可用。
- 若环境或协议变化使真实在线合成无法验证，T009 保持未完成并记录阻塞原因；不得仅凭模拟 transport 测试将 T009/T010 标记完成。
- 实际运行不得要求用户另启 Node/Python 子进程、本地 HTTP 代理或常驻 companion service。
- 如果成熟、小型、许可证合适且维护状态良好的 .NET 依赖明显优于自实现协议适配，可以采用；否则用隔离的小型 transport 实现。
- 不允许 Edge 具体 WebSocket/协议 DTO 泄露到 Application 公共合同。
- 全局 SpeakSpeed 映射到 Edge rate。
- Playback Volume 不发送给 Edge。
- `ProviderSynthesisFingerprint` 至少包含 VoiceId 与 Edge 合成协议/映射合同版本。
- Edge 外部协议失败应映射为现有稳定 Speech 错误语义，不让实验 Provider 破坏 Playback 状态机。

## 自动验收

长期测试只保护：

- 实验开关首次创建、隐藏、恢复；
- CurrentProvider 清空但不自动恢复；
- 未配置 Edge 不可用于播放，保存 Voice 后可用；
- Edge Runtime 使用 fake/in-memory transport 验证核心适配，不访问真实外部服务；
- fingerprint 随 Voice 变化；
- 全局 Rate 被正确传递/映射。

真实 Edge UI 试听仍是可选人工验收；上述真实 transport 在线合成是开发阶段的一次性自动/运行验证，不作为持续 CI 中依赖外部服务的测试。

运行 focused tests、Release build、format。

## 完成要求

更新 Backlog、清理临时资产、删除本任务文件，不等待人工验收。
