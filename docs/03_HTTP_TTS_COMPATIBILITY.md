# HTTP TTS 规则与后端兼容规范

## 目标

第一版实现一个“面向 Legado 常见 HTTP TTS 后端”的请求兼容层，并尽量覆盖其常见规则格式子集。

目标不是百分之百复制 Legado，而是支持最常见、最有价值的规则能力，使用户能够继续请求与 Legado 相同的朗读后端服务；若现有规则可直接导入则优先复用，若个别规则格式不完全兼容，也应能通过本地规则编辑或导入规范化后访问同一后端。

实现原则：

- 以后端 HTTP 接口兼容为硬目标，规则 JSON 原样兼容为优先目标但不是阻塞条件。
- 优先兼容 Legado 已有 HTTP TTS 规则的输入格式和请求语义，尽量减少额外改写。
- 直接请求规则中声明的目标 HTTP 接口，不引入本地 Java 桥接服务、额外代理层或自建中转后端。
- `java`、`source` 等兼容能力由应用内部用 C# 和 Jint 提供外观，不依赖 Java 运行时。

第一版承诺的兼容边界：

- GET
- POST JSON
- POST Form
- 自定义 Header 和 Body
- `speakText`
- `speakSpeed`
- `{{ ... }}` 表达式
- 少量白名单兼容辅助函数
- 规则页使用固定试听输入

第一版明确不承诺：

- Cookie
- `loginInfo`
- `jsLib`
- 动态登录 UI
- WebView 登录
- 复杂 `source.get/put` 可变状态语义
- 零改动兼容所有社区规则
- 将认证输入扩展为独立登录流程或额外权限模型

## 建议规则模型

```csharp
public sealed class HttpTtsRule
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public string? ConcurrentRate { get; set; }

    public string? Header { get; set; }

    public string? JsLib { get; set; }

    public long LastUpdateTime { get; set; }

    public bool IsEnabled { get; set; } = true;
}
```

上面的 `HttpTtsRule` 更适合作为导入并转换后的持久化规则模型。
第一版中，Legado 规则只作为导入来源；成功导入后，SQLite 直接保存结构化字段，导出时再生成 NovelSpeaker 自有规则 JSON，而不是长期保留原始导入 JSON 或数据库中的整条规则 JSON。

并非所有导入源字段都必须在第一版执行。第一版导入采用“规范化后保存”的策略：

- NovelSpeaker 自有规则模型只保存当前版本识别和执行所需的字段。
- 导入预览阶段可以提示未支持字段；规则实际保存后只保留已支持的结构化字段。
- 缺少必需字段、字段类型错误、请求模板无法解析或无法转换为内部模型时，不允许导入或保存。
- 不得把无法执行的字段静默保存为可用规则。

运行时应再规范化为内部模型，例如：

```csharp
public sealed record NormalizedHttpTtsRule(
    long RuleId,
    string Name,
    RequestTemplate Template,
    string? DeclaredContentType,
    string? ConcurrentRate);
```

播放链路、缓存键和 HTTP 执行只依赖规范化后的运行时模型，不直接理解导入 JSON 的原始细节。

## 规则上下文

```csharp
public sealed record TtsRuleContext(
    string SpeakText,
    int SpeakSpeed,
    HttpTtsRule Source);
```

第一版至少暴露：

- `speakText`
- `speakSpeed`
- `source`
- 当前时间戳
- 随机数函数

说明：

- `source` 在第一版应作为只读兼容外观对象，不暴露任意可变状态。

## 模板格式

必须支持：

```text
{{speakText}}
{{speakSpeed}}
{{encodeURIComponent(speakText)}}
{{JSON.stringify({ text: speakText })}}
```

实现方式：

1. 扫描 `{{ ... }}`。
2. 将内部内容交给受限 Jint 环境执行。
3. 将执行结果转换为字符串。
4. 替换原模板片段。

必须处理：

- 多个表达式。
- 表达式执行失败。
- 返回 `null`。
- 返回对象。
- 非闭合模板。
- 超长脚本。
- 执行超时或语句数量限制。

## 受限 JavaScript 环境

建议支持：

```javascript
encodeURI(value)
encodeURIComponent(value)
btoa(value)
atob(value)
JSON
Math
Date
```

兼容对象可逐步提供：

```javascript
java.encodeURI(value)
java.encodeURIComponent(value)
java.base64Encode(value)
java.md5Encode(value)
java.sha256Encode(value)
```

第一版禁止：

- 任意 CLR 类型访问。
- `System.IO`。
- 启动进程。
- 反射。
- 网络访问辅助函数。
- 任意文件读取。
- 任意环境变量读取。
- 无限循环。
- 跨规则共享可变全局状态。

第一版建议：

