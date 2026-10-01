# Speech Provider 与 HTTP TTS 规范

## 1. 目标

NovelSpeaker 使用统一的 **Speech Provider** 模型承载不同语音服务。Provider 是播放、预取、主动缓存和试听共同使用的稳定语音生成边界。

当前类型：

- **HTTP Provider**：用户可创建多个实例，使用 NovelSpeaker 自有 HTTP 模板语言描述请求。
- **Microsoft Edge Provider**：实验性、内置、单实例 Provider。
- **Local Provider**：只保留未来扩展空间，不预先定义配置结构、能力矩阵或运行模型。

HTTP、Microsoft Edge 和未来 Local Provider 在管理层级上等价，但各自拥有独立 typed config、Editor 和 Runtime。不要建立通用插件平台、万能 Provider 配置 Schema 或提前设计的 capability framework。

## 2. Provider 身份、名称与排序

Provider Type 与 Provider Instance 明确分离。

每个 Provider Instance 至少具有：

- 稳定 ProviderId；
- ProviderType；
- 全局唯一、大小写不敏感的显示名称；
- 持久化 SortOrder；
- 类型专属配置；
- 必要的创建/更新时间等内部元数据。

规则：

- Provider 真正身份是 ProviderId，名称只用于展示。
- Microsoft Edge 名称固定为“Microsoft Edge”，不可重命名；该名称作为内置保留名称。
- 所有 Provider 类型共用一份完整排序。
- 管理页和播放页读取同一排序，不维护第二套顺序。
- 隐藏 Provider 保留在完整排序中；隐藏本身不修改顺序。
- 用户对可见 Provider 重排时，修改的是完整排序中这些项的真实相对位置。
- 新导入 Provider 按导入顺序追加到完整列表末尾。
- 复制 Provider 得到新 ProviderId，并紧跟源 Provider 插入；名称自动生成唯一副本名。

## 3. Provider 可用性与 CurrentProvider

Provider 是否可用于播放，只由**是否完成必要配置**决定，不由网络是否临时可达、最近一次试听是否成功决定。

- HTTP Provider 只有在本地配置校验通过后才能保存，因此保存后的实例视为已配置。
- Microsoft Edge 只有保存了有效 Voice 后才视为已配置。
- 未配置或被实验功能隐藏的 Provider 仍可存在于管理数据中，但不出现在播放页 Provider 选择器。
- CurrentProvider 是全局设置，值为 ProviderId 或 None。
- 播放页是唯一可以主动切换 CurrentProvider 的入口。
- Provider 管理页只以全局 Current 视觉语义展示哪个 Provider 正在使用，不提供“设为当前”动作。
- 播放页不提供显式“None/不使用语音服务”选项。
- 删除当前 Provider、隐藏当前 Edge Provider、或把当前 Provider 保存为未配置状态时，CurrentProvider 清空为 None，不自动回退。
- Provider 重新出现或恢复配置后，不自动恢复为 CurrentProvider。

## 4. 播放与后台生命周期

CurrentProvider 或 Provider 配置发生变化时：

- 当前已经开始播放的语句/段落不被打断；
- 从下一条尚未开始的语句/段落起使用最新 CurrentProvider 与最新已保存配置；
- CurrentProvider 变为 None 时，当前语句正常结束，之后停止继续合成；
- 不建立自动 fallback 链。

Playback Prefetch 属于当前播放会话。Provider 或有效合成配置改变后，新的预取使用新配置；旧配置已经生成的音频可继续保留在物理缓存，但不能因身份错误而被命中。

Active Cache 在批次创建时冻结 Provider Instance、Provider 配置、全局语速以及其它影响合成的必要快照。运行中的批次不因用户之后切换 Provider 或编辑配置而混入新配置。

## 5. Provider 编辑生命周期

“语音服务”页面采用桌面双栏工作台：

```text
Provider 列表 | 当前 Provider 类型对应的编辑器
```

所有支持编辑的 Provider 共用生命周期语义，而不是共用配置字段：

```text
选择 Provider
→ 建立 Draft
→ 修改
→ Dirty
→ 试听 Draft / 保存 / 取消
```

