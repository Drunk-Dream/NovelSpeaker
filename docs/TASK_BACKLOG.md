# NovelSpeaker 下一阶段开发 Backlog

## 1. 阶段定位

上一轮架构重构和发布验证已经完成，历史摘要见 `archives/2026-07-26_ARCHITECTURE_REFACTOR_COMPLETED.md`。

本阶段目标从“迁移架构”转为：

1. 清理历史代码、低价值测试和残余架构债。
2. 收紧 DI、生命周期、资源所有权和 TTS 并发模型。
3. 统一 UI 视觉语言和桌面交互。
4. 完善规则与设置体验。
5. 新增主动缓存、章节 MP3 导出、媒体控制、托盘、迷你播放器和定时停止。
6. 对照终态文档完成设计—实现一致性收口。

不扩展到 EPUB、在线书源、云同步或其它新的内容生态。

## 2. 状态与优先级

状态：

- `[ ]` 未开始
- `[~]` 进行中
- `[x]` 完成
- `[!]` 阻塞；必须记录可验证证据和恢复条件

优先级：

- `P0`：后续功能依赖、数据/资源/并发正确性。
- `P1`：本阶段主线能力和主要体验。
- `P2`：全局一致性和清理收口。

## 3. 任务规则

每个任务必须：

1. 先阅读对应数字设计文档、生产代码和直接测试。
2. 行为变化先补或调整自动化测试。
3. 可以重构内部 API、目录和测试，但不得破坏已发布数据兼容。
4. 同一任务建立目标实现后删除无继续价值的旧入口/compat wrapper。
5. 不把“手动验证”规划为任务完成条件。
6. 任务级执行针对性测试；Wave 收口执行完整质量门禁。
7. 实现与文档有差异时同步数字文档；README 只有在功能实际实现后才增加该能力。
8. Git 提交遵循 `AGENTS.md`：一个任务切片也要按逻辑目的继续拆分多个原子 commit。

完整自动质量门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## 4. 总体依赖

```text
Wave 1 代码清理与架构基础
  ↓
Wave 2 全局视觉与桌面交互基础
  ↓
Wave 3 规则工作台与设置体验
  ↓
Wave 4 主动缓存、缓存管理与 MP3 导出
  ↓
Wave 5 Windows 媒体、托盘、迷你播放器与定时停止
  ↓
Wave 6 体验一致性与全仓收口
```

Wave 内若任务不修改同一公共合同，可以并行；涉及共享 DI、播放合同、Design Token 或缓存用例时优先串行。

---

# Wave 1：代码清理与架构基础

目标：先消除会让后台缓存、托盘和媒体集成变复杂的技术债，再新增功能。

## [x] ARCH-101（P0）：收紧 DI 与 `IServiceProvider` 边界

范围：Application 注册、App Bootstrap、Page provider/factory、现有非 Bootstrap 容器解析。

实现：

- 清点所有 `IServiceProvider` 注入、字段、参数和转发。
- Application 的用例注册保持模块化，但不把容器作为业务依赖向内扩散。
- App 非 Bootstrap/Page provider/framework bridge 代码改为显式构造依赖或专用 factory。
- 删除仅用于 service-locate 的中间 wrapper。
- 明确 Singleton/Transient 的真实状态所有权。

自动验收：

- 架构测试禁止新的非允许位置 `IServiceProvider` 依赖。
- 容器 `ValidateOnBuild/ValidateScopes` 和关键 service resolution 测试通过。
- 完整 Solution build/test 通过。

## [x] LIFE-102（P0）：统一页面 activation、操作和后台任务所有权

前置：ARCH-101。

实现：

- 统一 Page activation scope、版本、CTS、进入/离开与 guard 注册。
- 审计页面 `async void`、`_ = Task`、后台 Task 和事件订阅。
- 事件入口只桥接到可等待流程；fire-and-forget 进入明确 registry/owner。
- 播放、主动缓存等进程/会话任务不被 Page 离开误取消。
- 删除重复 activation/operation helper。

自动验收：

- 页面进入、离开、快速重入、旧结果晚到和异常转交测试。
- 架构/源码规则覆盖未登记 fire-and-forget 的已知入口。

## [x] RES-103（P0）：明确 HTTP、NAudio 与临时文件资源所有权

实现：

- 审计 HTTP response/stream 的所有权转移、读取超时和取消。
- 统一 NAudio reader/output/stream 的创建、会话替换和 Dispose 路径。
- 审计 TTS 临时文件、缓存 staging/候选文件的成功、失败、取消清理。
- 失败恢复不留下孤儿临时文件或被锁定的缓存文件。
- 必要时拆分过大的 adapter，但不改变用户行为。

