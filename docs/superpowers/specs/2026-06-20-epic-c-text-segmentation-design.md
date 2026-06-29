# Epic C 文本分段设计

## 背景

`docs/11_TASK_BACKLOG.md` 中的 Epic C 包含两类能力：

- 章节拆分的补强。
- 章节正文到朗读段落的分段模型。

当前仓库已经完成章节规则与 `IChapterSplitter` 的第一版实现，但尚未具备：

- 无章节回退的统一策略。
- `ITextSegmenter` 及其运行时分段模型。
- `DisplayText` / `SpeechText` 的职责边界。
- 段落拆分相关全局设置。
- 面向中文小说边界情况的分段测试集。

本设计只覆盖 Epic C 的“文本分段”部分，并明确当前阶段继续保持“无有效章节即导入失败”的产品语义，不在本次范围内调整导入链路。

## 目标

本次设计完成后，Epic C 的实现应满足以下目标：

- 新增 `ITextSegmenter`，把单个章节正文字符串动态转换为有序的 `SpeechSegment` 列表。
- 默认保留自然段结构，不对普通长度段落做额外拆分。
- 支持“超长自然段拆分”作为全局可配置能力。
- 支持“拆分阈值”作为全局可配置项。
- 第一版 `SpeechSegment` 同时暴露 `DisplayText` 和 `SpeechText`，但默认内容一致。
- 每个分段保留基于章节正文字符串的字符范围，服务后续正文高亮和进度恢复。
- 为中文小说常见边界情况提供稳定单元测试。

## 非目标

本次设计明确不做：

- 修改当前“无有效章节即失败”的导入策略。
- 把分段结果持久化到 SQLite。
- 为每本书单独配置分段阈值。
- 对 `SpeechText` 做书籍清洗、符号剔除或特殊朗读替换。
- 按逗号、分号等更细粒度标点切段。
- 为引号、书名号、省略号建立复杂语义规则。
- 与播放器、缓存、HTTP TTS、阅读进度恢复做完整集成。

## 已确认约束

- 无章节回退本轮不做，当前导入行为保持不变。
- 是否进行超长段落拆分是全局设置。
- 超长段落拆分阈值是全局设置。
- 默认产品行为支持超长段落拆分。
- 用户可在设置中开启或禁用该能力。
- 当自然段长度大于阈值时，优先按 `。！？` 断句。
- 当缺少可用断句点时，允许硬切作为兜底。
- 第一版 `DisplayText == SpeechText`。
- 字符范围基于章节正文字符串，不基于未来可能变化的 `SpeechText`。

## 方案比较

评估了三种实现路径：

### 方案 A：轻量规则型，推荐

对外提供一个简单的 `ITextSegmenter`，输入章节正文字符串和分段设置，输出 `SpeechSegment[]`。内部按自然段扫描、超长判断、句级拆分和硬切兜底逐步处理。

优点：

- 与当前仓库分层和文档方向一致。
- 不需要修改数据库和导入流程。
- 易于编写精确的 offset 测试。
- 可以在后续平滑扩展 `SpeechText` 清洗。

缺点：

- 需要在运行时重复分段。

### 方案 B：多阶段管线型

把自然段扫描、句级拆分、硬切兜底、结果映射拆成更多公开组件，对外由总编排器调用。

优点：

- 单元测试粒度最细。
- 更适合未来快速扩展复杂规则。

缺点：

- 对当前版本偏重。
- 新增类型更多，初始接线成本更高。

### 方案 C：持久化分段型

导入时预先生成所有 `SpeechSegment` 并落库，播放时直接读取。

优点：

- 运行时读取简单。

缺点：

- 与现有文档“分段动态生成”的方向冲突。
- 设置变更后需要整本书重建。
- 不利于算法迭代。

### 结论

采用方案 A，并在内部实现时借鉴方案 B 的分步思路：对外保持 `ITextSegmenter` 简洁，对内按自然段扫描和超长拆分分层组织，兼顾当前交付成本与后续扩展空间。

## 架构与职责边界

建议新增以下核心对象。

### `ITextSegmenter`

职责：