- 普通点击左侧 Provider 选择单项并切换右侧 Editor；Ctrl/Shift 点击只改变批量选择集，不切换右侧编辑对象。
- 切换左侧 Provider 或离开页面时，如有 Dirty Draft，使用保存 / 放弃 / 取消保护。
- 新建 HTTP Provider 先进入未持久化 Draft，保存后才进入 Provider 列表。
- 试听直接使用当前未保存 Draft，不要求先保存。
- 保存只要求本地配置校验通过，不要求网络请求成功，也不自动执行试听。
- 固定试听文本为：`君不见黄河之水天上来，奔流到海不复回。`
- 试听使用当前全局语速，不单独提供试听文本或试听语速设置。
- 试听不改变 CurrentProvider，试听临时音频不写入章节音频缓存。

### 统一语速合同

NovelSpeaker 的公共 `SpeakSpeed` 是 Provider 无关的整数控制量：

- 合法范围固定为 `0–100`，默认值为 `50`；
- `0` 是合法语速，不能再承担“未设置/无值”的 sentinel 语义；需要表达缺失时使用显式 nullable/state；
- 切换 Provider 时保持同一个 `SpeakSpeed` 数值，不按 Provider 改写全局状态；
- Provider 自己负责把 `0–100` 映射到真实协议；
- Microsoft Edge 使用线性映射 `edgeRate = speakSpeed * 2 - 100`，即 `0 → -100%`、`50 → 0%`、`100 → +100%`；
- HTTP Provider 模板中的 `speakSpeed` 直接暴露 NovelSpeaker 的 `0–100` 值；目标 HTTP API 需要其它范围时由模板表达式自行换算，不在公共 Playback/Speech 层增加服务特例；
- 播放页保留数字语速交互，`+/-` 每次步进 1，并允许直接输入 `0–100` 整数；连续步进可以 debounce 后只提交最终值；
- 已经开始播放的当前语句不因语速改变而打断；下一条尚未开始的语句使用新语速；
- `SpeakSpeed` 继续属于 synthesis profile 身份，因此不同语速不会错误命中同一缓存，切回旧语速时可以重新命中旧缓存。

## 6. Provider 动作

不同 Provider Type 支持不同动作，不要求能力完全一致。

| Provider Type | 新建 | 编辑/配置 | 复制 | 导入 | 导出 | 试听 | 删除 | 排序 |
|---|---|---|---|---|---|---|---|---|
| HTTP | 是 | 是 | 是 | 是 | 是 | 是 | 是 | 是 |
| Microsoft Edge | 否 | 是 | 否 | 否 | 否 | 是 | 否 | 是 |
| Local | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 是 |

当前只有 HTTP 支持用户创建与导入/导出。不要仅为动作矩阵建立通用 capability framework；类型分派保持简单，直到更多 Provider 类型产生真实压力。

## 7. HTTP Provider 配置

HTTP Provider 第一版持久配置只包含真正有意义的请求字段：

- Name；
- URL Template；
- Method：GET / POST；
- Headers；
- Body Template；
- 可选请求频率限制：MaxRequests + WindowMilliseconds。

删除旧 TTS Rule 中的：

- IsEnabled；
- LastUpdateTime；
- 单独的 ContentType 声明；
- RequestBodyIsJsonStructure；
- Legado 兼容元数据。

请求 Body 永远保存为模板文本。请求语义只由真实 HTTP Header 中的 Content-Type 决定：

- `application/json`：模板求值结果必须是合法 JSON；
- `application/x-www-form-urlencoded`：按 Form 发送；
- 其它或未声明：按 Raw Body 发送；
- GET 不允许 Body。

响应音频格式由响应 Header、文件头和实际解码验证共同识别，不要求用户另填“响应 Content-Type”。

## 8. NovelSpeaker HTTP Provider 模板语言

HTTP Provider 使用 NovelSpeaker 自有模板语言，不承诺 Legado TTS Rule 格式或运行时兼容。

核心上下文：

- `speakText`
- `speakSpeed`：NovelSpeaker 标准化 `0–100` 整数

允许的典型受限 JavaScript 能力：

- `encodeURI` / `encodeURIComponent`
- `btoa` / `atob`
- `JSON`
- `Math`
- `Date`

示例：

```text
{{speakText}}
{{speakSpeed}}
{{encodeURIComponent(speakText)}}
{{JSON.stringify({ text: speakText })}}
```

旧兼容环境中的 `source`、`java.*` 等接口移除。

模板环境不得开放：