自动验收：

- transport 取消/异常释放测试。
- NAudio 多次替换/失败释放测试。
- 临时文件 cleanup 和文件锁回归测试。

## [x] SYNC-104（P0）：异步化 TTS admission，并整理持久化同步边界

实现：

- 移除 `TtsRateLimiter` 等路径中的同步 `Mutex.Wait`/同步阻塞。
- 建立可取消的规则级异步 admission/rate limiter，为后续优先级调度预留明确入口。
- 保持当前规则已有并发/速率语义兼容。
- 统一 SQLite 时间提供器、时间序列化和损坏历史记录容错。
- 设置读取/保存不使用同步阻塞等待。

自动验收：

- limiter 并发、取消、计时和公平性基础测试。
- SQLite 旧时间格式/损坏记录 fixture 回归。
- 设置损坏/原子保存测试。

## [x] CLEAN-105（P1）：激进清理内部 API 与冗余实现

前置：ARCH-101、LIFE-102。

审计并处理：

- 无引用私有代码、失效接口、重复 DTO/mapper/validator。
- `LibraryBookItemViewModel` 等历史兼容构造器/属性。
- Presentation 公共合同中不必要的 WPF 视觉类型暴露。
- 一次性 wrapper、空目录、旧命名空间、误导性类型/文件名。
- 过大 service 中可以明确拆出的单一职责。
- 与目标架构不再匹配的注释和 TODO。

要求：

- 不为内部 API 保留无实际调用价值的兼容层。
- 纯移动/重命名与行为变化尽量拆开提交。

## [x] TEST-106（P1）：重构并瘦身测试体系

实现：

- 迁移旧测试命名空间到当前项目/功能切片。
- 统一重复 TestKit、fake、visual-tree helper 和 setup。
- 删除只为旧架构/compat wrapper 存在的测试。
- 合并完全重复、低故障价值的 ViewModel 转发测试。
- 保留 migration、fixture、缺陷回归、并发/取消、安全和 WPF 关键契约。
- 让纯单元测试尽可能并行，不因 WPF 测试全局禁用并行。

自动验收：

- 五个测试项目职责与引用边界测试通过。
- 删除测试后核心行为覆盖仍由契约/回归测试固定。

## [x] AUDIT-107（P1）：设计—实现一致性源码审计

对照 `docs/00–12` 自动/源码级审计：

- 文档已有但实现缺失的交互。
- 已实现但文档仍写“后续”的残留。
- UI 文案、按钮状态、导航入口和生命周期不一致。
- 重复或局部自建的平台能力。

低风险差异在任务内修复；涉及新公共合同或跨 Wave 功能的差异追加到对应现有任务说明，不创建无边界的大杂烩任务。

### Wave 1 Gate

- 完整质量门禁通过。
- 不新增非允许的 `IServiceProvider`/同步异步阻塞/无 owner Task。
- 资源释放专项测试通过。

---

# Wave 2：全局视觉与桌面交互基础

目标：先统一可复用视觉和选择语义，避免后续每个页面分别实现。

## [x] UI-201（P1）：重构全局图标按钮和交互状态层

实现：

- 扩充 `DesignTokens.xaml` / `SemanticStyles.xaml`。
- 普通 icon button 默认无边框；hover 使用圆角矩形背景，不出现方形 stroke。
- pressed、disabled、keyboard focus 使用统一状态。
- 媒体控制保持圆形/近圆形状态层。
- 清理页面内重复 button Trigger/局部样式。

自动验收：

- WPF resource/style contract 测试。
- 关键图标按钮存在 Tooltip/AutomationName。

## [x] UI-202（P1）：建立设置行与无边框导航入口组件

实现：

- 收敛并扩展现有 `SettingsGroupBorderStyle`、`SettingsSubpageItemBorderStyle` 和 `SettingsEntryButtonStyle`，建立轻量 Setting Row/Group 样式。
- 复用设置首页已有的 `icon + 标题 + Chevron` 整行入口，将同一语义扩展到二级进入三级的导航。
- 二级进入三级页面不再使用传统按钮外观。
- 入口不显示冗余说明文字。
- Hover/focus 复用 UI-201 状态语言。

## [x] UI-203（P0）：建立可复用桌面多选模型

实现：

- Presentation 层实现与虚拟化解耦的 selection state/controller。
- 支持单击、Ctrl 增减、Shift 区间、Ctrl+A、Esc。
- 维护 anchor/primary selection。
- 建立统一 Selected Card 视觉状态。
- 不依赖 checkbox 或已生成的 WPF item container 保存选择事实。