- 接收单个章节正文字符串。
- 根据当前分段设置生成按顺序排列的 `SpeechSegment` 列表。

不负责：

- 读写 SQLite。
- 读取或保存设置。
- 控制播放器。
- 发起 HTTP 请求。

建议接口：

```csharp
public interface ITextSegmenter
{
    IReadOnlyList<SpeechSegment> Segment(
        string chapterText,
        TextSegmentationOptions options);
}
```

说明：

- 当前接口保持同步，符合纯文本内存处理的特性。
- 后续如果出现超大章节的性能瓶颈，再评估流式或异步接口。

### `TextSegmentationOptions`

职责：

- 承载全局分段设置。

建议字段：

```csharp
public sealed record TextSegmentationOptions(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold);
```

建议默认值：

- `EnableLongParagraphSplitting = true`
- `LongParagraphThreshold = 300`

### `ITextSegmentationOptionsProvider`

职责：

- 对应用层提供当前全局分段配置。
- 隔离设置来源，避免 ViewModel 或 `ITextSegmenter` 直接耦合 `settings.json`。

建议接口：

```csharp
public interface ITextSegmentationOptionsProvider
{
    TextSegmentationOptions GetCurrent();
}
```

第一版使用同步读取即可，因为设置体量很小且读取频率低。

### `SpeechSegment`

职责：

- 表示章节中的一个朗读单元。
- 同时携带文本内容和与原始章节的映射范围。

建议模型：

```csharp
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText);
```

约束：

- `SegmentIndex` 在单章内从 `0` 连续递增。
- `StartOffset` 与 `Length` 始终基于章节正文字符串。
- 第一版 `DisplayText` 与 `SpeechText` 默认相同。

## 数据流

分段链路建议如下：

```text
ChapterText
  ↓
NaturalParagraphScanner
  ↓
LongParagraphSplitter
  ↓
SpeechSegment[]
```

说明：

- `NaturalParagraphScanner` 负责识别自然段并保留原文范围。
- `LongParagraphSplitter` 只处理超过阈值的自然段。
- 两个步骤都属于 `ITextSegmenter` 的内部实现细节，不必在第一版暴露为公共接口。

## 分段算法

### 1. 自然段扫描

章节内容先按换行识别自然段。

规则：

- 每个换行后的文本块都视为一个自然段。
- 纯空白段不生成 `SpeechSegment`。
- 自然段内部原文保持不改写。
- 必须保留每个自然段在章节正文字符串中的 `StartOffset` 和 `Length`。
- 连续空行会形成空文本块，但这些空块会被直接跳过。

设计原因：

- 中文网文天然以自然段组织信息。
- 保持原始段落更利于用户理解当前正在听的内容。
- 不跨自然段合并有助于高亮和恢复定位。

### 2. 是否拆分

对每个自然段按以下逻辑处理：

- 如果 `EnableLongParagraphSplitting == false`，直接保留整段。
- 如果自然段长度小于或等于阈值，直接保留整段。
- 只有当自然段长度大于阈值时，才进入进一步拆分。

注意：

- 第一版不跨自然段合并多个短段。
- “长度”按 .NET 字符数计算即可，不额外做中文宽度换算。

### 3. 句级拆分

对超长自然段，优先按 `。！？` 查找句子边界。

建议策略：

- 先把自然段划分为句子切片。
- 按顺序累积句子，尽量让单个 segment 接近阈值但不过度超出。
- 如果再加入下一句会明显超过阈值，则在当前句结束处落一个 segment。

本阶段不做：

- `；` 断句。
- `，` 断句。
- 引号闭合语义修复。
- 省略号特殊语义处理。

设计原因：

- `。！？` 能覆盖第一版绝大多数中文小说朗读停顿。
- 不引入更细标点，可避免输出过碎、播放切换过频繁。

### 4. 硬切兜底

如果某个自然段或某个句子本身已经超过阈值，且没有足够的 `。！？` 可用于拆分，则执行硬切。

硬切规则：

- 仅在无法依赖句级边界时启用。
- 直接按固定长度从原文中切出多个连续片段。
- 不插入、不删除、不改写原文字符。

