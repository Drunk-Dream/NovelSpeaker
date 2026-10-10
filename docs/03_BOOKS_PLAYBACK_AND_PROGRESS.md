# Books、Sources、Playback 与 Reading Progress

## 1. 核心链路

```text
Local TXT import
→ Book
→ Local BookSourceBinding
→ Active Source
→ CurrentCatalog + Content
→ text processing / segmentation
→ Speech Plan
→ Speech Provider / audio cache
→ Playback
→ Book-level Reading Progress
```

Book / Source / Catalog / Content / ReadingState 的精确合同见 `specs/BOOK_DATA_MODEL.md`。

## 2. Book

Book 表示用户认知中的一本书。

- 产品唯一身份是规范化 Title + Author。
- BookId 是数据库与内部引用的稳定技术主键。
- Title / Author 在入库前确定，正式入库后不提供普通修改入口。
- Description 等非身份信息允许更新，但不参与 Book 唯一性。
- Book 可以绑定多个 Source Binding，但任一时刻最多一个 Active Binding。
- Book 允许暂时没有 Active Binding。
- ReadingState 属于 Book。

不建立“Title + Author 只是候选，而 BookId 才决定用户语义上的同一本书”的长期模型。

## 3. BookSourceBinding

Binding 表示“一本具体 Book 与一个具体内容来源之间的关系”。

### Local Binding

- 每个 Book 最多一个。
- 保存应用内部正文位置、原始文件名、编码、SourceHash、导入时间等 Local-only 数据。
- 原始外部 TXT 只作为导入输入，不是长期运行时依赖。
- Local 正文属于业务持久数据，不是 Cache。

### Online Binding（future）

未来 Online Binding 将关联独立 Online Source Definition，并保存该书在来源中的 locator。具体规则、搜索、网络和 locator schema 后续设计，本轮不实现。

## 4. Active Source 与 CurrentCatalog

Book 保存 nullable `ActiveSourceBindingId`。

数据库只保存一份 `CurrentCatalog`：

```text
Book
├─ ActiveSourceBindingId
└─ CurrentCatalog
   └─ Chapter entries[]
```

**不再为每个 Binding 长期保存独立 Catalog。**

切换 Source 的核心事务是：

```text
prepare target-source catalog snapshot
→ validate
→ atomically replace CurrentCatalog + ActiveSourceBindingId
→ clamp ReadingProgress
→ publish committed change
```

如果目录准备失败，旧 Active Source / CurrentCatalog 保持可用。

切换回 Local Binding 时重新从应用内本地正文副本按当前章节规则解析目录，而不是从数据库恢复一份长期隐藏的 Local Catalog。

Catalog Entry 可以拥有技术 ChapterId、ChapterIndex、Title，以及 Source-specific typed locator/range。ChapterId 只服务数据库、Speech Plan、Audio Cache 等内部引用，不建立跨 Source Chapter Identity。

## 5. Local TXT 导入与重新导入

导入规则见 `specs/BOOK_IMPORT.md`。

关键行为：

- Title 与 Author 都由规则识别时直接继续。
- 任一身份字段未识别时先显示轻量确认面板，再创建/定位 Book。
- 新 Book：创建 Book + Local Binding，并让 Local Binding 成为 Active Source，提交 CurrentCatalog。
- 已有 Book：更新其唯一 Local Binding。
- 如果 Local Binding 是 Active Source，重新导入后用新解析结果原子替换 CurrentCatalog。
- 如果未来 Local Binding 不是 Active Source，重新导入只更新 Local 持久正文；CurrentCatalog 不改变，直到用户主动切回 Local Source。
- 重新导入失败保留旧可用数据。

## 6. Content

上层通过稳定 ContentService/port 获取章节正文。

```text
CurrentCatalogEntry
        ↓
Content boundary
   ├─ Local: stored content + range
   └─ Online future: locator → cache/network
```

Playback、详情页正文预览等消费者不得直接读取 Local 存储路径，也不得未来直接发 HTTP 请求。

Future Online 正文缓存使用 Binding + ContentLocator 等稳定身份，不以裸 ChapterIndex 作为缓存唯一键。

## 7. Reading Progress

ReadingState 属于 Book，不属于 Source。

当前持久化可以继续保存：

```text
BookId
ChapterIndex
SegmentIndex
CharacterOffset
AudioPositionMilliseconds
UpdatedAt
```

长期核心语义只有：

```text
Catalog ordinal
+
position inside chapter
```

### Source switch / Catalog replacement