自动验收：

- 选择、区间、全选、取消、列表变化和虚拟化边界测试。

## [x] UI-204（P1）：统一 UI 文案与 icon 使用原则

实现：

- 全局审计明显冗长或不一致的用户文案。
- “清理缓存”语义统一为“清理”；实体操作使用“删除”。
- 适合纯图标的工具按钮替换文本，同时补 Tooltip/AutomationName。
- 主动作、危险确认和含义不明确的动作保留文字。
- 不在本任务提前修改 Wave 4 缓存页面的功能结构。

### Wave 2 Gate

- Design Token/semantic style 测试通过。
- 多选 controller 单元测试通过。
- 完整质量门禁通过。

---

# Wave 3：规则工作台与设置体验

目标：减少按钮堆叠，统一 dirty state、列表卡片和设置视觉。

## [x] RULE-301（P1）：重构 TTS 规则工作台

实现：

- 左侧卡片承载名称、摘要、启用状态、当前规则入口和 `⋮`。
- `⋮` 放导出、删除等低频动作。
- 右侧只保留规则字段、试听、取消、保存。
- 无修改时取消/保存禁用；有修改时启用。
- 试听始终使用当前编辑副本。
- dirty-state guard 覆盖规则切换、返回、一级导航、快捷键和退出。
- 调整左栏宽度/卡片排版，使管理操作不再挤占右侧。

自动验收：

- dirty state、当前规则、启用、导出、删除、试听和导航 guard 测试。

## [x] RULE-302（P1）：统一章节规则工作台

实现：

- 左侧名称、摘要、启用、拖动排序、`⋮`。
- `⋮` 提供删除、上移/下移备用操作。
- 右侧聚焦字段编辑、取消、保存和帮助。
- 无修改时取消/保存禁用。
- 默认规则导入/恢复继续使用现有语义，不显示“内置/自定义”标签。

## [x] RULE-303（P1）：统一正则替换工作台

实现目标与 RULE-302 一致，并保持 Display/Speech pipeline 语义和播放刷新规则不变。

自动验收覆盖：排序、启用、空输出、dirty state、缓存键和当前播放刷新。

## [x] SET-304（P1）：重构设置首页和普通设置子页

实现：

- 保留设置首页已符合 `icon + 标题 + Chevron` 的入口，并迁移到 UI-202 收敛后的导航样式。
- 播放设置、导入与文本、缓存与数据、外观、诊断与关于改为统一 Setting Row 视觉。
- 正则替换、缓存管理等三级入口统一。
- 删除页面内重复圆角/边框/hover 样式。
- 文案按最终产品口径清理。

### Wave 3 Gate

- 三类规则工作台的 dirty-state/guard 自动测试通过。
- 设置导航和 semantic style WPF 测试通过。
- 完整质量门禁通过。

---

# Wave 4：主动缓存、缓存管理与 MP3 导出

目标：把“播放时顺便缓存”升级为可管理的章节级后台工作流，并提供可靠章节导出。

## [x] CACHE-401（P0）：建立应用级主动缓存协调器

前置：SYNC-104、UI-203。

实现：

- 新建 Application active-cache use case/coordinator，不挂在 PlayerViewModel。
- 全应用只允许一个 active batch。
- 批次冻结 Book/Chapter、TTS rule、语速和文本处理快照。
- 按章节、段落顺序处理；命中有效缓存直接跳过。
- 播放页切章、页面离开、主窗口隐藏不取消批次。
- 支持取消，已完成缓存保留。
- 发布 `ActiveCacheSnapshot`：总进度、当前章节、章节状态和安全错误摘要。

## [x] CACHE-402（P0）：实现共享优先级 TTS admission

前置：CACHE-401。

实现：

- 复用 SYNC-104 已建立的 `TtsAdmissionPriority` 和按规则异步优先级队列，不重建第二套调度器。
- 将 CACHE-401 的请求以 `ActiveCache` 优先级接入现有限流器，并固定播放/预取已有映射。
- 当前播放 > 预取 > 主动缓存。
- 同一规则所有请求共享并发/速率上限。
- 主动缓存不能绕过 limiter 或创建独立 HTTP client。
- 取消等待不占许可；失败不会卡死队列。

自动验收：

- 可控 barrier/clock 下验证优先级、取消、并发上限和无死锁。

## [ ] CACHE-403（P1）：播放页章节主动缓存选择体验

实现：

