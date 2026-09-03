# 决策、风险与开放问题

## 1. 已确认决策

### 产品范围

- 主线继续聚焦本地 TXT + HTTP TTS，不在本阶段加入 EPUB、在线书源或云同步。
- 根 README 只描述已经实现的功能；数字文档描述目标终态。

### 缓存

- 主动缓存以章节为选择粒度，后台批次与当前播放章节无关。
- 全应用一次一个主动缓存批次。
- 当前播放 > 预取 > 主动缓存，且同规则共享并发/速率限制。
- 缓存管理采用 PC 文件管理器式章节多选，不跨书籍多选。
- “清理全部缓存”放在“缓存与数据”二级页。
- 缓存身份不使用运行时 `SegmentIndex`；正文段使用稳定来源身份，章节标题作为独立合成段。
- `TextProfileFingerprint` 只用于判断当前章节朗读清单是否需要重算，不进入音频缓存键。
- 音频缓存使用最终 `SpeechText` 哈希和版本化 `SynthesisProfileFingerprint`；编辑现有 TTS 规则的请求语义后不得错误复用旧音频。
- 数据库每章只保存一份当前朗读清单，不保存 `TextProfile` 历史版本，不保存完整 `SpeechText`。
- 不实现跨书籍或跨章节全局音频去重。
- 新缓存格式不迁移旧缓存；升级时重置旧索引和应用内部缓存文件。
- 正常完整度查询只聚合 SQLite，不逐文件检查或解码；播放、导出和低优先级健康维护负责严格验证。
- 完整度查询发现过期朗读清单时必须登记后台重建；普通目录不补建缺失清单，缓存管理页对有缓存但缺失清单的异常状态自动补建。
- 删除某章最后一条缓存索引时，在同一 SQLite 事务中删除对应朗读清单和级联段记录，不等待启动维护。
- 朗读清单可在当前进程内暂时先于缓存存在，但不是无缓存章节的长期历史数据；启动缓存维护在索引修复/LRU 淘汰完成后，集合式清理所有不存在任何 `AudioCacheEntries` 的残留 `ChapterSpeechPlans`，段表依靠外键级联删除。

### 导出

- 只导出当前配置下完整缓存的章节，不自动补全。
- 混合选择完整与不可导出章节时，由用户确认是否跳过不可导出章节；导出用例仍对提交的完整章节子集
  做一次性严格验证，避免确认后缓存变化产生部分输出。
- 每章一个 MP3，同章多段合并，不生成整书单文件。
- 自动创建书名目录，安全清理文件名，同名使用编号后缀且不覆盖。
- MP3 统一使用现有 NAudio 2.2.1 的 Windows Media Foundation 编码适配器：输入规范化为
  44.1 kHz 双声道 PCM，输出 128 kbps MP3，不新增或随包携带第三方 native encoder。
- 章节导出提交后由独立进程级 coordinator 持有，全应用最多一个批次；页面离开/托盘隐藏不取消，真正退出应用才取消并有界等待。
- 导出进度只在 Shell Footer/Flyout 投影。成功后保留“导出完成 · N 章”直到用户打开目录或关闭；失败/取消 Snackbar 后清除。当前不建设通用后台任务中心。
- 缓存管理页 PageHeader 使用“已选择 N 章”+ 清理/导出图标；右侧不显示重复二级 Header，章节卡片统一铺满可用宽度。

### 桌面体验

- 系统 Previous/Next 映射上一/下一段。
- Windows 系统媒体控制使用 `SystemMediaTransportControls`，由 App 中的 Windows adapter 接入 Application port。
- 托盘使用 App 中基于 `Shell_NotifyIconW` 的小型 Windows adapter，不新增托盘框架依赖。
- 迷你播放器隐藏主窗口；恢复主窗口是独立动作，关闭迷你窗口统一退出应用。
- 记忆迷你窗口位置和置顶，不记忆迷你模式。
- 定时停止支持固定/自定义时长，触发后只暂停播放。
- 关闭主窗口行为支持托盘、退出、每次询问。

### 导航

