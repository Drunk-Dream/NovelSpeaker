# 测试与质量

## 1. 原则

- 测试保护用户可观察行为、数据兼容、安全边界和状态机，不保护私有实现形状。
- 缺陷修复先增加可复现的失败测试。
- 重构允许删除因旧架构存在、低价值重复或只验证属性转发的测试。
- migration、规则 fixture、损坏音频、路径安全样本和 WPF Test Host 属于受保护资产。
- 开发计划和任务验收使用自动测试/自动检查，不把“人工点一遍 UI”列为完成条件。

## 2. 测试项目职责

### `NovelSpeaker.Domain.UnitTests`

- 纯值对象、规则和领域约束。
- 不访问文件、SQLite、网络或 WPF。

### `NovelSpeaker.Application.UnitTests`

- 用例、状态机、缓存键、优先级、取消、错误映射。
- 使用 fake port，不依赖真实技术 adapter。

### `NovelSpeaker.Infrastructure.IntegrationTests`

- SQLite migration/repository。
- 文件与路径安全。
- HTTP transport、Jint、NAudio、缓存文件。
- 使用本地 fake server/fixture，不访问真实第三方服务。

### `NovelSpeaker.App.PresentationTests`

- 导航、activation、选择 controller、滚动协调、Shell presentation port。
- 尽量不启动真实 WPF Window。

### `NovelSpeaker.App.WpfTests`

- 必须依赖 WPF visual tree、STA、资源字典或窗口行为的少量测试。
- 需要数据库的 WPF 测试使用隔离临时数据目录，并显式完成 schema 初始化，不依赖开发机本地数据或测试顺序。
- 通过 `tests/TestKit/Wpf/WpfTestHost.cs` 共享唯一后台 STA Dispatcher；窗口测试统一使用 `WpfWindowHost`，布局/离屏渲染测试使用 `WpfControlHost`。
- 默认窗口位于虚拟屏幕之外且不激活、不进入任务栏；仅设置 `NOVELSPEAKER_TEST_SHOW_WINDOWS=1` 时允许可见调试窗口。
- 测试失败时由共享宿主写入 `TestResults/wpf-diagnostics/<test-name>/`，包括 PNG、视觉树和窗口状态；成功测试不生成视觉文件。

## 3. 测试清理准则

可以删除或合并：

- 完全重复同一行为路径的测试。
- 只因旧接口/compat wrapper 存在的测试。
- 过度验证 ViewModel getter/setter 转发的测试。
- 与更高价值契约测试完全重叠且没有额外故障信号的测试。
- 重复 fake、视觉树 helper 和 setup。

必须保留或增强：

- 缺陷回归。
- 数据 migration、损坏数据和升级兼容。
- 播放/缓存/规则状态机。
- 并发、取消、迟到结果和资源释放。
- 路径安全和脚本沙箱。
- TTS parser/compiler fixture。
- 关键 WPF 生命周期和 keyboard/selection 契约。

## 4. 缓存重构重点测试

### 架构

- 非 Bootstrap 代码禁止新增 `IServiceProvider`。
- Application 不暴露具体技术类型。
- App 页面不直接依赖 Infrastructure。

### 主动缓存

- 单批次限制。
- 章节顺序、缓存命中跳过、取消和失败。
- 切章/离开页面不取消后台批次。
- 配置快照冻结。
- `播放 > 预取 > 主动缓存` admission 优先级。
- 同一 TTS 规则共享 limiter。
- 冻结的稳定段身份与当前播放使用同一 AudioCacheKey 语义。

### 缓存身份与朗读清单

- 正文缓存身份不依赖运行时 `SegmentIndex`。
- 开关“朗读标题”不改变正文段身份，标题段独立命中和失效。
- TTS 请求语义变化时 `TtsRuleFingerprint` 变化；只改名称、启用状态或并发限制时保持不变。
- `TextProfileFingerprint` 变化但最终计划输出未变化时，不重写段表且继续复用音频。
- 每章始终只有一份当前朗读清单；反复修改配置不会形成历史版本倍增。
- 计划替换的取消、失败和进程中断不留下半套数据。
- 完整度查询能区分清单缺失、清单过期和有效 0%，并验证过期清单触发同章去重的后台重建。
- 普通目录查询不补建缺失清单；缓存管理查询会修复有缓存但清单缺失的异常状态。

