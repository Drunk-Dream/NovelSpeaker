# 产品与范围

## 1. 产品定义

NovelSpeaker 是面向 Windows 10/11 x64 的轻量小说听书应用。当前主要场景仍是导入本地 TXT 小说，通过可配置 Speech Provider 持续收听；长期允许一本书绑定多个内容来源，并在不改变 Book 级阅读状态的前提下切换来源。

当前核心链路：

```text
Local TXT import
→ Book + Local Binding
→ Active Source + Current Catalog
→ Content
→ text processing
→ Speech Provider
→ continuous playback
→ audio cache
→ recoverable Reading Progress
```

NovelSpeaker 优先保持轻量、可控和职责清晰，不追求完整电子书生态或通用插件平台。

## 2. 核心能力

### 2.1 Book、Source 与 Catalog

- `Book` 是用户认知中的一本书，也是书架中最高层实体。
- 产品语义上，规范化后的 `Title + Author` 唯一确定一本 Book；`BookId` 仅作为稳定数据库技术主键。
- Title / Author 在入库时确定，入库后不再提供普通修改入口；不维护长期的“同一本书歧义确认”系统。
- 一个 Book 可以绑定多个 `BookSourceBinding`：最多一个 Local Binding，未来可以有多个 Online Binding；任一时刻最多一个 Active Binding。
- Source Definition 与 Book 解耦。未来 Online Source Definition 描述“如何访问某个在线来源”，具体 Book 通过 Binding 保存该书在来源中的 locator。
- 当前阶段只实现 Local Source。Online Source 只搭建必要领域边界，不实现规则、搜索、刷新、在线目录或正文获取。
- 数据库只保存当前 Active Source 对应的一份 `CurrentCatalog`；不为每个 Binding 长期保存独立 Catalog。
- 切换到某个 Source 时重新构建该 Source 的目录快照并原子替换 CurrentCatalog。本地源切回时重新按当前章节规则解析目录。
- ReadingState 属于 Book，不属于 Source。
- Book / Source / Catalog / Content / ReadingState 的精确合同见 `specs/BOOK_DATA_MODEL.md`。

### 2.2 本地 TXT 导入

- Local Source 是应用管理的导入快照；原始 TXT 只作为输入，成功导入后移动、改名或删除原文件不影响书籍。
- NovelSpeaker 永不写回用户外部 TXT。
- 本地正文是持久业务数据，不属于可清理 Cache。
- 导入时通过文件名元数据规则和正文头部元数据规则识别 Title / Author / Description。
- 如果 Title 与 Author 都由规则明确识别，导入无需额外确认；若任一字段未被规则识别，则弹出轻量元数据确认面板，由用户确认最终 Title / Author 后再入库。
- 未识别 Title 可用文件名作为默认值；Author 允许为空。
- 使用章节规则识别显式章节标题，并可通过全局“空行分章”开关补充分章；显式标题优先。
- 重新导入按最终规范化 Title + Author 定位 Book，更新该 Book 的唯一 Local Binding；失败时保留旧持久快照。
- TXT 导入精确语义见 `specs/BOOK_IMPORT.md`。

### 2.3 阅读进度与换源

- ReadingProgress 属于 Book，核心坐标为 `ChapterIndex + PositionInChapter`。
- Source 切换或 CurrentCatalog 替换时不做标题、正文、URL、Hash、相似度或百分比匹配。
- 章节序号直接沿用；超过新目录边界时截断到最后一个合法章节；章内位置同样只做边界截断。
- Source 切换后停止当前播放，不自动续播。
- 旧 Source 的未完成正文获取、预取或音频准备必须取消或失效，迟到结果不能覆盖新上下文。

### 2.4 未来 Online Source

未来 Online Source 计划参考 Legado 在搜索、详情、目录、正文等方面的成熟经验，但不要求直接兼容 Legado 规则格式。

长期方向：

- NovelSpeaker 使用自己的结构化、显式、低歧义规则格式；
- 规则设计应便于校验、测试和维护；
- 允许借助 AI 将 Legado 规则转换为 NovelSpeaker 规则，而不是在运行时背负 Legado 兼容层；
- 全局搜索将采用按 Source 流式返回、按 Title + Author 聚合的 TemporaryBook 模型；
- 刷新绑定源列表将使用内存 Working Set，正常停止提交当前结果，应用退出直接取消并丢弃未提交结果；
- 这些能力均不在本轮基础重构中实现。

### 2.5 正文缓存

- Local Source 正文不是缓存。
- 未来 Online Source 正文采用独立文本文件缓存，数据库只保存必要元数据。
- 在线正文缓存身份应由具体 Book Binding 与章节 locator 共同确定，不以裸章节序号作为唯一身份。
- 正常换源、刷新目录或刷新绑定源不主动清理正文缓存，不使用 LRU。
- Book 删除或用户主动清理时才执行对应清理。
- Online 正文缓存与 Audio Cache 是不同数据类别，不强行合并。