- `java.*` 兼容函数应视为 JavaScript 内辅助对象，而不是外部 Java 服务或进程。
- `source.get(key)` / `source.put(key, value)` 推迟，不在 MVP 承诺范围内。

## 请求规则

规则应能够表达：

实现目标：

- 对常见 Legado 朗读后端保持相同的入参约定，使现有后端服务可直接复用。
- 对常见 Legado 规则尽量保持相同的表达方式；若规则格式与本地实现存在差异，应优先保证能表达同一后端请求。
- 请求拼装、模板求值、Header/Body 合成均在本地应用内完成，不依赖额外适配服务。

### GET

```json
{
  "name": "GET Example",
  "url": "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
  "contentType": "audio/mpeg",
  "concurrentRate": "2/1000"
}
```

### POST JSON

```json
{
  "name": "POST JSON Example",
  "url": "https://example.com/tts,{
    \"method\":\"POST\",
    \"headers\":{\"Content-Type\":\"application/json\"},
    \"body\":\"{\\\"text\\\":\\\"{{speakText}}\\\",\\\"speed\\\":{{speakSpeed}}}\"
  }",
  "contentType": "audio/mpeg"
}
```

由于 URL 与附加配置的具体分隔格式可能存在兼容差异，解析器必须独立封装并添加测试。不要在播放层直接拆字符串。

建议内部规范化为：

```csharp
public sealed record ParsedTtsRequest(
    Uri Url,
    HttpMethod Method,
    IReadOnlyDictionary<string, string> Headers,
    HttpContent? Content,
    int RetryCount);
```

第一版 `requestOptions` 只识别：

- `method`
- `body`

出现其他字段时，导入规范化阶段直接丢弃这些字段；若字段缺失或值非法导致无法构造请求模型，则拒绝导入或保存。

## Header 处理

支持来源：

1. 规则 `Header` 字段。
2. 应用默认 Header。

优先级建议：

```text
规则 Header > 应用默认值
```

敏感 Header 不得记录完整值：

- Authorization
- Api-Key
- X-Api-Key
- Subscription-Key

Token 和其它凭据值不得在普通日志或错误摘要中明文显示。

## 限流

`concurrentRate` 支持：

```text
1000
```

表示两次请求之间至少间隔约 1000 毫秒。

```text
3/1000
```

表示 1000 毫秒内最多 3 次请求。

限流应按规则 ID 分组。

```csharp
public interface ITtsRateLimiter
{
    Task WaitAsync(
        long ruleId,
        string? concurrentRate,
        CancellationToken cancellationToken);
}
```

限流器和 HTTP 重试是不同概念：

- 限流器控制主动请求频率。
- 重试处理失败请求。
- 遇到 429 时还需读取服务端等待信息。

## 响应验证

成功响应至少满足：

- HTTP 状态为 2xx。
- Body 非空。
- 响应不是明显的 JSON 或普通文本错误。
- 若规则声明 `contentType`，响应类型应兼容。
- 音频能够被播放器或解码器打开。

错误响应处理：

- JSON：读取有限长度正文并显示错误。
- Text：读取有限长度正文并显示错误。
- 401/403：标记鉴权失败，不自动连续重试。
- 429：按服务端提示等待。
- 5xx：有限次数重试。
- 空响应：视为生成失败。
- 无法解码：删除缓存并最多重新请求一次。

## 缓存键

建议：

```text
SHA256(
  compatibilityVersion
  + ruleId
  + normalizedRuleUrl
  + speakSpeed
  + normalizedSpeechText
)
```

注意：

- 不把敏感凭据直接写入缓存键字符串或文件名。
- 如果凭据影响音色，应使用不可逆摘要。
- 兼容规则语义改变时增加 `compatibilityVersion`。
- 后续启用正则替换后，`normalizedSpeechText` 指最终处理后的语音文本，而不是原始正文。
- 正则替换不使用规则配置哈希进入缓存键；多个规则组合只要产生相同最终语音文本，就可以复用同一音频缓存。

## 第一版兼容矩阵

| 能力 | MVP |
|---|---|
| GET | 支持 |
| POST JSON | 支持 |
| POST Form | 支持 |
| 自定义 Header | 支持 |
| Cookie | 不支持 |
| LoginInfo | 不支持 |
| `speakText` | 支持 |
| `speakSpeed` | 支持 |
| JavaScript 表达式 | 支持 |
| 基础 `java` 辅助函数 | 支持 |
| `source.get/put` 可变状态 | 不支持 |
| 请求限流 | 支持 |
| 自动重试 | 支持 |
| WebView 登录 | 不支持 |
| WebSocket | 不支持 |
| 完整 `java.ajax` | 不支持 |
| `jsLib` | 推迟 |
| 动态登录 UI | 推迟 |
| DOM 和浏览器对象 | 不支持 |
