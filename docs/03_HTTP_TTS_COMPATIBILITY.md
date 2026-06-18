# HTTP TTS 规则兼容规范

## 目标

第一版实现一个“Legado 风格”的 HTTP TTS 规则引擎。

目标不是百分之百复制 Legado，而是支持最常见、最有价值的规则能力，使用户能够导入现有规则或以类似格式创建新规则。

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

并非所有字段都必须在第一版执行。未实现字段必须：

- 保留导入值。
- 在规则详情中标记为“当前版本不支持”。
- 不得静默忽略并假装兼容。

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
source.get(key)
source.put(key, value)
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

## 请求规则

规则应能够表达：

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
- Cookie 不写入普通日志。
- 若持久化，必须放在受保护的数据存储中。

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
| Cookie | 支持 |
| `speakText` | 支持 |
| `speakSpeed` | 支持 |
| JavaScript 表达式 | 支持 |
| 基础 `java` 辅助函数 | 支持 |
| `source.getLoginInfoMap()` | 支持 |
| 请求限流 | 支持 |
| 自动重试 | 支持 |
| WebView 登录 | 不支持 |
| WebSocket | 不支持 |
| 完整 `java.ajax` | 不支持 |
| 任意外部 JS 库 | 推迟 |
| 动态登录 UI | 推迟 |
| DOM 和浏览器对象 | 不支持 |