- 应用级导航不维护浏览器式历史栈或 Back/Forward 链，只维护当前完整 `AppRoute`。
- 普通页面返回使用固定 ParentRoute：BookDetails 返回 Library；设置二级页返回 Settings；RegexReplacementRules 返回 ImportTextSettings；CacheManagement 返回 CacheAndData；Library/Settings 无父级。
- Player 是唯一动态返回页面；`PlayerRoute` 显式携带一次性的 `ReturnRoute`。从书库进入返回 Library，从详情页进入返回同一 `BookDetailsRoute(BookId)`，从 Shell“正在播放”入口进入则返回进入 Player 前捕获的完整当前路由。
- `ReturnRoute` 不允许指向 Player，也不递归保存历史链；Player 内部定位/切章不得重置来源。
- PageHeader、`Alt+Left` 与未被局部临时交互消费的 `Esc` 共用应用级 `NavigateBackAsync`；根路由返回不隐式跳转到 Library。
- Wpf.Ui `NavigationView` 的内部 history/cache 不作为业务状态或参数恢复来源，不通过页面 Singleton/缓存来修补返回。

### 阅读进度

- `PlaybackSnapshot` 是当前活动书籍的运行时即时真值；SQLite `ReadingProgress` 是持久化 checkpoint 和非活动/重启恢复基线。
- Library、BookDetails、Player 不各自维护第三套可变阅读位置。跨页面显示通过 Effective Reading Progress 纯投影在 BookId 匹配时用 Snapshot 覆盖持久化基线。
- 显式章节/段落跳转成功后必须 checkpoint 新位置；不能依赖下一次 session 替换时保存“旧 session”来间接完成上一次跳转。
- UI 即时一致性不以 SQLite 已经完成回写为前提；持久化最终仍必须在明确 checkpoint 边界收敛，保证应用重启恢复正确。

### UI

- 保留 Fluent/Wpf.Ui 信息架构和标准控件模板，通过 NovelSpeaker palette、具名样式和自有组件逐步形成统一视觉。
- 不在 Application/global 作用域定义接管标准 WPF/Wpf.Ui 控件的 NovelSpeaker 隐式样式。
- 主题切换只更新 Wpf.Ui 主题和 NovelSpeaker palette，不在运行时代码中恢复或重写 Style 类型资源。
- 完整模板替换只用于 NovelSpeaker 自有组件或严格局部的具名样式，不作为全局统一手段。
- 全局令牌只保存跨组件稳定标尺；页面密度、分栏和固定宽度由对应布局 owner 管理。
- 普通图标按钮 Hover 使用圆角状态层，不使用突兀方形边框。
- 二/三级入口使用 `icon + 标题 + Chevron` 导航行。
- 三类规则卡片统一为左右布局：左侧名称/摘要，右侧 ToggleSwitch；启用状态是列表级即时设置，不属于编辑草稿或 Dirty State。
- 规则页进入时不自动打开任何规则。单击卡片才打开编辑器；编辑器打开后“取消”始终可用并关闭编辑器，“保存”只在草稿修改且校验通过时可用。切换规则或离开页面仍受 Dirty State 导航保护。
- 规则页不显示 `⋮` 更多按钮。单规则导出和删除使用卡片右键菜单；章节/正则的上移/下移也位于右键菜单。右键不切换当前编辑对象，键盘仍可打开同一 ContextMenu。
- “从文件导入”“从剪切板导入”属于页面级动作并放入 `AppPageHeader.Actions`；不提供页面级导出。导入采用合并策略：完全重复跳过，同名不同内容作为新规则，不覆盖现有规则。
- TTS 规则页不再提供“当前规则/设为当前”。播放页只显示已启用 TTS 规则并独占当前规则切换；当前规则被禁用或删除时清空选择，不自动回退，导入、新建或重新启用也不自动成为当前规则。
- 章节规则和正则替换取消拖动手柄，使用整卡长按约 300 ms 后拖动；采用插入线、轻量拖动态反馈和边缘自动滚动，不实现相邻卡片位移动画，排序只在 Drop 后持久化。
- 播放页和书籍详情目录中的 0% 缓存完整度继续不显示。
- 缓存管理页显示全部有缓存章节，当前配置为 0% 时也显示该章节和 `0%`。
- 开发用 Style Gallery 和自动截图工具不进入正式导航和发布包。
- Dialog、Flyout、Popup 和独立状态浮窗采用 Single Surface 原则。ContentDialog 自身是唯一主 Surface，内部使用透明 `App.Feedback.DialogBody`；`StartupStatusWindow` 由 Window 自身承担 Raised Surface，并使用 `AppStatusView.IsEmbedded=true` 避免第二层 Section 卡片。复杂 Dialog 只有在存在明确独立二级信息分组时才允许弱背景或 Divider，默认不使用完整 Card-in-Dialog。