### SQLite 与性能

- version 6 到新版 schema 7 的追加 migration、重复启动和高版本拒绝。
- 旧缓存索引和内部缓存文件按明确重置边界清理，不建立兼容读取路径。
- 哈希 BLOB、`WITHOUT ROWID` 和单计划策略的数据库体积测试。
- 2,000 和 10,000 段完整度批量查询不得调用文件探测或音频解码。
- 一次批量刷新使用常数级连接/SQL 次数，不逐章或逐段打开连接。
- 缓存完整度查询不更新 `LastAccessedAt`。
- 前台查询返回过期状态时不等待正文读取；后台重建完成后发布按章节定位的刷新通知。

### 缓存管理/导出

- Ctrl/Shift/Ctrl+A 选择模型。
- 清理只作用于所选章节。
- 混合选择时确认跳过不可导出章节；取消确认不打开目录，全部不可导出不开始导出。
- 导出命令启用矩阵、目录选择取消、重复启动、页面离开和取消/失败反馈投影。
- 导出进度、取消、打开目录、章节状态 Tooltip 和 AutomationName 的 WPF 契约。
- 多段按顺序输出一个 MP3。
- 文件名非法字符、保留名、尾部点/空格和同名冲突处理。
- 导出取消/失败不会覆盖用户已有文件或留下临时文件。
- 缓存管理页物理条目/大小与当前配置完整度的统计口径分离。
- 缓存管理页保留并显示当前配置 0% 的有缓存章节；普通目录继续隐藏 0% 和非正常状态。
- 导出开始时严格验证文件和解码状态，不把目录完整度作为最终有效性证明。

### 删除与维护

- 删除书籍级联删除章节朗读清单、清单段和缓存索引。
- operation journal 保留内部音频路径，删除中断后可以恢复且不触碰外部源文件。
- 删除完成后数据库和 `Cache/Tts` 不存在该书残留。
- 缓存健康维护发现缺失或损坏文件后修正索引并发布章节变化通知。
- 清理、失效、健康维护或 LRU 删除某章最后一条缓存后，同一事务删除该章朗读清单及级联段记录；仍有缓存或受保护条目时保留清单。

### 桌面媒体

- 系统 media command 到播放命令映射。
- 迷你窗口隐藏/恢复、置顶状态视觉、章节段落进度投影/拖动/Tooltip 和空白区域拖动边界。
- 托盘 close/exit 状态机。
- 定时停止使用可控 `TimeProvider`。

### UI 与样式体系