设计原因：

- 保证任何超长文本都能产生有限、稳定的 segment。
- 便于后续缓存键、进度和定位逻辑保持一致。

## 设置设计

分段相关设置纳入全局设置体系，所有书共用。

建议新增以下字段：

```json
{
  "enableLongParagraphSplitting": true,
  "longParagraphThreshold": 300
}
```

设置行为：

- 用户可在设置页开启或禁用超长段落拆分。
- 用户可调整阈值。
- 阈值改变后，不需要数据库迁移。
- 阈值改变后，章节重新分段的结果允许发生变化。

建议的保护策略：

- 如果用户设置值过小，读取时自动夹到一个安全下限，例如 `50`。
- 如果设置缺失，回退到默认值。

## 与现有模块的关系

### 与导入链路

- `BookImportService` 继续只负责导入、章节识别和持久化。
- 本次不修改 `BookImportAnalysis`、`BookImportResult` 的导入结果语义。

### 与 UI

- 设置页提供全局开关与阈值编辑入口。
- 播放页后续只消费 `SpeechSegment[]`，不自行切文本。
- 当前段高亮依赖 `StartOffset` 和 `Length`。

### 与后续播放链路

- `PlaybackCoordinator` 后续以 `SpeechSegment` 作为最小播放单元。
- `PlaybackSnapshot` 和进度恢复未来可直接复用 segment 索引与字符范围。

## 错误与边界处理

建议行为：

- `Chapter.Content` 为空或全空白时，返回空列表，不抛异常。
- 分段算法遇到无法拆分的超长文本时，必须退化到硬切，而不是失败。
- 设置值非法时使用夹取后的安全值，不因为配置错误阻断播放。
- 第一版不保证语义最优，只保证规则稳定、结果可预测、映射准确。

## 测试策略

新增测试重点如下。

### `ITextSegmenter` 单元测试

覆盖：

- 小于等于阈值的自然段保持原样。
- 关闭超长拆分后，超长自然段也保持原样。
- 超长自然段可按 `。！？` 拆成多个 segment。
- 无 `。！？` 的超长自然段会进入硬切。
- `SegmentIndex` 连续递增。
- `StartOffset`、`Length` 与原文切片一致。
- `DisplayText` 与 `SpeechText` 第一版相同。

### 中文边界样例测试

覆盖：

- 连续空行。
- 段首段尾空白。
- 只有一个超长单行。
- 混合中英文文本。
- 引号和省略号出现但不触发特殊语义。
- 只有标点或接近空内容的段落。

### 设置相关测试

覆盖：

- 开关启用与禁用的分段差异。
- 阈值变化导致的 segment 数变化。
- 非法阈值被夹取到安全下限。

## 风险与后续扩展

当前方案的已知风险：

- `.NET` 字符计数不等于真实语音时长，300 字阈值只能近似控制单段长度。
- 不处理引号、省略号等复杂语义时，个别句界可能不够自然。
- 阈值变更会影响 segment 数与 segment 索引，后续进度恢复需要更多依赖字符偏移而不是仅依赖索引。

后续可以独立扩展：

- `SpeechText` 轻度清洗。
- 更细粒度句界规则。
- 按书覆盖的分段设置。
- 分段算法版本号。
- 基于字符偏移的恢复定位。

## 建议实施顺序

1. 新增 `SpeechSegment`、`TextSegmentationOptions`、`ITextSegmenter`、`ITextSegmentationOptionsProvider`。
2. 实现第一版 `TextSegmenter`。
3. 为设置加入默认值与读取保护。
4. 为中文边界场景补足单元测试。
5. 在设置页暴露全局开关和阈值。

## 结论

Epic C 第一版应聚焦于建立稳定、可预测、可测试的运行时分段模型，而不是提前把分段持久化或引入复杂语义规则。采用“自然段优先，超长时按 `。！？` 拆分，不足时硬切”的方案，能够在实现成本、听感稳定性、UI 高亮映射和后续播放链路之间取得平衡，并与当前仓库的分层约束和产品方向保持一致。
