# 产品与范围

## 1. 产品定义

NovelSpeaker 是面向 Windows 10/11 x64 的轻量本地小说听书应用。当前主要场景仍是导入本地 TXT 小说，通过可配置语音服务持续收听；应用负责书籍/来源建模、章节识别、文本处理、连续播放、预取、音频缓存、阅读进度与桌面媒体控制。

核心链路：

```text
TXT import
→ Book + Local Source
→ Source Catalog + Content
→ text processing
→ Speech Provider
→ continuous playback
→ audio cache
→ recoverable Reading Progress
```

NovelSpeaker 优先保持本地、轻量、可控，不追求完整电子书生态或通用插件平台。

## 2. 核心能力

### 书籍、来源与章节

- `Book` 是用户认知中的一本书，也是跨来源保持稳定的书籍实体；内部 `BookId` 才是创建后的永久身份。
- 一个 Book 可以绑定多个 `Source`，任一时刻最多只有一个 `ActiveSource`；Book 也允许暂时没有 ActiveSource。
- Source 自己拥有 Catalog。Book 不维护一套独立“主目录”；当前目录由 `ActiveSource.Catalog` 提供。
- 当前版本只实现 `Local Source`。在线书源不在近期实现范围，但数据模型不得把 Local TXT 特例重新固化到 Book 本体中。
- 每个 Book 最多一个 Local Source。Local Source 是导入快照，不依赖原始 TXT 文件之后仍存在；移动或删除原始 TXT 不影响已导入书籍。
- 本地 TXT 导入后保存解析得到的 Source Catalog 与正文持久数据，不长期保存“用户原始 TXT 文件引用”作为运行时真值，也不写回用户外部 TXT。
- Local Source 正文属于持久 Source 数据，不属于可清理缓存。
- 导入本地 TXT 时识别/选择文本编码；无编码问题时保持“选择文件后直接导入”的简便流程，不引入导入向导。
- 在导入时通过有序的文件名元数据规则和正文头部元数据规则识别书名、作者与简介；元数据识别只读取源文本，不删除或改写正文。
- 使用章节规则识别显式章节标题，并可通过全局“空行分章”开关把正文中的空行作为补充章节边界；两种机制同时生效且显式章节标题优先。
- 由空行产生且原文没有标题的章节使用自动标题“第 N 节”；用户可见目录展示章节自身 Title，不再根据内部 ChapterIndex 自动生成“第 N 章”。
- Book 当前展示的 Title / Author / Description / Cover 等元数据跟随 ActiveSource；没有 ActiveSource 时保留最后一次已投影的显示元数据快照。
- Source 自身保存自己的元数据快照。更新非 ActiveSource 不改变 Book 当前展示元数据。
- 章节识别配置变化后可以在明确的重新导入/更新 Local Source 流程中重建该 Source 的 Catalog；配置变化本身不静默重写已有书籍。
- 支持书库、书籍详情和超长连续章节目录；书库保留标题/作者搜索与现有排序。
- Book / Source / Catalog / Content / ReadingState 的精确长期模型见 `specs/BOOK_DATA_MODEL.md`。
- TXT 导入的精确元数据与章节识别合同见 `specs/BOOK_IMPORT.md`。

### 重新导入与来源更新

- 自动发现“同一本 Book”时使用书名 + 作者的严格匹配；空作者也作为一个明确作者值参与匹配。
- Book 一旦已经建立，后续 Source 元数据改变不改变 BookId。
- 一个 TXT 再次导入且严格匹配到唯一 Book 时，更新该 Book 的 Local Source，而不是创建重复 Book。
- 若严格匹配得到多个 Book 候选，不猜测，不按最近导入/最近阅读/列表第一项自动选择；由用户明确选择目标 Book 或作为新 Book 导入。
- 用户显式把 Source 绑定到某 Book 时，允许 Source 的书名/作者与 Book 当前元数据不同；显式绑定高于自动匹配，不做名称模糊匹配。

### 阅读进度

- 阅读进度属于 Book，而不是 Source。
- ReadingState 主要表达 Catalog 中的章节位置与章内位置，不依赖产品级 Chapter Identity。
- 不同 Source 共享同一份 Book 阅读进度。切换 Source 时不做标题、正文、哈希或比例匹配，只按相同章节序号/章内位置直接使用并做边界截断。
- 切换 Source 时停止当前播放且不自动续播。
- Catalog 更新后也不进行旧新章节身份推断，只保留位置并按新边界截断。

### 语音服务与文本处理

- 使用统一 Speech Provider 模型管理语音服务。
- HTTP Provider 支持用户创建、编辑、复制、批量导入/导出、删除、排序和试听；Microsoft Edge Provider 作为实验性内置单实例 Provider，仅在对应实验功能开启时可见，且不参与导入/导出。
- 不为未来 Local Provider 预先建立通用插件系统或万能配置 Schema。
- 支持显示文本与朗读文本的正则替换流水线。
- 章节规则、正则替换规则与元数据规则支持同类型的单项/批量交换。
- Provider 与 HTTP TTS 的精确合同见 `specs/HTTP_TTS.md`；正则规则合同见 `specs/REGEX_REPLACEMENT.md`。

### 播放

- 支持 Play/Pause/Stop、上一/下一段、章节跳转、段落跳转和阅读进度恢复。
- 播放页是主动切换当前 Speech Provider 的唯一入口。
- 当前 Speech Provider 可以为空，但不把 None 作为高频可选项展示。
- 切换 Speech Provider 或保存 Provider 新配置不打断已经开始播放的当前语句；下一条尚未开始的语句使用最新有效配置。
- 不建立自动 Speech Provider fallback。
- 对可恢复的瞬时合成失败只进行有限重试；最终失败后自动跳过当前段。成功播放任一后续段会清零连续自动跳过计数；连续自动跳过 3 个段后暂停自动推进并等待用户处理。
- 播放会话可以在页面切换、主窗口隐藏到托盘和迷你播放器之间持续。
- 系统媒体控制与媒体键复用同一播放会话。