- 任意 CLR 类型；
- 文件、进程、反射和环境变量访问；
- 模板内额外网络请求；
- 无限制循环、递归、语句或输出；
- 跨 Provider 共享可变全局状态。

Header 可包含 API Key、Token、Cookie 等 Provider 自身需要的值。日志、诊断和错误摘要必须继续脱敏这些敏感数据。

## 9. HTTP 请求频率与执行

请求频率限制使用结构化配置：

```text
MaxRequests = N
WindowMilliseconds = M
```

UI 表达为“最多 N 次 / M 毫秒”。留空表示不增加额外频率限制。

同一 Provider 的 TTS 请求共享 admission/rate limiter：

```text
Current Playback
> Playback Prefetch
> Active Cache
```

当前实现可保持每个 Provider 同时只执行一个真实 HTTP 请求；不向用户暴露可调并发数。

- 等待限流异步且可取消，不用同步 Mutex.Wait 阻塞线程。
- 取消等待不消耗配额。
- 运行中的 Active Cache 继续使用批次冻结的 Provider/config 快照。
- 请求频率限制、Provider 名称、SortOrder、最近使用时间不进入音频合成指纹。

## 10. HTTP 执行与响应验证

- 进程级复用 `HttpClient`/handler。
- 每次调用有明确超时与 `CancellationToken`。
- 网络瞬断、超时和有限 5xx 可以按稳定策略重试。
- 服务端显式限流按响应信息和 Provider limiter 处理，不无界重试。
- 非成功状态只生成有限长度、脱敏错误摘要。
- 取消稳定映射为 Cancelled，不记录为 Error。
- Header 名和值必须校验并禁止换行注入。

响应进入缓存前至少完成：

1. HTTP 状态检查；
2. 文本/JSON 错误响应识别；
3. 有限长度响应防护；
4. 临时落盘；
5. 音频格式探测与真实解码验证。

HTML、JSON 错误页、空响应或损坏音频不能作为正常缓存写入。

## 11. HTTP Provider 导入与导出

NovelSpeaker 使用自有版本化 Provider 交换格式。文件或剪贴板可以一次包含一个或多个 Provider；单项和批量导出共用相同 envelope。

长期形状：

```json
{
  "schemaVersion": 1,
  "providers": [
    {
      "providerType": "http",
      "name": "My TTS",
      "config": {
        "urlTemplate": "https://example.com/tts?text={{encodeURIComponent(speakText)}}",
        "method": "GET",
        "headers": {},
        "bodyTemplate": null,
        "rateLimit": null
      }
    }
  ]
}
```

交换格式不包含：

- ProviderId；
- SortOrder；
- CurrentProvider；
- CreatedAt / UpdatedAt / LastUsedAt；
- 设备级运行状态。

导入规则：

- 一个文档可以包含一条或多条 Provider，逐项独立解析与校验，单项失败不回滚其它有效项；
- 只有名称大小写不敏感地相同、且规范化 typed config 各字段相同时才视为完全相同并跳过。Method、Header 键大小写与顺序等使用确定性规范化；不执行模板或联网判断“等价”；
- 配置相同但名称不同仍新增；同名但配置不同则自动生成唯一名称后新增；
- 不覆盖既有 Provider；
- 不改变 CurrentProvider；
- 新 Provider 按导入顺序追加到排序末尾；
- 未知 Provider Type 或已知但不支持 Import 的类型按单项失败报告。

导出规则：

- 普通单项导出和 Ctrl/Shift 多选后的批量导出使用同一 envelope；
- 批量导出按当前选择集中可分享 Provider 的稳定可见顺序写入一个文件/剪贴板文档；
- Microsoft Edge 等不可分享 Provider 不进入批量导出选择；
- 右键已选中 Provider 时，“导出到文件/剪贴板”作用于当前选择集；
- 导出完整保存 HTTP 配置，包括其中可能存在的 API Key、Token、Cookie。

导出前明确提示文件可能包含敏感凭据；不建立自动 Secret 分离或自动脱敏导出。

私人完整配置备份不复用本节的“分享交换格式”语义；备份可以保存 ProviderId、SortOrder、CurrentProvider 等恢复完整配置所需身份，见 `05_DATA_AND_COMPATIBILITY.md`。

## 12. Microsoft Edge Provider

Microsoft Edge Provider 是实验性内置单实例 Provider。它实现的是 **Edge Read Aloud 协议**，不是驱动本机 Microsoft Edge 浏览器；运行不依赖本机 Edge 的安装、启动或版本。

