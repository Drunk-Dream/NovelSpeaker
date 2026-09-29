# T009：实现实验性 Microsoft Edge Provider 与统一语速合同

## 目标

增加第一个非 HTTP Provider，以真实验证 Provider Runtime 的可扩展性；同时建立通用“实验性功能”入口，并把 NovelSpeaker 公共语速合同从旧 `1–20` 直接切换为 `0–100`。

本任务必须以当前 `https://github.com/Drunk-Dream/ms-ra-forwarder` 的 Edge Read Aloud 实现作为协议行为的主要参考，不能主观猜测 Edge endpoint、Token/GEC、WebSocket 消息、Voice 元数据或 SSML 行为；但不得照搬其 Web/转发器产品架构。

## 1. 通用实验性功能

建立小而通用的实验功能配置边界，而不是 Edge 专属布尔设置。

- Settings store 保存稳定的已启用 FeatureId 集合；第一项为 `microsoft-edge-tts`。
- 默认没有启用项；未知 FeatureId 不阻止应用启动，当前版本只展示已注册功能。
- 通用边界只负责查询、启用/停用、持久化和状态变化通知；每个实验功能自行处理启停副作用。
- 不建立总开关、依赖图、rollout、capability framework 等当前没有真实需求的抽象。
- “实验性”只是产品发布状态，不进入 Provider Domain 模型，不增加 `IsExperimental` 字段。

Settings 首页在“应用”分组增加“实验性功能”入口，位于“外观”之后、“诊断与关于”之前。进入独立次级页面“实验性功能”，每项独立启用/停用。首页入口不显示数量或状态徽标。

Microsoft Edge 语音服务是第一项实验功能。

## 2. Edge Provider 生命周期

- Edge Provider 内置、单实例，名称固定为 `Microsoft Edge`，不可重命名。
- 首次启用 `microsoft-edge-tts` 时创建唯一实例并追加到完整 Provider SortOrder 末尾。
- 初次保持未配置，不自动选择 Voice。
- 未保存有效 Voice 前不出现在播放页 Provider 选择器。
- 关闭实验功能只隐藏 Provider，保留 ProviderId、Voice 配置和 SortOrder；若它是 CurrentProvider，则 CurrentProvider=None，不自动 fallback。
- 重新开启恢复原实例、配置和排序，不自动恢复 CurrentProvider。
- Edge 允许参与统一排序，但不能创建、删除、复制、导入或导出。
- 播放页 Provider 选择器只显示 `Microsoft Edge`，不显示“实验性”标识。
- 网络是否可达、最近一次试听是否成功不参与“已配置”判断，不持久化 Healthy/Offline/LastSuccessfulAt 等健康状态。

## 3. 统一 SpeakSpeed：0–100

把公共语速合同一次性改为：

- 合法范围 `0–100`；
- 新默认值 `50`；
- `0` 是合法语速，不能再表示未设置；
- 需要表达“无语速/未初始化”的地方改用显式 nullable/state，不使用其它魔法数字；
- 播放页保留现有数字输入与 `+/-` 交互，步进仍为 1，允许直接输入 `0–100`；连续步进保留适当 debounce；
- 切换 Provider 时保留相同的 NovelSpeaker SpeakSpeed 数值；
- 已开始播放的当前语句不因语速变化打断，下一条尚未开始的语句使用新语速；
- SpeakSpeed 继续进入 synthesis profile/cache identity。

Provider 映射：

- Microsoft Edge：`edgeRate = speakSpeed * 2 - 100`，即 `0 → -100%`、`50 → 0%`、`100 → +100%`；
- HTTP Provider 模板中的 `speakSpeed` 直接得到 `0–100`；目标 API 需要其它范围时由模板自行换算。

**不做旧语速迁移：**

- 不修改已有 `settings.json` 中的 `DefaultSpeakSpeed`；
- 不扫描、不识别、不改写 HTTP Provider/旧规则模板中的 speed 表达式；
- 不保留 `1–20` compatibility mode、legacy variable 或双语义；
- 用户自行调整旧设置和模板。

## 4. Edge Editor 与 Voice 选择

右侧 Edge Editor 与 HTTP Editor 独立，复用统一 Draft/Dirty/Save/Cancel/Test 生命周期，不共用字段模型。

