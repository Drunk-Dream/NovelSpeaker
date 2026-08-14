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
- 缓存健康维护修正缺失/损坏文件索引并发布变化通知。
- 系统 media command 到播放命令映射。
- 迷你播放器隐藏/恢复、置顶、进度、拖动和长标题边界。
- 托盘 close/exit 状态机。
- 定时停止使用可控 `TimeProvider`。

## 6. UI、视觉与可访问性测试

- UI 测试优先保护用户可观察行为：导航、命令启用、选择、Dirty State、缓存状态、页面生命周期、键盘焦点、Automation 和关键几何下限。
- 公共视觉资源只在其唯一 owner 层建立主合同；页面不重复冻结 Button、Icon、Input、Surface、Typography 等公共控件的内部实现。
- Style/ControlTheme、Palette/Token、Provider Bridge、主题切换、Icon Foreground、输入控件、设置组件、Shell、媒体控件等最终视觉规则统一以 `13_VISUAL_DESIGN_SYSTEM.md` 为定义来源。
- Feedback/Transient UI 测试必须保护 Single Surface 合同：`App.Feedback.DialogBody` 无背景、边框、圆角、Padding 和阴影；ContentDialog 不再套完整 Surface；`AppStatusView.IsEmbedded=true` 时自身 Section chrome 必须消失；StartupStatusWindow 由 Window 自身承担唯一 Raised Surface。
- WPF 自动合同应证明资源键唯一性、加载顺序、主题热切换、关键状态、最小点击区域、非零可用宽度、不重叠、核心内容可见、AutomationName 和关键键盘行为。
- 几何测试只固定稳定下限和业务布局边界，不冻结仍可调整的精确 Padding、Margin、Width 或 Height。
- 100%、125%、150% DPI 和适用的长文本/窄宽度场景用于发现布局退化，但同一公共合同不在每个页面重复建立等价 case。
- Style Gallery 用于公共资源/控件族的稳定展示和自动渲染；正式页面截图必须实例化真实 View 和确定性脱敏 fixture，不用 Gallery 页面副本替代。
- 视觉迁移历史、Legacy 删除过程和任务编号不属于长期页面测试合同；终态“无 Legacy/无旧聚合资源”只保留集中架构守卫。

## 7. 异步测试

- 不使用任意 `Task.Delay`、`Thread.Sleep` 等“等一会应该好了”的同步方式。
- 等待明确事件、Task、状态版本、channel、barrier/gate 或 fake clock。
- 取消验证 `OperationCanceledException`/Cancelled 语义，不把取消当 Error。
- 并发测试使用可控 barrier/gate 安排时序。
- 共享 Test Double 放在 `tests/TestKit`；跨测试项目复用的缓存身份、音频和导航 fixture 不在各项目重复定义。

## 8. WPF 视觉产物与失败诊断

- 默认 `dotnet test` 不生成成功截图、manifest 或仓库内审计文件。
- 只有设置 `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 时才生成明确请求的 Style Gallery、页面或窗口视觉产物。
- 视觉产物使用固定 DPI、viewport、主题和脱敏 fixture，输出到明确目录并带可重复校验信息。
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

- 主程序、许可证和第三方声明存在。
- Windows Media Foundation MP3 编码所需的 NAudio runtime assemblies 存在。
- 不包含测试程序集、TestAssets、损坏音频 fixture、Style Gallery 或临时文件。

## 10. Backlog 任务验收

每个任务至少定义：

- 针对性自动测试或架构检查。
- 受影响项目的 build/test。
- 行为、数据或安全边界改变时的专项回归。

阶段收口运行完整质量门禁。视觉任务可以额外生成 Style Gallery 或正式 View 的稳定截图供用户后续查看，但任务完成条件使用自动构建、契约、几何、可访问性和渲染检查，不依赖人工视觉判断。