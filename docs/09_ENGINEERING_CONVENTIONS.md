# 工程约定

## 代码风格

- 启用 Nullable Reference Types。
- 启用隐式 using 或统一显式 using，保持全仓库一致。
- 公共类型和接口使用 XML 注释，重点说明职责和边界。
- 方法尽量只承担一个明确职责。
- 不使用 `async void`，事件处理器除外。
- 异步方法以 `Async` 结尾。
- 所有可取消 I/O 接收 `CancellationToken`。
- 不吞掉异常。
- 不使用异常控制正常分支。

## 命名

- `Rule`：用户导入或编辑的规则。
- `ParsedRequest`：规则计算后的 HTTP 请求。
- `SpeechSegment`：章节中的朗读单元。
- `AudioCacheEntry`：已持久化音频。
- `PlaybackSession`：一次有独立取消和版本的播放过程。
- `PlaybackSnapshot`：给 UI 的只读状态。

避免使用含糊名称：

- Manager
- Helper
- Utils
- Processor

除非职责确实无法更具体描述。

## ViewModel 约定

ViewModel 可以：

- 暴露可绑定状态。
- 调用应用层服务。
- 转换用户命令。
- 进行轻量输入校验。

ViewModel 不可以：

- 执行 SQL。
- 直接使用 HttpClient。
- 直接运行 Jint。
- 直接读写音频文件。
- 自己维护播放状态机。
- 长期持有大量章节正文副本。

## Code-behind 约定

仅用于：

- 纯视图生命周期。
- 焦点。
- 拖放事件桥接。
- 无法合理绑定的视图行为。

业务逻辑必须转发到 ViewModel 或服务。

## 异常模型

建议定义可分类异常或结果类型：

```csharp
public enum TtsErrorKind
{
    Network,
    Timeout,
    Unauthorized,
    RateLimited,
    ServerError,
    InvalidRule,
    ScriptError,
    InvalidResponse,
    AudioDecode,
    Cancelled,
    Unknown
}
```

用户可见消息由应用层映射，不直接把底层堆栈显示给用户。

## 配置和常量

集中管理：

- 最大段落长度。
- 默认预取数量。
- 默认缓存上限。
- HTTP 超时。
- 最大错误正文长度。
- JavaScript 执行限制。
- 最大重试次数。

不要在多个类中散落魔法数字。

## HTTP

- 使用 `IHttpClientFactory` 或长期复用 HttpClient。
- 不为每次请求创建新 HttpClient。
- 规则级 CookieContainer 需要单独 Handler 管理。
- 设置合理超时。
- 使用流式复制到文件，避免无界 `byte[]`。
- 限制错误响应读取长度。
- 正确释放 Response 和 Stream。

## SQLite

- 所有写入使用参数化 SQL。
- 批量导入使用事务。
- 仓储不返回仍依赖活动连接的对象。
- 数据库迁移在应用启动早期执行。
- 大操作不得阻塞 UI 线程。

## 文件系统

- 路径只能来自应用数据目录或用户明确选择的文件。
- 缓存文件名只使用哈希。
- 使用临时文件和原子重命名。
- 删除文件失败不能导致数据库事务永久卡住。
- 不跟随不受信任规则提供的本地路径。

## JavaScript 安全

- 规则脚本默认不可信。
- 禁止 CLR 访问。
- 设置执行时间、语句数或递归限制。
- 每次计算使用隔离环境或严格重置状态。
- 只暴露白名单函数。
- 所有字符串结果设置最大长度。
- 不允许脚本直接发起网络请求。

## 日志和隐私

- 使用结构化日志。
- 对 Header、查询参数和登录信息脱敏。
- 小说文本只记录长度和哈希。
- Debug 构建也不得默认记录原文。
- 崩溃报告不得附带数据库、规则凭据或小说正文。

## Git 提交建议

提交应小而完整，例如：

```text
feat(import): add GB18030 fallback
feat(tts): support POST JSON rules
fix(playback): ignore stale session result
test(cache): cover atomic cache replacement
docs(tts): document compatibility limits
```

不要把无关格式化、重构和功能混在一个提交中。