### 2.6 Speech Provider 与文本处理

- 使用统一 Speech Provider 模型管理语音服务。
- HTTP Provider 与 Microsoft Edge Provider 通过统一上层能力使用，各自保持 typed config/runtime。
- 不为未来 Provider 类型预先建立通用插件系统或万能配置 Schema。
- 支持显示文本与朗读文本的正则替换流水线。
- Provider 与 HTTP TTS 精确合同见 `specs/HTTP_TTS.md`；正则规则合同见 `specs/REGEX_REPLACEMENT.md`。

### 2.7 播放

- 支持 Play/Pause/Stop、上一/下一段、章节跳转、段落跳转和阅读进度恢复。
- UI logical target 与慢速音频获取解耦；用户切换章节/段落后 UI 先提交新位置，音频随后准备。
- 迟到的音频结果不得复活旧 logical target。
- 播放会话可以在页面切换、主窗口隐藏到托盘和迷你播放器之间持续。
- 系统媒体控制与媒体键复用同一播放会话。

### 2.8 批量管理

支持批量管理的普通业务页面使用统一页面级 Management Mode；选择集不跨页面持久化，不混合不同类型对象，支持 Select All 和批量动作。CacheManagement 保留文件管理器式选择语义例外。精确合同见 `specs/BATCH_MANAGEMENT.md`。

### 2.9 Audio Cache 与后台工作

- 当前播放、Playback Prefetch 和主动音频缓存复用同一套文本/Provider/缓存语义。
- 主动音频缓存和导出属于明确的后台 owner，页面离开不取消已提交后台批次。
- 音频缓存属于可重建派生数据，不成为正文、Provider 配置或 ReadingProgress 的唯一来源。
- Local Source 正文永远不属于 Audio Cache。

### 2.10 配置备份与恢复

配置备份用于私人配置迁移，不等价于用于分享的规则/Provider 导出。备份可以包含应用设置、Speech Provider 配置和规则，但不包含书库正文、阅读进度、Audio Cache、日志、遥测或诊断会话。

### 2.11 桌面体验与诊断

- 支持 Light / Dark / System 主题、快捷主题切换、托盘和迷你播放器。
- 生产环境保留低频结构化日志；普通性能遥测默认关闭、本地保存；用户可以主动开启诊断会话。
- 诊断能力失败不得破坏业务流程。

## 3. UI 信息表达原则

- 已经由操作本身明确表达的状态，不再增加重复文字或状态块。
- 例如“加入书架 / 移出书架”已经足以表达书架状态，不额外显示“已加入书架 / 未加入书架”。
- 同一事实避免同时由按钮、标签和说明文字重复表达，以降低页面阅读成本。
- 瞬时操作结果、警告和失败继续使用统一反馈系统，而不是长期占用页面空间。

## 4. 导航与页面体验

- 不维护浏览器式 Back/Forward 历史。
- 普通页面具有明确父级返回关系。
- 播放页可以返回进入播放页之前的来源页面。
- PageHeader、Alt+Left 和未被局部交互消费的 Esc 使用统一返回语义。
- 大型页面先提供可交互内容，再补充非关键 enrichment。

## 5. 规模与性能目标

- 章节目录至少支持 10,000 章连续浏览。
- 用户不需要显式分页浏览长目录。
- UI virtualization 与数据/projection 规模控制同时成立。
- 首个可交互帧不等待完整数据集的动态 enrichment。
- UI Dispatcher 不执行无界、随完整数据集线性增长的重工作。

## 6. 用户数据与兼容

必须长期保护：

- 用户外部 TXT；
- 已发布 SQLite migration 与正式持久化数据；
- Book / Binding / CurrentCatalog / ReadingProgress 的正式用户数据；
- 已发布的 Speech Provider 配置、规则、设置与正式版本化交换/备份格式；
- 已发布的用户可观察核心交互语义。

内部模型可以一次性重构，不为旧 Book/Source/Catalog 内部模型长期保留双读、双写或 compatibility wrapper。对于可有界、安全迁移的数据优先自动迁移；若历史书籍身份冲突无法无歧义解决，则明确进入重新导入流程，而不是引入长期兼容复杂度。

## 7. 当前非目标

- EPUB、漫画等完整多格式阅读器。
- 本轮实现 Online Source 规则、搜索、刷新绑定源、在线目录或在线正文。
- 直接兼容 Legado 书源规则格式。
- 为未来 Online Source 提前实现万能插件框架、登录/变量/脚本宿主。
- Source 自动 fallback。
- 跨 Source Chapter Identity 或模糊章节匹配。
- Online 正文缓存容量上限、LRU 或高级逐章缓存管理。
- 账号体系、跨平台客户端、WebDAV/云同步近期实现。
- 专业音频编辑/后期。
- 自动远程上传日志、默认录屏/自动截图、Crash Dump/Minidump。
