# 测试与质量

## 1. 测试目标

- 测试保护用户可观察行为、数据兼容、安全边界、状态机和稳定平台合同，不保护私有实现形状。
- 缺陷修复先增加能够复现问题的失败测试，再修改实现。
- 重构允许删除因旧架构、迁移过程、重复覆盖或属性转发而存在的低价值测试。
- 测试数量不是长期质量指标；不通过拆分大量等价场景增加数量，也不通过把无关行为塞进单一 `[Fact]` 人为减少数量。
- migration、规则 fixture、损坏音频/数据、路径安全样本、缓存身份样本和 WPF Test Host 属于受保护测试资产。
- Backlog 任务使用自动测试和自动检查关闭，不把“人工点一遍 UI”或“肉眼确认没有弹窗”作为完成条件。

## 2. 测试项目职责

### `NovelSpeaker.Domain.UnitTests`

- 纯值对象、规则和领域约束。
- 不访问文件、SQLite、网络或 WPF。

### `NovelSpeaker.Application.UnitTests`

- 用例、状态机、缓存身份、优先级、取消、错误映射和资源所有权。
- 使用 fake port，不依赖真实技术 adapter。

### `NovelSpeaker.Infrastructure.IntegrationTests`

- SQLite migration/repository。
- 文件、路径安全和原子写入。
- HTTP transport、Jint、NAudio、缓存文件和本地技术适配。
- 使用本地 fake server/fixture，不访问真实第三方服务。

### `NovelSpeaker.App.PresentationTests`

- 导航、activation、Dirty State、选择模型、Command 启用、滚动协调、Shell presentation port 和错误投影。
- 不依赖 WPF STA、Window 或真实 Infrastructure adapter。
- 简单 getter/setter、构造参数赋值和无分支属性转发不单独建立测试；应由真正的状态转换或调用合同覆盖。

### `NovelSpeaker.App.WpfTests`

- 只保留必须依赖 WPF visual tree、资源字典、布局、主题、键盘/焦点、Popup、窗口生命周期或平台行为的测试。
- 需要数据库的 WPF 测试使用隔离临时数据目录并显式初始化 schema，不依赖开发机数据或测试顺序。
- 共享 WPF 测试基础设施统一位于 `tests/TestKit/Wpf`；测试项目不得各自创建 STA Dispatcher、Window host 或视觉诊断实现。

## 3. 测试精简与职责收敛

可以删除或合并：

- 完全重复同一行为路径的测试。
- UI/架构迁移过程中用于证明旧资源已替换、旧入口已删除的阶段性测试；终态约束只保留一处稳定架构合同。
- 只因旧接口、compat wrapper 或已删除并行实现存在的测试。
- 只验证 ViewModel getter/setter、构造赋值或无分支属性转发的测试。
- 页面级重复验证公共控件内部视觉属性，而同一合同已经由共享 Style/ControlTheme/自有控件测试覆盖的测试。
- 与更高价值契约完全重叠且没有额外故障信号的测试。
- 重复 fake、visual-tree helper、fixture builder 和 setup。
- 同一稳定契约下只改变等价参数的冗余 case；可在保持失败定位清晰的前提下合理参数化。

必须优先保留或增强：

- 已知缺陷回归。
- 数据 migration、损坏数据和升级兼容。
- 播放、主动缓存、导出、规则和生命周期状态机。
- 并发、取消、迟到结果、资源释放和单所有者边界。
- 缓存身份、朗读清单和完整度聚合。
- 路径安全、原子文件写入和脚本沙箱。
- TTS parser/compiler/transport fixture。
- 损坏音频和缓存健康维护。
- 关键键盘、选择、滚动、Popup、Focus 和 Window 生命周期合同。

测试精简不得建立永久的总数上限。阶段性数量目标只能写入 `TASK_BACKLOG.md`，不能进入 CI 或架构守卫成为长期门禁。

## 4. WPF Test Host 与无可见窗口约束

### 4.1 默认行为

