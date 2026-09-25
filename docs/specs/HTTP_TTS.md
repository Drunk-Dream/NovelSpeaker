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
- Provider 管理页只以轻量图标/标识展示哪个 Provider 正在使用，不提供“设为当前”动作。
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

- 切换左侧 Provider 或离开页面时，如有 Dirty Draft，使用保存 / 放弃 / 取消保护。
- 新建 HTTP Provider 先进入未持久化 Draft，保存后才进入 Provider 列表。
- 试听直接使用当前未保存 Draft，不要求先保存。
- 保存只要求本地配置校验通过，不要求网络请求成功，也不自动执行试听。
- 固定试听文本为：`君不见黄河之水天上来，奔流到海不复回。`
- 试听使用当前全局语速，不单独提供试听文本或试听语速设置。
- 试听不改变 CurrentProvider，试听临时音频不写入章节音频缓存。

## 6. Provider 动作

不同 Provider Type 支持不同动作，不要求能力完全一致。

| Provider Type | 新建 | 编辑/配置 | 复制 | 导入 | 导出 | 试听 | 删除 | 排序 |
|---|---|---|---|---|---|---|---|---|
| HTTP | 是 | 是 | 是 | 是 | 是 | 是 | 是 | 是 |
| Microsoft Edge | 否 | 是 | 否 | 否 | 否 | 是 | 否 | 是 |
| Local | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 按实际类型决定 | 是 |

当前只有 HTTP 支持用户创建与导入。不要仅为动作矩阵建立通用 capability framework；类型分派保持简单，直到更多 Provider 类型产生真实压力。

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
- `speakSpeed`

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

NovelSpeaker 使用自有版本化 Provider 交换格式。文件或剪贴板可以一次包含一个或多个 Provider；当前导出动作仍一次只导出一个 Provider，但使用相同 envelope。

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

- 每项独立解析与校验，单项失败不回滚其它有效项；
- 只有名称大小写不敏感地相同、且规范化 typed config 各字段相同时才视为完全相同并跳过。Method、Header 键大小写与顺序等使用确定性规范化；不执行模板或联网判断“等价”；
- 配置相同但名称不同仍新增；同名但配置不同则自动生成唯一名称后新增；
- 不覆盖既有 Provider；
- 不改变 CurrentProvider；
- 新 Provider 按导入顺序追加到排序末尾；
- 未知 Provider Type 或已知但不支持 Import 的类型按单项失败报告。

导出完整保存 HTTP 配置，包括其中可能存在的 API Key、Token、Cookie。导出前明确提示文件可能包含敏感凭据；不建立自动 Secret 分离或自动脱敏导出。

## 12. Microsoft Edge Provider

Microsoft Edge Provider 是实验性内置单实例 Provider。

生命周期：

- 实验功能默认关闭时不创建实例。
- 首次启用实验功能时创建唯一实例，并追加到 Provider 完整排序末尾。
- 初次创建保持未配置状态，不自动选择 Voice。
- 用户必须进入“语音服务”页选择并保存 Voice 后，Edge 才出现在播放页。
- 关闭实验功能只隐藏 Provider，保留实例、Voice 配置和排序位置；如它是 CurrentProvider，则清空 CurrentProvider。
- 重新开启后恢复原配置与排序，但不自动恢复 CurrentProvider。

配置：

- 名称固定为 Microsoft Edge；
- Voice 使用可搜索列表，展示友好名称、语言/地区，并保留 Voice ID 作为次要信息；
- 搜索覆盖名称、Voice ID 和语言/地区；
- 暂不提供独立性别/地区筛选器；
- 不在第一版加入 Pitch、Style、Role 等高级参数；
- 全局 Rate 由 Provider 映射到 Edge 请求；
- Volume 仍属于本地播放器。

Voice 列表允许短期本地缓存和手动刷新。已保存 Voice 暂时无法刷新或服务端不可达时保留原值，不自动改成其它 Voice。

Edge transport 位于 Infrastructure，并与 HTTP Provider 共用上层 Provider Runtime 合同。不得要求外部 Node/Python 进程、本地代理服务或另一套常驻服务才能工作。由于 Edge 接口属于实验性外部能力，具体协议实现必须被 transport 边界隔离，便于未来替换。首次实现时须用真实服务成功合成并验证一次可解码音频；持续 CI 使用隔离 transport 测试，不依赖在线 Edge 服务。

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

### 语音服务管理页

- Settings 入口名称使用“语音服务”。
- 桌面端保持双栏布局，不改成层层跳转的配置子页。
- 左侧 Provider 列表不按类型分组，按统一 SortOrder 展示。
- 左侧选中表示“正在编辑”；CurrentProvider 使用独立轻量状态图标/标识，不使用“当前”文字，也不能点击该标识切换。
- HTTP 支持复制、单条导出、删除；Edge 不显示无意义动作。
- 页首提供新建与导入；当前只有 HTTP 可创建时，不显示多余类型选择器。
- HTTP 模板帮助只属于 HTTP 编辑器语境。

### 播放页 Provider 选择器

- 只显示已配置、当前可见的 Provider；
- 只显示 Provider 名称，不显示 Type 副标题；
- 使用统一 Provider SortOrder；
- 不显示 None 选项；
- 每个可选项等宽并横向占满浮窗内容区，整行可点击；
- 浮窗自身保留合理内边距；
- 当前 Provider 使用与目录 Current Item 一致的整项选择视觉，不显示“当前”文字；
- CurrentProvider=None 时没有任何选中项；
- 底部保留“前往语音服务管理”导航动作。

### 拖拽排序

所有支持拖拽排序的列表统一使用**插入槽位**语义：

- N 个可见 item 对应 N+1 个可见插入槽；
- 指示横线位于两张卡片之间的 gap，不压在卡片上/下边界；
- 相邻卡片的 After/Before 不再形成两个等价目标；
- 列表顶部和底部均存在唯一插入槽；
- 隐藏 item 不产生不可见的额外命中边界；
- 章节规则、正则规则、Provider 等现有排序列表复用同一交互原则。

## 15. 兼容与迁移

Provider 化是开发阶段的模型重构，不长期保留旧 `TtsRule`/Legado compatibility wrapper。

- 旧 NovelSpeaker TTS Rule 导入格式不形成新 Provider 格式兼容承诺。
- Legado HTTP TTS Rule 导入兼容移除。
- `source`、`java.*` 等仅为 Legado 兼容存在的模板 API 移除。
- 现有本地 HTTP TTS 规则通过一次性 migration 逐项转换：能按新请求语义安全表达且通过本地校验的项迁移为 HTTP Provider，依赖已移除模板 API、无法等价转换或数据损坏的项跳过。旧表重名时确定性改名；旧禁用状态不迁移，原本禁用的规则不能自动成为 CurrentProvider。升级后向用户展示一次迁移数量与跳过项原因；不为跳过项保留旧格式运行或恢复接口。迁移完成后删除旧运行路径，不维持双读/双写。
- 已存在音频缓存文件不要求删除；Provider synthesis identity 不匹配时保留但不作为当前配置可用缓存。
- 新 Provider 交换格式从 schemaVersion 1 开始独立演进。