- 播放页增加缓存工具图标。
- 点击后章节列表进入选择模式，复用 UI-203。
- 选择模式中普通单击只选择，不跳章。
- 显示已选章节数、取消选择和开始缓存。
- 已存在 active batch 时不允许启动第二批次，并明确展示当前状态。
- 退出选择模式后恢复正常章节点击语义。

## [ ] CACHE-404（P1）：Shell 主动缓存状态与进度 Flyout

实现：

- 有 active batch 时显示 `缓存中 · x/y 章 · n%`。
- Flyout 显示各章完成/当前/等待状态、段落进度和取消。
- 页面切换后仍可见。
- PlayerPage 与 Shell 订阅同一 Application snapshot，不复制状态。
- 完成后入口消失，Snackbar 提示结果。

## [ ] CACHE-405（P1）：重构缓存管理为文件管理器式章节操作

前置：UI-203。

实现：

- 左侧保持单书选择，不支持跨书多选。
- 右侧章节卡片使用统一多选模型和 selected visual state。
- 删除逐章清理按钮和“清理本书”按钮。
- 顶部只保留“清理”“导出”；0 项选择时禁用。
- 清理只处理选中章节；选择全书全部章节等价于清理本书。
- 将“清理全部缓存”移动/保留在“缓存与数据”二级页。
- 章节卡片明确显示当前配置下的缓存完整度。

## [ ] EXPORT-406（P0）：建立章节 MP3 导出用例

实现前先通过代码/依赖审计确定统一 MP3 编码方案，标准：自包含发布可靠、许可证兼容、可测试、资源可释放。

实现：

- 输入为当前书 + 选中章节 + 当前 TTS/语速/文本配置快照。
- 仅完整缓存章节可导出，不自动补全。
- 同章节多个缓存段按播放顺序统一编码/合并为一个 MP3。
- 不同章节分别输出。
- 用户选择根目录，自动建立安全书名子目录。
- 文件名含章节序号和章节名。
- 集中 filename sanitizer 处理非法字符、Windows 保留名、尾部点/空格和路径边界。
- 已存在文件生成 ` (2)` 等后缀，不覆盖。
- staging/encoder/stream 在成功、失败、取消时确定性释放。

自动验收：

- 顺序合并和输出音频可解码测试。
- 不完整缓存拒绝测试。
- 文件名参数化测试。
- 同名冲突和取消/失败 cleanup 测试。
- publish 包依赖检查。

## [ ] EXPORT-407（P1）：接入缓存管理导出 UI

前置：CACHE-405、EXPORT-406。

实现：

- “导出”在选中章节全部可导出时启用。
- 不可导出章节在卡片上给出简洁状态/Tooltip。
- 目录选择通过统一 presentation file dialog port。
- 导出期间显示可取消进度，完成后 Snackbar 摘要并支持打开目录。

### Wave 4 Gate

- 主动缓存优先级/跨页面/取消测试通过。
- 多选缓存清理测试通过。
- MP3 导出和文件名安全测试通过。
- 完整质量门禁 + 自动 publish 内容检查通过。

---

# Wave 5：Windows 媒体、托盘、迷你播放器与定时停止

## [ ] MEDIA-501（P1）：Windows 系统媒体控制与耳机按键

实现：

- 建立平台无关 media-control port 和 Windows adapter。
- Play/Pause 映射播放/暂停。
- Previous/Next 映射上一/下一段。
- 系统媒体信息显示当前章节标题、书名和播放状态。
- 平台回调不直接触碰 WPF ViewModel/控件。

自动验收：

- adapter contract/fake platform 测试。
- 播放状态到系统元数据投影测试。

## [ ] TRAY-502（P1）：关闭行为、托盘与常规设置

实现：

- 新增“常规”设置二级页。
- 关闭主窗口：最小化到托盘 / 退出应用 / 每次询问。
- 启动后最小化到托盘。
- 托盘菜单：显示主窗口、播放/暂停、迷你播放器、退出。
- Close、Hide、显式 Exit 使用单一 desktop lifecycle coordinator。
- 未保存导航 guard 只在真正退出时阻止进程关闭；隐藏到托盘不误触发完整 shutdown。

## [ ] MINI-503（P1）：迷你播放器

前置：TRAY-502。

实现：

- 播放页和托盘菜单提供入口。
- 打开后隐藏主窗口。
- 显示书名、当前章节、上一/下一章、上一/下一段、播放/暂停、进度条。
- 提供置顶和恢复主窗口。
- 关闭迷你窗口等价于恢复主窗口。
- 记忆窗口位置和置顶；不记忆迷你模式。
- 复用现有 PlaybackSnapshot，不创建第二套播放状态机。