- 自动测试默认不得在用户当前交互桌面显示 NovelSpeaker 顶层窗口。
- Page/UserControl 测试如果只需要 Measure/Arrange/Render、资源解析或视觉树检查，使用 `WpfControlHost`，不得为了方便创建 Window。
- 确实依赖 `Loaded`、`PresentationSource`、键盘焦点、Popup、Window 状态或 HWND 生命周期时，统一使用 `WpfWindowHost`。
- 真实 Window 生命周期默认运行在 `WpfTestHost` 创建的独立隐藏 Windows Desktop 中，而不是通过把窗口坐标移到虚拟屏幕外来模拟“不可见”。
- WPF Dispatcher 线程必须在进入 WPF/Application 初始化前绑定到测试 Desktop。
- 测试 Desktop 创建或绑定失败时必须 fail closed；不得静默回退到用户当前交互 Desktop。
- 窗口清理、Dispatcher 串行化、失败诊断和资源释放在隐藏 Desktop 与显式可见调试模式下使用同一 TestKit 边界。

### 4.2 显式可见调试

- 唯一可见窗口授权开关为 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
- 该开关只用于用户明确授权后的交互式调试，不属于普通 `dotnet test`、CI 或 Codex 默认测试流程。
- Codex 不得自行设置该变量；授权只对用户明确指定的当前任务有效，不跨任务继承。
- `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 只允许生成确定性视觉产物，不等价于允许可见窗口。

### 4.3 自动架构守卫

自动测试持续保证：

- `NovelSpeaker.App.WpfTests` 不直接调用 `Window.Show()` 或 `ShowDialog()`。
- WPF 测试不自行创建 STA Thread/Dispatcher，不直接调用 Win32 Desktop 创建、切换或绑定 API。
- 真实 `Window.Show()` 和 Windows Desktop API 只允许存在于指定 TestKit 宿主实现中。
- 测试不重新引入旧的可见窗口环境变量或页面级窗口 helper。
- `WpfControlHost`、`WpfWindowHost` 和失败诊断边界保持唯一共享实现。

## 5. 核心业务回归覆盖

### 5.1 主动缓存

- 全应用单批次限制。
- 章节顺序、缓存命中跳过、取消和失败。
- 切章、离开播放页或隐藏主窗口不取消后台批次。
- 配置快照冻结。
- `播放 > 预取 > 主动缓存` admission 优先级。
- 同一 TTS 规则共享 limiter。
- 稳定段身份与当前播放使用一致的 AudioCacheKey 语义。

### 5.2 缓存身份与朗读清单

- 正文缓存身份不依赖运行时 `SegmentIndex`。
- “朗读标题”开关不改变正文段身份，标题段独立命中和失效。
- TTS 请求语义变化时 `TtsRuleFingerprint` 变化；只改名称、启用状态或并发限制时保持不变。
- `TextProfileFingerprint` 变化但最终计划输出未变化时，不无理由重写段表或使音频失效。
- 每章只持久化当前有效朗读清单；配置反复变化不会形成历史版本倍增。
- 计划替换的取消、失败和进程中断不留下半套数据。
- 完整度查询区分清单缺失、过期和有效 0%；过期清单按既定生命周期异步重建。
- 普通目录查询不建立缺失清单；缓存管理查询修复有缓存但清单缺失的异常状态。
- 删除某章最后一条缓存时同步删除该章朗读清单；仍有缓存或受保护条目时保留。
- 启动缓存维护在索引修复/淘汰后集合式删除无任何 `AudioCacheEntries` 的残留朗读清单，并通过外键级联删除清单段；重复执行结果保持稳定。
- 计划刚提交但缓存尚未落盘的运行时窗口不由即时孤立计划清理干扰；测试应区分“启动维护的长期收敛”与“活动缓存生成的瞬时状态”。

### 5.3 SQLite 与性能

- 已发布 migration 按版本追加、重复启动安全和高版本拒绝。
- 缓存 schema 重构遵循明确重置边界，不建立隐式长期双读。
- 哈希存储、计划表结构和数据库体积保持合理。
- 大章节完整度批量查询不得逐段文件探测或音频解码。
- 一次批量刷新使用常数级连接/SQL 次数，不逐章或逐段重新打开连接。
- 完整度查询不更新音频 LRU 的 `LastAccessedAt`。
- 前台查询不等待正文读取和正则重算；后台重建完成后发布按章节定位的刷新通知。

### 5.4 缓存管理与导出

- Ctrl/Shift/Ctrl+A 选择模型和命令启用矩阵。
- 清理只作用于选中章节。
- 混合选择的不可导出章节处理、确认取消和全部不可导出分支。
- 导出目录选择取消、重复启动、页面离开与提交后后台批次生命周期。
- `IChapterExportCoordinator` 单批次互斥、参数冻结、进度、取消和终态。
- Shell 导出状态、取消、完成后打开目录/关闭以及失败/取消通知。
- 多段按播放顺序输出一个 MP3；不同章节始终分文件。
- 文件名非法字符、保留名、尾部点/空格和同名冲突处理。
- 取消/失败不覆盖已有文件或留下临时文件；导出 lease 阻止清理/LRU 删除正在读取的来源缓存。
- 缓存管理页物理缓存统计与当前配置完整度口径分离。
- 缓存管理保留当前配置 0% 的有缓存章节；普通目录隐藏 0% 和非正常状态。
- 导出开始时验证实际文件和解码状态，不把完整度百分比当作最终有效性证明。

### 5.5 删除、维护与桌面媒体

- 删除书籍级联清理章节朗读清单、清单段和内部缓存索引，不触碰用户外部源文件。
- operation journal/补偿流程在中断后可恢复。
- 缓存健康维护修正缺失/损坏文件索引并发布变化通知；完成索引修复和淘汰后清理无缓存索引的残留朗读清单。
- 系统 media command 到播放命令映射。
- 迷你播放器隐藏/恢复、置顶、进度、拖动和长标题边界。
- 托盘 close/exit 状态机。
- 定时停止使用可控 `TimeProvider`。

## 6. UI、视觉与可访问性测试

- UI 测试优先保护用户可观察行为：导航、命令启用、选择、Dirty State、缓存状态、页面生命周期、键盘焦点、Automation 和关键几何下限。
- 公共视觉资源只在其唯一 owner 层建立主合同；页面不重复冻结 Button、Icon、Input、Surface、Typography 等公共控件的内部实现。
- Style/ControlTheme、Palette/Token、Provider Bridge、主题切换、Icon Foreground、输入控件、设置组件、Shell、媒体控件等最终视觉规则统一以 `13_VISUAL_DESIGN_SYSTEM.md` 为定义来源。
- Library 响应式网格测试应直接覆盖布局组件的 viewport 几何合同：`300–360 px` 卡宽、`16 px` 间距、列数计算、所有行左侧起点稳定为 `0`、达到最大卡宽后剩余空间保留在右侧，以及窄于最小卡宽时的单列安全收缩。页面集成测试再覆盖默认窗口和窄/宽窗口，验证实际 ScrollViewer viewport 下没有横向溢出，且单本/少量书籍不会因整组居中产生横向跳位。
- `BookCardView` 回归测试应证明 MoreButton 只侵占标题区域所需空间，封面尺寸和卡片 Padding 保持稳定，作者/章节/剩余章节/ProgressBar 不因右上角按钮被整列永久缩窄。几何测试保护有效内容宽度，不冻结可继续微调的标题安全区精确像素。
- Shell 主题快捷入口测试必须覆盖 Light→Dark、Dark→Light、System+实际 Light→Dark、System+实际 Dark→Light、持久化失败回滚、设置页外部变更后的状态同步，以及 Footer 展开/Compact 下的目标图标、动作文案、Tooltip 和 AutomationName。快捷入口不得生成 System；System 只能由设置页显式选择。
- 交互样式测试优先固定状态所有权和可访问性合同，不冻结可继续微调的像素颜色：至少覆盖 Hover/Pressed/Keyboard Focus/Selected 的区分、Selected+Hover 优先级、父子控件不出现重复大面积状态层，以及 Light/Dark/High Contrast 下资源可解析且具有可辨识状态。
- Button/Icon 测试除资源键外还要验证最终视觉树前景：Dark Mode 的 Icon/Toolbar/Media Button 在 Pressed 状态下，`Button.Icon` 中实际 `SymbolIcon` 不得回落为黑色/低对比度 Provider 前景，Normal/Hover/Pressed/Disabled 均由 owning Button 的主题语义控制。
- FloatingIcon 测试必须覆盖 Book Details 与 Player 的真实定位/返回调用：Rest/Hover/Pressed/Keyboard Focus 只有一个可见 Button Surface，Hover 不提升 Elevation，Pressed 不使用 Accent 持续态，Dark Mode 最终图标保持可读，状态切换不改变命中区/布局中心或造成裁切。静态 Style/资源检查不能替代真实 View 最终像素证据。
- Media 测试保护播放器交互合同：媒体按钮 Hover 不出现命中区背景块；播放进度 Thumb 在 Rest 隐藏且在 Hover/Focus/Dragging 可见；竖向音量 Slider 始终显示 Thumb，并保持已填充/未填充轨道的独立视觉语义。Volume 的可见 Rail 与交互 Track 分离；Thumb 上下轨道必须保持一致固定厚度，连接处允许被 Thumb 覆盖但不得出现局部收缩、鼓包或可见断缝。对于“掐腰”一类几何缺陷，测试应渲染真实控件并比较 Thumb 上下相邻扫描线的轨道像素宽度，不能只断言 RepeatButton 的 Width/Margin 相等。
- 播放页目录项和正文段落等“外层命中宿主 + 内层 Selection Surface”组合必须渲染真实 `ListBoxItem`、Button 与内容 Border 的最终 Hover 像素。仅断言外层 `Background=Transparent`、`BorderThickness=0` 或 Style Trigger 不足以证明没有第二层状态面，因为默认/Provider `ControlTemplate` 仍可能在内部绘制矩形 PointerOver 层。测试至少覆盖一个普通项和一个 Selected/Current 项，并确认可见 Hover 仅由约定的圆角 Selection Surface 持有。
- Volume Thumb 的 Pressed/Dragging 视觉变化不得改变用于布局和 Track 测量的外层几何包络；若需要轻微放大，应在预留的固定 Thumb envelope 内缩放/调整内部圆形视觉。回归测试需要覆盖 Light/Dark、Player/MiniPlayer 和 100%/125%/150% DPI，验证控制柄左右边缘不被裁切、视觉中心不横向漂移，且轨道连续性不受状态切换影响。
- Input 测试保护字段与状态控件的反馈边界：TextBox/ComboBox Hover 主要由 Border 表达，ToggleSwitch 的纯开关 Focus/HitTest 范围贴合可见轨道，Mouse Hover 不触发 Keyboard Focus Ring。
- Menu/Popup 测试保护 Single Surface 与分隔线合同：MenuItem 只有一个主要 Hover owner；Separator 是独立元素、具有稳定 inset，不依附于相邻项 Border，也不因 Disabled/Opacity 变成断裂或不完整。Popup/Flyout 的内容 Border 是唯一不透明圆角 Surface，宿主/bridge chrome 在四角外保持透明；确定性渲染至少覆盖语速、规则切换和音量浮层，能够发现圆角后方残留直角半透明底板或方形阴影边界。此类测试必须通过真实 `Popup`/Flyout host 捕获 HWND 内最终图层，并检查圆角外像素 Alpha；只把 Popup 内容拆出来放进替代 Window，或只检查 `Background=Transparent` / `Effect=null` 等资源属性，都不足以证明最终像素正确。
- Feedback/Transient UI 测试必须保护 Single Surface 合同：`App.Feedback.DialogBody` 无背景、边框、圆角、Padding 和阴影；ContentDialog 不再套完整 Surface；`AppStatusView.IsEmbedded=true` 时自身 Section chrome 必须消失；StartupStatusWindow 由 Window 自身承担唯一 Raised Surface。
- WPF 自动合同应证明资源键唯一性、加载顺序、主题热切换、关键状态、最小点击区域、非零可用宽度、不重叠、核心内容可见、AutomationName 和关键键盘行为。
- 当缺陷属于“透明残影、裁切、阴影边界、抗锯齿、局部几何收缩”等渲染结果时，结构合同只能作为第一层检查，必须增加针对最终像素/真实宿主的回归证据。不要因为 VisualTree、Style Setter 或尺寸属性符合预期就提前关闭视觉缺陷。
- 几何测试只固定稳定下限和业务布局边界，不冻结仍可调整的精确 Padding、Margin、Width 或 Height。
- 100%、125%、150% DPI 和适用的长文本/窄宽度场景用于发现布局退化，但同一公共合同不在每个页面重复建立等价 case。
- Style Gallery 用于公共资源/控件族的稳定展示和自动渲染；正式页面截图必须实例化真实 View 和确定性脱敏 fixture，不用 Gallery 页面副本替代。
- 视觉验收允许生成临时截图、manifest、调试脚本或一次性 fixture，但这些产物只用于当前任务定位和人工/自动比对；验收结束前必须删除，不得提交到仓库。任务关闭前用 `git status --short` 和生成目录审计确认没有截图、截图脚本、临时 manifest 或其它视觉验收副产物残留。
- 视觉迁移历史、Legacy 删除过程和任务编号不属于长期页面测试合同；终态“无 Legacy/无旧聚合资源”只保留集中架构守卫。

## 7. 异步测试

- 不使用任意 `Task.Delay`、`Thread.Sleep` 等“等一会应该好了”的同步方式。
- 等待明确事件、Task、状态版本、channel、barrier/gate 或 fake clock。
- 取消验证 `OperationCanceledException`/Cancelled 语义，不把取消当 Error。
- 并发测试使用可控 barrier/gate 安排时序。
- 共享 Test Double 放在 `tests/TestKit`；跨测试项目复用的缓存身份、音频和导航 fixture 不在各项目重复定义。

## 8. WPF 视觉产物与失败诊断

- 默认 `dotnet test` 不生成成功截图、manifest 或仓库内审计文件，并且必须在 `artifacts/visual-review/` 完全不存在时仍可通过。
- `artifacts/visual-review/` 是显式 UI 开发/验收时按需生成的本地临时资产，不属于仓库默认测试输入、长期截图基线或发布资产。
- 只有设置 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 或显式运行视觉生成工具时，才生成明确请求的 Style Gallery、页面或窗口视觉产物。
- 视觉产物使用固定 DPI、viewport、主题和脱敏 fixture，输出到明确目录并带可重复校验信息；PNG、子 manifest 与根 manifest 都属于该次生成结果，可随时删除并重新生成。
- 默认测试不得读取仓库中的历史截图、根 manifest、截图哈希或 child manifest 来判定功能回归。需要验证截图/manifest 生成器本身时，应在测试拥有的临时目录中生成最小资产并验证格式、哈希和可重复性，测试结束后清理。
- Style Gallery、正式 Page/Window fixture、截图 harness、稳定场景 ID 和生成脚本属于可长期维护的开发能力；取消长期维护的是生成结果，而不是视觉测试能力本身。
- 成功视觉产物与失败诊断分开；测试失败时共享 WPF Test Host 可写入 `TestResults/wpf-diagnostics/<test-name>/` 的 PNG、视觉树和窗口状态。
- 视觉产物生成仍受默认隐藏 Desktop 约束，不因为需要截图而获得显示到用户 Desktop 的权限。

## 9. 自动质量门禁

完整门禁固定为：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

默认门禁不得设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。

涉及发布内容时额外执行 self-contained `win-x64` publish 和自动包内容检查，确保：

- 主程序以 `NovelSpeaker.exe` 输出，且不存在旧 `NovelSpeaker.App.exe`。
- 许可证和第三方声明存在。
- Windows Media Foundation MP3 编码所需的 NAudio runtime assemblies 存在。
- 发布包不预置 `app.db`、`settings.json`、Books、Cache、Operations、Logs 或其它用户数据；正式运行按需创建同级 `Data/`。
- 不包含测试程序集、TestAssets、损坏音频 fixture、Style Gallery 或临时文件。

## 10. Backlog 任务验收

每个任务至少定义：

- 针对性自动测试或架构检查。
- 受影响项目的 build/test。
- 行为、数据或安全边界改变时的专项回归。

阶段收口运行完整质量门禁。视觉任务可以额外按需生成 Style Gallery 或正式 View 截图供用户查看或比较，但这些生成结果不进入默认质量门禁；任务完成条件使用自动构建、契约、几何、可访问性和渲染检查，不依赖人工视觉判断或历史像素基线。