### 架构与测试

- 允许激进清理内部 API、目录、重复抽象和低价值测试，只要书籍、规则、阅读进度等持久数据与目标行为受测试保护；音频缓存属于明确可重置数据。
- `IServiceProvider` 只留在组合根/框架桥接。
- 共享 TTS admission 使用进程内、按规则隔离的异步优先级队列；当前播放和预取已通过 `TtsAdmissionPriority` 接入，主动缓存由 CACHE-402 复用同一入口并以 `ActiveCache` 优先级接入。
- 当前测试精简阶段以完整自动测试总数 `<800` 作为一次性验收目标，不建立永久测试数量上限；后续有价值的回归测试允许总数重新增长到 800 以上。
- WPF 自动测试默认不得在用户当前交互 Desktop 显示窗口。普通控件/页面优先无 Window 宿主；真实 Window/Popup/Focus/HWND 生命周期统一进入 TestKit 创建的隔离隐藏 Windows Desktop，隔离初始化失败时 fail closed。
- 只有用户在当前任务明确允许可见窗口时才可启用 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；视觉产物生成不构成该授权。
- 不为下一阶段规划依赖人工验证才能关闭的任务。

### 数据目录与运行产物

- 正式版统一使用程序目录下的 `Data/` 作为应用数据根目录，数据库文件名保持为 `app.db`。
- 默认开发启动使用 `%LocalAppData%/NovelSpeaker.Dev`，自动测试继续使用测试自己的临时目录；三者不得互相复用数据。
- `NOVELSPEAKER_DATA_ROOT` 作为开发/诊断场景的显式根目录覆盖入口，优先级高于默认开发目录；不使用编译配置隐式切换数据位置。
- 旧 `%LocalAppData%/NovelSpeaker` 数据不迁移、不探测、不回退读取；该决策只针对数据根目录切换，不删除 SQLite schema migration 能力。
- 发布主程序统一命名为 `NovelSpeaker.exe`；项目名、命名空间和源码目录无需因此改名。运行时代码不得硬编码主程序文件名，应从当前进程路径获取实际可执行文件位置。

## 2. 主要实现风险

### TTS admission 公平性

共享 limiter 需要保证播放低延迟，同时不能让主动缓存形成不可取消的永久饥饿。应通过优先级队列和自动并发测试验证，而不是多个独立 semaphore。

### MP3 合并

缓存来源音频格式可能不同。导出应在受控解码后统一编码 MP3，而不是假设字节级拼接总是合法；实现前确认所选编码器的部署、许可证和自包含发布影响。

### WPF 多选与虚拟化

Ctrl/Shift 选择、anchor、滚动定位和虚拟化容器可能产生边界问题。选择状态必须独立于当前生成的视觉 item。

### 托盘/迷你窗口关闭

主窗口 Close、隐藏到托盘、迷你窗口 Close 和显式 Exit 是不同事件；必须有单一 desktop lifecycle coordinator 防止重复关闭或意外退出。

### 导航来源捕获与参数完整性

Player 的动态返回目标必须在切换 `CurrentRoute` 之前捕获；如果先进入 Player 再读取当前路由，会错误得到 Player 自身并形成无效返回。BookDetails 等参数化路由在任何返回路径中都必须保留完整参数，不能从页面类型、`AppRouteId`、播放会话或旧 Page 实例反推。guard 拒绝/导航失败时也不得提前提交新路由。以上边界应由 Presentation 回归和至少一条参数化页面集成测试共同保护。