第一版只保留：

- 固定只读名称；
- 当前 Voice 摘要；
- “更换 Voice”；
- 手动刷新；
- 试听；
- 保存 / 取消。

Voice 摘要显示：

1. FriendlyName；
2. Locale + Gender；
3. VoiceId（次要信息）。

Gender **展示但不筛选**。

采用“摘要 + 点击后展开搜索列表”的方案：

- 平时只显示当前 Voice 摘要；
- 点击“更换 Voice”后在 Editor 内展开搜索框 + 可滚动列表；
- 不使用 Locale + Voice 两级下拉，不使用传统可编辑 ComboBox；
- 搜索 FriendlyName、Locale、VoiceId；
- 不增加独立 Gender/Locale filter；
- 列表按 Locale、FriendlyName 确定性排序。

初次未配置时不自动选中第一个 Voice。选择 Voice 只修改 Draft；试听可以直接使用未保存 Draft；保存后才更新 Provider。

第一版明确不做：

- Pitch；
- Personality / Style / Role；
- Provider 侧 Volume；
- Sentence/Word Boundary；
- 输出格式 UI。

## 5. Voice Catalog 与配置持久化

Provider 配置保存：

- VoiceId；
- FriendlyName；
- Locale；
- Gender。

其中 VoiceId 是真实合成身份，其余字段是用于离线显示的 metadata snapshot。

完整 Voice Catalog：

- 只做进程内短期缓存，不写数据库、settings.json 或磁盘文件；
- TTL 可采用约 1 小时；
- 应用启动不主动联网，进入 Edge Editor/打开 Voice 选择时按需获取；
- 手动刷新必须绕过缓存；
- 应用重启后 Catalog 丢弃；
- 刷新失败保留旧 Catalog/已保存 Voice，不推断 Voice 失效；
- 成功刷新后若保存的 VoiceId 不在最新 Catalog，仍保留配置并继续视为已配置，只在当前 Editor 摘要区提示“当前 Voice 未出现在最新列表中”；
- 上述提示不传播到左侧 Provider 卡片或播放页；
- Catalog 加载/刷新不能锁死整个 Editor，已有配置仍可试听、保存和取消。

## 6. ms-ra-forwarder 参考边界

实现前必须阅读当前 `Drunk-Dream/ms-ra-forwarder` 中 Edge TTS client/service、SSML、Voice List 等实际代码，并以其**当前真实协议行为**为主要参考，至少核对：

- Voice List endpoint 与返回字段；
- FriendlyName / Name(VoiceId) / Locale / Gender 等映射；
- TrustedClientToken；
- Sec-MS-GEC 生成算法和版本参数；
- WebSocket endpoint；
- Origin / User-Agent 等必要握手信息；
- `speech.config`；
- SSML 请求；
- 文本消息与二进制 audio frame；
- `turn.end` 完成语义。

不要照搬与 NovelSpeaker 内置 Provider 无关的能力：

- Next.js/Web API；
- Bearer Token/forwarder 服务层；
- Node/Python companion process；
- Legado/二维码/历史记录；
- Locale 两级 UI；
- Personality UI；
- 其它转发器产品逻辑。

若参考项目实现本身存在明显不一致，以协议验证和 NovelSpeaker 自身稳定合同为准，不机械复制。

## 7. Edge Infrastructure / Protocol Profile

- 实现 Edge 类型 `IProviderRuntime` 并注册到 `IProviderRuntimeResolver`；上层只能通过统一 Provider Runtime 使用 Edge。
- transport 位于 Infrastructure；协议 DTO 不泄露到 Application 公共合同。
- 优先使用 .NET 自带 HTTP/WebSocket 能力；不得要求用户启动本机 Edge、Node/Python、本地代理或常驻 companion service。
- 把 Voice endpoint、WebSocket endpoint、TrustedClientToken、Sec-MS-GEC-Version、Chromium/Edge 协议版本、Origin、User-Agent 等集中到小型 Edge Read Aloud protocol profile。
- protocol profile 随 NovelSpeaker 版本固定发布；不读取本机 Edge 版本，不在线下载“最新协议配置”。
- 第一版每次 synthesis 建立独立 WebSocket，不实现连接池或长连接复用。
- 固定输出 `audio-24khz-96kbitrate-mono-mp3`。
- 发送必要 `speech.config` 与 SSML；不启用 sentence/word boundary metadata。
- Playback Volume 不发送给 Edge。
- 每次调用有明确 timeout（默认约 30 秒）与 CancellationToken；取消主动终止操作。
- Edge transport 第一版不自行自动重试，避免和 Playback 恢复策略叠加。
- Edge 外部协议失败映射为现有稳定 Speech 错误语义；不要建立 Edge 专属公共错误体系。

