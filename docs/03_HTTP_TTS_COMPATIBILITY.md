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
- 会话内 Cookie
- 只读 LoginInfo 输入
- `speakText`
- `speakSpeed`
- `{{ ... }}` 表达式
- 少量白名单兼容辅助函数

第一版明确不承诺：

- Cookie 持久化
- `jsLib`
- 动态登录 UI
- WebView 登录
- 复杂 `source.get/put` 可变状态语义
- 零改动兼容所有社区规则

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

    public string? LoginUrl { get; set; }

    public string? LoginUi { get; set; }

    public bool EnabledCookieJar { get; set; }

    public string? LoginCheckJs { get; set; }

    public string? JsLib { get; set; }

    public long LastUpdateTime { get; set; }

    public bool IsEnabled { get; set; } = true;
}
```

上面的 `HttpTtsRule` 更适合作为导入后持久化的规则模型。

并非所有字段都必须在第一版执行。未实现字段必须：

- 保留导入值。
- 在规则详情中标记为“当前版本不支持”。
- 不得静默忽略并假装兼容。

运行时应再规范化为内部模型，例如：

```csharp
public sealed record NormalizedHttpTtsRule(
    long RuleId,
    string Name,
    RequestTemplate Template,
    string? DeclaredContentType,
    string? ConcurrentRate,
    bool EnableSessionCookieJar,
    IReadOnlyList<string> UnsupportedFields);
```

播放链路、缓存键和 HTTP 执行只依赖规范化后的运行时模型，不直接理解导入 JSON 的原始细节。

## 规则上下文

```csharp
public sealed record TtsRuleContext(
    string SpeakText,
    int SpeakSpeed,
    HttpTtsRule Source,
    IReadOnlyDictionary<string, string> LoginInfo);
```

第一版至少暴露：

- `speakText`
- `speakSpeed`
- `source`
- `loginInfo`
- 当前时间戳
- 随机数函数

说明：

- `loginInfo` 是显式输入，只读。
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

```javascript
source.getLoginInfo()
source.getLoginInfoMap()
```

```javascript
cookie.get(url)
cookie.set(url, value)
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

- `source.getLoginInfo()` / `source.getLoginInfoMap()` 若实现，仅作为 `loginInfo` 的只读兼容别名。
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
    TimeSpan Timeout,
    int RetryCount);
```

## Header 处理

支持来源：

1. 规则 `Header` 字段。
2. URL 附加配置中的 `headers`。
3. 应用默认 Header。

优先级建议：

```text
规则请求配置 > 规则 Header > 应用默认值
```

敏感 Header 不得记录完整值：

- Authorization
- Api-Key
- X-Api-Key
- Subscription-Key
- Cookie
- Set-Cookie

## Cookie

第一版建议使用每条规则独立的 CookieContainer。

- 相同规则共享 Cookie。
- 不同规则默认隔离。
- 用户可以清除单条规则 Cookie。
- 第一版仅在应用运行期间保存 Cookie。
- Cookie 不写入普通日志。
- 若未来持久化，必须放在受保护的数据存储中。

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
  + voiceRelatedLoginInfoHash
  + speakSpeed
  + normalizedSpeechText
)
```

注意：

- 不把敏感凭据直接写入缓存键字符串或文件名。
- 如果凭据影响音色，应使用不可逆摘要。
- 兼容规则语义改变时增加 `compatibilityVersion`。

## 第一版兼容矩阵

| 能力 | MVP |
|---|---|
| GET | 支持 |
| POST JSON | 支持 |
| POST Form | 支持 |
| 自定义 Header | 支持 |
| 会话内 Cookie | 支持 |
| Cookie 持久化 | 不支持 |
| LoginInfo 只读输入 | 支持 |
| `speakText` | 支持 |
| `speakSpeed` | 支持 |
| JavaScript 表达式 | 支持 |
| 基础 `java` 辅助函数 | 支持 |
| `source.getLoginInfoMap()` | 可作为只读兼容别名 |
| `source.get/put` 可变状态 | 不支持 |
| 请求限流 | 支持 |
| 自动重试 | 支持 |
| WebView 登录 | 不支持 |
| WebSocket | 不支持 |
| 完整 `java.ajax` | 不支持 |
| `jsLib` | 推迟 |
| 动态登录 UI | 推迟 |
| DOM 和浏览器对象 | 不支持 |
