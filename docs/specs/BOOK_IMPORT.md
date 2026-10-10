# TXT 导入、元数据与章节识别规范

## 1. 定位

本规范定义 NovelSpeaker 通过本地 TXT 建立或更新 `Book + LocalBookSourceBinding + CurrentCatalog + Local Content` 的长期导入合同。

Book / Source / Catalog / Content / ReadingState 的权威模型见 `BOOK_DATA_MODEL.md`。

用户外部 TXT 只作为导入输入：

- NovelSpeaker 永不写回或删除用户外部 TXT；
- 成功导入后，运行时只依赖应用数据目录中的持久正文副本；
- 用户随后移动、改名或删除原 TXT，不影响已导入书籍。

## 2. 默认导入流程

```text
选择 TXT
→ 路径 / 编码检查
→ 文本规范化
→ 识别显式章节标题
→ 执行文件名与正文头部元数据规则
→ 判断 Title / Author 是否都被规则识别
   ├─ 是 → 跳过人工确认
   └─ 否 → 弹出元数据确认面板
            ├─ 已识别字段预填
            ├─ Title 缺失时使用文件名作为可确认回退值
            └─ Author 缺失时允许为空
→ 得到最终 Title + Author
→ 规范化身份并查询唯一 Book
→ 章节规则 + 可选空行分章生成完整 Catalog snapshot
→ 构建 Local Source snapshot
→ 原子提交
```

导入保持轻量：只有真正缺少 Title 或 Author 自动识别结果时才打断用户。

## 3. 元数据确认

### 3.1 何时弹出

如果已配置元数据规则没有同时识别出 Title 和 Author，则进入确认面板。

注意：

- “规则识别成功”指字段由文件名元数据规则或正文头部元数据规则明确捕获；
- Title 的文件名回退值不算“规则已识别 Title”；
- 因此只有 Title 与 Author 都被规则明确识别时，导入才完全无确认继续。

### 3.2 确认面板

确认面板只解决入库前的 Book Identity，不变成完整导入向导。

要求：

- Title 必须最终非空；
- Author 可以为空；
- 自动识别出的字段预填；
- 未识别 Title 使用源文件名（不含扩展名）作为默认值；
- 未识别 Author 默认空；
- 用户可以修改预填字段，也可以直接确认默认值继续；
- Description 等非身份字段不要求阻塞导入确认。

确认完成后，Title / Author 就成为该 Book 的正式身份字段。入库后不再提供普通修改入口。

如果用户以后发现自动规则识别错误，应调整规则并重新导入，而不是建立长期 Title / Author override 系统。

## 4. Book 唯一匹配

导入完成最终 Title / Author 后：

```text
Normalize(Title, Author)
→ query unique Book identity
```

规则：

- 0 个 Book：创建新 Book + Local Binding；
- 1 个 Book：建立或更新该 Book 的唯一 Local Binding；
- 正常数据库状态下不应出现多个候选，因为身份有唯一约束；
- 不提供“作为同名新书强行再建一本”的普通路径；
- 不使用 SourceHash、文件名、正文 Hash、章节标题、最近阅读时间等替代 Book Identity。

Author 为空是合法身份值，例如：

```text
NormalizedTitle = "示例"
NormalizedAuthor = ""
```

仍然可以唯一确定一本 Book。

## 5. Book Identity 与重新导入

Title / Author 在正式 Book 创建后不可编辑。

重新导入的语义不是“更新 Book Identity”，而是：

```text
本次解析得到 Title + Author
→ 定位对应 Book
→ create/update that Book's Local Binding
```

如果重新导入得到不同 Title / Author，则按新的身份处理为另一 Book，而不是偷偷修改原 Book 的身份。

## 6. Local Binding

每个 Book 最多一个 Local Binding。

Local Binding 保存：

- 导入后的内部持久正文路径；
- 原始文件名，仅作为来源信息；
- SourceHash；
- Encoding；
- ImportedAt / LastImportedAt；
- 其它确实只属于 Local Source 的 typed 数据。

不要把这些 Local-only 字段放回 Book 主表。

## 7. Active Source 与导入提交

### 新建 Book

新 Book 第一次通过 TXT 导入时：

- 创建 Book；
- 创建 Local Binding；
- Local Binding 成为 ActiveSourceBinding；
- 本次解析的完整 Catalog 成为 CurrentCatalog。

### 已有 Book

未来当某个已有 Book 可能拥有其它 Source 时：

- 新建或更新 Local Binding 不意味着自动激活；
- 如果 Local Binding 当前就是 Active Binding，则成功重新导入后替换 CurrentCatalog；
- 如果 Local Binding 不是 Active Binding，则只更新 Local Source persistent content，不覆盖当前其它 Active Source 的 CurrentCatalog；
- 以后切回 Local Binding 时再根据当前章节规则重新解析 Local Content 并替换 CurrentCatalog。

当前阶段只有 Local Source，但实现不得把“Local 一定永远是 Active”重新固化为无法扩展的数据模型。