### 详情页返回性能

Player 返回 BookDetails 会按强类型路由创建新的 transient Page/VM，因此重新加载本身是正常架构行为；风险在于导航切换、SQLite、章节投影、`ObservableCollection` 通知、初始缓存刷新、当前项定位和 Wpf.Ui transition 可能在同一 Dispatcher 时间窗叠加。`Task.Yield()` 只改变调度时机，`Microsoft.Data.Sqlite` 的 async API 也不能证明同步工作已经离开 UI 线程。性能修复必须先对同一本书做分阶段测量和 A/B 隔离，再只处理有证据的主瓶颈；不得用 Page Singleton、Navigation cache 或全局关闭动效掩盖问题。

### WPF 样式作用域与 Provider 漂移

WPF Style 不是 CSS。应用级隐式样式、合并字典顺序和完整模板替换可能同时改变 Wpf.Ui 默认模板、测量和交互状态。实现必须通过 Provider Style Bridge、具名样式、局部组件和架构测试限制影响范围；Wpf.Ui 升级必须单独执行并重新运行 Style Gallery 与资源契约。

### WPF 测试 Desktop 隔离

独立 Windows Desktop 涉及 Win32 Desktop handle、STA thread 绑定和 WPF Application/Dispatcher 初始化顺序。测试宿主必须在线程进入 WPF 初始化前完成 Desktop 绑定，确定性释放 native handle，并在 CI/非交互会话中保持 fail-closed 行为；任何初始化异常都不得退回用户当前 Desktop。可见调试模式只改变 Desktop/显示策略，不应形成第二套测试生命周期或绕过清理与诊断。

### 布局所有权冲突

Shell、NavigationView、页面和组件同时设置相同边距或宽度会产生重复空白、零宽度编辑器和窗口尺寸相关漂移。每段页面外边距、分栏宽度和组件内部尺寸必须有唯一 owner，几何测试只固定可用下限和不重叠合同。

### 资源释放

HTTP response、NAudio、缓存 staging 和导出临时文件存在长期运行泄漏风险，需要在新增后台任务前完成 owner/cleanup 收口。

### 配置指纹规范化

规则字段顺序、空值、默认请求方法和 Header 大小写如果没有统一规范化，可能导致同一请求语义产生不同指纹，或不同请求语义错误产生相同指纹。必须使用版本化规范序列化和固定 fixture 测试。

### 朗读清单补建

文本配置变化后，已有章节计划可能过期。完整度读取先返回非阻塞状态，再由补建协调器有限并发、可取消、同章去重地重建对应计划；不得同步重建整本书。普通目录不为从未有计划的章节创建计划，缓存管理页只自动修复有缓存但计划缺失的章节。

### 数据库占用

若保存历史 TextProfile、完整 SpeechText、十六进制哈希，或长期保留“曾生成计划但从未留下缓存”的章节计划，数据库都会无意义增长。实现必须坚持单章单计划、BLOB 哈希、段表不重复章节元数据，并由启动维护清理无任何缓存索引的残留计划；通过体积和维护幂等测试建立上限。

### 健康状态最终一致性

完整度查询信任 `Ready` 索引，因此用户从应用外部删除文件后可能短暂显示旧进度。播放和导出必须严格校验，后台健康维护随后修正索引和 UI；不得为追求瞬时一致性恢复逐段解码查询。

### 书籍删除跨数据库与文件系统一致性

SQLite 外键只能级联记录，不能删除音频文件。删除书籍必须先在 operation journal 保存内部路径，再执行安全文件删除和数据库级联；中断恢复和孤立文件清理必须有自动测试。

## 3. 开放问题

当前没有尚待确认且会影响既定产品行为的技术选择。后续新增平台能力仍按发布可靠性、资源所有权清晰、可自动测试、依赖体积和实现复杂度依次评估。
