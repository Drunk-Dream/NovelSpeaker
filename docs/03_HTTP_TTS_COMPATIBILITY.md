# HTTP TTS 兼容规范

## 1. 目标

NovelSpeaker 直接请求用户规则声明的 HTTP TTS 后端，兼容常见 Legado 风格规则的有用子集，但不复制完整 Legado 运行时。

核心原则：

- HTTP 后端请求语义优先于原始 JSON 字段逐字兼容。
- 导入后转换为 NovelSpeaker 自有结构化规则模型。
- 播放、预取、主动缓存和试听共用同一规则编译与执行链路。
- 不引入 Java 桥接服务、本地代理或第二套网络执行器。

## 2. 支持的请求能力

- GET
- POST JSON
- POST Form
- 自定义 Header
- 自定义 Body
- `speakText`
- `speakSpeed`
- `{{ ... }}` 模板表达式
- 受限 JavaScript 与少量兼容辅助函数
- 规则级并发/速率限制
- 请求超时、有限重试和响应类型检查

无法转换为内部安全请求模型的规则在导入、保存或执行前失败，不静默降级成“可用”。

## 3. 模型边界

```text
JSON source
  → Infrastructure typed parser
  → Application import/validation
  → HttpTtsRule
  → normalized/compiled request
  → Infrastructure HTTP transport
```

- JSON DOM、`HttpClient`、`HttpRequestMessage`、Jint Engine 不进入 Application 公共合同。
- 导入只保存当前版本真正识别和执行的结构化字段；无关字段可丢弃。
- 缺少必需字段、类型错误、模板非法或请求无法构造时拒绝导入/保存。
- 导出生成 NovelSpeaker 自有单规则 JSON，不依赖保留原始导入文本，并完整保留 `IsEnabled` 等可移植规则状态。
- 从文件或剪切板导入采用合并语义：完全重复规则跳过，同名但内容不同的规则作为新规则加入，不覆盖现有规则；导入不会自动改变当前播放规则。

## 4. 模板上下文

至少提供：

- `speakText`
- `speakSpeed`
- 只读 `source` 兼容视图
- 当前时间相关安全值
- 随机数函数

常见表达式：

```text
{{speakText}}
{{speakSpeed}}
{{encodeURIComponent(speakText)}}
{{JSON.stringify({ text: speakText })}}
```

解析器必须正确处理多个表达式、非闭合模板、`null`、对象结果、脚本异常、超时和资源限制。

## 5. JavaScript 安全边界

允许的典型能力：

```text
encodeURI / encodeURIComponent
btoa / atob
JSON / Math / Date
java.encodeURI / java.encodeURIComponent
java.base64Encode / java.md5Encode / java.sha256Encode
```

脚本环境不得开放：

- 任意 CLR 类型。
- 文件、进程、反射和环境变量访问。
- 任意附加网络请求。
- 无限制循环、递归、语句或输出。
- 跨规则共享可变全局状态。

对于外部规则中当前运行时没有实现的状态型 API，兼容验证必须给出安全失败；不把它们写入产品功能承诺。

## 6. 请求编译

Application 将规则和文本上下文编译为传输中立请求：

```text
Rule + SpeakText + SpeakSpeed
  → template evaluation
  → method/url/headers/body
  → ParsedTtsRequest
```

URL、method、Header 与 Body 的兼容解析集中在 Speech 模块，播放层不得自行拆字符串。

Header 名和值都必须经过校验，禁止换行注入。应用默认 Header 与规则 Header 的覆盖规则必须稳定并有测试。

### 6.1 TTS 规则指纹

缓存系统不使用稳定 `RuleId` 直接代表音频生成语义。每次保存或使用规则时，从规范化后的执行合同计算版本化 `TtsRuleFingerprint`：

- URL 模板。
- 请求方法。
- 按 Header 名稳定排序的名称和模板值。
- Body 模板及 JSON 结构标记。
- 声明 Content-Type。
- 模板/请求执行合同版本。

规则名称、启用状态、并发/速率限制、最近使用时间和更新时间不进入指纹，因为它们不改变单次音频结果。指纹只保存哈希，不在缓存索引中复制完整 URL、Header、Body 或凭据。

编辑现有规则的请求语义时，即使 `RuleId` 不变，规则指纹和音频合成配置指纹也必须变化，防止错误复用旧缓存。只修改名称或并发限制时不得无理由使音频缓存失效。

## 7. 规则级限流

同一个规则的所有 TTS 请求共享同一个异步 admission/rate limiter：

```text
当前播放请求
    ↓ highest priority
播放预取
    ↓
主动缓存
```

- 不能为不同调用场景创建彼此独立的限流器。
- 等待限流必须异步且可取消，不能使用同步 `Mutex.Wait` 阻塞线程。
- 取消等待不消耗配额。
- 规则切换只影响新请求；已启动主动缓存使用批次快照对应的规则实例。

## 8. HTTP 执行与重试

- 进程级复用 `HttpClient`/handler。
- 每次调用都有明确超时与 `CancellationToken`。
- 网络瞬断、超时和有限 5xx 可按策略重试。
- 服务端显式限流按响应信息和规则 limiter 处理，不做无界重试。
- 非成功状态只生成有限长度、脱敏错误摘要。
- 取消稳定映射为 Cancelled，不记录为 Error。

## 9. 音频响应验证

响应进入缓存前至少完成：

1. HTTP 状态检查。
2. Content-Type/声明类型检查。
3. 有限长度响应防护。
4. 临时落盘。
5. 音频可解码探测。

HTML、JSON 错误页或损坏音频不能作为正常缓存写入。

## 10. 资源所有权

- transport 拥有 response/stream，直到显式转交。
- 临时音频由 TemporaryAudioStore 管理。
- 所有 response、stream、临时文件在成功、失败和取消路径上都必须确定性释放。
- 技术异常只记录脱敏摘要，不把完整 URL、Header、Body、正文或凭据投影到 UI。

## 11. 规则管理 UI 合同

TTS 规则工作台最终采用：

- `AppPageHeader.Actions` 提供新建、从文件导入、从剪切板导入和规则帮助入口，不提供页面级导出。
- 左侧规则卡片采用左右布局：左侧名称和请求摘要，右侧 ToggleSwitch 独立即时控制启用状态。
- 卡片不显示“当前规则”“设为当前”或“更多”按钮；右键菜单提供“导出到文件”“复制到剪切板”和删除等单规则操作，右键不切换当前编辑对象。
- 右侧：规则字段、试听、取消、保存。
- 页面进入时不自动选中或打开规则；单击卡片后才打开右侧编辑器，导入完成后也不自动打开导入项。
- 启用状态不属于编辑草稿；切换 ToggleSwitch 立即持久化，不触发 Dirty State，也不能在随后保存草稿时被旧值覆盖。
- 右侧编辑器打开后“取消”始终可用并直接丢弃草稿、关闭编辑器；“保存”只在草稿修改且校验通过时可用。切换规则或离开页面时继续复用统一未保存保护。
- TTS 规则页不负责当前规则选择。播放页只显示已启用规则；当前规则被禁用或删除时清空选择且不自动回退，导入、新建或重新启用规则也不自动成为当前规则。

规则兼容行为改变时必须增加 parser/compiler/execution fixture，不以真实第三方服务作为自动测试前提。
