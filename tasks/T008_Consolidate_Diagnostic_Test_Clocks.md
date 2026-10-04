# T008：去除可共享的诊断测试时钟重复实现

## 目标

对照诊断 store/export/ViewModel 测试中的本地 Fixed/Manual time provider 与 `tests/TestKit` 共享实现；行为完全等价时复用共享实现并删除本地副本。

## 背景与证据

审计报告 D006：`SqliteDiagnosticSessionStoreTests` 及诊断导出/ViewModel 测试中有本地时钟 doubles，与 TestKit 的 `ManualTimeProvider`、`FixedTimeProvider` 重复。

## 范围

- 比较构造 API、Timer/ITimer 行为、UTC 时间、timestamp frequency/monotonic advance、Dispose 和取消语义。
- 只将完全等价的使用点改为共享 TestKit 类型；有真实特殊语义时保留本地类并说明原因。
- 不把共享 helper 扩张成新测试项目或新的抽象层。

## 验收

- 删除的本地实现无剩余引用；保留的本地实现有注释说明无法共享的具体差异。
- 受影响的诊断 Infrastructure/Presentation focused tests 通过，异步测试使用可控时钟，不使用固定延时猜测。
- 不新增永久测试；运行相关 focused tests 与 format。无需执行完整 Release 门禁，除非改动影响超出测试项目。

## 长期合同

- `docs/08_QUALITY_AND_TESTING.md` 第 3、4、9 节：避免重复实现细节测试、临时验证清理和确定性异步测试。

## 依赖与风险

无前置任务。共享 helper 若语义不匹配，不为消除重复而削弱测试。
