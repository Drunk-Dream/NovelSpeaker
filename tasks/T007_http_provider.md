# T007：将 HTTP TTS Rule 收敛为 HTTP Provider

## 目标

保留现有成熟 HTTP 请求、模板、限流、错误分类和音频验证能力，但把它们正式定义为 HTTP Provider，并删除 Legado/旧 TTS Rule 兼容包袱。

## HTTP Provider 模型

长期字段：

- Name
- UrlTemplate
- Method：GET / POST
- Headers
- BodyTemplate
- 可选 RateLimit：MaxRequests + WindowMilliseconds

删除：

- IsEnabled
- LastUpdateTime
- 单独的 ContentType 声明
- RequestBodyIsJsonStructure
- 其它只为 Legado TTS Rule 兼容存在的元数据

Body 永远保存模板文本。

请求体解释只看真实 `Content-Type` Header：

- `application/json` → 求值后必须是合法 JSON；
- `application/x-www-form-urlencoded` → Form；
- 其它/缺失 → Raw；
- GET + Body → 校验失败。

响应音频格式继续通过响应 Header、文件头和真实解码验证自动识别。

## NovelSpeaker HTTP 模板语言

保留并正式定义：

- `speakText`
- `speakSpeed`
- `encodeURI` / `encodeURIComponent`
- `btoa` / `atob`
- `JSON`
- `Math`
- `Date`
- 现有安全资源限制

删除：

- `source`
- `java.*`
- Legado source adapter/parser/converter
- 仅为旧格式兼容存在的 Cookie/LoginInfo 禁止逻辑

普通 HTTP `Cookie` Header 作为用户配置应允许使用，但日志、诊断、错误摘要必须继续脱敏。

## 请求频率限制

把旧 `ConcurrentRate` 字符串改成结构化：

```text
MaxRequests
WindowMilliseconds
```

UI 语义为“最多 N 次 / M 毫秒”。

保持现有优先级：

```text
Current Playback > Playback Prefetch > Active Cache
```

如果当前 limiter 已经以单 active lease 保证同 Provider 同时只执行一个 HTTP 请求，可继续保持，不增加“可调并发数”产品功能。

## 编辑与试听

- 新建默认名称：`HTTP Provider`，自动保证唯一。
- 默认 Method=GET，其余请求字段为空。
- 新建只形成 Draft，保存成功后才进入列表。
- 配置本地校验通过即可保存，不要求联网或试听成功。
- 试听直接测试未保存 Draft。
- 固定试听文本：`君不见黄河之水天上来，奔流到海不复回。`
- 使用当前全局语速。
- 试听不改变 CurrentProvider，不进入章节 Audio Cache。
- HTTP 模板帮助更新为 NovelSpeaker 自有语法，不再出现 Legado 兼容描述。

## Provider 导入/导出

实现 schemaVersion 1 的 NovelSpeaker Provider envelope。

要求：

- 一个文件/剪贴板支持一个或多个 Provider。
- 当前实际支持 import 的类型只有 HTTP，但 parser 需要逐项读取 `providerType`。
- 只有名称大小写不敏感地相同、且规范化 typed config 各字段相同时才视为完全相同并跳过；规范化包含 Method 与 Header 键的大小写/顺序等确定性表示，不执行模板或联网判断“等价”。
- 配置相同但名称不同仍新增；同名但配置不同自动生成全局唯一名称后新增。
- 单项失败不回滚其它有效项。
- 导入项按文件顺序追加 SortOrder 末尾。
- 导入不改变 CurrentProvider。
- 导出仍是单 Provider，但使用相同 envelope。
- 不导出 ProviderId、SortOrder、CurrentProvider、CreatedAt/UpdatedAt/LastUsedAt。
- 导出完整 HTTP 配置，包括可能存在的凭据。
- 导出前明确提示 API Key、Token、Cookie 等敏感信息风险。
- 不实现 SecretStore、Sensitive Variable、自动凭据识别或自动脱敏导出。
- 不支持旧 NovelSpeaker TTS Rule / Legado Rule 导入兼容。

## 自动验收

永久测试聚焦：

- 代表性的模板求值与 sandbox；
- JSON/Form/Raw Body；
- GET Body 拒绝；
- Header 注入防护与敏感数据脱敏；
- 结构化 RateLimit；
- 多项导入：成功、重复、同名、部分失败；
- 同配置不同名新增，名称大小写差异且配置相同跳过；
- 单 Provider 导出后重新导入的 round-trip；
- Draft 试听使用 Draft + 当前全局语速且不改变 CurrentProvider。

不要恢复已删除的细粒度 ViewModel/XAML 测试。

完成前运行受影响 focused tests、Release build 和 format。

## 完成要求

更新 Backlog、清理临时资产、删除本任务文件，不等待人工验收。
