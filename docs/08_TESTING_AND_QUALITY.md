# 测试与质量策略

## 测试分层

### 单元测试

重点覆盖：

- 编码检测。
- 章节识别。
- 文本分段。
- 模板替换。
- JavaScript 表达式。
- 请求解析。
- 限流计算。
- 缓存键。
- 播放位置推进。
- 状态机转换。
- 错误分类。
- 敏感信息脱敏。

### 集成测试

重点覆盖：

- SQLite 仓储。
- 数据库迁移。
- 缓存文件和数据库一致性。
- HttpClient 与本地测试服务器。
- Cookie。
- GET、POST JSON、POST Form。
- 429、401、5xx 和超时。
- 原子文件写入。
- 损坏音频恢复。

### UI 测试

第一版可以减少自动 UI 测试，保留：

- ViewModel 单元测试。
- 少量关键手动验收脚本。
- 后续再评估 FlaUI 或 WinAppDriver 类方案。

## HTTP 测试服务器

集成测试应使用本地可控 HTTP Server，提供端点：

```text
GET /audio
POST /audio-json
POST /audio-form
GET /error-json
GET /error-text
GET /unauthorized
GET /rate-limited
GET /server-error
GET /slow
GET /empty
GET /corrupt-audio
```

不要在自动测试中依赖真实第三方 TTS 服务。

## 章节解析测试样例

覆盖：

```text
第一章 开始
第十二章 夜色
第100章 终点
第一卷
序章
楔子
番外一
后记
```

还应覆盖：

- 标题前有空格。
- 标题后有标点。
- 正文中出现“第一章”但不是独立标题。
- 无章节。
- 大量连续空行。
- Windows 和 Unix 换行。
- 超长单行。

## 文本分段测试

覆盖：

- 空自然段。
- 中文句号、问号、感叹号。
- 引号内标点。
- 省略号。
- 英文标点。
- 超过最大长度。
- 只有标点。
- 混合中英文。
- 段落开头和结尾空白。
- `DisplayText` 与 `SpeechText` 不同。

## 规则引擎安全测试

至少验证：

- 脚本无法读取本地文件。
- 脚本无法启动进程。
- 脚本无法访问任意 CLR 类型。
- 无限循环被终止。
- 超大对象返回受到限制。
- 异常不会导致整个应用崩溃。
- 不同规则脚本状态隔离。
- 凭据不会出现在异常文本或日志。

## 播放状态机测试

使用假实现：

- `FakeAudioPlayer`
- `FakeTtsRuleEngine`
- `FakeAudioCache`
- `FakeProgressRepository`

场景：

1. 缓存命中并播放完成。
2. 缓存未命中，生成后播放。
3. 播放时预取下一段。
4. 快速切换章节。
5. 旧请求晚到。
6. 暂停和继续。
7. 停止。
8. 当前段失败后重试。
9. 连续多段失败后暂停。
10. 章节结束后进入下一章。
11. 全书结束。
12. 语速变化创建新会话。

## 性能和稳定性测试

- 大文件导入。
- 数千章节目录。
- 长时间连续播放。
- 缓存目录包含大量文件。
- 应用异常退出后残留 `.tmp`。
- 数据库记录和缓存文件不一致。
- 网络频繁断开和恢复。
- 快速连续点击下一章。

## 日志要求

日志可记录：

- 规则 ID。
- URL 主机名。
- HTTP 状态。
- Content-Type。
- 请求耗时。
- 文本长度。
- 文本摘要哈希。
- 缓存命中。
- 会话 ID。
- 错误类型。

日志不得记录：

- 小说原文。
- 完整 Authorization。
- API Key。
- Cookie。
- 登录信息。
- 完整带敏感查询参数的 URL。
- 完整响应正文。

## 发布前检查

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

另外手动验证：

- 全新用户数据目录。
- 升级已有数据库。
- 导入 TXT。
- 导入规则。
- 连续播放。
- 断网。
- 清理缓存。
- 删除书籍。
- 重启恢复。
