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

## 4. 下一阶段重点测试

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

### 缓存管理/导出

- Ctrl/Shift/Ctrl+A 选择模型。
- 清理只作用于所选章节。
- 不完整章节不可导出。
- 多段按顺序输出一个 MP3。
- 文件名非法字符、保留名、尾部点/空格和同名冲突处理。
- 导出取消/失败不会覆盖用户已有文件或留下临时文件。

### 桌面媒体

- 系统 media command 到播放命令映射。
- 迷你窗口隐藏/恢复/置顶状态。
- 托盘 close/exit 状态机。
- 定时停止使用可控 `TimeProvider`。

### UI

- dirty state：未修改时取消/保存禁用。
- 导航 guard。
- 当前章节定位与虚拟化列表。
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