### 实验功能门控

“实验性功能”是通用产品能力，不为 Edge 建立 `EnableEdgeTts` 一类专属布尔开关。

- Settings store 保存一组稳定的已启用 FeatureId；第一项为 `microsoft-edge-tts`。
- 所有实验功能默认关闭；未知 FeatureId 不应阻止应用启动，UI 只展示当前版本已注册的功能。
- 通用实验功能边界只负责查询、启用/停用、持久化和状态变化通知；具体功能自行负责启停副作用。
- “实验性”只是产品发布状态，不进入 Provider Domain 模型，不加入 `IsExperimental` 字段，也不泄露到 Provider Runtime、Playback 或 Cache。
- 首次启用 Edge 功能时创建唯一 Microsoft Edge Provider，并追加到完整 Provider SortOrder 末尾。
- 初次创建保持未配置，不自动选择 Voice。
- 关闭实验功能只隐藏 Edge Provider，保留 ProviderId、Voice 配置和 SortOrder；若它是 CurrentProvider，则清空 CurrentProvider，不自动 fallback。
- 重新开启恢复原实例、配置和排序，但不自动恢复 CurrentProvider。
- Edge 允许参与统一排序，但不能创建、删除、复制、导入或导出。

### Edge Editor 与 Voice 选择

右侧 Editor 与 HTTP 完全独立，第一版只包含固定只读名称、Voice 配置、试听、保存和取消。

Voice 摘要默认显示：

- FriendlyName；
- Locale + Gender，其中 Gender 只展示，不提供筛选；
- VoiceId 作为次要信息。

没有选择 Voice 时明确显示未配置状态。点击“更换 Voice”后，在 Editor 内展开 **搜索框 + 可滚动 Voice 列表**，不使用 Locale + Voice 两级下拉，也不使用传统可编辑 ComboBox。搜索覆盖 FriendlyName、Locale 和 VoiceId，不增加独立地区/性别筛选。排序保持确定性，优先按 Locale，再按 FriendlyName。

第一版不提供：

- Pitch；
- Personality / Style / Role；
- 合成音量；
- Sentence/Word Boundary；
- 输出格式选择。

选择新 Voice 只修改 Draft；试听直接使用 Draft，保存后才写入 Provider。试听使用统一固定文本和当前全局语速。

Edge 配置持久化 VoiceId，并保存 FriendlyName、Locale、Gender 作为显示快照。VoiceId 是真实合成身份；显示快照可以在成功刷新 Catalog 且 VoiceId 仍存在时更新。

### Voice Catalog

- 完整 Voice Catalog 只做进程内短期缓存，不写数据库、settings.json 或独立磁盘文件；
- 默认 TTL 可采用约 1 小时；手动“刷新”必须绕过缓存；
- 应用启动不主动联网，进入 Edge Editor/展开 Voice 选择器时按需获取；
- 应用重启后 Catalog 丢弃并在下次需要时重新获取；
- 刷新失败不清空已保存 Voice，也不能推断 Voice 已失效；
- 成功刷新后若已保存 VoiceId 不在最新 Catalog，只在当前 Editor 摘要区域轻量提示“当前 Voice 未出现在最新列表中”，Provider 仍视为已配置、仍允许播放/试听/保存，不自动换 Voice；
- 不在 Provider 左侧卡片或播放页 Provider 选择器传播该警告；
- Catalog 加载时不锁死整个 Editor，已有配置仍可试听、保存或取消。

### 协议实现边界

Edge 协议行为必须以当前 `https://github.com/Drunk-Dream/ms-ra-forwarder` 的实际实现作为**主要参考**，重点核对其 Edge TTS client/service/SSML/voice-list 相关代码。不得凭印象猜测 endpoint、TrustedClientToken、Sec-MS-GEC 算法与版本、Origin/User-Agent、WebSocket 握手、`speech.config`、SSML 消息和二进制音频帧解析。

参考的是经过实际使用验证的**协议行为**，不是其 Next.js/Web 服务产品架构。不得照搬：

- Web API / Bearer Token 服务层；
- Node/Next.js 进程模型；
- Legado 导入、二维码、历史记录等转发器功能；
- Locale 两级选择 UI；
- Personality UI；
- 其它与 NovelSpeaker 内置 Provider 无关的能力。

