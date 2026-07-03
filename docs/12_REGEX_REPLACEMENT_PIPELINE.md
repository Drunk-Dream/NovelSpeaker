# 正则替换管线规划

## 1. 文档定位

本文规划 NovelSpeaker 后续的“正则替换”能力。该能力第一版暂不实现，但会影响后续 UI、播放链路、文本展示和音频缓存键设计。

统一命名为 **正则替换**，不再使用“文本清理规则”作为功能名称。

---

## 2. 目标

正则替换用于在不重新导入书籍的前提下，对已导入小说的正文进行动态处理。

目标：

- 不改写已导入的 `content.txt`。
- 不改写 `Books` 和 `Chapters` 中的原始偏移信息。
- 规则变更后不要求重新导入书籍。
- 支持针对展示文本、语音文本或两者应用不同替换。
- 语音文本变化时，自动形成新的音频缓存键。

非目标：

- 第一版不实现该功能。
- 不替代章节识别规则。
- 不把正文处理结果长期写回数据库。
- 不作为广告规则市场或插件系统。

---

## 3. 在文本链路中的位置

正则替换位于“从书籍中按偏移和长度读取内容”与“消费内容”之间。

```text
Books/<book-id>/content.txt
  ↓
按 Chapter.StartOffset / Length 读取原始章节文本
  ↓
动态段落切分，保留原始 StartOffset / Length
  ↓
对段落文本应用正则替换
  ├─ 生成 DisplayText
  └─ 生成 SpeechText
  ↓
消费内容
  ├─ UI 展示正文
  └─ 构建 TTS 请求和音频缓存键
```

要求：

- `StartOffset` 和 `Length` 始终指向原始规范化正文。
- 正则替换只改变运行时产生的 `DisplayText` 和 `SpeechText`。
- 展示文本改变不应破坏阅读进度恢复；进度恢复仍优先使用原始字符偏移。
- 语音文本改变必须影响音频缓存键。

---

## 4. 规则模型

后续可使用类似模型：

```csharp
public enum RegexReplacementScope
{
    Display,
    Speech,
    Both
}

public sealed record RegexReplacementRule(
    Guid Id,
    string Name,
    bool IsEnabled,
    int SortOrder,
    string Pattern,
    string Replacement,
    RegexReplacementScope Scope,
    RegexOptions Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

字段说明：

- `Id`：稳定规则标识。
- `Name`：用户可见名称。
- `IsEnabled`：是否参与执行。
- `SortOrder`：执行顺序。
- `Pattern`：正则表达式。
- `Replacement`：替换文本。
- `Scope`：作用范围，支持展示、语音、两者。
- `Options`：正则选项，例如忽略大小写、多行模式等。

---

## 5. 执行规则

对每个段落原始文本，分别生成展示文本和语音文本。

```text
rawSegmentText
  ↓
应用 Scope = Display 或 Both 的启用规则，按 SortOrder 排序
  ↓
DisplayText

rawSegmentText
  ↓
应用 Scope = Speech 或 Both 的启用规则，按 SortOrder 排序
  ↓
SpeechText
```

要求：

- 多条规则按稳定顺序执行。
- 禁用规则不参与执行。
- 展示范围规则不影响语音文本。
- 语音范围规则不影响展示文本，除非作用范围为 Both。
- 正则异常必须被捕获，并在 UI 中标记该规则无效，不能导致播放链路崩溃。
- 必须设置正则执行超时，避免灾难性回溯阻塞 UI 或播放线程。

---

## 6. 音频缓存键策略

正则替换可能由多条规则组合产生最终语音文本。缓存键不使用规则配置哈希，而使用最终处理后的语音文本哈希。

缓存键中的文本部分应使用：

```text
normalizedProcessedSpeechTextHash = SHA256(Normalize(SpeechTextAfterRegexReplacement))
```

音频缓存键应至少包含：

```text
SHA256(
  compatibilityVersion
  + ruleId
  + normalizedRuleUrl
  + voiceRelatedLoginInfoHash
  + speakSpeed
  + normalizedProcessedSpeechTextHash
)
```

规则：

- 多个正则规则组合、顺序或内容变化后，只要最终 `SpeechText` 发生变化，就会产生新的文本哈希和新的音频缓存键。
- 多个不同规则组合如果产生完全相同的最终 `SpeechText`，可以复用同一音频缓存。
- 只影响展示文本的规则不改变音频缓存键。
- 旧缓存不立即删除，交给 LRU 清理。
- 不把正则规则原文、用户凭据或敏感信息写入缓存文件名。

这样可以保证缓存键与真正提交给 TTS 后端的语音文本一致，避免每次调整无关展示规则都导致音频缓存失效。

---

## 7. UI 入口

正则替换入口位于：

```text
设置 → 导入与文本 → 正则替换
```

该入口为后续规划，第一版不实现。

长期页面能力：

- 新建规则。
- 编辑规则。
- 启用/禁用。
- 排序。
- 删除。
- 选择作用范围：展示、语音、两者。
- 对示例文本预览替换前后结果。
- 显示正则错误和超时风险。

---

## 8. 与章节规则的边界

章节规则只负责识别章节标题和章节范围。

正则替换负责已导入文本在展示或语音消费前的动态替换。

二者不能混合：

- 不用章节规则去做广告过滤、符号替换或朗读规范化。
- 不用正则替换去重新划分章节。
- 修改正则替换规则不触发章节重建。
- 修改章节规则或重新解析章节才会影响章节边界。

---

## 9. 测试要求

后续实现时至少覆盖：

- 单条规则替换展示文本。
- 单条规则替换语音文本。
- Scope = Both 同时影响展示和语音。
- 多条规则按顺序组合。
- 禁用规则不生效。
- 正则表达式错误不会导致页面或播放崩溃。
- 正则执行超时被中断并提示。
- 语音文本改变后生成新的缓存键。
- 展示文本改变但语音文本不变时，不生成新的音频缓存键。
- 不同规则组合产生相同语音文本时，可复用缓存。