### 批量管理

支持批量管理的普通业务页面使用统一的页面级管理模式：

- 正常模式下保持页面原本的“使用对象”行为；Ctrl/Shift 选择或显式“选择/批量管理”入口进入管理模式。
- 进入管理模式后，普通点击只改变选择集，不再打开书籍、跳转章节、切换 Provider 或切换规则编辑对象。
- 选择集只作用于当前页面、当前可见且可管理的同类对象；离开页面清空。
- 支持 Select All、统一批量动作、单次删除确认、部分成功/跳过/失败汇总。
- CacheManagement 是明确例外，继续使用当前文件管理器式选择语义。
- 精确交互合同见 `specs/BATCH_MANAGEMENT.md`。

### 缓存与后台工作

- 当前播放、播放预取和主动音频缓存复用同一套文本/Provider/缓存语义。
- 主动音频缓存以章节为用户选择粒度，可在离开页面后继续；任务开始时冻结 Speech Provider 和影响合成的配置快照。
- 缓存管理页展示已缓存音频章节并支持批量清理和每章 MP3 导出。
- 音频缓存属于可重建派生数据，不成为正文、Provider 配置或阅读进度的唯一来源。
- Local Source 正文不是缓存。
- 未来 Online Source 的章节正文缓存也不进入 `app.db`；其生命周期跟随 Source 绑定关系。当前只预留这一边界，不实现在线书源与高级正文缓存管理。

### 配置备份与恢复

- 提供第一版“配置备份/恢复”，定位为私人配置迁移，不等价于用于分享的单类规则或 Provider 导出。
- 备份包含应用设置、Speech Provider 完整配置、章节规则、正则替换规则、文件名元数据规则和正文头部元数据规则。
- Provider 中的 API Key、Token、Cookie 等凭据随私人备份完整保存，因此备份前明确提示敏感信息风险；第一版不要求加密备份。
- 备份不包含 TXT/书库内容、阅读进度、音频缓存、日志、性能遥测或诊断会话。
- 恢复使用完整配置快照语义，不把私人备份当成“合并导入”；恢复前先完整校验并明确提示会替换当前配置。

### 桌面体验

- 支持 Light / Dark / System 主题。
- 提供高频 Light/Dark 快捷切换。
- 支持主窗口隐藏到托盘、退出或每次询问。
- 提供迷你播放器。
- 设置页保持轻量，不用状态面板堆叠技术信息。

### 可诊断性

- 生产环境具有低频结构化日志。
- 用户可以主动开启本地性能遥测并导出诊断信息。
- 用户遇到可复现问题时可以使用诊断会话记录问题现场，并可主动标记问题或截取当前 NovelSpeaker 窗口。
- 所有诊断能力默认本地保存，不自动上传。

## 3. 导航与页面体验

- 不维护浏览器式 Back/Forward 历史。
- 普通页面具有明确父级返回关系。
- 播放页可以返回进入播放页之前的来源页面。
- PageHeader、Alt+Left 和未被局部交互消费的 Esc 使用统一返回语义。
- 大型页面先提供可交互内容，再补充非关键 enrichment。

## 4. 规模与性能目标

长期架构至少按以下规模设计：

- 章节目录支持至少 10,000 章连续浏览。
- 用户不需要显式分页浏览长目录。
- UI virtualization 与数据/projection 规模控制同时成立。
- 首个可交互帧不等待完整数据集的动态 enrichment。
- UI Dispatcher 不执行无界、随完整数据集线性增长的重工作。

## 5. 用户数据与兼容

必须长期保护：

- 用户外部 TXT。
- 已发布 SQLite migration 与正式持久化数据。
- Book / Source / Catalog / ReadingProgress 的正式用户数据。
- 已发布的 Speech Provider 配置、规则、设置与正式版本化交换/备份格式。
- 已发布的用户可观察核心交互语义。

内部 Book/Source 模型迁移可以一次性重构，不为旧 Book/Chapter 内部代码模型长期保留双读、双写或 compatibility wrapper。已发布 v0.8.0 用户书籍应优先通过有界、可验证的一次性迁移保留；如果真实实现证明自动迁移必须引入复杂长期兼容层、内容模糊匹配或新的重型恢复系统，则宁可明确要求用户重新导入本地书籍，也不长期背负一次性迁移复杂度。

内部 namespace、目录、未发布抽象、诊断内部格式的未发布实现细节不属于兼容承诺；项目仍允许为清晰职责进行内部破坏性重构。

## 6. 当前非目标

- EPUB、漫画等完整多格式阅读器。
- Online Source / Legado 类在线书源的近期实现。
- 为 Online Source 提前实现站点规则、目录抓取、正文请求、登录、变量系统等具体机制。
- 账号体系。
- 跨平台客户端。
- 插件市场或通用脚本宿主。
- 专业音频编辑/后期。
- Speech Provider 自动 fallback 链。
- 为未来 Local Speech Provider 预先设计通用插件/能力框架。
- WebDAV/云同步的近期实现。
- 高级在线正文缓存管理、容量上限、LRU/自动淘汰。
- 为第三方提供稳定 SDK/API。
- 自动远程上传日志或性能遥测。
- 默认录屏、自动截图、Crash Dump/Minidump。