协议实现放在 Infrastructure，并集中到一个小型、可替换的 Edge Read Aloud protocol profile/transport 边界：

- 使用 .NET 自带网络/WebSocket 能力优先，不要求 Node/Python、本地 HTTP 代理或 companion service；
- Voice endpoint、WebSocket endpoint、TrustedClientToken、Sec-MS-GEC-Version、Chromium/Edge 协议版本、Origin、User-Agent 等集中管理，不散落到 Runtime/UI；
- 随 NovelSpeaker 版本发布一份已验证 protocol profile，不读取本机 Edge 版本，也不从远端动态下载协议配置；
- 若协议变化，只替换该 profile/transport，不让协议 DTO 泄露到 Application 公共合同；
- 第一版每次 synthesis 新建独立 WebSocket，不建立连接池/长连接复用；
- 固定输出 `audio-24khz-96kbitrate-mono-mp3`，不根据 SuggestedCodec 动态改变；
- 发送必要 `speech.config` 与 SSML，只处理合成所需的核心文本/二进制帧和 `turn.end`；
- 全局 `SpeakSpeed 0–100` 映射到 Edge `-100%–+100%`；
- Playback Volume 始终由本地播放器处理，不发送给 Edge。

当前固定 profile 为 `edge-readaloud-144-v1`，参考 [ms-ra-forwarder 的协议实现](https://github.com/Drunk-Dream/ms-ra-forwarder/tree/5ab6ce809c2418402c4743b98d74215a74509b00)；Edge synthesis contract version 为 `1`。连接层 profile 与合成合同分别版本化。

每次合成必须有明确超时和 CancellationToken。第一版 Edge transport 不自行自动重试；取消应主动终止当前操作并映射为 Cancelled，而不是 Error。协议内部可以区分 VoiceList、Connection、Protocol、Synthesis、InvalidAudio、Timeout、Network 等诊断阶段，但 Provider Runtime/Playback 只暴露现有稳定 Speech 错误语义，不建立 Edge 专属公共错误状态，也不持久化 Healthy/Offline/LastSuccessfulAt 等 Provider 健康状态。

### 在线验证

Edge transport 可用性须以**生产代码路径**对真实 Edge 服务的成功合成和解码验证为证，不能只依赖 fake/in-memory transport：

```text
获取 Voice List
→ 选择真实 Voice
→ 建立 WebSocket
→ 合成固定试听文本
→ 收到完整 MP3
→ 使用 NovelSpeaker 现有音频解码路径确认可解码
```

只保留脱敏的验证日期、环境、protocol profile 版本、VoiceId 和 VoiceList/Synthesis/Decode 成败作为任务完成证据。不把正文、完整 SSML/请求、Token 或音频保存为长期测试资产。持续 CI 使用隔离 transport/协议解析测试，不依赖外部 Edge 服务。

当前 profile 验证：2026-09-29，Windows `10.0.19045.0` / .NET `10.0.12`；VoiceId 为 `Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)`；生产 Voice List、Runtime 合成和现有音频解码路径均成功。验证音频与临时验证入口已删除。

## 13. Provider Runtime 与缓存身份

Playback、Prefetch、Active Cache、试听和需要补全音频的其它流程只依赖 Provider Runtime，不直接依赖 HTTP/Edge 具体类型。

```text
Provider Instance + typed config + synthesis context
→ Provider Runtime
→ audio result
```

每个 Provider Type 负责产生版本化 `ProviderSynthesisFingerprint`，只包含实际会改变音频生成结果的有效合成配置。

HTTP 至少考虑：

- URL Template；
- Method；
- 规范化并稳定排序的 Headers；
- Body Template；
- 模板/请求执行合同版本。

Microsoft Edge 至少考虑：

- Voice ID；
- Edge 合成协议/映射合同版本；
- 其它未来真正影响音频的 Edge 配置。

全局 Rate 继续由 synthesis profile 组合。

ProviderId、名称、SortOrder、请求频率限制、最近使用时间不进入合成指纹。

结果：

- Provider 重命名不使缓存失效；
- 两个配置完全相同的 HTTP Provider 可以复用同一音频缓存；
- 修改真实合成配置后旧文件保留，但新配置不会错误命中；
- 切换到其它 Provider 后旧缓存保留；
- 切回未改变的原 Provider 时原缓存可以重新命中。

## 14. UI 合同

### 实验性功能入口

Settings 首页在“应用”分组中提供“实验性功能”入口，顺序为：

```text
缓存与数据
外观
实验性功能
诊断与关于
```

入口进入独立次级页面“实验性功能”。页面统一管理当前版本所有实验功能，每项独立启用/停用，不设置总开关；首页入口不显示已启用数量或状态徽标。Microsoft Edge 语音服务是第一项。实验性标识只存在于该页面和语音服务管理语境，播放页 Provider 选择器不显示“实验性”。

### 语音服务管理页

- Settings 入口名称使用“语音服务”。
- 桌面端保持双栏布局，不改成层层跳转的配置子页。
- 左侧 Provider 列表不按类型分组，按统一 SortOrder 展示。
- 左侧普通点击选择表示“正在编辑”，使用全局 Selected surface；CurrentProvider 使用全局 Current 左侧 Accent rail，不显示“当前”文字。
- Current 与 Selected 可以同时存在；正在编辑 CurrentProvider 时表现为 Current rail + Selected surface。
- Ctrl/Shift 点击只修改批量选择集，不切换右侧 Editor；批量导出复用当前选择集。
- HTTP 支持复制、单项/批量导出、删除；Edge 不显示无意义动作，也不进入批量导出选择。
- 页首提供新建与导入；当前只有 HTTP 可创建时，不显示多余类型选择器。
- HTTP 模板帮助只属于 HTTP 编辑器语境。

### 播放页 Provider 选择器

- 只显示已配置、当前可见的 Provider；
- 只显示 Provider 名称，不显示 Type 副标题，也不显示“实验性”；
- 使用统一 Provider SortOrder；
- 不显示 None 选项；
- 每个可选项等宽并横向占满浮窗内容区，整行可点击；
- 行高/内边距和文字大小保证 Provider 名称垂直居中且可快速扫视，长名称省略；
- 当前 Provider 使用全局 Current 左侧 Accent rail，不显示“当前”文字，也不使用 Selected 背景代替 Current；
- CurrentProvider=None 时没有任何 Current 项；
- 底部使用独立“语音服务管理”导航行，与选择列表通过轻量分隔线区分。

### 拖拽排序

所有支持拖拽排序的列表统一使用**插入槽位**语义：

- N 个可见 item 对应 N+1 个可见插入槽；
- 指示横线位于两张卡片之间的 gap，不压在卡片上/下边界；
- 相邻卡片的 After/Before 不再形成两个等价目标；
- 列表顶部和底部均存在唯一插入槽；
- 隐藏 item 不产生不可见的额外命中边界；
- 章节规则、正则规则、Provider、元数据规则等排序列表复用同一交互原则。

## 15. 兼容与迁移

Provider 化是开发阶段的模型重构，不长期保留旧 `TtsRule`/Legado compatibility wrapper。

- 旧 NovelSpeaker TTS Rule 导入格式不形成新 Provider 格式兼容承诺。
- Legado HTTP TTS Rule 导入兼容移除。
- `source`、`java.*` 等仅为 Legado 兼容存在的模板 API 移除。
- 现有本地 HTTP TTS 规则通过一次性 migration 逐项转换：能按新请求语义安全表达且通过本地校验的项迁移为 HTTP Provider，依赖已移除模板 API、无法等价转换或数据损坏的项静默跳过，不展示或持久化跳过数量与原因。旧表重名时确定性改名；旧禁用状态不迁移，原本禁用的规则不能自动成为 CurrentProvider。不为跳过项保留旧格式运行或恢复接口。迁移完成后删除旧运行路径，不维持双读/双写。
- 已存在音频缓存文件不要求删除；Provider synthesis identity 不匹配时保留但不作为当前配置可用缓存。
- Provider 交换格式从 schemaVersion 1 开始独立演进，并继续支持单项/多项同 envelope。
- 本阶段把公共 `SpeakSpeed` 合同直接从旧 `1–20` 切换为 `0–100`，新默认值为 `50`；不为旧范围保留运行时兼容层。
- 不修改已有 `settings.json` 中保存的 `DefaultSpeakSpeed` 数值；旧值若仍位于 `0–100` 合法范围内按新语义直接使用，由用户自行调整。
- 不扫描、不识别、不自动改写已有 HTTP Provider/旧规则中的 `speakSpeed` 模板表达式；模板升级后的语义由用户自行调整。