- 不做章节标题、正文、Hash、URL、locator、相似度或百分比匹配。
- ChapterIndex 直接沿用；超过新 Catalog 最大序号时截断到最后一章。
- 章内位置直接沿用并按目标章节实际边界截断。
- 新 Catalog 为空时保持可解释的未定位状态，不伪造进度。
- Source 切换后停止当前播放，不自动续播。

## 8. Text 与 Speech Plan

```text
chapter content
→ normalize / segment
→ regex/text profile
→ DisplayText / SpeechText
→ current ChapterSpeechPlan
```

Speech Plan 是派生数据，不成为正文权威来源。ChapterId 可以作为当前 Catalog 的技术外键，但不能用于推断跨 Source 章节对应关系。

## 9. Playback Session

Playback session 是当前 Book / ActiveSource context 的运行时 owner；logical target 与音频 preparation 解耦。

```text
user command / automatic advance / committed domain change
→ resolve valid logical target
→ commit target + target revision
→ publish PlaybackSnapshot
→ prepare audio asynchronously
→ validate session + target identity
→ accept or discard result
```

规则：

- 用户显式切段/切章在必要位置解析完成后立即提交 logical target，不等待 TTS/缓存/解码。
- UI 立即投影新章节/段落；真实等待时才显示“正在准备音频”。
- 旧 target 音频立即停止；迟到结果直接丢弃。
- preparation 失败不回滚已提交 logical target。
- 只有 Book/ActiveSource context 真正变化时 replacement/retire session。
- 页面生命周期不能销毁长期 Playback session。

## 10. Source 生命周期对 Playback 的影响

- Binding 新增/更新不自动激活。
- Active Source 暂时不可用时不自动 fallback。
- 删除 Active Binding 时 ActiveSourceBindingId 变为空，不自动选其它 Binding。
- 删除最后一个 Binding 时删除 Book。
- Source 切换或 CurrentCatalog replacement 时，旧 Source 的正文请求、Playback Prefetch 和强绑定旧上下文的 preparation 必须取消或失效。
- 已经安全落地、身份仍有效的 Audio Cache 可以继续保留。

## 11. Playback Prefetch

Playback Prefetch 属于 Playback session，不是第二个 target owner，也不是 Cache process job。

优先级：

```text
Current Playback > Playback Prefetch > Active Cache
```

Target revision 或 Active Source context 改变后，与旧 target 强绑定且尚未消费的准备结果失效；可安全复用的物理缓存仍按自身 identity 保留。

## 12. 未来搜索与换源 UI

本轮不实现，但长期语义固定：

- 全局 SearchSession 产生按 Title + Author 聚合的 TemporaryBook；搜索结果本身不入库。
- TemporaryBook 加入书架时，把当时已经聚合的全部 Binding 一并入库，临时 Active Source 成为正式 Active Source。
- 已有 Book 的全局搜索只是浏览，不自动修改正式 Binding。
- 详情页和播放页未来可以提供换源入口；刷新列表属于页面级 RefreshSession。
- 离开承载换源的页面立即停止接收结果、取消未完成 Source search，不等待，只提交已进入 Working Set 的结果。
- 刷新过程中点击换源同样先停止并提交当前 Working Set，再执行换源。
- 应用退出时直接取消并丢弃未提交 Working Set，不延长退出流程。

## 13. UI 投影

- Library、BookDetails、Player 只消费稳定 read model/snapshot。
- CurrentCatalog 与 cache/loading/selection 等动态 decoration 分离。
- 用户主动定位优先于后台 decoration。
- 已由按钮操作表达的状态不重复增加标签文本，例如“加入书架 / 移出书架”本身即表达书架状态。

## 14. 必须保护的行为

- 规范化 Title + Author 的唯一性。
- BookId 在 Local re-import、CurrentCatalog replacement 后保持稳定。
- CurrentCatalog 是唯一持久目录真值，不存在每 Source 一份长期 Catalog。
- ReadingProgress 为 Book 级，Source switch 只做 ordinal/position 边界截断。
- Local Source 正文是持久业务数据，不受普通 Cache 清理影响。
- 用户显式跳转先提交 logical target，音频失败/取消不回滚。
- 迟到的 Source/Provider/cache/audio 结果不能覆盖新 session context 或 target revision。
- 页面生命周期不能销毁 Playback session。
- 主窗口、MiniPlayer、SMTC 共用同一播放状态。
- 超长章节目录仍可连续定位、滚动和播放。