## 8. 元数据规则

当前支持三类导入元数据：

- `name` → Title；
- `author` → Author；
- `description` → Description。

规则继续使用 .NET Regex named capture group。

示例：

```regex
^(?<name>.+?)\s+作者[:：]\s*(?<author>.+)$
```

要求：

- Pattern 必须是合法 .NET Regex；
- 一条元数据规则至少包含一个当前支持字段；
- 只有成功匹配且得到非空字段时才算该字段被识别；
- Title / Author 是否需要确认，以规则是否明确识别为准，而不是最终是否存在文件名 fallback。

## 9. 文件名元数据规则

文件名规则是独立有序规则列表。

执行：

1. 对不含扩展名的源文件名执行；
2. 只执行 Enabled 规则；
3. 按 SortOrder 顺序匹配；
4. 第一条有效命中成为该阶段唯一胜出规则；
5. 后续文件名规则不再执行；
6. 缺失字段继续由正文头部规则补充。

文件名 fallback 只在最终 Title 仍缺失时提供确认面板默认值，不等价于规则命中。

## 10. 正文头部元数据规则

头部区域继续通过章节规则确定：

- 找到第一个显式章节标题时，头部区域是文件开头到该标题之前；
- 没有显式章节标题时，只扫描有界前缀；
- “空行分章”不参与确定头部结束位置。

正文头部规则按 SortOrder 执行全部 Enabled 规则：

- 文件名规则已经识别的字段不覆盖；
- 先识别出的字段不被后续规则覆盖；
- 后续规则只补缺失字段；
- 多条规则可以分别识别 Title / Author / Description。

元数据识别只读取正文，不删除已经识别为元数据的原文行。

## 11. 章节规则与空行分章

章节识别继续组合：

```text
显式章节规则
+
可选空行分章
```

显式章节标题优先。

要求：

- 文件开头到第一个显式章节标题前的空行不生成额外章节；
- 标题后、正文前的空行不生成额外章节；
- 如果空行之后的下一个非空行本身是显式章节标题，由显式标题建立下一章；
- 连续空行视为一个候选边界；
- 文件首尾空行不生成空章节；
- 没有任何显式章节标题时，开关关闭使用全文单章节，开启时由空行划分章节块。

运行时 `TextSegmenter` 的自然段语义仍然独立，不因导入期“空行分章”改变。

## 12. 自动章节标题

显式章节规则产生的章节使用原始章节标题。

空行生成且没有源标题的章节使用：

```text
第 N 节
```

N 为最终 CurrentCatalog 中的 1-based 位置。

ChapterIndex 继续使用内部 0-based ordinal。

## 13. Current Catalog 与 Local Content

导入期始终先准备一个完整候选 snapshot：

```text
Normalized local content file
+
Catalog entries
+
Local chapter ranges
```

如果 Local Binding 将成为/仍是 Active Binding：

```text
prepare full snapshot
→ validate offsets / lengths / order
→ finalize new content file
→ atomically persist binding + CurrentCatalog
→ cleanup obsolete content file
```

失败时：

- 旧 Local Binding 数据仍可用；
- 旧 CurrentCatalog 仍可用；
- 不留下半成品正文文件被数据库引用。

如果 Local Binding 非 Active：

- 可以使用完整解析结果做校验；
- 只提交 Local persistent content 与 Binding 信息；
- 不把它的 Catalog 长期写入数据库覆盖 CurrentCatalog。

## 14. ReadingState 后处理

Local Source 更新或 CurrentCatalog 替换后：

- 不匹配旧新 ChapterId；
- 不按标题/正文/Hash 猜测对应章节；
- 保留原 ChapterIndex；
- 超出新 Catalog 最大范围则截到最后一章；
- 章内位置超出新正文范围时截到合法边界。

精确规则见 `BOOK_DATA_MODEL.md`。

## 15. 编码处理

编码检测沿用当前轻量流程：

- 自动检测可信时继续；
- 检测低置信度或失败时请求用户选择编码；
- 重新选择编码后重新执行完整导入准备；
- 不把编码选择与 Book Identity 确认合并为一个大型向导。

如果同时需要编码选择和元数据确认，应按自然依赖顺序完成：先得到可靠文本，再解析/确认 Title 与 Author。

## 16. 规则工作台与交换格式

章节规则、文件名元数据规则、正文头部元数据规则继续是独立规则类型。

它们可以继续支持：

- Enabled；
- SortOrder；
- 新建、编辑、删除；
- 拖拽排序；
- 文件/剪贴板导入导出；
- 批量管理。

不因为未来 Online Source 规则系统而把当前 Local TXT 规则强行塞进一个万能“书源规则 DSL”。

## 17. 非目标

本轮不实现：

- Online Source 规则；
- Online Source 搜索；
- 在线目录或正文抓取；
- SearchSession / RefreshSession；
- Legado 规则兼容；
- 本地导入完整多步骤向导；
- 入库后的 Title / Author 编辑；
- 模糊 Book 匹配；
- 模糊章节匹配。