- 导航、播放、选择、Dirty State、缓存和页面生命周期继续以用户可观察行为测试为主。
- 应用级资源不得出现接管标准 WPF/Wpf.Ui 控件的 NovelSpeaker 隐式样式；允许项必须有最小局部作用域和明确白名单。
- Wpf.Ui provider dictionaries、Provider Style Bridge、NovelSpeaker palette/tokens 和具名样式的加载顺序由资源契约测试固定。
- 主题切换不得在运行时代码中重新插入 Style 或 ControlTemplate；已打开窗口和 Style Gallery 场景使用 `DynamicResource` 更新颜色。
- 共享样式默认不得替换标准控件完整模板；经视觉系统明确批准的控件族级模板例外必须集中在对应资源族中，并由专项测试证明状态、内容对齐、键盘/鼠标命中、Focus、Disabled、Editable、Popup 和主题切换语义完整。页面级模板覆盖仍禁止。
- Design Token 只包含跨组件稳定值；架构测试阻止页面列宽、设置控件宽度、工作台分栏等页面几何进入全局令牌。
- Style Gallery 自动覆盖浅色/深色下的 Default、Hover、Pressed、Focus、Disabled、Selected 和 Error 场景。
- ComboBox 视觉/交互回归以稳定的 `inputs` Gallery family 为入口：宽控件保持左侧文案、右侧 Chevron 和全表面点击命中；Popup 使用 Raised Surface、Subtle Border、Medium Radius、Medium Elevation，并与闭合态保持约 4 px 间隔；Popup 宽度不小于闭合态控件；Normal/Hover/Selected/Disabled Item 分别验证透明、Secondary、Accent.Subtle + 左侧 Accent 状态条、Tertiary 文本；纯字符串长选中项单行省略且不得挤压或移动 Chevron；对象项/自定义模板在存在长文本时提供等价截断。
- Shell 视觉契约测试固定内容背景所有权：`NavigationView` 内容宿主跟随 `App.Brush.Canvas` 与边界语义并保留非零左上圆角，已迁移正式 Page 根背景保持透明；Light/Dark 热切换后 Provider 投影键必须与应用 Palette 同步，页面不得用不透明根背景重新遮住 Shell 圆角。
- 设置页面结构契约区分首页与子页面：`SettingsPage` 首页继续允许并要求按导航类别使用 `AppSettingsGroup`；具体设置子页面不得包含 `AppSettingsGroup` 或重复分类 Header，设置项以 `AppSettingsRow`/`AppSettingsNavigationRow` 的单一扁平列表呈现。已迁移的 Appearance/General 与后续 Playback/ImportText/CacheData/Diagnostics 页面均由静态 XAML 契约或视觉树测试固定这一边界。
- 设置子页面几何测试必须覆盖 `AppSettingsRow` 脱离 Group 的独立布局：宽/窄窗口、长说明、ToggleSwitch/ComboBox/TextBox/导航行以及 100/125/150% DPI 下均不得依赖 Group Padding、ItemContainer 或 Group Header 才能获得正确对齐和命中区域。
- 自动截图工具在固定 DPI、窗口尺寸和测试数据下生成 PNG 与 manifest；任务验收只要求可重复生成、尺寸正确、场景完整和无渲染异常，不以主观审美作为自动关闭条件。
- 几何测试只固定最小点击区域、非零可用宽度、关键内容可见和不重叠等下限，不冻结尚可调整的精确 Padding、Margin、Width 或 Height。
- 迷你播放器覆盖隐藏/恢复、置顶、段落进度、Tooltip、拖动边界、长标题和主题热切换。
- 页面视觉迁移必须保留原有命令启用、键盘焦点顺序、AutomationName、虚拟化和滚动行为。

## 5. 异步测试

- 不使用任意 `Task.Delay`、`Thread.Sleep` 等“等一会应该好了”的测试。
- 等待明确事件、Task、状态版本、channel 或 fake clock。
- 取消必须验证 `OperationCanceledException`/Cancelled 语义，不把取消当 Error。
- 并发测试通过可控 barrier/gate 排列时序。
- 共享 Test Doubles 放在 `tests/TestKit`；跨测试项目复用的缓存身份和音频 fixture helper 不在各项目重复定义。

## 6. WPF 视觉产物

- 默认 `dotnet test` 不生成 PNG、manifest 或仓库内审计文件。
- Style Gallery、媒体控件和迷你播放器截图测试只有在设置 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 时运行。
- 视觉产物写入显式输出目录；失败诊断与成功截图分开，CI 仅在 WPF job 失败时上传 `TestResults/wpf-diagnostics`。

## 7. 自动质量门禁

完整门禁固定为：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

涉及发布内容时额外执行 self-contained `win-x64` publish 和自动包内容检查，确保：

- 主程序、许可证和第三方声明存在。
- Windows Media Foundation MP3 编码所需的 NAudio runtime assemblies 存在。
- 不包含测试程序集、TestAssets、损坏音频 fixture 或临时文件。

## 8. 任务验收

每个 Backlog 任务至少定义：

- 针对性自动测试。
- 受影响项目的 build/test。
- 行为/数据/安全边界改变时的回归测试。

阶段收口运行完整质量门禁。视觉任务额外生成 Style Gallery 或目标页面的浅色/深色截图和 manifest，作为用户后续查看的产物，但任务完成条件只使用自动构建、契约、几何、可访问性和渲染检查。
