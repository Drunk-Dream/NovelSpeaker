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

### SQLite 与性能

- version 6 到新版 schema 7 的追加 migration、重复启动和高版本拒绝。
- 旧缓存索引和内部缓存文件按明确重置边界清理，不建立兼容读取路径。
- 哈希 BLOB、`WITHOUT ROWID` 和单计划策略的数据库体积测试。
- 2,000 和 10,000 段完整度批量查询不得调用文件探测或音频解码。
- 一次批量刷新使用常数级连接/SQL 次数，不逐章或逐段打开连接。
- 缓存完整度查询不更新 `LastAccessedAt`。

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
- 导出开始时严格验证文件和解码状态，不把目录完整度作为最终有效性证明。

### 删除与维护

- 删除书籍级联删除章节朗读清单、清单段和缓存索引。
- operation journal 保留内部音频路径，删除中断后可以恢复且不触碰外部源文件。
- 删除完成后数据库和 `Cache/Tts` 不存在该书残留。
- 缓存健康维护发现缺失或损坏文件后修正索引并发布章节变化通知。

### 桌面媒体

- 系统 media command 到播放命令映射。
- 迷你窗口隐藏/恢复、置顶状态视觉、章节段落进度投影/拖动/Tooltip 和空白区域拖动边界。
- 托盘 close/exit 状态机。
- 定时停止使用可控 `TimeProvider`。

### UI

- dirty state：未修改时取消/保存禁用。
- 导航 guard。
- 当前章节定位与虚拟化列表。
- 播放页章节卡片 Tooltip 显示完整章节名。
- 播放页和详情页目录的当前章节无文字标记、缓存百分比投影、缓存变化刷新和页面退订。
- 目录 0% 继续投影为空文本；1%–100% 保持现有整数百分比格式。
- 主动缓存多选项的整卡视觉状态，包含当前章节同时被选中的组合状态。
- icon button semantic style、focus 和 AutomationName。

## 5. 异步测试

- 不使用任意 `Task.Delay`、`Thread.Sleep` 等“等一会应该好了”的测试。
- 等待明确事件、Task、状态版本、channel 或 fake clock。
- 取消必须验证 `OperationCanceledException`/Cancelled 语义，不把取消当 Error。
- 并发测试通过可控 barrier/gate 排列时序。

## 6. 自动质量门禁

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

## 7. 任务验收

每个 Backlog 任务至少定义：

- 针对性自动测试。
- 受影响项目的 build/test。
- 行为/数据/安全边界改变时的回归测试。

Wave 收口运行完整质量门禁。无法自动证明的视觉细节应尽量通过样式资源、VisualState、Automation 属性和 WPF 测试建立机器可检查的契约，而不是把人工验证写成任务完成条件。