`ProviderSynthesisFingerprint` 至少包含：

- VoiceId；
- Edge synthesis contract version。

其中 contract version 覆盖会改变音频语义的映射/SSML/输出格式合同。仅 endpoint、Token、UA、Chromium protocol profile 等连接层兼容修正本身不应无意义地让全部音频缓存失效。全局 SpeakSpeed 继续由公共 synthesis profile 组合。

## 8. 实际在线验证

T009 完成前必须使用**生产代码路径**对真实 Edge 服务至少成功完成一次：

```text
获取 Voice List
→ 选择真实 Voice
→ 建立 WebSocket
→ 合成固定试听文本
→ 收到完整 MP3
→ 使用 NovelSpeaker 现有音频解码路径确认可解码
```

验证完成后只记录脱敏证据：

- 日期；
- Windows/.NET 基本环境；
- Edge protocol profile/contract version；
- VoiceId；
- Voice List / Synthesis / Decode 成败。

不得把正文、完整 SSML/请求、Token 或生成音频作为长期测试资产。

若当前环境或协议变化导致真实在线合成无法验证，T009 保持未完成并记录阻塞原因；不能只凭 fake transport 宣称完成。

## 9. 错误、日志与诊断

Edge Infrastructure 内部可以为诊断区分 VoiceList、Connection、Protocol、Synthesis、InvalidAudio、Timeout、Network 等阶段，但上层只接收现有稳定错误语义。

日志/诊断不得记录：

- 小说正文；
- 完整 SSML；
- 完整 WebSocket URL/query；
- Trusted token 或其它协议敏感值；
- 音频内容。

可记录 ProviderType、Stage、VoiceId、FailureKind、Elapsed 等有限脱敏信息。

Voice List/合成临时故障不能自动禁用 Provider、清空 Voice、切换 Provider 或修改 CurrentProvider。

## 10. 自动验收

永久测试只保护核心合同，不锁死无意义实现细节。至少覆盖：

- 实验功能首次启用创建、关闭隐藏、重新开启恢复；
- CurrentProvider 在关闭 Edge 时清空但不自动恢复；
- 未选择 Voice 时 Edge 不可用于播放，保存 Voice 后可用；
- Voice Catalog 纯进程缓存与手动 refresh bypass；
- 刷新失败保留配置；成功刷新但 VoiceId 缺失时仍保留已配置状态；
- Edge Runtime 使用 fake/in-memory transport 验证核心适配；
- `SpeakSpeed 0/50/100 → Edge -100/0/+100%`；
- `0` 可作为合法 SpeakSpeed 穿过设置/播放边界，不再被当作 sentinel；
- HTTP Provider 模板上下文接收新的 `0–100` SpeakSpeed，不存在 legacy 语义；
- SSML 对文本进行正确转义；
- Voice DTO → VoiceId/FriendlyName/Locale/Gender 的核心映射；
- WebSocket 核心消息/音频帧/`turn.end` 解析；
- fingerprint 随 VoiceId 或 Edge synthesis contract version 改变；
- 单纯连接层 protocol profile 常量变化不错误改变 synthesis fingerprint。

不要为每个 header、文案、控件层级、像素位置建立永久测试。需要 UI/在线协议辅助验证时可以建立临时测试或脚本，完成后必须删除。

运行 focused tests、Release build、format。真实 Edge 在线验证不进入持续 CI。

## 完成要求

- 同步长期文档与本任务最终实现；
- 更新 Backlog 状态与简短完成成果；
- 清理所有临时测试、脚本、截图、下载的 Voice/音频和其它验证副产物；
- 删除本任务文件；
- 不等待人工验收。