## [ ] TIMER-504（P1）：播放页定时停止

实现：

- 播放页计时器 icon + Flyout。
- 15/30/45/60/90 分钟、自定义时长。
- 当前段结束、当前章节结束。
- 取消定时停止。
- 到时只 Pause；主动缓存继续。
- 定时状态不持久化。

自动验收：

- 使用 fake `TimeProvider`，不依赖真实等待。
- session 替换、取消、段末、章末和后台缓存独立性测试。

### Wave 5 Gate

- 媒体、托盘、迷你窗口、定时停止状态机自动测试通过。
- 进程 shutdown/隐藏路径无重复 dispose 或未观察异常。
- 完整质量门禁通过。

---

# Wave 6：体验一致性与全仓收口

## [ ] UX-601（P1）：章节列表“定位到当前章节”

实现：

- 书籍详情目录和播放页章节目录均增加悬浮定位按钮。
- 只有用户滚动使当前章节离开可见区域时显示。
- 点击后使用虚拟化安全定位和平滑滚动。
- 到达当前章节后自动隐藏。
- Tooltip/AutomationName 为“定位到当前章节”。

自动验收：滚离/回到、虚拟化索引和章节切换测试。

## [ ] UX-602（P1）：全局交互状态与文案收口

审计并修复：

- 应可禁用但仍可点击的保存/取消/清理/导出/缓存动作。
- 空状态、加载状态、错误状态和 Snackbar 文案。
- 仍出现方形 hover 边框的 icon button。
- 可以合理替换为 icon 的低频工具文字按钮。
- 二/三级入口仍使用传统执行按钮的页面。
- “清理本书缓存”等与最终文案不一致的残留。
- Tooltip、AutomationName、键盘 focus 和 disabled 视觉。

## [ ] CLEAN-603（P2）：最终全仓 cleanup/refactor 复审

在所有新功能稳定后再次审计：

- 将 `LibraryViewModel` / `LibraryImportCoordinator` 的本地文件检查，以及 `TtsRulesViewModel` 的规则文件读写，收敛到明确、可取消且可测试的 Application/presentation operation port；文件选择继续复用现有对话框 port。
- 新增功能形成的重复 DTO、adapter、helper 和状态复制。
- 死代码、空目录、旧测试、临时 migration bridge。
- `async void`、未登记 Task、通用 catch、同步阻塞异步。
- 资源 Dispose/CTS/事件退订。
- 文档与代码中的“后续/暂时”历史注释。
- 未使用依赖和 publish 冗余。

不得删除 migration、必要 fixture、安全防御代码或真实平台 adapter。

## [ ] DOC-604（P1）：按实际实现更新用户文档

前置：本阶段功能任务完成。

实现：

- 根 `README.md` 增加已经真正实现的主动缓存、MP3 导出、媒体/托盘/迷你播放器/定时停止能力。
- 核对快捷键、设置入口和当前限制。
- 数字文档只修正最终实现偏差，不写迁移历史。
- 已完成 Backlog 可再归档摘要，但当前任务状态要保持可追溯。

## [ ] QA-605（P0）：最终自动发布质量门禁

只使用自动化验证：

- locked restore / format / Release build / 全量 test。
- 架构依赖和源码规则测试。
- migration/路径安全/资源释放/并发取消测试。
- 主动缓存、导出、媒体、托盘、迷你窗口、定时停止测试。
- self-contained `win-x64` publish。
- 自动检查发布目录和 ZIP 不含测试资产/临时文件，且包含所需许可证和运行依赖。

完成条件：所有自动阻塞项通过，发现的非阻塞问题必须被修复或记录为新的明确 Backlog，不使用“人工验证待完成”作为阶段尾项。

---

## 5. 阶段完成标准

- DI/生命周期/资源所有权没有已知高风险边界债务。
- 代码和测试已完成一轮激进但受保护的清理，不为历史架构保留无用 compat API。
- 全局 UI 使用统一 Design Token、圆角 hover/focus 语言和设置导航样式。
- TTS、章节、正则规则工作台操作职责清楚，dirty state 一致。
- 主动缓存支持多章节选择、后台进度、取消和共享限流优先级。
- 缓存管理采用文件管理器式多选，清理和导出作用于选中章节。
- 完整章节可稳定导出为按章独立 MP3，文件名和同名冲突安全。
- Windows 媒体控制、托盘、迷你播放器和定时停止共享同一个播放状态。
- 书籍详情和播放页均能快速定位当前章节。
- 根 README 与实际发布能力一致。
- 完整自动质量门禁和 publish 检查通过。
