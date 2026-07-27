# 工程约定

## 1. 代码组织

- 生产代码按 feature/职责组织，不按“所有 Service/Model/ViewModel”建巨型技术目录。
- 文件名、命名空间和主公共类型保持一致。
- 一个类型只承担可解释的单一状态所有权或技术适配职责。
- 不新增 `New/V2/Final/Old/Compat` 平行主线；迁移完调用者后删除旧入口。
- 避免万能 `Manager/Helper/Utils`；通用代码必须有明确业务或平台语义。

## 2. 命名

- 用例/命令使用动作语义：`ImportBook`、`ExportChapters`、`StartActiveCache`。
- 查询模型使用结果语义：`CacheOverview`、`ActiveCacheSnapshot`。
- `Coordinator` 仅用于真正协调多个拥有明确边界的参与者。
- `Controller` 用于局部交互/状态协调，不作为业务服务的默认后缀。
- `Adapter` 明确表示平台/技术适配。

## 3. 异步

- I/O 和可等待业务流程接受 `CancellationToken` 并向下传递。
- 不使用 `.Result`、`.Wait()` 或同步 `Mutex.Wait` 阻塞异步路径。
- `async void` 仅限事件入口。
- fire-and-forget 必须有 owner、取消和异常观察。
- `OperationCanceledException` 是正常控制流。

## 4. 错误处理

- 不使用空 `catch`。
- 技术异常在边界处转换为稳定错误分类和脱敏用户信息。
- catch 只捕获能处理的范围；取消优先重新抛出或映射为 Cancelled。
- 资源在 `finally`/`await using`/`using` 中确定性释放。

## 5. Application/API

- Application 合同不引用 SQLite、HTTP message、Jint、NAudio、WPF、Wpf.Ui 类型。
- 不因为测试方便就为每个类机械建立接口。
- 新公共接口必须能回答：谁调用、谁实现、谁拥有状态、取消语义是什么。
- DTO 不与 UI 卡片/控件一一绑定；Presentation 自行投影。

## 6. WPF

- 业务逻辑不写在 code-behind。
- code-behind 可处理 WPF 必需的焦点、拖放、滚动、虚拟化、动画、窗口和事件桥接。
- 视觉样式优先放入共享 ResourceDictionary/Design Token，不在页面复制 Trigger。
- UI 平台能力通过 presentation port/adapter 暴露给可测试代码。

## 7. 数据与文件

- 所有内部路径经过集中 resolver；不直接相信数据库绝对路径。
- 文件写入使用 staging + atomic move/replace。
- 多资源操作设计补偿或 journal。
- migration 只追加。
- 导出永不静默覆盖用户已有文件。

## 8. 安全

- 外部规则和文本均视为不可信输入。
- 不放宽脚本沙箱、路径根限制和请求校验来“提高兼容率”。
- 日志/诊断/UI 错误不输出正文、完整请求或凭据。
- 测试 fixture 使用虚构、脱敏内容。

## 9. 文档

- 数字编号文档描述终态，不写执行 Wave。
- `TASK_BACKLOG.md` 是唯一任务计划。
- 根 `README.md` 只写当前已实现能力。
- 行为/架构/数据边界变化时同步对应文档；纯重命名不制造无意义文档改动。

## 10. 依赖与格式

- 使用仓库固定的 .NET SDK 和中央包版本。
- 依赖变化后审查所有 `packages.lock.json`。
- 不为清理代码顺带升级无关依赖。
- `dotnet format --verify-no-changes` 必须通过。